using Avalonia.Controls;
using PropGenerator.Desktop.Backups;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

// The backup commands in the window: to a file, the daily folder, and the cloud against a store in memory with a fake
// keychain (never the network, never the real keychain).
public class BackupCommandsTests
{
    private static (MainWindow W, FakeDialogs Dialogs, Scratch S) Open(params string[] names)
    {
        Scratch s = Ui.NewScratch();
        var dialogs = new FakeDialogs();
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        foreach (string n in names)
        {
            w.Library.Add(YourPropsTests.SaveProp(s, n, YourPropsTests.Point("boulder")));
        }

        return (w, dialogs, s);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void BackUpToAFileAndRestoreItAfterAPropWasLost()
    {
        (MainWindow w, FakeDialogs dialogs, Scratch s) = Open("A", "B");
        dialogs.SaveFilePath = s.P("mine.zip");
        CommandListTests.Run(w, "backup-file");
        Assert.True(File.Exists(s.P("mine.zip")));
        Assert.StartsWith("prop-generator-backup-", dialogs.SaveFileSuggestions[0], StringComparison.Ordinal);
        Assert.StartsWith("Backed up 2 props", w.StatusLine.Text, StringComparison.Ordinal);

        File.Delete(s.P("props", "A.propgen"));
        dialogs.OpenFilePath = s.P("mine.zip");
        CommandListTests.Run(w, "restore-file");
        Assert.True(File.Exists(s.P("props", "A.propgen")));
        Assert.Equal("Restored 1 prop.", w.StatusLine.Text);

        CommandListTests.Run(w, "restore-file");
        Assert.Equal("Everything in that backup is already here.", w.StatusLine.Text);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ChoosingABackupFolderBacksUpAtOnceAndOnlyOnceADay()
    {
        (MainWindow w, FakeDialogs dialogs, Scratch s) = Open("A");
        Directory.CreateDirectory(s.P("daily"));
        dialogs.Folder = s.P("daily");
        CommandListTests.Run(w, "backup-folder");

        Assert.Equal(s.P("daily"), new Prefs(s.P("prefs.json")).BackupFolder);
        Assert.Single(Directory.GetFiles(s.P("daily")));
        Assert.Null(Done(w.Backups.RunDailyAsync()));
        Assert.Null(CommandListTests.Find(w, "backup-folder-off").Unavailable());
        CommandListTests.Run(w, "backup-folder-off");
        Assert.Null(new Prefs(s.P("prefs.json")).BackupFolder);
        Assert.Equal("The daily backup is off", CommandListTests.Find(w, "backup-folder-off").Unavailable());
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheDailyBackupIsWrittenOffTheWindowsThreadOneAtATime()
    {
        using Scratch s = Ui.NewScratch();
        var library = new Library(s.P("library.json"));
        library.Add(YourPropsTests.SaveProp(s, "A", YourPropsTests.Point("boulder")));
        Directory.CreateDirectory(s.P("daily"));
        var prefs = new Prefs(s.P("prefs.json")) { BackupFolder = s.P("daily") };
        var said = new List<string>();
        using var backups = new BackupActions(library, prefs, new FakeDialogs(), s.P("config"), (line, _) => said.Add(line));

        // Asked twice at once (a save right after start), it is made once, and neither call holds the window's thread.
        Task<string?> first = backups.RunDailyAsync();
        Task<string?> second = backups.RunDailyAsync();
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.NotNull(Done(first));
        Assert.Null(Done(second));
        Assert.Single(Directory.GetFiles(s.P("daily")));
        Assert.Empty(said);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheCloudIsSetUpWithKeysInTheKeychainThenBacksUpAndRestores()
    {
        (MainWindow w, _, Scratch s) = Open("A", "B");
        var store = new FakeS3("AKCLOUD", "s3cret", "my-bucket");
        var keychain = new FakeKeychain();
        w.Backups.Keychain = keychain;
        w.Backups.Http = store;
        Assert.Equal("Set up the cloud backup first (File, Cloud backup…)", CommandListTests.Find(w, "backup-cloud").Unavailable());

        CommandListTests.Run(w, "cloud-setup");
        CloudSetupCard card = Assert.IsType<CloudSetupCard>(w.Cards.Shown);
        card.Fill("r2", "https://acc.r2.cloudflarestorage.com", "my-bucket", "AKCLOUD", "s3cret");
        ClaudeKit.Run(card.CheckAsync());
        Assert.StartsWith("Connected.", card.Status, StringComparison.Ordinal);

        Assert.Equal(new CloudKeys("AKCLOUD", "s3cret"), keychain.Keys);
        string prefs = File.ReadAllText(s.P("prefs.json"));
        Assert.DoesNotContain("s3cret", prefs, StringComparison.Ordinal);
        Assert.DoesNotContain("AKCLOUD", prefs, StringComparison.Ordinal);
        Assert.Equal("my-bucket", new Prefs(s.P("prefs.json")).CloudBucket);

        CommandListTests.Run(w, "backup-cloud");
        Assert.StartsWith("Backed up 2 props to the cloud", w.StatusLine.Text, StringComparison.Ordinal);
        Assert.Single(store.Objects.Keys, k => k.StartsWith("prop-generator/prop-generator-backup-", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(w.ConfigFolder, "*.zip"));

        File.Delete(s.P("props", "B.propgen"));
        CommandListTests.Run(w, "restore-cloud");
        Assert.True(File.Exists(s.P("props", "B.propgen")));
        Assert.Equal("Restored 1 prop.", w.StatusLine.Text);

        // Wrong keys are said, and the old ones stay.
        card = Assert.IsType<CloudSetupCard>(OpenSetup(w));
        card.Fill("r2", "acc.r2.cloudflarestorage.com", "my-bucket", "AKCLOUD", "wrong");
        ClaudeKit.Run(card.CheckAsync());
        Assert.StartsWith("The storage didn't accept these keys", card.Status, StringComparison.Ordinal);
        Assert.Equal("s3cret", keychain.Keys!.SecretKey);

        CommandListTests.Run(w, "cloud-off");
        Assert.Null(keychain.Keys);
        Assert.Null(new Prefs(s.P("prefs.json")).CloudService);
        w.Close();
    }

    // Waits for a call, the window's thread pumped meanwhile, and gives its result.
    private static T Done<T>(Task<T> t)
    {
        ClaudeKit.Run(t);
        return t.Result;
    }

    private static Control? OpenSetup(MainWindow w)
    {
        CommandListTests.Run(w, "cloud-setup");
        return w.Cards.Shown;
    }
}
