using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;
using NatureHash = PropGenerator.Nature.Random.Hash;

namespace PropGenerator.Desktop.Tests;

public class CommandListTests
{
    private static StartingPoint Boulder => RockRecipe.StartingPoints.Single(p => p.Id == "boulder");

    internal static Command Find(MainWindow w, string id) =>
        CommandList.Find(w.Commands, id) ?? throw new InvalidOperationException($"no command '{id}'");

    internal static void Run(MainWindow w, string id)
    {
        ClaudeKit.Run(Find(w, id).Run());
        Ui.Pump();
    }

    private static void Key(MainWindow w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPressQwerty(key, mods);
        Ui.Pump();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void NewOldStylePalmStartsFromTheEnginesSpecs()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Run(w, "new-palm");
        Assert.Equal("palm-tree", w.Files.Recipe);
        Assert.Equal(EngineKit.Recipe("palm-tree").Specs, w.Panel.Specs);
        Assert.Equal(Settings.Defaults(EngineKit.Recipe("palm-tree").Specs), w.Panel.Current);
        Assert.Equal(("Old-style palm", EngineKinds.OldStyleTrees), (EngineRecipes.PalmDefaults.Name, EngineRecipes.PalmDefaults.Group));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MenusListREFsItemsInOrder()
    {
        MainWindow w = Ui.Open(new FakePainter());
        string[] Lines(string title)
        {
            var (_, items) = CommandList.Menus.Single(m => m.Title == title);
            return [.. items.Select(id => id is null ? "-" : Line(Find(w, id)))];
        }

        static string Line(Command c) => c.Label + (c.Keys is null ? string.Empty : $" [{c.Keys}]") + (c.Children is null ? string.Empty : " >");

        Assert.Equal(["File", "Edit", "View", "Prop", "Help"], CommandList.Menus.Select(m => m.Title));
        Assert.Equal(
            [
                "New floor tile [Ctrl+N]", "New rock", "New grass", "New cactus", "New tree", "-", "My props", "Recently removed", "Open a file… [Ctrl+O]", "-", "Save [Ctrl+S]",
                "Save a copy… [Ctrl+Shift+S]", "-", "Export… [Ctrl+E]", "Send to Groundwork", "Send to Blender", "Map folder…", "Save a picture…", "-", "Export many…", "-",
                "Back up to a file…", "Restore from a file…", "Daily backup folder…", "Stop the daily backup", "Cloud backup…",
                "Back up to the cloud", "Restore from the cloud", "Turn off the cloud backup", "-", "Settings [Ctrl+,]",
            ],
            Lines("File"));
        Assert.Equal(
            ["Undo [Ctrl+Z]", "Redo [Ctrl+Shift+Z]", "Undo remove", "-", "Copy part [Ctrl+D]", "Remove part [Delete]", "Restore this part", "Break branch", "-", "Clear selection [Esc]"],
            Lines("Edit"));
        Assert.Equal(
            [
                "Shading >", "Detail level >", "-", "Frame [F]", "Back to the piece", "Camera angle >", "-", "Show stats",
                "Show a person for scale", "Show what you can walk into", "Wind in the view", "-", "Hide or show panels [Ctrl+\\]",
                "Maximize the view [Ctrl+Space]", "Show the drawer [Ctrl+G]", "Dock the drawer on the left", "Reset layout",
            ],
            Lines("View"));
        Assert.Equal(
            [
                "Surprise me [R]", "Show me more", "Make pieces from this tree…", "Use a seed…", "Copy seed", "Restore to preset", "-", "Save as my preset…", "Start from my preset >",
                "Rename my preset >", "Remove my preset >", "-", "Save colours as a palette…", "Use a palette >", "Remove a palette >", "-",
                "Style: Normal [1]", "Style: Low-poly [2]",
            ],
            Lines("Prop"));
        Assert.Equal(["Search or run a command [Ctrl+K]", "Keyboard shortcuts [Ctrl+/]", "-", "Report a problem…", "About Prop Generator"], Lines("Help"));

        Assert.Equal(["Lit", "Flat", "Wireframe", "Wind"], Find(w, "shading").Children!.Select(c => c.Label));
        Assert.Equal(["Perspective", "Front", "Side", "Top"], Find(w, "camera-angle").Children!.Select(c => c.Label));

        // The floor offers only its full detail; a rock all three levels.
        Assert.Equal(["Level 0 (LOD0)"], Find(w, "lod").Children!.Select(c => c.Label));
        Assert.True(RunAsync(w.Files.StartFromAsync(Boulder)));
        Assert.Equal(["Level 0 (LOD0)", "Level 1 (LOD1)", "Level 2 (LOD2)"], Find(w, "lod").Children!.Select(c => c.Label));

        // The tools and the shading cycle have keys but no menu.
        Assert.Equal(("Q", "G", "E", "S", "Z"), (Find(w, "select-tool").Keys, Find(w, "move-tool").Keys, Find(w, "turn-tool").Keys, Find(w, "resize-tool").Keys, Find(w, "next-shading").Keys));
        Assert.All(w.Commands.Where(c => c.Checked() is null && c.Children is null), c => Assert.NotNull(c.Tooltip));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void GreyedItemsGiveREFsReasons()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        const string Parts = "This kind of piece has no parts to pick";
        foreach ((string id, string reason) in new[]
        {
            ("export-many", "The floor tile has one version: change its seed instead"), ("undo", "Nothing to undo yet"), ("redo", "Nothing to redo"),
            ("copy-part", Parts), ("remove-part", Parts), ("restore-part", Parts), ("break-part", Parts), ("clear-selection", "Nothing is selected"),
            ("move-tool", Parts), ("turn-tool", Parts), ("resize-tool", Parts), ("shading-wind", "Comes with wind"),
            ("show-more", "The floor tile has one version: change its seed instead"),
            ("build-normal", "This kind has no low-poly style"),
            ("build-low-poly", "This kind has no low-poly style"),
            ("back-to-piece", "Nothing from Blender is shown"),
        })
        {
            Assert.Equal(reason, Find(w, id).Unavailable());
        }

        foreach (string id in new[] { "new-floor", "new-rock", "my-props", "open", "save", "save-copy", "export", "send-groundwork", "send-blender", "map-folder", "settings", "frame", "stats", "surprise", "restore-preset", "shortcuts", "about" })
        {
            Assert.Null(Find(w, id).Unavailable());
        }

        // A change can be undone, and then redone.
        Assert.Null(w.Panel.SetFromOutside("contrast", System.Text.Json.JsonSerializer.SerializeToElement(0.9)));
        Assert.Null(Find(w, "undo").Unavailable());
        Run(w, "undo");
        Assert.Null(Find(w, "redo").Unavailable());
        w.Close();

    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AGreyedShortcutSaysWhy()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Settings before = w.Panel.Current;
        int steps = w.Files.History.Steps.Count;

        Key(w, PhysicalKey.G);

        Assert.Equal("This kind of piece has no parts to pick", w.StatusLine.Text);
        Assert.Equal(before, w.Panel.Current);
        Assert.Equal(steps, w.Files.History.Steps.Count);

        // Run from anywhere (the palette, Claude), a greyed command only says why too.
        w.StatusLine.Text = string.Empty;
        Run(w, "show-more");
        Assert.Equal("The floor tile has one version: change its seed instead", w.StatusLine.Text);

        // Esc and Delete, greyed, are left to whatever has the focus: nothing is said and the key is not taken.
        w.StatusLine.Text = string.Empty;
        foreach (Avalonia.Input.Key k in new[] { Avalonia.Input.Key.Escape, Avalonia.Input.Key.Delete })
        {
            var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = k, Source = w };
            w.RaiseEvent(e);
            Assert.False(e.Handled, k.ToString());
        }

        Assert.Equal(string.Empty, w.StatusLine.Text);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EscStillClosesAnOpenDropDown()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.True(RunAsync(w.Files.StartFromAsync(Boulder)));
        ComboBox kind = Ui.Named<ComboBox>(w, "Kind of rock");
        kind.Focus();
        kind.IsDropDownOpen = true;
        Ui.Pump();
        Assert.True(kind.IsDropDownOpen);

        Key(w, PhysicalKey.Escape);

        Assert.False(kind.IsDropDownOpen);
        Assert.NotEqual("This kind of piece has no parts to pick", w.StatusLine.Text);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void LetterKeysDoNothingWhileTyping()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        Assert.True(RunAsync(w.Files.StartFromAsync(Boulder)));
        TextBox search = Ui.Named<TextBox>(w, "Search");
        search.Focus();
        Ui.Pump();
        Settings before = w.Panel.Current;
        int steps = w.Files.History.Steps.Count;
        string status = w.StatusLine.Text ?? string.Empty;

        foreach (PhysicalKey k in new[] { PhysicalKey.R, PhysicalKey.Z, PhysicalKey.Q, PhysicalKey.F, PhysicalKey.Digit1, PhysicalKey.Digit2, PhysicalKey.G, PhysicalKey.E, PhysicalKey.Delete, PhysicalKey.Escape })
        {
            Key(w, k);
        }

        Assert.Equal(before, w.Panel.Current);
        Assert.Equal(steps, w.Files.History.Steps.Count);
        Assert.Equal(ViewState.Lit, w.ViewState.Shading);
        Assert.Equal(status, w.StatusLine.Text);

        // Nor do keys with Ctrl: Ctrl+Z, Ctrl+S and Ctrl+G are the box's. Only the palette's key still works.
        Assert.Null(w.Panel.SetFromOutside("moss", System.Text.Json.JsonSerializer.SerializeToElement(0.1)));
        Settings changed = w.Panel.Current;
        search.Focus();
        Ui.Pump();
        Key(w, PhysicalKey.Z, RawInputModifiers.Control);
        Key(w, PhysicalKey.G, RawInputModifiers.Control);
        Assert.Equal(changed, w.Panel.Current);
        Assert.True(w.Drawer.IsOpen);
        Key(w, PhysicalKey.K, RawInputModifiers.Control);
        Assert.True(w.Palette.IsOpen);
        Key(w, PhysicalKey.Escape);
        Assert.False(w.Palette.IsOpen);

        // The same in the box of a value and in the rename box.
        foreach (TextBox box in new[] { w.Panel.ValueBoxFor("width"), w.TopBar.Prop.Box })
        {
            if (box == w.TopBar.Prop.Box)
            {
                w.TopBar.Prop.StartRename();
            }

            box.Focus();
            Ui.Pump();
            Assert.True(box.IsFocused);
            Key(w, PhysicalKey.Z, RawInputModifiers.Control);
            Assert.Equal(changed, w.Panel.Current);
        }

        // Out of the box, the same keys work.
        w.TopBar.Brand.Focus();
        Ui.Pump();
        Key(w, PhysicalKey.Z);
        Assert.Equal(ViewState.Flat, w.ViewState.Shading);
        Key(w, PhysicalKey.Digit2);
        Assert.Equal("low-poly", w.Panel.Current.GetString("build"));
        Key(w, PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal(changed, w.Panel.Current);
        Key(w, PhysicalKey.Z, RawInputModifiers.Control);
        Assert.NotEqual(changed, w.Panel.Current);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void CtrlGTogglesTheDrawerAndCtrlSpaceMaximizes()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        Assert.True(w.Drawer.IsOpen);

        Key(w, PhysicalKey.G, RawInputModifiers.Control);
        Assert.False(w.Drawer.IsOpen);
        Assert.False(w.ViewState.DrawerOpen);
        Assert.False(new Prefs(s.P("prefs.json")).DrawerOpen);
        Assert.True(Find(w, "drawer").Checked() == false);
        Key(w, PhysicalKey.G, RawInputModifiers.Control);
        Assert.True(w.Drawer.IsOpen);
        Assert.True(w.ViewState.DrawerOpen);

        Key(w, PhysicalKey.Space, RawInputModifiers.Control);
        Assert.True(w.ViewState.Maximized);
        Assert.False(w.SidePanel.IsVisible);
        Assert.False(w.StatusBar.IsVisible);
        Assert.False(w.Drawer.Card.IsVisible);
        Assert.True(w.Drawer.IsOpen);

        Key(w, PhysicalKey.Space, RawInputModifiers.Control);
        Assert.False(w.ViewState.Maximized);
        Assert.True(w.SidePanel.IsVisible);
        Assert.True(w.StatusBar.IsVisible);
        Assert.True(w.Drawer.Card.IsVisible);

        // Ctrl+\ hides the panel only; Reset layout brings it back.
        Key(w, PhysicalKey.Backslash, RawInputModifiers.Control);
        Assert.True(w.ViewState.PanelsHidden);
        Assert.False(w.SidePanel.IsVisible);
        Assert.True(w.StatusBar.IsVisible);
        Run(w, "reset-layout");
        Assert.False(w.ViewState.PanelsHidden);
        Assert.True(w.SidePanel.IsVisible);

        // Maximized, Ctrl+G brings the view back with the drawer shown, as My props does; closed, it opens.
        Key(w, PhysicalKey.Space, RawInputModifiers.Control);
        Key(w, PhysicalKey.G, RawInputModifiers.Control);
        Assert.False(w.ViewState.Maximized);
        Assert.True(w.Drawer.IsOpen);
        Assert.True(w.Drawer.Card.IsVisible);
        Key(w, PhysicalKey.G, RawInputModifiers.Control);
        Assert.False(w.Drawer.IsOpen);
        Key(w, PhysicalKey.Space, RawInputModifiers.Control);
        Key(w, PhysicalKey.G, RawInputModifiers.Control);
        Assert.False(w.ViewState.Maximized);
        Assert.True(w.Drawer.IsOpen);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AClosedDrawerStaysClosedFromTheOldPref()
    {
        using var s = new Scratch();
        File.WriteAllText(s.P("prefs.json"), "{\"drawerOpen\": false}");
        MainWindow w = BridgeKit.Open(s);
        Assert.False(w.ViewState.DrawerOpen);
        Assert.False(w.Drawer.IsOpen);

        // Opening it on a tab (the brand opens All) keeps the tab; My props opens Your props, not the drawer.
        w.ShowDrawer("Mine");
        Assert.True(w.Drawer.IsOpen);
        Assert.Equal("Mine", new Prefs(s.P("prefs.json")).DrawerTab);
        Run(w, "my-props");
        Assert.IsType<Props.PropsPage>(w.Cards.Shown);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SurpriseMeIsEasyPropsHashAndOneStep()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.True(RunAsync(w.Files.StartFromAsync(Boulder)));
        Assert.Null(w.Panel.SetFromOutside("seed", System.Text.Json.JsonSerializer.SerializeToElement(401)));
        Settings before = w.Panel.Current;
        int steps = w.Files.History.Steps.Count;

        Run(w, "surprise");

        uint expected = NatureHash.Seed(401, 402);
        Assert.Equal(expected, w.Panel.Current.GetSeed("seed"));
        Assert.Equal(before.With("seed", (long)expected), w.Panel.Current);
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal("Surprise me", w.Files.History.Steps[^1].Label);

        w.Files.Undo();
        Assert.Equal(401u, w.Panel.Current.GetSeed("seed"));

        // R does the same.
        Key(w, PhysicalKey.R);
        Assert.Equal(expected, w.Panel.Current.GetSeed("seed"));
        w.Close();

        // The floor's seed (a whole number up to 9999) stays inside its range.
        MainWindow floor = Ui.Open(new FakePainter());
        int seed = floor.Panel.Current.GetInt("seed");
        Run(floor, "surprise");
        Assert.Equal((int)(NatureHash.Seed(seed, seed + 1) % 10000), floor.Panel.Current.GetInt("seed"));
        Assert.Equal("Surprise me", floor.Files.History.Steps[^1].Label);
        floor.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RestoreToPresetIsOneStep()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.True(RunAsync(w.Files.StartFromAsync(Boulder)));
        Assert.Null(w.Panel.SetFromOutside("moss", System.Text.Json.JsonSerializer.SerializeToElement(0.1)));
        Assert.Null(w.Panel.SetFromOutside("seed", System.Text.Json.JsonSerializer.SerializeToElement(7)));
        Settings changed = w.Panel.Current;
        int steps = w.Files.History.Steps.Count;

        Run(w, "restore-preset");

        Assert.Equal(Boulder.Settings, w.Panel.Current);
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal("Restore to preset", w.Files.History.Steps[^1].Label);
        w.Files.Undo();
        Assert.Equal(changed, w.Panel.Current);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheFloorHasNoLowPolyStyle()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Settings before = w.Panel.Current;
        foreach (string id in new[] { "build-normal", "build-low-poly" })
        {
            Assert.Equal("This kind has no low-poly style", Find(w, id).Unavailable());
            w.StatusLine.Text = string.Empty;
            Run(w, id);
            Assert.Equal("This kind has no low-poly style", w.StatusLine.Text);
            Assert.False(Find(w, id).Checked());
        }

        Assert.Equal(before, w.Panel.Current);
        w.Close();
    }

    internal static bool RunAsync(Task<bool> t)
    {
        bool done = ClaudeKit.Run(t, 30000);
        Ui.Pump();
        return done;
    }
}
