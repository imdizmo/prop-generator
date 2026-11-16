using System.Net;
using PropGenerator.Desktop.Backups;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

// The cloud backup against a store in memory (FakeS3): no network and no real keychain anywhere.
public class CloudBackupTests
{
    private static readonly CloudKeys Keys = new("AKTEST", "secret/key+1");

    // No window here: the calls are waited for on this thread, never by pumping the UI thread.
    private static T Wait<T>(Task<T> t) => t.GetAwaiter().GetResult();

    private static void Wait(Task t) => t.GetAwaiter().GetResult();

    private static CloudTarget Target(string bucket = "props") => CloudTarget.From("r2", "https://ACCOUNT.r2.cloudflarestorage.com/", "auto", bucket, "prop-generator");

    private static S3Client Client(FakeS3 store, CloudKeys? keys = null, string bucket = "props") => new(store, Target(bucket), keys ?? Keys);

    [Fact]
    public void TheAddressIsTidiedAndPathStyleExceptOnAmazon()
    {
        CloudTarget r2 = Target();
        Assert.Equal("https://account.r2.cloudflarestorage.com/props/prop-generator/a%20b.zip", r2.UriFor("prop-generator/a b.zip").AbsoluteUri);
        var aws = CloudTarget.From("aws", null, "eu-west-1", "props", "pg");
        Assert.Equal("https://props.s3.eu-west-1.amazonaws.com/pg/x.zip", aws.UriFor("pg/x.zip").AbsoluteUri);
        var local = CloudTarget.From("minio", "localhost:9000", null, "props", "pg");
        Assert.Equal("http://localhost:9000/props/pg/x.zip", local.UriFor("pg/x.zip").AbsoluteUri);
        Assert.Equal("us-east-1", local.Region);
        Assert.Equal(["Cloudflare R2", "Backblaze B2", "Amazon S3", "Wasabi", "DigitalOcean Spaces", "MinIO", "Other"], CloudServices.All.Select(s => s.Label));
    }

    [Fact]
    public void PutGetListAndDeleteAreSignedAndWork()
    {
        var store = new FakeS3(Keys.AccessKey, Keys.SecretKey, "props") { PageSize = 2 };
        using S3Client s3 = Client(store);

        for (int i = 0; i < 5; i++)
        {
            Wait(s3.PutAsync($"prop-generator/{i}.txt", [(byte)i]));
        }

        Assert.Equal([(byte)3], Wait(s3.GetAsync("prop-generator/3.txt")));
        IReadOnlyList<S3Object> listed = Wait(s3.ListAsync("prop-generator/"));
        Assert.Equal(5, listed.Count);
        Assert.Equal(1, listed[0].Size);
        Wait(s3.DeleteAsync("prop-generator/0.txt"));
        Assert.Equal(4, store.Objects.Count);
        Assert.All(store.Requests, r => Assert.Matches("^(PUT|GET|DELETE) ", r));
    }

    [Fact]
    public void WrongKeysAMissingBucketAndNoNetworkSayWhatToDo()
    {
        var store = new FakeS3(Keys.AccessKey, Keys.SecretKey, "props");
        using (S3Client wrong = Client(store, new CloudKeys("AKTEST", "other")))
        {
            Assert.StartsWith("The storage didn't accept these keys", Assert.Throws<BackupException>(() => Wait(wrong.ListAsync("x/"))).Message, StringComparison.Ordinal);
        }

        using (S3Client noBucket = Client(store, bucket: "nope"))
        {
            Assert.StartsWith("There's no bucket called nope", Assert.Throws<BackupException>(() => Wait(noBucket.ListAsync("x/"))).Message, StringComparison.Ordinal);
        }

        store.Unreachable = true;
        using S3Client s3 = Client(store);
        Assert.StartsWith("Prop Generator couldn't reach account.r2.cloudflarestorage.com", Assert.Throws<BackupException>(() => Wait(s3.ListAsync("x/"))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BackUpListRestoreAndKeepTheNewest()
    {
        using var s = new Scratch();
        var store = new FakeS3(Keys.AccessKey, Keys.SecretKey, "props");
        using S3Client s3 = Client(store);
        var cloud = new CloudBackup(s3, "prop-generator");

        Wait(cloud.CheckAsync());
        Assert.Empty(store.Objects);

        File.WriteAllText(s.P("b.zip"), "zip bytes");
        var t = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < 4; i++)
        {
            Wait(cloud.UploadAsync(s.P("b.zip"), t.AddHours(i), keep: 3));
        }

        IReadOnlyList<S3Object> backups = Wait(cloud.ListAsync());
        Assert.Equal(
            ["prop-generator/prop-generator-backup-2026-11-12-120000.zip", "prop-generator/prop-generator-backup-2026-11-12-110000.zip", "prop-generator/prop-generator-backup-2026-11-12-100000.zip"],
            backups.Select(b => b.Key));

        Wait(cloud.DownloadAsync(backups[0].Key, s.P("down.zip")));
        Assert.Equal("zip bytes", File.ReadAllText(s.P("down.zip")));
    }

    [Fact]
    public void ADownloadThatNeverEndsStopsAtTheLimitAndLeavesNoFile()
    {
        using var s = new Scratch();
        var body = new EndlessStream(stalled: false);
        using S3Client s3 = Misbehaving(new StubHttp(_ => Answer(body)));
        Assert.Contains("more than", Assert.Throws<BackupException>(() => Wait(s3.GetFileAsync("prop-generator/b.zip", s.P("down.zip")).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken))).Message, StringComparison.Ordinal);
        Assert.True(body.Sent <= s3.DownloadLimit + (1024 * 1024));
        Assert.Empty(Directory.GetFiles(s.Dir));
    }

    [Fact]
    public void ADownloadThatStallsStopsAfterTheIdleLimitAndLeavesNoFile()
    {
        using var s = new Scratch();
        using S3Client s3 = Misbehaving(new StubHttp(_ => Answer(new EndlessStream(stalled: true))));
        Assert.Contains("stopped answering", Assert.Throws<BackupException>(() => Wait(s3.GetFileAsync("prop-generator/b.zip", s.P("down.zip")).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken))).Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(s.Dir));
    }

