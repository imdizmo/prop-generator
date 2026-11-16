using Avalonia.Controls;
using Avalonia.Interactivity;
using PropGenerator.Desktop.Props;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class PropsPageTests
{
    private static (MainWindow W, FakeDialogs Dialogs, Scratch S) Open(params (string Name, string Point)[] props)
    {
        Scratch s = Ui.NewScratch();
        var dialogs = new FakeDialogs();
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        foreach ((string name, string point) in props)
        {
            w.Library.Add(YourPropsTests.SaveProp(s, name, YourPropsTests.Point(point)));
        }

        return (w, dialogs, s);
    }

    private static PropsPage Page(MainWindow w)
    {
        CommandListTests.Run(w, "my-props");
        return Assert.IsType<PropsPage>(w.Cards.Shown);
    }

    private static void Do(PropsPage page, string action)
    {
        ClaudeKit.Run(page.RunAsync(action));
        Ui.Pump();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MyPropsOpensThePageWithEveryPropAndThePickedOnesDetails()
    {
        (MainWindow w, _, _) = Open(("Old boulder", "boulder"), ("Yard", "short-lawn"));
        PropsPage page = Page(w);

        Assert.Equal(["Yard", "Old boulder"], page.Names);
        page.Pick("Old boulder");
        Assert.Contains("Rock, from Boulder", page.Details, StringComparison.Ordinal);
        Assert.Contains("Seed", page.Details, StringComparison.Ordinal);

        page.Search("yar");
        Assert.Equal(["Yard"], page.Names);
        page.Search(string.Empty);
        page.SortBy(PropSort.Name);
        Assert.Equal(["Old boulder", "Yard"], page.Names);
        Assert.Equal(PropSort.Name, new Prefs(w.Files.Prefs.FilePath).PropsSort);
        page.ShowKind("Grass");
        Assert.Equal(["Yard"], page.Names);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ThePropsAreReadOffTheWindowsThreadBeforeThePageShows()
    {
        (MainWindow w, _, _) = Open(("Old boulder", "boulder"), ("Yard", "short-lawn"));
        Task shown = w.ShowPropsAsync();
        Assert.False(shown.IsCompleted);
        Assert.IsNotType<PropsPage>(w.Cards.Shown);

        ClaudeKit.Run(shown);
        PropsPage page = Assert.IsType<PropsPage>(w.Cards.Shown);
        Assert.Equal(["Yard", "Old boulder"], page.Names);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void OpenFromThePageOpensThePropAndClosesThePage()
    {
        (MainWindow w, _, _) = Open(("Old boulder", "boulder"));
        PropsPage page = Page(w);
        page.Pick("Old boulder");
        Button open = Ui.Named<Button>(page, "Open");
        open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Wait(100);

        Assert.Null(w.Cards.Shown);
        Assert.Equal("Old boulder.propgen", w.Files.Name);
        Assert.Equal("cut-rock", w.Files.Recipe);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void CopyRenameAndRemoveSeveralWithUndo()
    {
        (MainWindow w, FakeDialogs dialogs, _) = Open(("A", "boulder"), ("B", "boulder"), ("C", "short-lawn"));
        PropsPage page = Page(w);

        page.Pick("A");
        Do(page, "Copy");
        Assert.Contains("A copy", page.Names);
        Assert.Equal("Copied as \"A copy\"", w.StatusLine.Text);

        page.Pick("B");
        dialogs.Texts.Enqueue("Bee");
        Do(page, "Rename…");
        Assert.Contains("Bee", page.Names);
        Assert.Equal("Renamed to \"Bee\"", w.StatusLine.Text);

        page.Tick("A");
        page.Tick("C");
        Assert.Contains("2 props", page.Details, StringComparison.Ordinal);
        Do(page, "Remove");
        Assert.DoesNotContain("A", page.Names);
        Assert.DoesNotContain("C", page.Names);
        Assert.Equal("Removed 2 props", w.StatusLine.Text);
        Assert.Equal("Recently removed (2)", page.BinLabel);

        // Undo (on the page, or the command) puts both back.
        Assert.Null(CommandListTests.Find(w, "undo-remove").Unavailable());
        Do(page, "Undo");
        Assert.Contains("A", page.Names);
        Assert.Contains("C", page.Names);
        Assert.Equal("Nothing was removed", CommandListTests.Find(w, "undo-remove").Unavailable());
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RecentlyRemovedPutsBackAndRemovesForGood()
    {
        (MainWindow w, FakeDialogs dialogs, _) = Open(("A", "boulder"), ("B", "boulder"), ("C", "boulder"));
        PropsPage page = Page(w);
        foreach (string n in new[] { "A", "B", "C" })
        {
            page.Pick(n);
            Do(page, "Remove");
        }

        Do(page, "Recently removed");
        Assert.True(page.ShowingBin);
        Assert.Equal(["C", "B", "A"], page.Names);

        page.Pick("B");
        Do(page, "Put back");
        Assert.Equal(["C", "A"], page.Names);
        Assert.Equal("Put back \"B\"", w.StatusLine.Text);

        page.Pick("C");
        dialogs.Yes = false;
        Do(page, "Remove for good");
        Assert.Equal(["C", "A"], page.Names);
        dialogs.Yes = true;
        Do(page, "Remove for good");
        Assert.Equal(["A"], page.Names);
        Assert.Contains("for good? This can't be undone.", dialogs.Questions[^1], StringComparison.Ordinal);

        Do(page, "Empty the bin");
        Assert.Empty(page.Names);
        Do(page, "Back to Your props");
        Assert.Equal(["B"], page.Names);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheOpenPropIsNotRemovedAndExportWritesTheOthers()
    {
        (MainWindow w, FakeDialogs dialogs, Scratch s) = Open(("A", "boulder"), ("B", "short-lawn"));
        ClaudeKit.Run(w.Files.OpenPathAsync(s.P("props", "A.propgen")));
        PropsPage page = Page(w);

        page.Pick("A");
        Do(page, "Remove");
        Assert.Contains("A", page.Names);
        Assert.Contains(w.RecentMessages, m => m.Contains("working on", StringComparison.Ordinal));

        string map = s.P("map");
        Directory.CreateDirectory(map);
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        dialogs.Folder = map;
        page.Tick("A");
        page.Tick("B");
        Do(page, "Export…");
        Assert.True(w.RecentMessages.Any(m => m.StartsWith("Exported 2 props", StringComparison.Ordinal)), string.Join(" | ", w.RecentMessages));
        Assert.Contains("pg_a_rock", File.ReadAllText(Path.Combine(map, "catalog.json")), StringComparison.Ordinal);
        w.Close();
    }
}
