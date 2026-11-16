using System.Text.Json;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

// My presets: the settings now saved as the user's own starting point, in the drawer, renamed and removed.
public class MyPresetsTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SaveAsMyPresetThenStartFromItRenameAndRemoveIt()
    {
        Scratch s = Ui.NewScratch();
        var dialogs = new FakeDialogs { Choice = SaveChoice.Discard };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(YourPropsTests.Point("boulder"))));
        string key = w.Panel.Specs.First(p => p.Type == "float").Key;
        double max = w.Panel.Specs.First(p => p.Key == key).Max!.Value;
        Assert.Null(w.Panel.SetFromOutside(key, JsonSerializer.SerializeToElement(max)));
        Settings mine = w.Panel.Current;

        dialogs.Texts.Enqueue("Big boulder");
        CommandListTests.Run(w, "save-preset");
        Assert.Contains("Saved \"Big boulder\" to My presets", w.RecentMessages);
        Assert.Contains("My presets 1", w.Drawer.TabNames);
        w.Drawer.ChooseTab("My presets");
        Assert.Equal(["Big boulder"], w.Drawer.VisibleTileNames);

        // Starting from it gives a new untitled piece with its settings; Restore to preset goes back to them.
        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(YourPropsTests.Point("short-lawn"))));
        ClaudeKit.Run(w.Drawer.ClickTileAsync("Big boulder"));
        Assert.Equal("cut-rock", w.Files.Recipe);
        Assert.Equal(mine, w.Panel.Current);
        Assert.Null(w.Files.FilePath);
        Assert.Null(w.Panel.SetFromOutside(key, JsonSerializer.SerializeToElement(w.Panel.Specs.First(p => p.Key == key).Min!.Value)));
        CommandListTests.Run(w, "restore-preset");
        Assert.Equal(mine, w.Panel.Current);

        // The menu reaches it too, and it is kept for the next start.
        Commands.Command start = Assert.Single(CommandListTests.Find(w, "my-presets").Children!);
        Assert.Equal("Big boulder", start.Label);
        var again = new UserPresets(s.P("presets.json"));
        Assert.Equal("Big boulder", Assert.Single(again.All).Name);

        dialogs.Texts.Enqueue("Huge boulder");
        CommandListTests.Run(w, Assert.Single(CommandListTests.Find(w, "rename-preset").Children!).Id);
        Assert.Equal(["Huge boulder"], w.Drawer.VisibleTileNames);
        CommandListTests.Run(w, Assert.Single(CommandListTests.Find(w, "remove-preset").Children!).Id);
        Assert.Empty(w.Drawer.VisibleTileNames);
        Assert.Contains("My presets 0", w.Drawer.TabNames);
        Assert.Equal("No presets of your own yet", CommandListTests.Find(w, "my-presets").Unavailable());
        w.Close();
    }

    [Fact]
    public void APresetsFileLargerThanTheLimitIsReadAsNone()
    {
        using var s = new Scratch();
        const string Json = "{\"presets\":[{\"id\":\"mine-1\",\"name\":\"Big\",\"recipe\":\"cut-rock\",\"settings\":{}}]}";
        ColourBookTests.WritePadded(s.P("presets.json"), Json, UserPresets.MaxBytes + 1);
        Assert.Empty(new UserPresets(s.P("presets.json")).All);

        ColourBookTests.WritePadded(s.P("presets.json"), Json, 1000);
        Assert.Equal("Big", Assert.Single(new UserPresets(s.P("presets.json")).All).Name);
    }
}
