using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

public class CommandPaletteTests
{
    private static void Key(MainWindow w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPressQwerty(key, mods);
        Ui.Pump();
    }

    private static MainWindow Rock()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Run(w.Files.StartFromAsync(RockRecipe.StartingPoints[0]));
        return w;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EmptyQueryShowsRecentThenSuggested()
    {
        MainWindow w = Ui.Open(new FakePainter());
        w.Palette.Open();
        Ui.Pump();
        Assert.True(w.Palette.IsOpen);
        Assert.Equal(
            [("Suggested", "Export…"), ("Suggested", "Surprise me"), ("Suggested", "Save")],
            w.Palette.Rows);

        // A command run moves up to Recent, and out of Suggested.
        Run(w.Palette.ChooseAsync(1));
        Assert.False(w.Palette.IsOpen);
        w.Palette.Open();
        Assert.Equal(
            [("Recent", "Surprise me"), ("Suggested", "Export…"), ("Suggested", "Save")],
            w.Palette.Rows);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void PrefixesLimitTheSourcesInTheWindow()
    {
        MainWindow w = Rock();
        w.Palette.Open();
        w.Palette.Type(">");
        Assert.All(w.Palette.Rows, r => Assert.Equal("Actions", r.Section));
        Assert.DoesNotContain(w.Palette.Rows, r => r.Label == "Search or run a command");
        Assert.Contains(w.Palette.Rows, r => r.Label == "Export…");
        w.Palette.Type("#");
        Assert.Equal(w.Panel.Rows.Count + 1, w.Palette.Rows.Count);
        Assert.All(w.Palette.Rows, r => Assert.Equal("Controls", r.Section));
        w.Palette.Type("@");
        Assert.Equal(w.StartingPoints.Count, w.Palette.Rows.Count);
        Assert.All(w.Palette.Rows, r => Assert.Equal("Presets", r.Section));
        w.Palette.Type("zzzzzz");
        Assert.Empty(w.Palette.Rows);

        // The best match leads: a control called Stone beats the passing mentions in the actions.
        w.Palette.Type("stone");
        Assert.Equal(("Controls", "Stone"), w.Palette.Rows[0]);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AChoiceOpensItsOptions()
    {
        MainWindow w = Rock();
        int steps = w.Files.History.Steps.Count;
        w.Palette.Open();
        w.Palette.Type("#stone");
        Assert.Equal(("Controls", "Stone"), w.Palette.Rows[0]);
        Run(w.Palette.ChooseAsync(0));
        Assert.True(w.Palette.IsOpen);
        Assert.Equal("Stone", w.Palette.SubList);
        Assert.Equal(["Lavender", "Slate", "Granite", "Sandstone", "Redrock", "Basalt", "Frost", "Greystone", "My own colours"], w.Palette.Rows.Select(r => r.Label));
        Assert.All(w.Palette.Rows, r => Assert.Equal("Choose one", r.Section));

        // Typing narrows the options; Enter sets the top one, as one step of the history.
        w.Palette.Type("gran");
        Assert.Equal(["Granite"], w.Palette.Rows.Select(r => r.Label));
        Key(w, PhysicalKey.Enter);
        Assert.False(w.Palette.IsOpen);
        Assert.Equal("granite", w.Panel.Current.GetString("palette"));
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal("Stone Granite", w.Files.History.Steps[^1].Label);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ANumberControlIsRevealed()
    {
        MainWindow w = Ui.Open(new FakePainter());
        w.Height = 260;
        Ui.Pump();
        ParamSpec spec = w.Panel.Specs.Last(s => s.Type == "float");
        w.Palette.Open();
        w.Palette.Type("#" + spec.Label);
        int row = w.Palette.Rows.ToList().IndexOf(("Controls", spec.Label));
        Assert.True(row >= 0);
        Run(w.Palette.ChooseAsync(row));
        Ui.Pump();

        Assert.False(w.Palette.IsOpen);
        Assert.Equal(Look.Theme.SurfaceSelected, w.Panel.RowFor(spec.Key)!.Background);
        Assert.Same(w.Panel.ValueBoxFor(spec.Key), w.FocusManager!.GetFocusedElement());
        ScrollViewer scroller = Assert.IsType<ScrollViewer>(w.Panel.Parent);
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "the test window must be too short for the panel");
        Assert.True(scroller.Offset.Y > 0);

        // Lit for 1.4 s, then back.
        Ui.Wait(1700);
        Assert.Null(w.Panel.RowFor(spec.Key)!.Background);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnUnavailableEntryShowsItsReason()
    {
        MainWindow w = Ui.Open(new FakePainter());
        w.Palette.Open();
        w.Palette.Type("copy part");
        Assert.Equal(("Actions", "Copy part"), w.Palette.Rows[0]);
        Run(w.Palette.ChooseAsync(0));
        Assert.True(w.Palette.IsOpen);
        Assert.Equal("This kind of piece has no parts to pick", w.Palette.Notice);

        // Typing again brings the key hints back.
        w.Palette.Type("copy");
        Assert.Null(w.Palette.Notice);

        // Beside an available entry, the greyed one comes last (Show me more matches "seeds").
        w.Palette.Type("seed");
        List<string> actions = [.. w.Palette.Rows.Where(r => r.Section == "Actions").Select(r => r.Label)];
        Assert.True(actions.IndexOf("Copy seed") < actions.IndexOf("Show me more"), string.Join(", ", actions));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void KeysMoveRunAndGoBack()
    {
        MainWindow w = Rock();
        Key(w, PhysicalKey.K, RawInputModifiers.Control);
        Assert.True(w.Palette.IsOpen);
        w.Palette.Type(">");
        int last = w.Palette.Rows.Count - 1;

        // Up and Down stop at the ends.
        Key(w, PhysicalKey.ArrowUp);
        Assert.Equal(0, w.Palette.Active);
        Key(w, PhysicalKey.ArrowDown);
        Key(w, PhysicalKey.ArrowDown);
        Assert.Equal(2, w.Palette.Active);
        for (int i = 0; i < last + 5; i++)
        {
            Key(w, PhysicalKey.ArrowDown);
        }

        Assert.Equal(last, w.Palette.Active);

        // Enter opens a choice; Backspace on an empty box goes back; Esc goes back, then closes.
        w.Palette.Type("#stone");
        Key(w, PhysicalKey.Enter);
        Assert.Equal("Stone", w.Palette.SubList);
        Key(w, PhysicalKey.Backspace);
        Assert.Null(w.Palette.SubList);
        Assert.True(w.Palette.IsOpen);
        w.Palette.Type("#stone");
        Key(w, PhysicalKey.Enter);
        Assert.Equal("Stone", w.Palette.SubList);
        Key(w, PhysicalKey.Escape);
        Assert.Null(w.Palette.SubList);
        Assert.True(w.Palette.IsOpen);
        Key(w, PhysicalKey.Escape);
        Assert.False(w.Palette.IsOpen);

        // Enter runs the top row and closes; Ctrl+K opens again and a second Ctrl+K closes.
        Key(w, PhysicalKey.K, RawInputModifiers.Control);
        w.Palette.Type("shading");
        Key(w, PhysicalKey.Enter);
        Assert.True(w.Palette.IsOpen);
        Assert.Equal("Shading", w.Palette.SubList);
        Key(w, PhysicalKey.Escape);
        Key(w, PhysicalKey.K, RawInputModifiers.Control);
        Assert.False(w.Palette.IsOpen);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheTopBarButtonOpensItAndAClickOnARowRunsIt()
    {
        MainWindow w = Ui.Open(new FakePainter());
        w.TopBar.Palette.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
        Assert.True(w.Palette.IsOpen);
        Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(w.Palette.Box) as string));
        w.Palette.Type("show stats");
        Run(w.Palette.ChooseAsync(0));
        Assert.True(w.ViewState.ShowStats);
        Assert.False(w.Palette.IsOpen);
        w.Close();
    }

    [Fact]
    public void RecentKeepsFiveAndUsageIsCapped()
    {
        using Scratch s = Ui.NewScratch();
        string path = s.P("prefs.json");
        var prefs = new Prefs(path);
        foreach (string id in new[] { "a", "b", "c", "d", "e", "f" })
        {
            prefs.NotePaletteUsed(id);
        }

        Assert.Equal(["f", "e", "d", "c", "b"], prefs.PaletteRecent);

        // Running one again moves it to the front without a repeat.
        prefs.NotePaletteUsed("c");
        Assert.Equal(["c", "f", "e", "d", "b"], prefs.PaletteRecent);
        for (int i = 0; i < 30; i++)
        {
            prefs.NotePaletteUsed("a");
        }

        Assert.Equal(20, prefs.PaletteUsage["a"]);
        Assert.Equal(2, prefs.PaletteUsage["c"]);
        prefs.Save();
        var again = new Prefs(path);
        Assert.Equal(prefs.PaletteRecent, again.PaletteRecent);
        Assert.Equal(20, again.PaletteUsage["a"]);
    }
}