    [Fact]
    public void AReadIntoMemoryOrAnErrorNeverHoldsMoreThanTheReplyLimitOrWaitsForEver()
    {
        var body = new EndlessStream(stalled: false);
        using (S3Client s3 = Misbehaving(new StubHttp(_ => Answer(body))))
        {
            Assert.Contains("more than", Assert.Throws<BackupException>(() => Wait(s3.GetAsync("prop-generator/check").WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken))).Message, StringComparison.Ordinal);
            Assert.True(body.Sent <= S3Client.MaxReply + (1024 * 1024));
        }

        var error = new EndlessStream(stalled: false);
        using (S3Client s3 = Misbehaving(new StubHttp(_ => Answer(error, HttpStatusCode.InternalServerError))))
        {
            Assert.Throws<BackupException>(() => Wait(s3.ListAsync("prop-generator/").WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)));
            Assert.True(error.Sent <= S3Client.MaxReply + (1024 * 1024));
        }

        using (S3Client s3 = Misbehaving(new StubHttp(_ => Answer(new EndlessStream(stalled: true), HttpStatusCode.Forbidden))))
        {
            Assert.Throws<BackupException>(() => Wait(s3.DeleteAsync("prop-generator/check").WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)));
        }
    }

    [Fact]
    public void AListThatRepeatsItsContinuationTokenStops()
    {
        int n = 0;
        var http = new StubHttp(_ => Page("same", ++n < GiveUp));
        using S3Client s3 = Misbehaving(http);
        Assert.Contains("list", Assert.Throws<BackupException>(() => Wait(s3.ListAsync("prop-generator/").WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken))).Message, StringComparison.Ordinal);
        Assert.Equal(2, http.Requests);
    }

    [Fact]
    public void AListThatNeverEndsStopsAtThePageLimit()
    {
        int n = 0;
        var http = new StubHttp(_ => Page($"token-{n}", ++n < GiveUp));
        using S3Client s3 = Misbehaving(http);
        Assert.Contains("list", Assert.Throws<BackupException>(() => Wait(s3.ListAsync("prop-generator/").WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken))).Message, StringComparison.Ordinal);
        Assert.Equal(S3Client.MaxListPages, http.Requests);
    }

    [Theory]
    [InlineData("bad host")]
    [InlineData("a:b:c")]
    [InlineData("host:99999999")]
    [InlineData("[::1")]
    public void SetUpWithAnAddressThatIsNotOneSaysSo(string endpoint)
    {
        using var s = new Scratch();
        var http = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using BackupActions backups = Actions(s, http);
        string said = Wait(backups.SetUpCloudAsync("other", endpoint, null, "props", "pg", "AK", "secret"));
        Assert.Contains("address", said, StringComparison.Ordinal);
        Assert.Equal(0, http.Requests);
    }

    [Fact]
    public void SetUpWithAListThatIsNotXmlSaysWhatToCheck()
    {
        using var s = new Scratch();
        byte[] last = [];
        var http = new StubHttp(r =>
        {
            if (r.Method == HttpMethod.Put)
            {
                last = r.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }

            return r.Method == HttpMethod.Get && r.RequestUri!.Query.Contains("list-type", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<ListBucketResult><Contents>") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(last) };
        });
        using BackupActions backups = Actions(s, http);
        string said = Wait(backups.SetUpCloudAsync("r2", "https://ACCOUNT.r2.cloudflarestorage.com/", null, "props", "pg", "AK", "secret"));
        Assert.Contains("list", said, StringComparison.Ordinal);
    }

    private static BackupActions Actions(Scratch s, StubHttp http) =>
        new(new Library(s.P("library.json")), new Prefs(s.P("prefs.json")), new FakeDialogs(), s.P("config"), (_, _) => { })
        {
            Keychain = new FakeKeychain(),
            Http = http,
        };

    // After so many pages the misbehaving storage says the list is done, so a client without a limit ends its test.
    private const int GiveUp = 5 * S3Client.MaxListPages;

    // A client of a storage that misbehaves, with a short idle limit and a small download limit.
    private static S3Client Misbehaving(StubHttp http) =>
        new(http, Target(), Keys) { Idle = TimeSpan.FromMilliseconds(300), DownloadLimit = 4L * 1024 * 1024 };

    private static HttpResponseMessage Answer(Stream body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StreamContent(body) };

    // A list page with one object that says there is more (until told otherwise), after the token given.
    private static HttpResponseMessage Page(string token, bool more) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            "<?xml version=\"1.0\"?><ListBucketResult><Contents><Key>prop-generator/x</Key><Size>1</Size></Contents>"
            + $"<IsTruncated>{(more ? "true" : "false")}</IsTruncated><NextContinuationToken>{token}</NextContinuationToken></ListBucketResult>"),
    };
}
