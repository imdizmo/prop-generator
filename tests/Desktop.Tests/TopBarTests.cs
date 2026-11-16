using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Menus;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class TopBarTests
{
    private static void Click(Button b)
    {
        b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
    }

    private static string Tip(Control c) => ToolTip.GetTip(c) as string ?? string.Empty;

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheOldButtonRowIsGone()
    {
        MainWindow w = Ui.Open(new FakePainter());
        string[] old = ["New", "Open…", "Save as…", "Undo", "Redo", "Export to Groundwork", "Send to Blender", "Back to the piece", "Map folder…", "⚙"];
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<Button>(), b => old.Contains(AutomationProperties.GetName(b)));
        // The app's name shows once, in the app mark at the left (the design's top bar), not as the old title label.
        TextBlock name = Assert.Single(w.TopBar.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Prop Generator", StringComparison.Ordinal) == true);
        Assert.Contains(w.TopBar.Brand, name.GetVisualAncestors());

        // Left to right: the brand, the menus, the name, the palette, Save and Export.
        Assert.Equal(["File", "Edit", "View", "Prop", "Help"], w.TopBar.Menus.Select(m => m.Title));
        Assert.Equal(Look.Theme.MenuBarHeight, w.TopBar.Height);
        double x(Control c) => c.TranslatePoint(default, w.TopBar)!.Value.X;
        Assert.True(x(w.TopBar.Brand) < x(w.TopBar.Menus[0]));
        Assert.True(x(w.TopBar.Menus[^1]) < x(w.TopBar.Prop));
        Assert.True(x(w.TopBar.Prop) < x(w.TopBar.Palette));
        Assert.True(x(w.TopBar.Palette) < x(w.TopBar.Save));
        Assert.True(x(w.TopBar.Save) < x(w.TopBar.Export));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AMenuShowsItsRowsAndAClickRunsOne()
    {
        MainWindow w = Ui.Open(new FakePainter());
        MenuButton view = w.TopBar.Menu("View");
        Click(view);
        Assert.True(view.IsOpen);
        Assert.Equal(
            [
                "Shading >", "Detail level >", "-", "Frame [F]", "Back to the piece ✗", "Camera angle >", "-", "Show stats",
                "Show a person for scale", "Show what you can walk into", "Wind in the view", "-", "Hide or show panels [Ctrl+\\]",
                "Maximize the view [Ctrl+Space]", "Show the drawer [Ctrl+G] ✓", "Dock the drawer on the left", "Reset layout",
            ],
            view.Lines);

        MenuRow stats = view.Rows.OfType<MenuRow>().Single(r => r.Label == "Show stats");
        Click(stats);
        Assert.True(w.ViewState.ShowStats);
        Assert.False(view.IsOpen);

        // A submenu opens beside its row; its rows carry a check on the current one. Flat and Wireframe are for rocks.
        Click(view);
        Click(view.Rows.OfType<MenuRow>().Single(r => r.Label == "Shading"));
        Assert.Equal(["Lit ✓", "Flat ✗", "Wireframe ✗", "Wind ✗"], view.SubLines);
        w.TopBar.CloseMenus();
        Ui.Pump();
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(Recipes.RockRecipe.StartingPoints.First(p => p.Id == "boulder"))));
        Ui.Pump();
        Click(view);
        MenuRow shading = view.Rows.OfType<MenuRow>().Single(r => r.Label == "Shading");
        Click(shading);
        Assert.Equal(["Lit ✓", "Flat", "Wireframe", "Wind ✗"], view.SubLines);
        Click(view.SubRows.Single(r => r.Label == "Wireframe"));
        Assert.Equal(Commands.ViewState.Wireframe, w.ViewState.Shading);
        Assert.False(view.IsOpen);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void HoveringAnotherMenuWhileOneIsOpenSwitches()
    {
        MainWindow w = Ui.Open(new FakePainter());
        MenuButton file = w.TopBar.Menu("File"), edit = w.TopBar.Menu("Edit");
        Point Middle(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;

        // Without one open, hovering opens nothing.
        w.MouseMove(Middle(edit));
        Ui.Pump();
        Assert.False(edit.IsOpen);

        w.MouseMove(Middle(file));
        Ui.Pump();
        Click(file);
        w.MouseMove(Middle(edit));
        Ui.Pump();
        Assert.True(edit.IsOpen);
        Assert.False(file.IsOpen);

        // A click outside closes it; Esc does too.
        w.MouseDown(new Point(600, 500), MouseButton.Left);
        w.MouseUp(new Point(600, 500), MouseButton.Left);
        Ui.Pump();
        Assert.False(edit.IsOpen);
        Click(file);
        w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Ui.Pump();
        Assert.False(file.IsOpen);

        // A press elsewhere on the bar closes it too (the name, say).
        Click(file);
        w.MouseDown(Middle(w.TopBar.Prop.Button), MouseButton.Left);
        w.MouseUp(Middle(w.TopBar.Prop.Button), MouseButton.Left);
        Ui.Pump();
        Assert.False(file.IsOpen);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void PropNameShowsTheUnsavedDot()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("oak.propgen") };
        MainWindow w = BridgeKit.Open(s, dialogs);
        PropName name = w.TopBar.Prop;
        Assert.Equal("untitled", name.Text);
        Assert.False(name.DotShown);

        Assert.Null(w.Panel.SetFromOutside("contrast", System.Text.Json.JsonSerializer.SerializeToElement(0.9)));
        Ui.Pump();
        Assert.True(name.DotShown);
        Assert.Equal(Look.Theme.Selection, name.Dot.Fill);

        FileActionsTests.Press(w, PhysicalKey.S);
        Ui.Pump();
        Assert.Equal("oak", name.Text);
        Assert.False(name.DotShown);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RenameAnUntitledPieceNamesItsFirstSave()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("chosen.propgen") };
        MainWindow w = BridgeKit.Open(s, dialogs);
        PropName name = w.TopBar.Prop;

        // Through the control: click, type, Enter.
        Click(name.Button);
        Assert.True(name.Editing);
        Assert.Equal(60, name.Box.MaxLength);
        name.Box.Text = "Mossy path";
        name.Box.Focus();
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Ui.Wait(50);
        Assert.False(name.Editing);
        Assert.Equal("Mossy path", name.Text);
        Assert.Equal("Mossy path.propgen", w.Files.Name);
        Assert.StartsWith("Mossy path.propgen", w.Title, StringComparison.Ordinal);
        Assert.Null(w.Files.FilePath);

        // Sent to Blender before any save, it goes under that name too.
        BridgeKit.RunCommand(w, "Send to Blender");
        Ui.Wait(1500);
        Assert.True(File.Exists(Path.Combine(BridgeKit.Outbox(s), "pg_mossy_path_floor_tile.glb")), w.StatusLine.Text);

        FileActionsTests.Press(w, PhysicalKey.S);
        Assert.Equal("Mossy path.propgen", dialogs.SaveSuggestions[^1]);

        // Esc leaves the name as it was.
        Click(name.Button);
        name.Box.Text = "Something else";
        name.Box.Focus();
        w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Ui.Pump();
        Assert.False(name.Editing);
        Assert.Equal("chosen", name.Text);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RenameASavedPieceMovesTheFile()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("oak.propgen") };
        MainWindow w = BridgeKit.Open(s, dialogs);
        FileActionsTests.Press(w, PhysicalKey.S);
        Assert.True(File.Exists(s.P("oak.propgen")));

        Assert.True(ClaudeKit.Run(w.Files.RenameAsync("pine")));

        Assert.False(File.Exists(s.P("oak.propgen")));
        Assert.True(File.Exists(s.P("pine.propgen")));
        Assert.Equal(s.P("pine.propgen"), w.Files.FilePath);
        Assert.Equal("pine", w.TopBar.Prop.Text);
        Assert.Equal(["pine"], new Library(s.P("library.json")).List().Select(e => e.Name));
        Assert.False(w.Files.Dirty);
        Assert.StartsWith("pine.propgen", w.Title, StringComparison.Ordinal);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RenamingWithUnsavedChangesShowsTheNewNameInMine()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("oak.propgen"), Choice = SaveChoice.Discard };
        MainWindow w = BridgeKit.Open(s, dialogs);
        FileActionsTests.Press(w, PhysicalKey.S);
        Assert.Null(w.Panel.SetFromOutside("contrast", System.Text.Json.JsonSerializer.SerializeToElement(0.9)));
        Ui.Pump();
        Assert.True(w.Files.Dirty);

        Assert.True(ClaudeKit.Run(w.Files.RenameAsync("pine")));
        w.Drawer.ChooseTab("Mine");

        Assert.Equal(["pine"], w.Drawer.VisibleTileNames);
        ClaudeKit.Run(w.Drawer.ClickTileAsync("pine"));
        Assert.Equal(s.P("pine.propgen"), w.Files.FilePath);
        Assert.False(w.Files.Dirty);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RenameToATakenNameIsRefused()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("oak.propgen") };
        MainWindow w = BridgeKit.Open(s, dialogs);
        FileActionsTests.Press(w, PhysicalKey.S);
        File.WriteAllText(s.P("pine.propgen"), "someone else's");

        Assert.False(ClaudeKit.Run(w.Files.RenameAsync("pine")));

        Assert.True(File.Exists(s.P("oak.propgen")));
        Assert.Equal("someone else's", File.ReadAllText(s.P("pine.propgen")));
        Assert.Equal(s.P("oak.propgen"), w.Files.FilePath);
        Assert.Equal("oak", w.TopBar.Prop.Text);
        Assert.Contains("pine.propgen", w.StatusLine.Text, StringComparison.Ordinal);
        Assert.Contains("already", w.StatusLine.Text, StringComparison.Ordinal);

        // A name no file can have is refused too, and an empty one changes nothing.
        Assert.False(ClaudeKit.Run(w.Files.RenameAsync("a/b")));
        Assert.False(ClaudeKit.Run(w.Files.RenameAsync("   ")));
        Assert.Equal(s.P("oak.propgen"), w.Files.FilePath);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EveryTopBarControlHasATooltip()
    {
        MainWindow w = Ui.Open(new FakePainter());
        TopBar bar = w.TopBar;
        Assert.Equal("Show every starting point (Ctrl+G)", Tip(bar.Brand));
        Assert.Equal("Save (Ctrl+S)", Tip(bar.Save));
        Assert.Equal("Export to a Groundwork map (Ctrl+E)", Tip(bar.Export));
        foreach (Control c in bar.GetVisualDescendants().OfType<Control>().Where(c => c.TemplatedParent is null && c is Button or TextBox))
        {
            Assert.False(string.IsNullOrWhiteSpace(Tip(c)), $"{c.GetType().Name} '{AutomationProperties.GetName(c)}' has no tooltip");
        }

        foreach (MenuButton menu in bar.Menus)
        {
            Assert.True(Tip(menu).Length >= 40, menu.Title);
            Click(menu);
            foreach (MenuRow row in menu.Rows.OfType<MenuRow>())
            {
                string? reason = row.Item.Unavailable();
                Assert.Equal(reason is null, row.IsEnabled);
                Assert.Equal(reason ?? row.Item.Tooltip, Tip(row));
                if (reason is null)
                {
                    Assert.True(Tip(row).Length >= 20, row.Label);
                }
                else
                {
                    // A greyed row says REF's reason, short as some are ("Nothing to redo").
                    Assert.True(ToolTip.GetShowOnDisabled(row));
                }

                if (row.HasChildren)
                {
                    Click(row);
                    Assert.All(menu.SubRows, r => Assert.True(Tip(r).Length >= 15, r.Label));
                }
            }

            menu.Close();
        }

        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheShortcutsAndAboutCardsOpenAndClose()
    {
        MainWindow w = Ui.Open(new FakePainter());
        FileActionsTests.Press(w, PhysicalKey.Slash);
        Assert.IsType<Cards.ShortcutsCard>(w.Cards.Shown);
        var lines = w.Cards.Shown!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Surprise me", lines);
        Assert.Contains("Ctrl+Space", lines);
        Assert.Contains("Maximize the view", lines);
        w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Ui.Pump();
        Assert.Null(w.Cards.Shown);

        CommandListTests.Run(w, "about");
        Assert.IsType<Cards.AboutCard>(w.Cards.Shown);
        string text = string.Join("\n", w.Cards.Shown!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Contains(BuildInfo.Version, text, StringComparison.Ordinal);
        Assert.Contains("easy-props", text, StringComparison.Ordinal);
        Assert.Contains("lucide", text, StringComparison.Ordinal);
        Click(w.Cards.Shown!.GetVisualDescendants().OfType<Button>().Single());
        Assert.Null(w.Cards.Shown);
        w.Close();
    }
}
