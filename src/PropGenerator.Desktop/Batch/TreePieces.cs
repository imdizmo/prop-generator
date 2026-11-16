// Adapted from easy-props frontend/src/store/editor-store.ts (makeTreePieces) and frontend/src/panels/Details.tsx
// (canMakePieces) (MIT, © 2026 AHS12).
using PropGenerator.Desktop.Cards;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Pictures;
using PropGenerator.Desktop.Props;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Engine;

namespace PropGenerator.Desktop.Batch;

/// <summary>
/// Make pieces from this tree (upstream's <c>makePieces</c>): its stump, a fallen log, a fallen branch and its leaves on
/// the ground, each saved as a prop of its own beside the tree, named after it ("oak stump"), with a picture each. The
/// command is offered from the recipe alone (a painted tree), with no engine call, since menus ask often. Which pieces
/// the tree makes is asked on the engine thread when the card opens; the pieces are made one kind per job on the
/// engine's export turn (E13); the scene, the saves and the pictures are made off the window's thread. The tree's own
/// file is left as it is.
/// </summary>
internal sealed class TreePieces : IDisposable
{
    /// <summary>The kind pieces are made from (upstream's <c>canMakePieces</c>).</summary>
    internal const string TreeRecipe = "painted-tree";

    // The style every kind is built in (as EngineRecipe's).
    private const string Style = "low-poly";

    // The pictures' side, as the drawer's.
    private const int PictureSize = 128;

    private readonly MainWindow _w;
    private readonly CancellationTokenSource _stop = new();

    // The window's closing, read without the source (which is let go when the window closes).
    private readonly CancellationToken _closing;

    /// <summary>Works on the window's tree.</summary>
    /// <param name="window">The window.</param>
    internal TreePieces(MainWindow window)
    {
        _w = window;
        _closing = _stop.Token;
    }

    /// <summary>Gets the engine calls a test puts in place of easy-props' engine, or null for the engine.</summary>
    internal static AsyncLocal<PieceEngine?> EngineForTests { get; } = new();

    /// <summary>Gets a test's hook, told each step done off the window's thread (offered, scene, save, picture).</summary>
    internal static AsyncLocal<Action<string>?> StepForTests { get; } = new();

    /// <summary>Gets the card while it is open, else null.</summary>
    internal PiecesCard? Card { get; private set; }

    /// <summary>Gets the task drawing the last pieces' pictures (done when they are, or none were saved).</summary>
    internal Task Thumbnails { get; private set; } = Task.CompletedTask;

    /// <summary>The tree's name as pieces are named after it (upstream's <c>treeName</c>).</summary>
    /// <param name="fileName">The work file's name, or the name an untitled tree was given; null for neither.</param>
    /// <param name="startingPoint">The name of the starting point the tree was made from, or null.</param>
    /// <returns>The file's name, else the starting point's, else "tree".</returns>
    internal static string NameOf(string? fileName, string? startingPoint) =>
        !string.IsNullOrWhiteSpace(fileName) ? Path.GetFileNameWithoutExtension(fileName)
        : !string.IsNullOrWhiteSpace(startingPoint) ? startingPoint
        : "tree";

    /// <summary>Why pieces cannot be made of the prop, or null when they can: from the recipe alone, no engine call.</summary>
    /// <returns>The reason.</returns>
    internal string? WhyNoPieces() => _w.Files.Recipe == TreeRecipe ? null : "Only a painted tree makes pieces";

    /// <summary>The open tree's name: its file's, else its starting point's, else "tree".</summary>
    /// <returns>The name.</returns>
    internal string TreeName()
    {
        string? file = _w.Files.FilePath ?? (_w.Files.Name == WorkFile.DefaultName ? null : _w.Files.Name);
        string? point = _w.Files.StartedFrom is { } id ? MeshRecipes.Find(_w.Files.Recipe)?.StartingPoints.FirstOrDefault(p => p.Id == id)?.Name : null;
        return NameOf(file, point);
    }

