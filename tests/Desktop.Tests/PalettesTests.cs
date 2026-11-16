using System.Text.Json;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

// Saved palettes: a kind's own-colour rows saved under a name and used again, by any kind with the same colours.
public class PalettesTests
{
    private static JsonElement J(object v) => JsonSerializer.SerializeToElement(v);

    private static string Hex(Settings s, string key)
    {
        Rgb c = s.GetColour(key);
        static int B(float x) => (int)Math.Round(x * 255);
        return $"#{B(c.R):x2}{B(c.G):x2}{B(c.B):x2}";
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ARocksOwnColoursAreSavedAppliedAsOneStepAndRemoved()
    {
        Scratch s = Ui.NewScratch();
        var dialogs = new FakeDialogs { Choice = SaveChoice.Discard };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        Assert.Equal("This kind has no own colours", CommandListTests.Find(w, "save-palette").Unavailable());
        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(YourPropsTests.Point("boulder"))));

        OwnColourGroup stone = Assert.Single(MeshRecipes.Find("cut-rock")!.OwnColourGroups);
        Assert.Equal(("stone", "Rocks"), (stone.Set, stone.Title));
        Assert.Equal("Choose My own colours first", CommandListTests.Find(w, "save-palette").Unavailable());
        Assert.Equal("No saved palette for this kind yet", CommandListTests.Find(w, "palettes").Unavailable());

        Assert.Null(w.Panel.SetFromOutside(stone.ChoiceKey, J("custom")));
        Assert.Null(w.Panel.SetFromOutside(stone.Keys[0], J("#112233")));
        dialogs.Texts.Enqueue("Moor");
        CommandListTests.Run(w, "save-palette");
        Assert.Contains("Saved \"Moor\"", w.RecentMessages);
        Colours.SavedPalette moor = Assert.Single(w.Colours.Palettes);
        Assert.Equal("#112233", moor.Colours[0]);

        // Another colour, and a named option again: the palette brings both back, as one undo step.
        Assert.Null(w.Panel.SetFromOutside(stone.Keys[0], J("#ffffff")));
        string named = w.Panel.Specs.Single(p => p.Key == stone.ChoiceKey).Options![0].Value;
        Assert.Null(w.Panel.SetFromOutside(stone.ChoiceKey, J(named)));
        Assert.Null(CommandListTests.Find(w, "palettes").Unavailable());
        Commands.Command use = Assert.Single(CommandListTests.Find(w, "palettes").Children!);
        Assert.Equal("Moor (Rocks)", use.Label);
        CommandListTests.Run(w, use.Id);
        Assert.Equal("custom", w.Panel.Current.GetString(stone.ChoiceKey));
        Assert.Equal("#112233", Hex(w.Panel.Current, stone.Keys[0]));
        Assert.Equal("Use palette Moor", w.Files.History.Steps[w.Files.History.Index].Label);

        // Saving under the same name updates it.
        Assert.Null(w.Panel.SetFromOutside(stone.Keys[0], J("#445566")));
        dialogs.Texts.Enqueue("moor");
        CommandListTests.Run(w, "save-palette");
        Assert.Contains("Updated \"Moor\"", w.RecentMessages);
        Assert.Equal("#445566", Assert.Single(w.Colours.Palettes).Colours[0]);

        // Grass has other colours: the rock's palette is not offered there.
        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(YourPropsTests.Point("short-lawn"))));
        Assert.Equal("No saved palette for this kind yet", CommandListTests.Find(w, "palettes").Unavailable());

        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(YourPropsTests.Point("boulder"))));
        CommandListTests.Run(w, Assert.Single(CommandListTests.Find(w, "remove-palette").Children!).Id);
        Assert.Empty(w.Colours.Palettes);
        Assert.Contains("Removed \"Moor\". Props using it keep their colours.", w.RecentMessages);
        w.Close();
    }
}
