using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PropGenerator.Desktop.Batch;
using PropGenerator.Desktop.Cards;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Props;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// The Pieces card (Make pieces from this tree): a stump, a fallen log, a fallen branch and leaf litter made from the
/// open painted tree on easy-props' engine, each saved as a prop of its own beside the tree.
/// </summary>
public class PiecesCardTests
{
    private static readonly string[] Kinds = ["stump", "log", "branch", "litter"];

    private static bool Waited(Func<bool> done, int ms = 120000)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    private static MainWindow Start(string pointId, Scratch s, FakeDialogs? dialogs = null)
    {
        MainWindow w = Ui.Open(new FakePainter(), dialogs ?? new FakeDialogs(), s);
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(YourPropsTests.Point(pointId))));
        return w;
    }

    // The tree started from a starting point and saved as name.propgen in the scratch folder.
    private static MainWindow Saved(string pointId, string name, Scratch s)
    {
        var dialogs = new FakeDialogs { SavePath = s.P(name + ".propgen") };
        MainWindow w = Start(pointId, s, dialogs);
        Assert.True(CommandListTests.RunAsync(w.Files.SaveAsAsync()));
        Assert.Equal(s.P(name + ".propgen"), w.Files.FilePath);
        return w;
    }

    private static PiecesCard Open(MainWindow w)
    {
        CommandListTests.Run(w, "make-pieces");
        PiecesCard card = Assert.IsType<PiecesCard>(w.Cards.Shown);
        Assert.True(Waited(() => card.Offered.IsCompleted), "the engine never said which pieces the tree makes");
        Ui.Pump();
        return card;
    }

    private static void Make(PiecesCard card)
    {
        card.MakeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(Waited(() => card.Making.IsCompleted), "the pieces were never made");
        Ui.Pump();
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheCardOffersFourForOakAndThreeForAPalm()
    {
        using var s = new Scratch();
        MainWindow w = Start("oak", s);
        EnginePieceLabels labels = EngineHost.Default.Rules.PieceLabels();
        CommandListTests.Run(w, "make-pieces");
        PiecesCard card = Assert.IsType<PiecesCard>(w.Cards.Shown);

        // Until the engine answers, every row says it is checking and nothing can be made.
        if (!card.Offered.IsCompleted)
        {
            Assert.All(Kinds, k => Assert.Equal("Checking…", card.HelpOf(k)));
            Assert.False(card.MakeButton.IsEnabled);
        }

        Assert.True(Waited(() => card.Offered.IsCompleted));
        Ui.Pump();
        Assert.Equal(Kinds, card.Kinds);
        foreach (string kind in Kinds)
        {
            Assert.Equal(labels.Of(kind)!.Name, card.NameOf(kind));
            Assert.Equal(labels.Of(kind)!.Help, card.HelpOf(kind));
            Assert.True(card.Row(kind).IsEnabled, kind);
            Assert.True(card.Row(kind).IsChecked, kind);
        }

        Assert.True(card.MakeButton.IsEnabled);
        Assert.Equal("Make 4 pieces", card.MakeLabel);
        Assert.Contains("make them again after changing it", card.Hint, StringComparison.Ordinal);

        // Unticking one makes one fewer.
        card.Row("litter").IsChecked = false;
        Ui.Pump();
        Assert.Equal("Make 3 pieces", card.MakeLabel);
        Click(card.CancelButton);
        Assert.Null(w.Cards.Shown);

        // A palm has no big branches to drop: its branch is unticked and greyed, with upstream's reason.
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(YourPropsTests.Point("coconut-palm-painted"))));
        PiecesCard palm = Open(w);
        Assert.False(palm.Row("branch").IsEnabled);
        Assert.False(palm.Row("branch").IsChecked);
        Assert.Equal("This tree has no big branches to drop", palm.HelpOf("branch"));
        Assert.All(["stump", "log", "litter"], k => Assert.True(palm.Row(k).IsEnabled && palm.Row(k).IsChecked == true, k));
        Assert.Equal("Make 3 pieces", palm.MakeLabel);
        palm.Row("log").IsChecked = false;
        palm.Row("litter").IsChecked = false;
        Ui.Pump();
        Assert.Equal("Make 1 piece", palm.MakeLabel);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MakingPiecesSavesEachAsItsOwnProp()
    {
        using var s = new Scratch();
        MainWindow w = Saved("oak", "oak", s);

        // A prop called "oak stump" is there already: the new stump takes a free name beside it, and the old one is kept.
        File.WriteAllText(s.P("oak stump.propgen"), "not a work file");
        PiecesCard card = Open(w);
        Make(card);

        string[] names = ["oak stump 2", "oak fallen log", "oak fallen branch", "oak leaf litter"];
        Assert.Equal("4 pieces saved", card.Title);
        Assert.Equal(names, card.SavedNames);
        Assert.Equal("not a work file", File.ReadAllText(s.P("oak stump.propgen")));
        foreach ((string name, string kind) in names.Zip(Kinds))
        {
            string path = s.P(name + ".propgen");
            Assert.True(File.Exists(path), name);
            (Piece piece, Settings settings, History history) = WorkFile.Load(path);
            Assert.Equal("tree-" + kind, piece.Recipe);
            Assert.Equal("oak", settings.GetString("treeName"));
            Assert.False(string.IsNullOrEmpty(settings.GetString("tree")), $"{name} lost its tree");
            Assert.Equal("oak", history.Steps[0].Label);

            // Each is in Your props, as a prop of its own kind.
            PropSummary summary = Assert.Single(w.Props.List(name), p => p.Name == name);
            Assert.Equal(EngineKit.Recipe("tree-" + kind).Label, summary.KindLabel);
        }

        // Each has its picture, for the drawer and Your props.
        Assert.True(Waited(() => w.Pieces.Thumbnails.IsCompleted));
        Assert.All(names, n => Assert.NotNull(w.Files.Thumbs.Get(Pictures.ThumbCache.FileKey(s.P(n + ".propgen")))));
        Assert.Null(w.LastError);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnUntitledTreeNamesPiecesAfterItsStartingPoint()
    {
        using var s = new Scratch();
        string folder = s.P("pieces");
        Directory.CreateDirectory(folder);
        var dialogs = new FakeDialogs { Folder = folder };
        MainWindow w = Start("oak", s, dialogs);
        StartingPoint oak = YourPropsTests.Point("oak");
        Assert.Null(w.Files.FilePath);
        Assert.Equal(oak.Name, w.Pieces.TreeName());

        PiecesCard card = Open(w);
        foreach (string kind in Kinds.Skip(1))
        {
            card.Row(kind).IsChecked = false;
        }

        Ui.Pump();
        Make(card);

        // Untitled, with no folder saved to before: the card asks where the pieces go.
        Assert.Equal(1, dialogs.FolderAsked);
        Assert.Equal("Piece saved", card.Title);
        Assert.Equal([$"{oak.Name} stump"], card.SavedNames);
        Assert.True(File.Exists(Path.Combine(folder, $"{oak.Name} stump.propgen")));
        Assert.Equal(oak.Name, WorkFile.Load(Path.Combine(folder, $"{oak.Name} stump.propgen")).Settings.GetString("treeName"));
        w.Close();

        // With no starting point and no file, the tree is "tree" (upstream's treeName).
        Assert.Equal("tree", TreePieces.NameOf(null, null));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MakingPiecesNeverBlocksTheUiThread()
    {
        using var s = new Scratch();
        MainWindow w = Saved("oak", "oak", s);
        int ui = Environment.CurrentManagedThreadId;
        var steps = new ConcurrentQueue<(string Step, int Thread)>();
        TreePieces.StepForTests.Value = step => steps.Enqueue((step, Environment.CurrentManagedThreadId));
        try
        {
            CommandListTests.Run(w, "make-pieces");
            var card = (PiecesCard)w.Cards.Shown!;
            Assert.True(Waited(() => card.Offered.IsCompleted));
            Ui.Pump();

            long queued = EngineHost.Default.Builds.JobsQueued(EngineJobKind.Export);
            var clock = Stopwatch.StartNew();
            card.MakeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            long returned = clock.ElapsedMilliseconds;
            Assert.True(returned < 500, $"Make held the UI thread {returned} ms");
            Assert.False(card.Making.IsCompleted, "the pieces were made on the UI thread");
            Assert.True(card.Busy);
            Assert.Equal("Making pieces…", card.MakeLabel);
            Assert.False(card.MakeButton.IsEnabled);
            Assert.False(card.CancelButton.IsEnabled);

            // Busy, the card stays: neither Esc nor its own close takes it away.
            Assert.False(w.Cards.CloseUnlessBusy());
            Assert.Same(card, w.Cards.Shown);

            // One kind per job on the engine's export turn; the window keeps answering meanwhile.
            Assert.True(Waited(() => EngineHost.Default.Builds.JobsQueued(EngineJobKind.Export) > queued, 30000));
            bool answered = false;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => answered = true);
            Assert.True(Waited(() => answered, 5000), "the window did not answer while the pieces were made");
            Assert.True(Waited(() => card.Making.IsCompleted));
            Assert.True(Waited(() => w.Pieces.Thumbnails.IsCompleted));
            Assert.True(EngineHost.Default.Builds.JobsQueued(EngineJobKind.Export) >= queued + 4);
        }
        finally
        {
            TreePieces.StepForTests.Value = null;
        }

        // Which pieces the tree makes, the tree's scene, each save and each picture: none on the UI thread.
        string[] seen = [.. steps.Select(x => x.Step).Distinct()];
        Assert.Equal(["offered", "scene", "save", "picture"], seen);
        Assert.Equal(4, steps.Count(x => x.Step == "save"));
        Assert.Equal(4, steps.Count(x => x.Step == "picture"));
        Assert.All(steps, x => Assert.NotEqual(ui, x.Thread));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheMenuNeverAsksTheEngine()
    {
        using var s = new Scratch();
        MainWindow w = Start("oak", s);
        int asked = 0;
        TreePieces.EngineForTests.Value = new TreePieces.PieceEngine(
            () =>
            {
                Interlocked.Increment(ref asked);
                return EngineHost.Default.Rules.PieceLabels();
            },
            (_, _) =>
            {
                Interlocked.Increment(ref asked);
                return Task.FromResult<IReadOnlyList<string>>(Kinds);
            },
            (_, _, _, _) =>
            {
                Interlocked.Increment(ref asked);
                return Task.FromException<EnginePiece>(new InvalidOperationException("not asked"));
            });
        try
        {
            Commands.Command command = CommandListTests.Find(w, "make-pieces");
            Assert.Equal("Make pieces from this tree…", command.Label);
            Assert.Equal("Prop", command.Group);
            Assert.Contains("make-pieces", Commands.CommandList.Menus.Single(m => m.Title == "Prop").Items);
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 500; i++)
            {
                Assert.Null(command.Unavailable());
                w.PanelHead.Refresh();
            }

            Assert.True(clock.ElapsedMilliseconds < 2000, $"500 asks took {clock.ElapsedMilliseconds} ms");
            Assert.True(w.PanelHead.Pieces.IsVisible);
            Assert.Equal(0, asked);

            // Opening the card is what asks.
            CommandListTests.Run(w, "make-pieces");
            Assert.True(Waited(() => asked >= 2, 10000));
        }
        finally
        {
            TreePieces.EngineForTests.Value = null;
        }

        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void GoToYourPropsSelectsTheFirst()
    {
        using var s = new Scratch();
        MainWindow w = Saved("oak", "oak", s);
        PiecesCard card = Open(w);
        card.Row("stump").IsChecked = false;
        Ui.Pump();
        Make(card);
        Assert.Equal(["oak fallen log", "oak fallen branch", "oak leaf litter"], card.SavedNames);

        Click(card.GoToPropsButton);
        Assert.True(Waited(() => w.Cards.Shown is PropsPage));
        var page = (PropsPage)w.Cards.Shown!;
        Assert.Equal(s.P("oak fallen log.propgen"), page.Picked);
        Assert.Contains("oak fallen log", page.Details, StringComparison.Ordinal);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheTreesFileIsUnchanged()
    {
        using var s = new Scratch();
        MainWindow w = Saved("oak", "oak", s);
        string path = s.P("oak.propgen");
        byte[] before = File.ReadAllBytes(path);
        DateTime written = File.GetLastWriteTimeUtc(path);
        Settings settings = w.Panel.Current;
        int steps = w.Files.History.Steps.Count;

        PiecesCard card = Open(w);
        Make(card);
        Assert.Equal(4, card.SavedNames!.Count);
        Click(card.StayButton);
        Assert.Null(w.Cards.Shown);
        Assert.True(Waited(() => w.Pieces.Thumbnails.IsCompleted));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
        Assert.Equal(path, w.Files.FilePath);
        Assert.Equal("painted-tree", w.Files.Recipe);
        Assert.Equal(settings, w.Panel.Current);
        Assert.Equal(steps, w.Files.History.Steps.Count);
        Assert.False(w.Files.Dirty);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheCommandIsUnavailableForABush()
    {
        using var s = new Scratch();
        MainWindow w = Start("round-bush", s);
        Assert.Equal("Only a painted tree makes pieces", CommandListTests.Find(w, "make-pieces").Unavailable());
        Assert.False(w.PanelHead.Pieces.IsVisible);
        CommandListTests.Run(w, "make-pieces");
        Assert.Null(w.Cards.Shown);

        // The floor and an old-style tree make none either; a painted tree does.
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(MeshRecipes.Find("branching-tree")!.StartingPoints[0])));
        Assert.Equal("Only a painted tree makes pieces", CommandListTests.Find(w, "make-pieces").Unavailable());
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(YourPropsTests.Point("oak"))));
        Assert.Null(CommandListTests.Find(w, "make-pieces").Unavailable());
        Assert.True(w.PanelHead.Pieces.IsVisible);
        w.Close();

        MainWindow floor = Ui.Open(new FakePainter());
        Assert.Equal("Only a painted tree makes pieces", CommandListTests.Find(floor, "make-pieces").Unavailable());
        Assert.False(floor.PanelHead.Pieces.IsVisible);
        floor.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnEngineErrorLeavesTheCardUsable()
    {
        using var s = new Scratch();
        MainWindow w = Saved("oak", "oak", s);
        int made = 0;
        TreePieces.EngineForTests.Value = new TreePieces.PieceEngine(
            () => EngineHost.Default.Rules.PieceLabels(),
            EngineHost.Default.Builds.PiecesOfferedAsync,
            (_, _, _, _) =>
            {
                made++;
                return Task.FromException<EnginePiece>(new EngineScriptException("makePieces broke"));
            });
        try
        {
            PiecesCard card = Open(w);
            Make(card);
            Assert.Equal(1, made);
            Assert.Null(card.SavedNames);
            Assert.False(card.Busy);
            Assert.Contains("makePieces broke", card.Note, StringComparison.Ordinal);
            Assert.StartsWith("No pieces were made", card.Note, StringComparison.Ordinal);
            Assert.True(card.MakeButton.IsEnabled);
            Assert.True(card.CancelButton.IsEnabled);
            Assert.Equal("Make 4 pieces", card.MakeLabel);
            Assert.All(Kinds, k => Assert.True(card.Row(k).IsEnabled, k));
            Assert.Equal(["oak.propgen"], Directory.GetFiles(s.Dir, "*.propgen").Select(Path.GetFileName));

            // It can be tried again, and closed.
            Make(card);
            Assert.Equal(2, made);
            Click(card.CancelButton);
            Assert.Null(w.Cards.Shown);

            // An engine that cannot say which pieces the tree makes: the card says so, Make stays grey, Cancel closes it.
            TreePieces.EngineForTests.Value = new TreePieces.PieceEngine(
                () => EngineHost.Default.Rules.PieceLabels(),
                (_, _) => Task.FromException<IReadOnlyList<string>>(new TimeoutException("the engine took too long")),
                (_, _, _, _) => Task.FromException<EnginePiece>(new InvalidOperationException("not asked")));
            PiecesCard stuck = Open(w);
            Assert.Contains("the engine took too long", stuck.Note, StringComparison.Ordinal);
            Assert.False(stuck.MakeButton.IsEnabled);
            Assert.True(stuck.CancelButton.IsEnabled);
            Click(stuck.CancelButton);
            Assert.Null(w.Cards.Shown);
        }
        finally
        {
            TreePieces.EngineForTests.Value = null;
        }

        w.Close();
    }
}