    /// <summary>Stops what is still being made or drawn (the window is closing).</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    /// <summary>Opens the card on the tree as it is now, and asks the engine which pieces it makes.</summary>
    internal void Open()
    {
        if (WhyNoPieces() is not null || MeshRecipes.Find(TreeRecipe) is not EngineRecipe recipe)
        {
            return;
        }

        PieceEngine engine = Engine();
        EnginePieceLabels labels;
        try
        {
            labels = engine.Labels();
        }
        catch (Exception e) when (EngineFailed(e))
        {
            _w.Say($"Pieces cannot be made now: easy-props' engine did not answer ({e.Message}).", error: true);
            return;
        }

        Settings tree = _w.Panel.Current;
        string name = TreeName();
        string? folder = _w.Files.FilePath is { } path ? Path.GetDirectoryName(path) : null;
        var closing = CancellationTokenSource.CreateLinkedTokenSource(_closing);
        Task<string> scene = Task.Run(() => SceneOf(recipe, tree, "offered"), closing.Token);
        Task<IReadOnlyList<string>> offered = OfferedAsync(engine, scene, closing.Token);
        bool closed = false;
        PiecesCard? card = null;
        void Done()
        {
            if (!closed)
            {
                closed = true;
                closing.Cancel();
                closing.Dispose();
            }

            if (ReferenceEquals(Card, card))
            {
                Card = null;
            }
        }

        card = new PiecesCard(
            name,
            labels,
            offered,
            kinds => MakeAsync(engine, recipe, tree, name, folder, kinds),
            () =>
            {
                Done();
                if (ReferenceEquals(_w.Cards.Shown, card))
                {
                    _w.Cards.Close();
                }
            },
            first =>
            {
                Done();
                _w.Cards.Close();
                _ = _w.ShowPropsAsync(pick: first);
            });
        card.Detached += Done;
        Card = card;
        _w.Cards.Show(card, nearTop: true);
    }

    // The tree's scene, as upstream's pieces read it: its values and seed (and its own limb edits, which are values);
    // the part edits are not read by makePieces, so they are left out.
    private static string SceneOf(EngineRecipe recipe, Settings tree, string step)
    {
        StepForTests.Value?.Invoke(step);
        return recipe.SceneJson(tree.Without(SettingsPanel.PartEditsKey));
    }

    private static async Task<IReadOnlyList<string>> OfferedAsync(PieceEngine engine, Task<string> scene, CancellationToken cancel) =>
        await engine.Offered(await scene.ConfigureAwait(false), cancel).ConfigureAwait(false);

    // A failure easy-props' engine (or the disk) is expected to give.
    private static bool EngineFailed(Exception e) =>
        e is EngineException or TimeoutException or ObjectDisposedException or OperationCanceledException or IOException or UnauthorizedAccessException or WorkFileException or ArgumentException or System.Text.Json.JsonException;

    private static PieceEngine Engine() => EngineForTests.Value ?? new PieceEngine(
        () => EngineHost.Default.Rules.PieceLabels(),
        EngineHost.Default.Builds.PiecesOfferedAsync,
        (scene, treeName, kind, cancel) => EngineHost.Default.Builds.MakePieceAsync(scene, treeName, kind, Style, cancel));

