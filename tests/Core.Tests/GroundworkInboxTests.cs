using System.Text.Json.Nodes;
using Xunit;

namespace PropGenerator.Tests;

public sealed class GroundworkInboxTests : IDisposable
{
    // A GLB as far as the inbox cares: the header and a JSON chunk.
    private static readonly byte[] Glb = MakeGlb("""{"asset":{"version":"2.0"}}""");

    private readonly string _data = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "prop-generator", "scratch",
        "gw-" + Guid.NewGuid().ToString("N"));

    public GroundworkInboxTests() => Directory.CreateDirectory(_data);

    public void Dispose() => Directory.Delete(_data, true);

    private string Inbox => Path.Combine(_data, "inbox");

    [Fact]
    public void TheDataFolderFollowsXdgDataHomeThenTheHomeShareOnLinux()
    {
        Assert.Equal(Path.Combine("/x/data", "Groundwork"), GroundworkInbox.DefaultDataDir(v => v == "XDG_DATA_HOME" ? "/x/data" : null, windows: false, home: "/home/u", localAppData: "unused"));
        Assert.Equal(Path.Combine("/home/u", ".local", "share", "Groundwork"), GroundworkInbox.DefaultDataDir(_ => null, windows: false, home: "/home/u", localAppData: "unused"));
        Assert.Equal(Path.Combine("/home/u", ".local", "share", "Groundwork"), GroundworkInbox.DefaultDataDir(_ => "  ", windows: false, home: "/home/u", localAppData: "unused"));

        // XDG_DATA_HOME must be absolute, by the spec; a relative one is ignored.
        Assert.Equal(Path.Combine("/home/u", ".local", "share", "Groundwork"), GroundworkInbox.DefaultDataDir(_ => "rel/dir", windows: false, home: "/home/u", localAppData: "unused"));
        Assert.Equal(Path.Combine("C:/Users/u/AppData/Local", "Groundwork"), GroundworkInbox.DefaultDataDir(_ => "/x/data", windows: true, home: "unused", localAppData: "C:/Users/u/AppData/Local"));
    }

    [Fact]
    public void AnIdIsAPlainNameWithTheTimeAndAShortTag()
    {
        string id = GroundworkInbox.NewId(new DateTime(2026, 11, 15, 15, 30, 12, DateTimeKind.Local));

        Assert.Matches("^propgen-20261115-153012-[0-9a-f]{4}$", id);
        Assert.NotEqual(id, GroundworkInbox.NewId(new DateTime(2026, 11, 15, 15, 30, 12, DateTimeKind.Local)) + "x");
    }

    [Fact]
    public void ADeliveryIsWrittenHiddenAndOnlyShowsOnceRenamedWhole()
    {
        var inbox = new GroundworkInbox(_data);
        Assert.False(Directory.Exists(Inbox));

        string final;
        using (InboxDelivery d = inbox.Begin())
        {
            // The inbox is made if missing; the delivery starts as a hidden .<id>.partial folder.
            Assert.True(Directory.Exists(Inbox));
            Assert.Equal(Path.Combine(Inbox, "." + d.Id + ".partial"), d.PartialFolder);
            d.Add(new InboxKind("pg_mossy_rock", "Mossy rock", "Rocks", [0.8f, 1.2f]), Glb);
            d.Add(new InboxKind("pg_oak_tree", "Oak tree", "Trees"), Glb);

            // Nothing Groundwork would read yet: every name in the inbox starts with '.', and send.json is not written.
            Assert.All(Directory.GetFileSystemEntries(Inbox), e => Assert.StartsWith(".", Path.GetFileName(e), StringComparison.Ordinal));
            Assert.False(File.Exists(Path.Combine(d.PartialFolder, "send.json")));
            Assert.True(File.Exists(Path.Combine(d.PartialFolder, "pg_mossy_rock.glb")));

            final = d.Complete();
            Assert.Equal(Path.Combine(Inbox, d.Id), final);
            Assert.False(Directory.Exists(d.PartialFolder));
        }

        // Whole, flat: send.json and the GLBs, nothing else, no folders.
        Assert.Equal(["pg_mossy_rock.glb", "pg_oak_tree.glb", "send.json"], Directory.GetFileSystemEntries(final).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(Glb, File.ReadAllBytes(Path.Combine(final, "pg_oak_tree.glb")));
        Assert.Equal([Path.GetFileName(final)], Directory.GetFileSystemEntries(Inbox).Select(Path.GetFileName));
    }

    [Fact]
    public void SendJsonHasTheContractsShape()
    {
        var inbox = new GroundworkInbox(_data);
        string final;
        using (InboxDelivery d = inbox.Begin())
        {
            d.Add(new InboxKind("pg_mossy_rock", "Mossy rock", "Rocks", [0.8f, 1.2f]), Glb);
            d.Add(new InboxKind("pg_dark_floor_tile", "dark floor tile", "Buildings"), Glb);
            final = d.Complete();
        }

        var send = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(final, "send.json")))!;
        Assert.Equal(["version", "source", "kinds"], send.Select(p => p.Key));
        Assert.Equal(1, send["version"]!.GetValue<int>());
        Assert.Equal("Prop Generator", send["source"]!.GetValue<string>());
        var kinds = (JsonArray)send["kinds"]!;
        Assert.Equal(2, kinds.Count);
        var rock = (JsonObject)kinds[0]!;
        Assert.Equal(["file", "model", "label", "category", "scale"], rock.Select(p => p.Key));
        Assert.Equal("pg_mossy_rock.glb", rock["file"]!.GetValue<string>());
        Assert.Equal("pg_mossy_rock", rock["model"]!.GetValue<string>());
        Assert.Equal("Mossy rock", rock["label"]!.GetValue<string>());
        Assert.Equal("Rocks", rock["category"]!.GetValue<string>());
        Assert.Equal([0.8f, 1.2f], ((JsonArray)rock["scale"]!).Select(n => n!.GetValue<float>()));

        // No scale given: [1,1], said outright.
        Assert.Equal([1f, 1f], ((JsonArray)kinds[1]!["scale"]!).Select(n => n!.GetValue<float>()));
        Assert.Equal("Buildings", kinds[1]!["category"]!.GetValue<string>());
    }

    [Fact]
    public void OnlyTheContractsCategoriesAreTaken()
    {
        Assert.Equal(["Trees", "Rocks", "Bushes", "Plants", "Props", "Buildings", "Markers", "Other"], GroundworkInbox.Categories);
        using InboxDelivery d = new GroundworkInbox(_data).Begin();
        foreach (string c in GroundworkInbox.Categories)
        {
            d.Add(new InboxKind("pg_" + c.ToLowerInvariant(), c, c), Glb);
        }

        Assert.Throws<InboxException>(() => d.Add(new InboxKind("pg_x", "X", "Construction"), Glb));
        Assert.Throws<InboxException>(() => d.Add(new InboxKind("pg_y", "Y", "rocks"), Glb));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(".hidden")]
    [InlineData("")]
    [InlineData(" pad")]
    [InlineData("a:b")]
    [InlineData("con")]
    public void AModelNameMustBeAPlainFileName(string model)
    {
        using InboxDelivery d = new GroundworkInbox(_data).Begin();
        Assert.Throws<InboxException>(() => d.Add(new InboxKind(model, "L", "Rocks"), Glb));
        Assert.Empty(Directory.GetFileSystemEntries(d.PartialFolder));
    }

    [Fact]
    public void TheLimitsAreRefused()
    {
        using InboxDelivery d = new GroundworkInbox(_data).Begin();

        // Not a GLB, the same model twice (Groundwork compares names without case), a bad scale.
        Assert.Throws<InboxException>(() => d.Add(new InboxKind("pg_a", "A", "Rocks"), [1, 2, 3, 4, 5]));
        d.Add(new InboxKind("pg_a", "A", "Rocks"), Glb);
        Assert.Throws<InboxException>(() => d.Add(new InboxKind("PG_A", "A", "Rocks"), Glb));
        Assert.Throws<InboxException>(() => d.Add(new InboxKind("pg_b", "B", "Rocks", [0f, 1f]), Glb));
        Assert.Throws<InboxException>(() => d.Add(new InboxKind("pg_b", "B", "Rocks", [2f, 1f]), Glb));
        Assert.Throws<InboxException>(() => d.Add(new InboxKind("pg_b", "B", "Rocks", [1f, 101f]), Glb));
        Assert.Throws<InboxException>(() => d.Add(new InboxKind("pg_b", "B", "Rocks", [1f]), Glb));

        // At most 200 models.
        for (int i = 1; i < GroundworkInbox.MaxKinds; i++)
        {
            d.Add(new InboxKind($"pg_{i}", "N", "Props"), Glb);
        }

        InboxException e = Assert.Throws<InboxException>(() => d.Add(new InboxKind("pg_201", "N", "Props"), Glb));
        Assert.Contains("200", e.Message, StringComparison.Ordinal);
        Assert.Equal(GroundworkInbox.MaxKinds, Directory.GetFiles(d.PartialFolder, "*.glb").Length);
    }

    [Fact]
    public void AGlbOverTheSizeLimitIsRefusedBeforeItIsWritten()
    {
        Assert.Equal(512L * 1024 * 1024, GroundworkInbox.MaxFileBytes);
        using InboxDelivery d = new GroundworkInbox(_data, maxFileBytes: 16).Begin();
        InboxException e = Assert.Throws<InboxException>(() => d.Add(new InboxKind("pg_big", "Big", "Rocks"), [.. Glb, .. new byte[8]]));
        Assert.Contains("MB", e.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(d.PartialFolder, "pg_big.glb")));
    }

    [Fact]
    public void ALongLabelIsCutToWhatGroundworkShows()
    {
        using InboxDelivery d = new GroundworkInbox(_data).Begin();
        d.Add(new InboxKind("pg_a", new string('x', 100), "Rocks"), Glb);
        string final = d.Complete();
        string label = JsonNode.Parse(File.ReadAllText(Path.Combine(final, "send.json")))!["kinds"]![0]!["label"]!.GetValue<string>();
        Assert.Equal(80, label.Length);
    }

    [Fact]
    public void AnEmptyDeliveryCannotBeCompletedAndAnAbandonedOneLeavesNothing()
    {
        var inbox = new GroundworkInbox(_data);
        using (InboxDelivery d = inbox.Begin())
        {
            Assert.Throws<InboxException>(d.Complete);
        }

        using (InboxDelivery d = inbox.Begin())
        {
            d.Add(new InboxKind("pg_a", "A", "Rocks"), Glb);

            // Disposed without Complete: the partial folder goes.
        }

        Assert.Empty(Directory.GetFileSystemEntries(Inbox));
    }

    [Fact]
    public void TheAnswerIsRead()
    {
        InboxAnswer ok = GroundworkInbox.ReadAnswer("""{"ok":true,"map":"Isle","models":["pg_a","pg_b"]}""");
        Assert.True(ok.Ok);
        Assert.False(ok.Waiting);
        Assert.Equal("Isle", ok.Map);
        Assert.Equal(["pg_a", "pg_b"], ok.Models);
        Assert.Equal("In Groundwork map Isle", ok.Text);

        InboxAnswer no = GroundworkInbox.ReadAnswer("""{"ok":false,"map":"Isle","models":[],"error":"The map has another model named pg_a: rename it before sending."}""");
        Assert.False(no.Ok);
        Assert.False(no.Waiting);
        Assert.Equal("The map has another model named pg_a: rename it before sending.", no.Error);
        Assert.Equal("Groundwork refused it: The map has another model named pg_a: rename it before sending.", no.Text);

        Assert.Throws<InboxException>(() => GroundworkInbox.ReadAnswer("[1]"));
        Assert.Throws<InboxException>(() => GroundworkInbox.ReadAnswer("{\"ok\":"));
    }

    [Fact]
    public async Task TheAnswerIsWaitedForAndFoundWhenGroundworkWritesIt()
    {
        var inbox = new GroundworkInbox(_data);
        string id = Deliver(inbox);
        string results = Path.Combine(Inbox, ".results");

        Task<InboxAnswer> waiting = inbox.WaitForAnswerAsync(id, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        Directory.CreateDirectory(results);
        File.WriteAllText(Path.Combine(results, id + ".json"), """{"ok":true,"map":"Isle","models":["pg_a"]}""");

        InboxAnswer answer = await waiting;
        Assert.True(answer.Ok);
        Assert.Equal("Isle", answer.Map);
    }

    [Fact]
    public async Task NoAnswerInTimeMeansWaitingInTheInbox()
    {
        var inbox = new GroundworkInbox(_data);
        string id = Deliver(inbox);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        InboxAnswer answer = await inbox.WaitForAnswerAsync(id, TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);

        Assert.True(answer.Waiting);
        Assert.False(answer.Ok);
        Assert.Equal("Waiting in Groundwork's inbox (open a map in Groundwork)", answer.Text);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"waited {clock.Elapsed}");
    }

    [Fact]
    public async Task AnErrorAnswerIsReturnedAndAHalfWrittenOneIsReadAgain()
    {
        var inbox = new GroundworkInbox(_data);
        string id = Deliver(inbox);
        string results = Path.Combine(Inbox, ".results");
        Directory.CreateDirectory(results);
        File.WriteAllText(Path.Combine(results, id + ".json"), "{\"ok\":fal");

        Task<InboxAnswer> waiting = inbox.WaitForAnswerAsync(id, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(results, id + ".json"), """{"ok":false,"error":"send.json is not version 1 of the inbox."}""");

        InboxAnswer answer = await waiting;
        Assert.False(answer.Ok);
        Assert.False(answer.Waiting);
        Assert.Equal("send.json is not version 1 of the inbox.", answer.Error);
    }

    [Fact]
    public async Task WaitingCanBeStopped()
    {
        var inbox = new GroundworkInbox(_data);
        string id = Deliver(inbox);
        using var stop = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inbox.WaitForAnswerAsync(id, TimeSpan.FromSeconds(30), stop.Token));
    }

    [Fact]
    public void AGlbThatPointsAtFilesBesideItIsRefused()
    {
        using InboxDelivery d = new GroundworkInbox(_data).Begin();
        InboxException e = Assert.Throws<InboxException>(() => d.Add(new InboxKind("pg_a", "A", "Rocks"), MakeGlb("""{"asset":{"version":"2.0"},"images":[{"uri":"bark.png"}]}""")));
        Assert.Contains("bark.png", e.Message, StringComparison.Ordinal);
        Assert.Throws<InboxException>(() => d.Add(new InboxKind("pg_b", "B", "Rocks"), MakeGlb("""{"asset":{"version":"2.0"},"buffers":[{"uri":"x.bin","byteLength":4}]}""")));
        Assert.Throws<InboxException>(() => d.Add(new InboxKind("pg_c", "C", "Rocks"), MakeGlb("""{"asset":{"version":"2.0"},"images":[{"mimeType":"image/png"}]}""")));

        // Inside it, in a buffer view or as a data: URI, is fine.
        d.Add(new InboxKind("pg_d", "D", "Rocks"), MakeGlb("""{"asset":{"version":"2.0"},"images":[{"bufferView":0,"mimeType":"image/png"}],"buffers":[{"byteLength":4}]}"""));
        d.Add(new InboxKind("pg_e", "E", "Rocks"), MakeGlb("""{"asset":{"version":"2.0"},"images":[{"uri":"data:image/png;base64,AAAA"}]}"""));
        Assert.Empty(Directory.GetFiles(d.PartialFolder, "pg_[abc].glb"));
    }

    [Fact]
    public void TheFloorTilesGlbHoldsItsPicture()
    {
        byte[] rgba = new byte[16 * 16 * 4];
        foreach (Mesh m in new[] { TileMesh.Square(), TileMesh.Triangle() })
        {
            byte[] glb = GlbExport.ToBytes(m, rgba, 16, "pg_x_floor_tile");
            Assert.Equal(1, GlbContents.Images(glb));
            Assert.Null(GlbContents.NotSelfContained(glb));
        }
    }

    private static byte[] MakeGlb(string json)
    {
        byte[] text = System.Text.Encoding.UTF8.GetBytes(json);
        int padded = (text.Length + 3) & ~3;
        byte[] glb = new byte[20 + padded];
        "glTF"u8.CopyTo(glb);
        BitConverter.GetBytes(2).CopyTo(glb, 4);
        BitConverter.GetBytes(glb.Length).CopyTo(glb, 8);
        BitConverter.GetBytes(padded).CopyTo(glb, 12);
        "JSON"u8.CopyTo(glb.AsSpan(16));
        text.CopyTo(glb, 20);
        glb.AsSpan(20 + text.Length).Fill((byte)' ');
        return glb;
    }

    private static string Deliver(GroundworkInbox inbox)
    {
        using InboxDelivery d = inbox.Begin();
        d.Add(new InboxKind("pg_a", "A", "Rocks"), Glb);
        d.Complete();
        return d.Id;
    }
}
