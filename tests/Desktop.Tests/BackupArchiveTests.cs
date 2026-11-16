using System.IO.Compression;
using System.Text;
using PropGenerator.Desktop.Backups;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class BackupArchiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static Library Library(Scratch s, params string[] names)
    {
        var library = new Library(s.P("library.json"));
        foreach (string n in names)
        {
            library.Add(YourPropsTests.SaveProp(s, n, YourPropsTests.Point("boulder")));
        }

        return library;
    }

    [Fact]
    public void ABackupHoldsEveryPropAndTheExtrasAndRestoresNothingThatIsAlreadyThere()
    {
        using var s = new Scratch();
        Library library = Library(s, "A", "B");
        string zip = s.P("backup.zip");
        int written = BackupArchive.Write(zip, [.. library.List().Select(e => e.Path)], new Dictionary<string, byte[]> { ["colours.json"] = "{}"u8.ToArray() }, Now);
        Assert.Equal(2, written);

        using (ZipArchive z = ZipFile.OpenRead(zip))
        {
            Assert.Equal(["backup.json", "extras/colours.json", "props/A.propgen", "props/B.propgen"], z.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        }

        BackupRestore r = BackupArchive.Restore(zip, library, s.P("restored"));
        Assert.Equal((0, 2, 0), (r.Added, r.Same, r.Copies));
        Assert.Equal("{}", Encoding.UTF8.GetString(r.Extras["colours.json"]));
    }

    [Fact]
    public void RestorePutsMissingPropsBackAndKeepsAChangedOneAsASecondCopy()
    {
        using var s = new Scratch();
        Library library = Library(s, "A", "B");
        string a = s.P("props", "A.propgen"), b = s.P("props", "B.propgen");
        string zip = s.P("backup.zip");
        BackupArchive.Write(zip, [a, b], new Dictionary<string, byte[]>(), Now);
        File.Delete(a);
        library.Forget(a);
        File.AppendAllText(b, " ");

        BackupRestore r = BackupArchive.Restore(zip, library, s.P("restored"));

        Assert.Equal((1, 0, 1), (r.Added, r.Same, r.Copies));
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(s.P("props", "B (from backup).propgen")));
        Assert.EndsWith(" ", File.ReadAllText(b), StringComparison.Ordinal);
        Assert.Equal(3, library.List().Count);

        // A second restore finds both copies there.
        BackupRestore again = BackupArchive.Restore(zip, library, s.P("restored"));
        Assert.Equal((0, 2, 0), (again.Added, again.Same, again.Copies));
    }

    [Fact]
    public void APropFromAFolderThatCannotBeMadeComesBackInTheRestoredFolder()
    {
        using var s = new Scratch();
        Library library = Library(s, "A");
        string zip = s.P("backup.zip");
        BackupArchive.Write(zip, [s.P("props", "A.propgen")], new Dictionary<string, byte[]>(), Now);
        string text = ReadBackupJson(zip).Replace(s.P("props", "A.propgen"), "/proc/not-here/A.propgen", StringComparison.Ordinal);
        ReplaceBackupJson(zip, text);

        BackupRestore r = BackupArchive.Restore(zip, library, s.P("restored"));
        Assert.Equal(1, r.Added);
        Assert.True(File.Exists(s.P("restored", "A.propgen")));
    }

    // The manifest's "original" is only a hint: a tampered backup must not write outside the folders the library
    // already knows, through "..", or into a folder it would have to make.
    [Theory]
    [InlineData("outside")]
    [InlineData("dotdot")]
    [InlineData("missing")]
    [InlineData("unc")]
    public void AnOriginalOutsideTheKnownFoldersComesBackInTheRestoredFolderOnly(string how)
    {
        using var s = new Scratch();
        Library library = Library(s, "Known");
        string zip = s.P("backup.zip");
        BackupArchive.Write(zip, [s.P("props", "Known.propgen")], new Dictionary<string, byte[]>(), Now);
        Directory.CreateDirectory(s.P("elsewhere"));
        string original = how switch
        {
            "outside" => s.P("elsewhere", "Evil.propgen"),
            "dotdot" => s.P("props") + "/../elsewhere/Evil.propgen",
            "missing" => s.P("props", "made-up", "Evil.propgen"),
            _ => "//server/share/Evil.propgen",
        };
        ReplaceBackupJson(zip, ReadBackupJson(zip).Replace(s.P("props", "Known.propgen"), original, StringComparison.Ordinal));

        BackupRestore r = BackupArchive.Restore(zip, library, s.P("restored"), [s.P("props")]);

        Assert.Equal(1, r.Added);
        Assert.True(File.Exists(s.P("restored", "Evil.propgen")));
        Assert.False(File.Exists(s.P("elsewhere", "Evil.propgen")));
        Assert.False(Directory.Exists(s.P("props", "made-up")));
    }

    [Fact]
    public void AFileThatIsNotABackupOrIsTooNewOrSneaksOutIsRefused()
    {
        using var s = new Scratch();
        Library library = Library(s);
        File.WriteAllText(s.P("plain.zip"), "not a zip");
        Assert.Contains("isn't a Prop Generator backup", Assert.Throws<BackupException>(() => BackupArchive.Restore(s.P("plain.zip"), library, s.P("r"))).Message, StringComparison.Ordinal);

        string newer = s.P("newer.zip");
        using (ZipArchive z = ZipFile.Open(newer, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(z.CreateEntry("backup.json").Open());
            w.Write("{\"format\":2,\"files\":[]}");
        }

        Assert.Contains("newer Prop Generator", Assert.Throws<BackupException>(() => BackupArchive.Restore(newer, library, s.P("r"))).Message, StringComparison.Ordinal);

        string sneaky = s.P("sneaky.zip");
        using (ZipArchive z = ZipFile.Open(sneaky, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(z.CreateEntry("backup.json").Open()))
            {
                w.Write("{\"format\":1,\"files\":[{\"path\":\"props/../../evil.propgen\",\"original\":\"/x/evil.propgen\",\"sha256\":\"00\"}]}");
            }

            using var e = new StreamWriter(z.CreateEntry("props/../../evil.propgen").Open());
            e.Write("{}");
        }

        BackupRestore r = BackupArchive.Restore(sneaky, library, s.P("r"));
        Assert.Equal((0, 0, 0, 1), (r.Added, r.Same, r.Copies, r.Refused));
        Assert.False(File.Exists(s.P("evil.propgen")));
    }

    [Fact]
    public void TheDailyFolderBackupRunsOncePerDayAndKeepsTheNewest()
    {
        using var s = new Scratch();
        string folder = s.P("daily");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "mine");
        int made = 0;
        void Make(string path)
        {
            made++;
            File.WriteAllText(path, "zip");
        }

        DateTimeOffset day = Now;
        for (int i = 0; i < 4; i++)
        {
            Assert.NotNull(FolderBackup.RunIfDue(folder, 3, day, Make));
            Assert.Null(FolderBackup.RunIfDue(folder, 3, day.AddHours(1), Make));
            day = day.AddDays(1);
        }

        Assert.Equal(4, made);
        Assert.Equal(
            ["notes.txt", "prop-generator-backup-2026-11-13.zip", "prop-generator-backup-2026-11-14.zip", "prop-generator-backup-2026-11-15.zip"],
            Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(
            "prop-generator-backup-2026-11-16.zip",
            FolderBackup.FileName(new DateTimeOffset(2026, 11, 16, 23, 0, 0, TimeSpan.Zero)));
        Assert.Throws<DirectoryNotFoundException>(() => FolderBackup.RunIfDue(s.P("gone"), 3, day, Make));
    }

    [Fact]
    public void ABackupJsonLargerThanTheLimitIsRefusedBeforeItIsRead()
    {
        using var s = new Scratch();
        string zip = s.P("big-manifest.zip");
        using (ZipArchive z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(z.CreateEntry("backup.json").Open());
            w.Write("{\"format\":1,\"files\":[]");
            w.Write(new string(' ', (int)BackupArchive.MaxManifest));
            w.Write('}');
        }

        Assert.Contains("too large", Assert.Throws<BackupException>(() => BackupArchive.Restore(zip, Library(s), s.P("r"))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AManifestListingMoreThanTheMostPropsIsRefused()
    {
        using var s = new Scratch();
        string zip = s.P("many.zip");
        using (ZipArchive z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(z.CreateEntry("backup.json").Open());
            w.Write("{\"format\":1,\"files\":[");
            w.Write(string.Join(',', Enumerable.Repeat("{}", BackupArchive.MaxProps + 1)));
            w.Write("]}");
        }

        Assert.Contains("too large", Assert.Throws<BackupException>(() => BackupArchive.Restore(zip, Library(s), s.P("r"))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABackupWhosePropsTogetherAreLargerThanTheLimitIsRefused()
    {
        using var s = new Scratch();
        Library library = Library(s, "A", "B");
        string a = s.P("props", "A.propgen"), b = s.P("props", "B.propgen");
        string zip = s.P("backup.zip");
        BackupArchive.Write(zip, [a, b], new Dictionary<string, byte[]>(), Now);
        File.Delete(a);
        File.Delete(b);

        BackupException e = Assert.Throws<BackupException>(() => BackupArchive.Restore(zip, library, s.P("restored"), null, maxTotal: 10));
        Assert.Contains("too large", e.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(a));
        Assert.False(File.Exists(b));
    }

    [Fact]
    public void APropWhoseZipUnderstatesItsSizeComesBackCutToWhatItSays()
    {
        using var s = new Scratch();
        Library library = Library(s);
        string zip = s.P("liar.zip");
        string original = s.P("props", "Liar.propgen");
        using (ZipArchive z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(z.CreateEntry("backup.json").Open()))
            {
                w.Write($"{{\"format\":1,\"files\":[{{\"path\":\"props/Liar.propgen\",\"original\":\"{original}\",\"sha256\":\"00\"}}]}}");
            }

            using Stream p = z.CreateEntry("props/Liar.propgen", CompressionLevel.Optimal).Open();
            p.Write(new byte[1024 * 1024]);
        }

        // The zip says the prop is 10 bytes; it holds a megabyte.
        SayUncompressedSize(zip, "props/Liar.propgen", 10);

        // The limits are checked against the sizes the zip gives: .NET's zip reading never gives more than that.
        BackupRestore r = BackupArchive.Restore(zip, library, s.P("restored"), null, maxTotal: 1000);
        Assert.Equal(1, r.Added);
        Assert.Equal(10, new FileInfo(s.P("restored", "Liar.propgen")).Length);
    }

    [Fact]
    public void AManifestNamingOneEntryManyTimesRestoresItOnce()
    {
        using var s = new Scratch();
        Library library = Library(s);
        string zip = s.P("again.zip");
        using (ZipArchive z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(z.CreateEntry("backup.json").Open()))
            {
                IEnumerable<string> items = Enumerable.Range(0, 1000)
                    .Select(i => $"{{\"path\":\"props/Big.propgen\",\"original\":\"/nowhere/Big{i}.propgen\",\"sha256\":\"00\"}}");
                w.Write($"{{\"format\":1,\"files\":[{string.Join(',', items)}]}}");
            }

            using Stream p = z.CreateEntry("props/Big.propgen").Open();
            p.Write(new byte[100]);
        }

        // The zip's props come to 100 bytes, under the limit; written 1000 times they would come to 100 000.
        BackupRestore r = BackupArchive.Restore(zip, library, s.P("restored"), null, maxTotal: 10_000);
        Assert.Equal((1, 999), (r.Added, r.Refused));
        Assert.Single(Directory.GetFiles(s.P("restored")));
    }

    [Fact]
    public void OnlyTheAppsOwnListsComeBackAsExtras()
    {
        using var s = new Scratch();
        string zip = s.P("backup.zip");
        BackupArchive.Write(zip, [], new Dictionary<string, byte[]> { ["colours.json"] = "{}"u8.ToArray(), ["huge-other.json"] = "[]"u8.ToArray() }, Now);

        BackupRestore r = BackupArchive.Restore(zip, Library(s), s.P("restored"));
        Assert.Equal(["colours.json"], r.Extras.Keys);
    }

    // Rewrites the uncompressed size an entry's local header and central directory record give.
    private static void SayUncompressedSize(string zip, string name, uint size)
    {
        byte[] bytes = File.ReadAllBytes(zip);
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        for (int i = 0; i + 46 < bytes.Length; i++)
        {
            uint signature = BitConverter.ToUInt32(bytes, i);
            int nameAt = signature switch { 0x04034b50 => 30, 0x02014b50 => 46, _ => -1 };
            int sizeAt = signature == 0x04034b50 ? 22 : 24;
            if (nameAt > 0 && bytes.AsSpan(i + nameAt).StartsWith(nameBytes))
            {
                BitConverter.TryWriteBytes(bytes.AsSpan(i + sizeAt), size);
            }
        }

        File.WriteAllBytes(zip, bytes);
    }

    private static string ReadBackupJson(string zip)
    {
        using ZipArchive z = ZipFile.OpenRead(zip);
        using var r = new StreamReader(z.GetEntry("backup.json")!.Open());
        return r.ReadToEnd();
    }

    private static void ReplaceBackupJson(string zip, string text)
    {
        using ZipArchive z = ZipFile.Open(zip, ZipArchiveMode.Update);
        z.GetEntry("backup.json")!.Delete();
        using var w = new StreamWriter(z.CreateEntry("backup.json").Open());
        w.Write(text);
    }
}