    // Upstream's makeTreePieces: each kind made (one job each), saved as its own prop, then listed; the pictures follow.
    private async Task<PiecesMade> MakeAsync(PieceEngine engine, EngineRecipe recipe, Settings tree, string treeName, string? folder, IReadOnlyList<string> kinds)
    {
        folder ??= _w.Files.Prefs.SaveFolder is { } saved && Directory.Exists(saved)
            ? saved
            : await _w.Files.Dialogs.AskFolderAsync("Choose where to save the pieces", null).ConfigureAwait(true);
        if (folder is null)
        {
            return new PiecesMade([], "no folder was chosen to save them in.");
        }

        CancellationToken stop = _closing;
        var made = new List<(SavedPiece Piece, EngineRecipe Recipe, Settings Settings)>();
        string? error = null;
        try
        {
            string scene = await Task.Run(() => SceneOf(recipe, tree, "scene"), stop).ConfigureAwait(true);
            foreach (string kind in kinds)
            {
                EnginePiece piece = await engine.Make(scene, treeName, kind, stop).ConfigureAwait(true);
                string where = folder;
                (string path, EngineRecipe kindRecipe, Settings settings) = await Task.Run(() => Save(piece, where, treeName), stop).ConfigureAwait(true);
                _w.Props.Add(path);
                made.Add((new SavedPiece(kind, Path.GetFileNameWithoutExtension(path), path), kindRecipe, settings));
            }
        }
        catch (Exception e) when (EngineFailed(e))
        {
            if (stop.IsCancellationRequested)
            {
                return new PiecesMade([.. made.Select(m => m.Piece)], null);
            }

            error = e.Message;
            TraceLog.Write($"Make pieces: stopped after {made.Count} of {kinds.Count}: {e.Message}");
        }

        if (made.Count > 0)
        {
            _w.Drawer.RefreshMine();
            Thumbnails = DrawAsync(made, Thumbnails, stop);
        }

        return new PiecesMade([.. made.Select(m => m.Piece)], error);
    }

    // One piece as a prop: its kind's settings from the scene the engine made, saved under a free name beside the tree.
    private static (string Path, EngineRecipe Recipe, Settings Settings) Save(EnginePiece piece, string folder, string treeName)
    {
        StepForTests.Value?.Invoke("save");
        if (MeshRecipes.Find("tree-" + piece.Kind) is not EngineRecipe kind)
        {
            throw new ArgumentException($"There is no kind for a piece called {piece.Kind}.", nameof(piece));
        }

        Settings settings = kind.SettingsOf(piece.Scene);
        var history = new History();
        history.Push(treeName, settings);
        string path = YourProps.SaveNew(folder, piece.Name, new Piece(kind.Id, []), settings, history, kind.Engine);
        return (path, kind, settings);
    }

    // Each piece's picture, as the drawer draws a prop's (one at a time, on the engine's picture turn, off the window's thread).
    private async Task DrawAsync(IReadOnlyList<(SavedPiece Piece, EngineRecipe Recipe, Settings Settings)> made, Task before, CancellationToken stop)
    {
        await before.ConfigureAwait(true);
        ThumbCache thumbs = _w.Files.Thumbs;
        foreach ((SavedPiece piece, EngineRecipe recipe, Settings settings) in made)
        {
            try
            {
                await Task.Run(
                    () =>
                    {
                        StepForTests.Value?.Invoke("picture");
                        byte[] png = MeshThumb.Png(recipe.Build(settings, recipe.PictureDetail, BuildPurpose.Picture, stop), PictureSize);
                        thumbs.Put(ThumbCache.FileKey(piece.Path), png);
                    },
                    stop).ConfigureAwait(true);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                if (stop.IsCancellationRequested)
                {
                    return;
                }

                TraceLog.Write($"Make pieces: no picture of {piece.Name}: {e.Message}");
            }
        }

        _w.Drawer.RefreshMine();
    }

    /// <summary>The engine calls Make pieces needs (easy-props' engine, or a test's).</summary>
    /// <param name="Labels">Each piece's name and help (the rules engine).</param>
    /// <param name="Offered">Which pieces a tree's scene makes (a job on the engine thread).</param>
    /// <param name="Make">One piece of a tree's scene: scene, tree name, kind (an export job).</param>
    internal sealed record PieceEngine(
        Func<EnginePieceLabels> Labels,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> Offered,
        Func<string, string, string, CancellationToken, Task<EnginePiece>> Make);
}
