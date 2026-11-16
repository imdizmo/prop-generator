using PropGenerator.Desktop.Pictures;
using PropGenerator.Desktop.Recipes;

namespace PropGenerator.Desktop;

/// <summary>
/// The piece being worked on (the entry) as a file: its recipe (the floor tile or a rock), its name, whether it has
/// unsaved changes, its undo history, and what New, starting from a preset, Open, Save, Save As, Undo, Redo, Export to
/// Groundwork and Send to Blender do. A new piece is untitled.propgen and is not on disk; saving it always asks for a
/// name. Every question goes through <see cref="IDialogs"/>.
/// </summary>
public sealed partial class FileActions
{
    private const string AppName = "Prop Generator";
    private const string ProblemStart = "Cannot export";

    private readonly SettingsPanel _panel;
    private readonly LiveRepaint _repaint;
    private readonly RockLive _rocks;
    private readonly IReadOnlyList<ParamSpec> _floorSpecs;
    private readonly Library _library;
    private readonly ExportDialog _export;
    private readonly Action<string, bool> _say;
    private Piece _piece = TileMesh.FloorPiece();
    private Settings _saved;
    private bool _exporting;
    private bool _sending;
    private int _asking;
    private string? UntitledName { get; set; }

    /// <summary>Starts with a new untitled floor showing the panel's settings (the panel holds the floor's list).</summary>
    /// <param name="panel">The settings.</param>
    /// <param name="repaint">Repaints the floor after undo, redo and opening.</param>
    /// <param name="rocks">Builds and shows the rock when a rock is the entry.</param>
    /// <param name="dialogs">Asks the user.</param>
    /// <param name="library">Notes the files opened and saved.</param>
    /// <param name="prefs">Remembers folders.</param>
    /// <param name="say">Shows a line in the status bar; the flag is true for a failure.</param>
    public FileActions(
        SettingsPanel panel, LiveRepaint repaint, RockLive rocks, IDialogs dialogs, Library library, Prefs prefs, Action<string, bool> say)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(repaint);
        ArgumentNullException.ThrowIfNull(rocks);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(say);
        _panel = panel;
        _repaint = repaint;
        _rocks = rocks;
        _floorSpecs = panel.Specs;
        Dialogs = dialogs;
        _library = library;
        Prefs = prefs;
        _say = say;
        _export = new ExportDialog(dialogs, prefs);
        _saved = panel.Current;
        History = Fresh(panel.Current);
        StartedFrom = FloorRecipe.StartingPoint(_floorSpecs).Id;
    }

    /// <summary>Raised when the floor is to be the view again: a floor entry started or opened, or Back to the piece.</summary>
    public event Action? FloorShown;

    /// <summary>
    /// Raised when an entry was started or opened, with its recipe ("floor_tile", "cut-rock" or "slab-cliff"), once it is shown; also
    /// when the recipe is the same as before.
    /// </summary>
    public event Action<string>? RecipeChanged;

    /// <summary>
    /// Raised as soon as an entry was started or opened, before it is painted or built: the panel already has its
    /// settings and <see cref="StartedFrom"/> is set, so the panel's header and resets can follow at once.
    /// </summary>
    public event Action? Entered;

    /// <summary>Gets the entry's recipe: "floor_tile" for the floor, "cut-rock" for a rock, "slab-cliff" for a cliff.</summary>
    public string Recipe => _piece.Recipe;

    /// <summary>Gets or sets where the picture of the view taken at every save is kept.</summary>
    internal ThumbCache Thumbs { get; set; } = new(ThumbCache.DefaultDir());

    /// <summary>
    /// Gets or sets what takes the picture of the view at every save (the OpenGL view's own; the tests give theirs).
    /// Null takes none.
    /// </summary>
    internal Func<Task<(byte[] Rgba, int Width, int Height)>>? Capture { get; set; }

    /// <summary>Raised when the saved work file was renamed (moved) on disk; the library already has its new path.</summary>
    public event Action? Renamed;

    /// <summary>Raised when the name or the unsaved mark may have changed.</summary>
    public event Action? Changed;

    /// <summary>
    /// Gets or sets what writes a prop's GLB and catalog entry into a map folder (<see cref="ExportDialog.WriteMesh"/>;
    /// the tests give theirs, to fail as a missing native library does).
    /// </summary>
    internal Func<string, string, Recipes.IMeshRecipe, Settings, Recipes.ExportOptions, string> WriteMesh { get; set; } = ExportDialog.WriteMesh;

    /// <summary>Gets or sets how long an export waits for the full-detail paint.</summary>
    internal TimeSpan FullPaintTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets what asks the user (the window's dialogs, or the tests' fake).</summary>
    internal IDialogs Dialogs { get; }

    /// <summary>Gets the remembered folders and choices.</summary>
    internal Prefs Prefs { get; }

    /// <summary>Gets the undo history.</summary>
    public History History { get; private set; }

    /// <summary>Gets a value indicating whether a question is open; commands are ignored until it is answered.</summary>
    public bool Asking => _asking > 0;

    /// <summary>
    /// Gets why another piece cannot be started or opened now (an export or a send to Blender is running and would
    /// write what it is waiting for), or null when it can.
    /// </summary>
    internal string? BusyReason => _exporting
        ? "An export is running: wait for it to finish before starting or opening another piece."
        : _sending ? "Sending to Blender: wait for it to finish before starting or opening another piece." : null;

    /// <summary>Gets the work file's full path, or null while the piece is untitled.</summary>
    internal string? FilePath { get; private set; }

    /// <summary>
    /// Gets the id of the starting point the entry was started from ("floor-v5" for the floor the app starts with), or
    /// null when the entry was opened from a file.
    /// </summary>
    internal string? StartedFrom { get; private set; }

    /// <summary>
    /// Gets the file name shown in the title: the work file's, or for a piece never saved untitled.propgen (or the name
    /// it was given, which its first save offers).
    /// </summary>
    public string Name => FilePath is null ? UntitledName ?? WorkFile.DefaultName : Path.GetFileName(FilePath);

    /// <summary>Gets where the piece stands against its work file: unsaved changes, never saved, or saved.</summary>
    public SaveState SaveState => Dirty ? SaveState.Unsaved : FilePath is null ? SaveState.NotSavedYet : SaveState.Saved;

    /// <summary>Gets a value indicating whether the settings differ from what was last saved (or started with).</summary>
    public bool Dirty => !_panel.Current.Equals(_saved);

    /// <summary>Gets the window title: the file name, a dot in front when there are unsaved changes.</summary>
    public string TitleText => $"{(Dirty ? "• " : string.Empty)}{Name} — {AppName}";

    /// <summary>Tells the window to refresh the title.</summary>
    public void Refresh() => Changed?.Invoke();

    /// <summary>A change was finished in the panel: one more step in the history.</summary>
    /// <param name="label">The setting's label.</param>
    public void OnCommitted(string label)
    {
        Settings now = _panel.Current;
        if (History.Index < 0 || !History.Steps[History.Index].Settings.Equals(now))
        {
            History.Push(_panel.DescribeChange(label), now);
        }

        Refresh();
    }

    /// <summary>Goes back one step (or back to the last step first, if something changed since it).</summary>
    public void Undo()
    {
        Settings? step = History.Index >= 0 ? History.Steps[History.Index].Settings : null;
        Settings? target = step is not null && !_panel.Current.Equals(step) ? step : History.Undo();
        Show(target);
    }

    /// <summary>Goes forward one step.</summary>
    public void Redo() => Show(History.Redo());

    /// <summary>
    /// Goes straight to a step of the history (the history strip), shown once however many steps it passes. Going to
    /// the current step shows it again when something changed since it, as Undo does first.
    /// </summary>
    /// <param name="index">The step; one outside the history is ignored.</param>
    public void JumpTo(int index)
    {
        if (Asking || index < 0 || index >= History.Steps.Count)
        {
            return;
        }

        Settings step = History.Steps[index].Settings;
        if (index != History.Index)
        {
            Show(History.Go(index));
        }
        else if (!_panel.Current.Equals(step))
        {
            Show(step);
        }
    }

    /// <summary>Starts a new untitled floor with the default wood.</summary>
    /// <returns>False when the user cancelled.</returns>
    public Task<bool> NewAsync() => StartFromAsync(FloorRecipe.StartingPoint(_floorSpecs));

    /// <summary>
    /// Starts a new untitled entry from a starting point, after the question about unsaved changes: the panel shows the
    /// recipe's settings with the point's values, the history starts with one step named after the point, and the
    /// floor is painted or the rock built and framed.
    /// </summary>
    /// <param name="point">The starting point.</param>
    /// <returns>False when the user cancelled, or an export or a send is running (said on the status line).</returns>
    /// <exception cref="ArgumentException">The starting point's recipe is not one this app makes.</exception>
    public async Task<bool> StartFromAsync(StartingPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        IReadOnlyList<ParamSpec> specs = SpecsFor(point.Recipe)
            ?? throw new ArgumentException($"There is no recipe '{point.Recipe}' in this app.", nameof(point));
        if (Asking || RefusedWhileBusy())
        {
            return false;
        }

        if (!await ConfirmDiscardAsync())
        {
            return false;
        }

        Enter(point.Recipe == FloorRecipe.Id ? TileMesh.FloorPiece() : new Piece(point.Recipe, []), specs, point.Settings);
        var h = new History();
        h.Push(point.Name, _panel.Current);
        History = h;
        FilePath = null;
        UntitledName = null;
        StartedFrom = point.Id;
        _saved = _panel.Current;
        Entered?.Invoke();
        await ShowEntryAsync(frame: true);
        RecipeChanged?.Invoke(Recipe);
        return true;
    }

    /// <summary>Shows the entry again in the view (the floor or the rock) after a mesh from Blender was shown.</summary>
    /// <returns>Done when it is shown.</returns>
    public Task BackToThePieceAsync()
    {
        if (MeshRecipes.Find(Recipe) is not null)
        {
            return _rocks.ShowAgain();
        }

        FloorShown?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>Asks which file to open and opens it.</summary>
    /// <returns>False when the user cancelled or the file could not be opened.</returns>
    public async Task<bool> OpenAsync()
    {
        if (Asking)
        {
            return false;
        }

        string? path = await Ask(() => Dialogs.AskOpenPathAsync(StartFolder()));
        return path is not null && await OpenPathAsync(path);
    }

    /// <summary>Opens a work file: its settings and history are restored and it goes to the top of the library.</summary>
    /// <param name="path">The file.</param>
    /// <returns>False when the user cancelled, the file could not be opened, or an export or a send is running.</returns>
    public async Task<bool> OpenPathAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Asking || RefusedWhileBusy())
        {
            return false;
        }

        if (!await ConfirmDiscardAsync())
        {
            return false;
        }

        (Piece Piece, Settings Settings, History History) loaded;
        IReadOnlyList<ParamSpec>? specs;
        try
        {
            // The recipe says which settings list the values are checked against.
            string recipe = WorkFile.Load(path).Piece.Recipe;
            if (MeshRecipes.EngineFailure is { } failure && MeshRecipes.Unavailable(recipe) is not null)
            {
                await Fail(
                    $"{Path.GetFileName(path)} could not be opened",
                    $"{Path.GetFileName(path)} was made with easy-props' engine, which could not be loaded ({failure.Message}); slab cliffs and stylized rocks still work.");
                return false;
            }

            specs = SpecsFor(recipe);
            if (specs is null)
            {
                await Fail(
                    $"{Path.GetFileName(path)} could not be opened",
                    $"{Path.GetFileName(path)} is an {Props.PropSummary.UnknownKind(recipe)}: this version of the app cannot open it (a newer version made it, or the kind is gone).");
                return false;
            }

            // An old rock opens as it looked (easy-props' earlier looks), in its settings and every undo step.
            loaded = WorkFile.Load(path, specs, s => MeshRecipes.Loaded(recipe, s));
        }
        catch (Exception e) when (e is WorkFileException or IOException or UnauthorizedAccessException)
        {
            await Fail($"{Path.GetFileName(path)} could not be opened", e.Message);
            return false;
        }

        // A work file is shared, so its part edits are checked: edits out of bounds are left out, here and in every
        // undo step, and the status line says so.
        string? dropped = null;
        if (EngineRecipes.WithParts(loaded.Piece.Recipe) is { } parts)
        {
            (Settings settings, History history, dropped) = WithoutBadEdits(parts, loaded.Settings, loaded.History);
            loaded = (loaded.Piece, settings, history);
        }

        Enter(loaded.Piece, specs, loaded.Settings);
        History = loaded.History;
        if (History.Steps.Count == 0)
        {
            History.Push("Opened", _panel.Current);
        }

        FilePath = Path.GetFullPath(path);
        UntitledName = null;
        StartedFrom = null;
        _saved = _panel.Current;
        Remember(FilePath);
        Entered?.Invoke();
        await ShowEntryAsync(frame: true);
        RecipeChanged?.Invoke(Recipe);
        if (dropped is not null)
        {
            _say($"Opened {Path.GetFileName(path)} without its part edits. {dropped}", true);
        }

        return true;
    }

    // The settings and history with any part edits that are out of bounds left out, and the first problem found.
    private static (Settings Settings, History History, string? Problem) WithoutBadEdits(EngineRecipe parts, Settings settings, History history)
    {
        string? problem = parts.EditsProblem(settings);
        Settings current = problem is null ? settings : PartEditsSettings.Without(settings);
        var steps = new List<HistoryStep>(history.Steps.Count);
        foreach (HistoryStep step in history.Steps)
        {
            string? bad = parts.EditsProblem(step.Settings);
            problem ??= bad;
            steps.Add(bad is null ? step : step with { Settings = PartEditsSettings.Without(step.Settings) });
        }

        if (problem is null)
        {
            return (settings, history, null);
        }

        var clean = new History();
        foreach (HistoryStep step in steps)
        {
            clean.Push(step.Label, step.Settings);
        }

        for (int i = clean.Index; i > history.Index; i--)
        {
            clean.Undo();
        }

        return (current, clean, problem);
    }

    /// <summary>Saves; an untitled piece asks for a name first.</summary>
    /// <returns>False when the user cancelled or saving failed.</returns>
    public async Task<bool> SaveAsync() => !Asking && (FilePath is null ? await SaveAsAsync() : await WriteAsync(FilePath));

    /// <summary>Asks for a name and saves under it; an existing file is replaced only after a yes.</summary>
    /// <returns>False when the user cancelled or saving failed.</returns>
    public async Task<bool> SaveAsAsync()
    {
        if (Asking)
        {
            return false;
        }

        string? chosen = await Ask(() => Dialogs.AskSavePathAsync(Name, StartFolder()));
        if (string.IsNullOrWhiteSpace(chosen))
        {
            return false;
        }

        // A work file is always a .propgen (as Claude's save insists): "Oak v1.2" or "notes.txt" get it added, so
        // Save as never writes over another kind of file.
        if (!string.Equals(Path.GetExtension(chosen), ".propgen", StringComparison.OrdinalIgnoreCase))
        {
            chosen += ".propgen";
        }

        if (File.Exists(chosen) && !await Ask(() => Dialogs.ConfirmAsync($"Replace {Path.GetFileName(chosen)}?", "Replace", "Keep it")))
        {
            return false;
        }

        return await WriteAsync(chosen);
    }

    /// <summary>
    /// Renames the piece. A piece never saved keeps the name for its first save (the save dialog offers it); a saved
    /// one has its work file renamed in its folder, and in the library and the pictures. Refused, with a line on the
    /// status line and nothing changed, when another file has that name, the name has a character a file name cannot
    /// have, or the file cannot be moved. An empty name, or the same name, changes nothing.
    /// </summary>
    /// <param name="newName">The new name, without or with ".propgen".</param>
    /// <returns>True when the piece has that name now.</returns>
    public Task<bool> RenameAsync(string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);
        string name = newName.Trim();
        if (name.EndsWith(".propgen", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".propgen".Length].TrimEnd();
        }

        if (Asking || name.Length == 0)
        {
            return Task.FromResult(false);
        }

        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('\\', StringComparison.Ordinal))
        {
            _say("A file name cannot have / or \\ (or a few other marks) in it: the name was not changed.", true);
            return Task.FromResult(false);
        }

        string file = name + ".propgen";
        if (file == Name)
        {
            return Task.FromResult(true);
        }

        if (FilePath is null)
        {
            UntitledName = file;
            Refresh();
            return Task.FromResult(true);
        }

        string folder = Path.GetDirectoryName(FilePath)!;
        string target = Path.Combine(folder, file);
        bool caseOnly = string.Equals(target, FilePath, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(target) || Directory.Exists(target)))
        {
            _say($"There is already a file called {file} in {folder}: the name was not changed.", true);
            return Task.FromResult(false);
        }

        string old = FilePath;
        try
        {
            File.Move(old, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _say($"{Path.GetFileName(old)} could not be renamed: {e.Message}", true);
            return Task.FromResult(false);
        }

        FilePath = Path.GetFullPath(target);
        try
        {
            _library.Rename(old, FilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _say($"{file} was not renamed in the library: {e.Message}", true);
        }

        MovePicture(old, FilePath);
        Renamed?.Invoke();
        _say($"Renamed to {file}.", false);
        Refresh();
        return Task.FromResult(true);
    }

    /// <summary>
    /// Sets every setting at once as one step for the history under the given name (Surprise me, Restore to preset, a
    /// style), and shows the result: the floor repainted or the rock rebuilt. The same settings add no step.
    /// </summary>
    /// <param name="target">The settings to show; a setting missing from them keeps its value.</param>
    /// <param name="label">The step's name.</param>
    public void ChangeAll(Settings target, string label)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(label);
        if (Asking)
        {
            return;
        }

        Settings before = _panel.Current;
        _panel.Apply(target);
        if (_panel.Current.Equals(before))
        {
            return;
        }

        History.Push(label, _panel.Current);
        Show(_panel.Current);
    }

    /// <summary>
    /// Shows settings while a drag in the view is going on (a part being moved, turned or resized), with no history
    /// step: <see cref="ChangeFrom"/> makes the step when the drag is let go.
    /// </summary>
    /// <param name="target">The settings to show.</param>
    internal void Preview(Settings target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!Asking)
        {
            Show(target);
        }
    }

    /// <summary>
    /// Ends a drag that <see cref="Preview"/> showed: the settings become one history step, named by the label, when they
    /// differ from those before the drag. A drag that ends where it began shows the settings before it.
    /// </summary>
    /// <param name="before">The settings before the drag.</param>
    /// <param name="target">The settings it ends on.</param>
    /// <param name="label">What changed.</param>
    internal void ChangeFrom(Settings before, Settings target, string label)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(target);
        if (Asking)
        {
            return;
        }

        Show(target);
        if (!_panel.Current.Equals(before))
        {
            History.Push(label, _panel.Current);
        }

        Refresh();
    }

    // The picture kept for the drawer follows the file to its new name.
    private void MovePicture(string oldPath, string newPath)
    {
        try
        {
            if (Thumbs.Get(ThumbCache.FileKey(oldPath)) is byte[] png)
            {
                Thumbs.Put(ThumbCache.FileKey(newPath), png);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TraceLog.Write($"no picture kept of {Path.GetFileName(newPath)}: {e.Message}");
        }
    }

    /// <summary>Asks about unsaved changes, if there are any.</summary>
    /// <returns>True when it is fine to go on (saved, discarded or nothing to lose).</returns>
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (Asking)
        {
            return false;
        }

        if (!Dirty)
        {
            return true;
        }

        return await Ask(() => Dialogs.AskSaveChangesAsync(Name)) switch
        {
            SaveChoice.Save => await SaveAsync(),
            SaveChoice.Discard => true,
            _ => false,
        };
    }

    /// <summary>Asks for a different map folder to export to and remembers it.</summary>
    /// <returns>Done when the user has chosen or cancelled.</returns>
    public async Task ChangeMapFolderAsync()
    {
        if (Asking)
        {
            return;
        }

        string? folder = await _export.ChooseMapFolderAsync(change: true);
        if (folder is not null)
        {
            _export.Remember(folder);
            _say($"Exports go to {folder}.", false);
        }
    }

    /// <summary>
    /// Exports the entry to a Groundwork map: the piece must be saved (named) first. A floor waits for the full-detail
    /// paint of the current settings, composed with the current colour, contrast and drift, and writes both GLBs and
    /// their catalog entries; a rock or a cliff writes its one GLB and its catalog entry in the Rocks group.
    /// </summary>
    /// <returns>Done when the export has finished, failed or been cancelled.</returns>
    public async Task ExportAsync()
    {
        if (_exporting || Asking)
        {
            return;
        }

        _exporting = true;
        try
        {
            if (FilePath is null && !await SaveAsAsync())
            {
                return;
            }

            string? map = await _export.ChooseMapFolderAsync(change: false);
            if (map is null)
            {
                return;
            }

            (string[]? models, string? error) = await ExportToAsync(map);
            if (models is null && error is not null && !error.StartsWith(ProblemStart, StringComparison.Ordinal))
            {
                // A name to change is only said on the status line; anything else is a card.
                await Fail("Export failed; nothing was written", error);
            }
        }
        catch (Exception e)
        {
            // Nothing may escape: the status line must not stay on "Exporting…" or "Waiting for full detail…".
            _say($"Export failed: {e.Message}", true);
        }
        finally
        {
            _exporting = false;
        }
    }

    /// <summary>
    /// Saves for Claude, who cannot answer a card: to the given file (an absolute path; an existing file is replaced
    /// only with <paramref name="overwrite"/>), or to the current file when no path is given. Nothing is ever asked.
    /// </summary>
    /// <param name="path">The file to save to, or null for the current file.</param>
    /// <param name="overwrite">True to replace a file that is already there.</param>
    /// <returns>The problem, or null when saved.</returns>
    public string? SaveWithoutAsking(string? path, bool overwrite)
    {
        if (Asking)
        {
            return "A question is open in the app (a card is waiting for the user). It must be answered first.";
        }

        string target;
        if (string.IsNullOrWhiteSpace(path))
        {
            if (FilePath is null)
            {
                return "This piece is untitled and has no file yet: give a path (an absolute path to a .propgen file).";
            }

            target = FilePath;
        }
        else
        {
            if (!Path.IsPathFullyQualified(path))
            {
                return $"'{path}' is not an absolute path. Give the full path of the .propgen file.";
            }

            target = Path.GetFullPath(path);
            if (Path.GetExtension(target).Length == 0)
            {
                target += ".propgen";
            }

            if (!string.Equals(Path.GetExtension(target), ".propgen", StringComparison.OrdinalIgnoreCase))
            {
                return $"Only .propgen work files can be saved here; '{Path.GetFileName(target)}' has another extension.";
            }

            string? dir = Path.GetDirectoryName(target);
            if (dir is null || !Directory.Exists(dir))
            {
                return $"The folder {dir} does not exist.";
            }

            if (File.Exists(target) && !overwrite)
            {
                return $"{target} already exists. Pass overwrite: true to replace it, or choose another path.";
            }

            if (File.Exists(target) && !IsWorkFile(target))
            {
                return $"{target} exists but is not a readable Prop Generator work file, so it is not replaced.";
            }
        }

        return TryWrite(target);
    }

    /// <summary>
    /// Exports for Claude, who cannot answer a card: the piece must already be saved (named), and the map folder is the
    /// given one (an absolute path) or the remembered one. Nothing is ever asked.
    /// </summary>
    /// <param name="mapFolder">The Groundwork map folder, or null for the remembered one.</param>
    /// <returns>The names of the models written, or the problem.</returns>
    public async Task<(string[]? Models, string? Error)> ExportWithoutAskingAsync(string? mapFolder)
    {
        if (Asking)
        {
            return (null, "A question is open in the app (a card is waiting for the user). It must be answered first.");
        }

        if (_exporting)
        {
            return (null, "An export is already running.");
        }

        _exporting = true;
        try
        {
            if (FilePath is null)
            {
                return (null, "This piece is untitled: save it first (save with a path), the models are named after the file.");
            }

            string? map = mapFolder;
            if (string.IsNullOrWhiteSpace(map))
            {
                map = Prefs.MapFolder;
                if (map is null)
                {
                    return (null, "No map folder is remembered yet: pass mapFolder (the Groundwork map's folder).");
                }
            }
            else if (!Path.IsPathFullyQualified(map))
            {
                return (null, $"'{map}' is not an absolute path. Give the full path of the Groundwork map folder.");
            }

            map = Path.GetFullPath(map);
            if (!Directory.Exists(map))
            {
                return (null, $"The map folder {map} does not exist.");
            }

            return await ExportToAsync(map);
        }
        finally
        {
            _exporting = false;
        }
    }

    /// <summary>
    /// Exports saved props without opening them (Your props, Export…): each rock, cliff, grass, flower, bush or cactus
    /// writes its GLB and catalog entry into the map folder (the remembered one, else asked), in the export look. A floor
    /// tile is left out: it is painted by the painter, so it is exported from the open piece. A file that cannot be read,
    /// or whose model name another label of the map uses, is left out too; the status line says what was.
    /// </summary>
    /// <param name="paths">The work files.</param>
    /// <returns>How many were exported.</returns>
    internal async Task<int> ExportFilesAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (_exporting || Asking || paths.Count == 0)
        {
            return 0;
        }

        _exporting = true;
        try
        {
            string? map = await Ask(() => _export.ChooseMapFolderAsync(change: false));
            if (map is null)
            {
                return 0;
            }

            Recipes.ExportOptions options = ExportOptionsNow;
            int done = 0;
            var left = new List<string>();
            foreach (string path in paths)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    string recipe = WorkFile.Load(path).Piece.Recipe;
                    if (MeshRecipes.Find(recipe) is not { } mesh)
                    {
                        left.Add(recipe == FloorRecipe.Id ? $"{name} (a floor tile: open it to export it)" : $"{name} ({MeshRecipes.Unavailable(recipe) ?? Props.PropSummary.UnknownKind(recipe)})");
                        continue;
                    }

                    if (ExportDialog.Problem(map, name, recipe) is string problem)
                    {
                        left.Add($"{name} ({problem})");
                        continue;
                    }

                    Settings settings = Complete(mesh.Specs, WorkFile.Load(path, mesh.Specs, x => MeshRecipes.Loaded(recipe, x)).Settings);
                    _say($"Exporting {name}…", false);
                    await Task.Run(() => WriteMesh(map, name, mesh, settings, options));
                    done++;
                }
                catch (Exception e) when (e is WorkFileException or IOException or UnauthorizedAccessException or CatalogException || EngineFailed(e))
                {
                    left.Add($"{name} ({e.Message})");
                }
            }

            if (done > 0)
            {
                _export.Remember(map);
            }

            string said = done == 1 ? "Exported 1 prop" : $"Exported {done} props";
            _say(left.Count == 0 ? $"{said} to {map}." : $"{said} to {map}. Left out: {string.Join("; ", left)}.", left.Count > 0 && done == 0);
            return done;
        }
        finally
        {
            _exporting = false;
        }
    }

    /// <summary>
    /// Reads saved props for a set (Your props, several ticked): each easy-props kind as a <see cref="Batch.SetProp"/>
    /// named after its file; the rest (our own kinds, floor tiles, files that cannot be read) are left out, each with why.
    /// </summary>
    /// <param name="paths">The work files.</param>
    /// <returns>The props that can be in the set, and what was left out.</returns>
    internal static (IReadOnlyList<Batch.SetProp> Props, IReadOnlyList<string> LeftOut) SetPropsOf(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var props = new List<Batch.SetProp>();
        var left = new List<string>();
        foreach (string path in paths)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            try
            {
                string recipe = WorkFile.Load(path).Piece.Recipe;
                switch (MeshRecipes.Find(recipe))
                {
                    case EngineRecipe engine:
                        Settings settings = Complete(engine.Specs, WorkFile.Load(path, engine.Specs, x => MeshRecipes.Loaded(recipe, x)).Settings);
                        props.Add(new Batch.SetProp(name, engine, settings));
                        break;
                    case { } ours:
                        left.Add($"{name} ({ours.Label.ToLowerInvariant()}: only easy-props' kinds share set colours)");
                        break;
                    default:
                        left.Add(recipe == FloorRecipe.Id ? $"{name} (a floor tile)" : $"{name} ({MeshRecipes.Unavailable(recipe) ?? Props.PropSummary.UnknownKind(recipe)})");
                        break;
                }
            }
            catch (Exception e) when (e is WorkFileException or IOException or UnauthorizedAccessException || EngineFailed(e))
            {
                left.Add($"{name} ({e.Message})");
            }
        }

        return (props, left);
    }

    /// <summary>
    /// Export as a set (decision E7): each prop is made again in the set's colours (upstream's <c>inSetColours</c>; the
    /// saved props stay as they are) and exported in its own engine jobs, one at a time, with the set's name on its
    /// materials so an engine can share them; every file goes flat into <c>&lt;folder&gt;/&lt;folderNameOf(set)&gt;</c>,
    /// and the set's list, <c>&lt;exportNameOf(set)&gt;.set.json</c>, last. Not a map: nothing is added to a catalog. A prop
    /// that fails is left out; a list that cannot be written leaves the props written, and says so.
    /// </summary>
    /// <param name="props">The props, named as upstream's batch names them (<see cref="Batch.ExportSet.NamedAsync"/>).</param>
    /// <param name="set">The set's name, colour groups and colours.</param>
    /// <param name="folder">The folder the set's own folder is made in.</param>
    /// <returns>How many props were exported.</returns>
    internal async Task<int> ExportSetAsync(IReadOnlyList<Batch.SetProp> props, Batch.SetChoice set, string folder)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        if (_exporting || Asking || props.Count == 0)
        {
            return 0;
        }

        _exporting = true;
        try
        {
            string typed = Batch.ExportSet.NameOf(set.Name);
            PropGenerator.Engine.EngineExportNames names;
            try
            {
                names = Engine.EngineHost.Default.Rules.ExportNames(typed);
            }
            catch (Exception e) when (EngineFailed(e))
            {
                _say($"Nothing was exported: easy-props' engine could not name the set ({e.Message}).", true);
                return 0;
            }

            // Upstream's: a name that makes no folder name writes into the folder itself.
            string into = names.Folder.Length > 0 ? Path.Combine(folder, names.Folder) : folder;
            try
            {
                Directory.CreateDirectory(into);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _say($"Nothing was exported: the set's folder {into} could not be made ({e.Message}).", true);
                return 0;
            }

            Recipes.ExportOptions options = ExportOptionsNow with { SetName = names.Export };
            string colours = set.ColoursJson();
            var entries = new System.Text.Json.Nodes.JsonArray();
            int done = 0;
            var left = new List<string>();
            foreach (Batch.SetProp prop in props)
            {
                try
                {
                    _say($"Exporting {prop.Name} ({done + left.Count + 1} of {props.Count}) into the set {typed}…", false);
                    Settings coloured = await Batch.ExportSet.InSetColoursAsync(prop, colours, PropGenerator.Engine.EngineJobKind.Export, CancellationToken.None);
                    IReadOnlyList<PropGenerator.Engine.EngineFile> files = await prop.Recipe.ExportFilesAsync(coloured, prop.Name, options);
                    await Task.Run(() =>
                    {
                        foreach (PropGenerator.Engine.EngineFile file in files)
                        {
                            File.WriteAllBytes(Path.Combine(into, Path.GetFileName(file.Name)), file.Bytes);
                        }
                    });
                    entries.Add(new System.Text.Json.Nodes.JsonObject { ["name"] = prop.Name, ["files"] = Batch.ExportSet.EntryFiles(files) });
                    done++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException || EngineFailed(e))
                {
                    left.Add($"{prop.Name} ({e.Message})");
                }
            }

            string? listProblem = null;
            if (done > 0)
            {
                try
                {
                    PropGenerator.Engine.EngineFile list = await Engine.EngineHost.Default.Builds.SetListAsync(names.Export, entries.ToJsonString(), set.GroupsJson(), colours);
                    await Task.Run(() => File.WriteAllBytes(Path.Combine(into, Path.GetFileName(list.Name)), list.Bytes));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException || EngineFailed(e))
                {
                    TraceLog.Write($"Export as a set: the list was not written: {e.Message}");
                    listProblem = "The set's list couldn't be written. Its props were.";
                }

                if (Prefs.SetFolder != folder)
                {
                    Prefs.SetFolder = folder;
                    Prefs.Save();
                }
            }

            string said = done == 1 ? $"Exported 1 prop as the set {typed} to {into}." : $"Exported {done} props as the set {typed} to {into}.";
            if (left.Count > 0)
            {
                said += $" Left out: {string.Join("; ", left)}.";
            }

            if (listProblem is not null)
            {
                said += " " + listProblem;
            }

            _say(said, done == 0 || listProblem is not null);
            return done;
        }
        finally
        {
            _exporting = false;
        }
    }

    /// <summary>Gets how exports are made now: the export look and picture size chosen in Settings.</summary>
    internal Recipes.ExportOptions ExportOptionsNow => new(Prefs.ExportLook, Recipes.ExportOptions.KeptPictureSize(Prefs.ExportPictureSize));

    /// <summary>Stands in for the export of each version of Export many (name, settings → model), for the tests.</summary>
    internal static readonly AsyncLocal<Func<string, Settings, string>?> WriteManyForTests = new();

    // How many versions of Export many are being made right now (one at most).
    private int _versionsAlive;

    /// <summary>Gets the most versions of Export many ever made at once (one: each is made at its turn).</summary>
    internal int MostVersionsAtOnce { get; private set; }

    /// <summary>
    /// Export many (easy-props' batch): several versions of the open prop, each written as its own model into the map
    /// folder (the remembered one, else asked), in the export look. Each item is named by easy-props' batch naming
    /// (<c>name_01</c>, <c>name_02</c>, …) and is the prop on the item's seed: on the prop's own seed it is the prop
    /// exactly as it is, hand edits and all; on another, the same settings without the part edits but with a
    /// painted tree's own edits (easy-props' withSeed); a damaged version then has its damage settings set on that (easy-props' versionScene). They are built one at a time off the window's thread, so a batch of fifty holds one prop at a time.
    /// An item whose model name another label of the map uses is left out; the status line says which.
    /// </summary>
    /// <param name="items">The batch's items, in order, named by upstream's <c>batchItems</c> (names only).</param>
    /// <returns>How many were exported.</returns>
    internal async Task<int> ExportManyAsync(IReadOnlyList<Batch.BatchVersion> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (_exporting || Asking || items.Count == 0 || MeshRecipes.Find(Recipe) is not { } mesh)
        {
            return 0;
        }

        _exporting = true;
        try
        {
            string? map = await Ask(() => _export.ChooseMapFolderAsync(change: false));
            if (map is null)
            {
                return 0;
            }

            Settings basis = _panel.Current;
            Recipes.ExportOptions options = ExportOptionsNow;
            int done = 0;
            var left = new List<string>();
            foreach (Batch.BatchVersion item in items)
            {
                try
                {
                    if (ExportDialog.Problem(map, item.Name, mesh.Id) is string problem)
                    {
                        left.Add($"{item.Name} ({problem})");
                        continue;
                    }

                    Settings settings = item.Version.Of(basis);
                    _say($"Exporting {item.Name} ({done + left.Count + 1} of {items.Count})…", false);
                    Func<string, Settings, string>? stand = WriteManyForTests.Value;
                    int alive = Interlocked.Increment(ref _versionsAlive);
                    try
                    {
                        MostVersionsAtOnce = Math.Max(MostVersionsAtOnce, alive);
                        await Task.Run(() => stand is null ? WriteMesh(map, item.Name, mesh, settings, options) : stand(item.Name, settings));
                    }
                    finally
                    {
                        _ = Interlocked.Decrement(ref _versionsAlive);
                    }

                    done++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or CatalogException || EngineFailed(e))
                {
                    left.Add($"{item.Name} ({e.Message})");
                }
            }

            if (done > 0)
            {
                _export.Remember(map);
            }

            string said = done == 1 ? "Exported 1 version" : $"Exported {done} versions";
            _say(left.Count == 0 ? $"{said} to {map}." : $"{said} to {map}. Left out: {string.Join("; ", left)}.", left.Count > 0 && done == 0);
            return done;
        }
        finally
        {
            _exporting = false;
        }
    }

    // A failure easy-props' engine is expected to give (D10): a script error or out of memory, a job that timed out, was
    // stopped or dropped, or a host already shut down. The port never threw these; an export leaves that prop out.
    private static bool EngineFailed(Exception e) =>
        e is PropGenerator.Engine.EngineException or TimeoutException or ObjectDisposedException or OperationCanceledException;

    // An export or a send waiting for the floor's paint must not find another piece in the panel when it goes on.
    private bool RefusedWhileBusy()
    {
        if (BusyReason is not string why)
        {
            return false;
        }

        _say(why, false);
        return true;
    }

    private static bool IsWorkFile(string path)
    {
        try
        {
            _ = WorkFile.Load(path);
            return true;
        }
        catch (Exception e) when (e is WorkFileException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // The one export. A floor waits for the full-detail paint of the current settings, composes it with the colour,
    // contrast and drift of when the export began, and writes both GLBs and the catalog entries; a rock writes its GLB
    // and its entry. The name, recipe and settings are taken before any wait. Every outcome is also said on the status
    // line.
    private async Task<(string[]? Models, string? Error)> ExportToAsync(string map)
    {
        string name = Path.GetFileNameWithoutExtension(FilePath!);
        string recipe = Recipe;
        Settings settings = _panel.Current;
        try
        {
            string? problem = ExportDialog.Problem(map, name, recipe);
            if (problem is not null)
            {
                _say(problem, true);
                return (null, problem);
            }

            if (MeshRecipes.Find(recipe) is { } mesh)
            {
                string what = ExportDialog.KindLabel(mesh);
                _say($"Exporting {name} {what}…", false);
                Recipes.ExportOptions options = ExportOptionsNow;
                string model = await Task.Run(() => WriteMesh(map, name, mesh, settings, options));
                _export.Remember(map);
                _say($"Exported {name} {what} to {map}.", false);
                return ([model], null);
            }

            (byte[] Rgb, int Size)? painted = await ComposeAsync($"Exporting {name}…", settings);
            if (painted is null)
            {
                const string Late = "The floor was not painted at full detail in time, so nothing was exported.";
                _say(Late, true);
                return (null, Late);
            }

            await Task.Run(() => ExportDialog.Write(map, name, painted.Value.Rgb, painted.Value.Size));
            _export.Remember(map);
            _say($"Exported {name} to {map}.", false);
            return ([ExportDialog.ModelName(name, "floor_tile"), ExportDialog.ModelName(name, "floor_tri")], null);
        }
        catch (CatalogException e)
        {
            string text = $"The map's catalog could not be used, nothing was written: {e.Message}";
            _say($"Export failed: {text}", true);
            return (null, text);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _say($"Export failed: {e.Message}", true);
            return (null, $"Export failed; nothing was written: {e.Message}");
        }
    }

    /// <summary>
    /// Writes the entry into the Blender outbox folder: a floor as its two tiles (square and triangle), painted as they
    /// are now, as pg_&lt;name&gt;_floor_tile.glb and pg_&lt;name&gt;_floor_tri.glb; a rock as pg_&lt;name&gt;_rock.glb
    /// and a cliff as pg_&lt;name&gt;_cliff.glb, the same GLB an export writes. Nothing is added to any catalog.
    /// </summary>
    /// <param name="outbox">The outbox folder (made if missing).</param>
    /// <returns>The paths written, or the problem.</returns>
    public async Task<(string[]? Files, string? Error)> SendToBlenderAsync(string outbox)
    {
        ArgumentException.ThrowIfNullOrEmpty(outbox);
        if (_sending)
        {
            return (null, "A send to Blender is already running.");
        }

        _sending = true;

        // Taken before any wait: what is sent is the piece of when the send began.
        string name = Path.GetFileNameWithoutExtension(Name);
        Settings settings = _panel.Current;
        try
        {
            if (MeshRecipes.Find(Recipe) is { } mesh)
            {
                return await SendMeshAsync(outbox, name, mesh, settings);
            }

            (byte[] Rgb, int Size)? painted = await ComposeAsync($"Sending {name} to Blender…", settings);
            if (painted is null)
            {
                const string Late = "The floor was not painted at full detail in time, so nothing was sent to Blender.";
                _say(Late, true);
                return (null, Late);
            }

            string[] written = await Task.Run(() =>
            {
                Directory.CreateDirectory(outbox);
                string tile = ExportDialog.ModelName(name, "floor_tile");
                string tri = ExportDialog.ModelName(name, "floor_tri");
                string[] paths = [Path.Combine(outbox, tile + ".glb"), Path.Combine(outbox, tri + ".glb")];
                GlbExport.Write(paths[0], TileMesh.Square(), painted.Value.Rgb, painted.Value.Size, tile);
                GlbExport.Write(paths[1], TileMesh.Triangle(), painted.Value.Rgb, painted.Value.Size, tri);
                return paths;
            });
            _say($"Sent {name} to Blender: in the Prop Generator panel there, press Get painted. ({outbox})", false);
            return (written, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            string text = $"Could not write to the Blender outbox {outbox}: {e.Message}";
            _say(text, true);
            return (null, text);
        }
        catch (Exception e) when (EngineFailed(e))
        {
            string text = $"The prop could not be built for Blender, nothing was sent: {e.Message}";
            _say(text, true);
            return (null, text);
        }
        finally
        {
            _sending = false;
        }
    }

    // A rock's (or cliff's) GLB into the outbox, written beside it first and moved into place, so Blender never
    // reads half a file.
    private async Task<(string[]? Files, string? Error)> SendMeshAsync(string outbox, string name, IMeshRecipe recipe, Settings settings)
    {
        string what = ExportDialog.KindLabel(recipe);
        Recipes.ExportOptions options = ExportOptionsNow;
        _say($"Sending {name} {what} to Blender…", false);
        string written = await Task.Run(() =>
        {
            Directory.CreateDirectory(outbox);
            string model = ExportDialog.ModelName(name, ExportDialog.Part(recipe));
            string target = Path.Combine(outbox, model + ".glb");
            string temp = target + ".tmp";
            File.WriteAllBytes(temp, recipe.Glb(settings, model, options));
            File.Move(temp, target, overwrite: true);
            return target;
        });
        _say($"Sent {name} {what} to Blender: in the Prop Generator panel there, press Get painted. ({outbox})", false);
        return ([written], null);
    }

    // Waits for the full-detail paint of the current settings and composes it with the colour, contrast and drift of
    // the given settings (taken before the wait) into one RGBA picture; null when the paint did not come in time.
    private async Task<(byte[] Rgb, int Size)?> ComposeAsync(string doing, Settings s)
    {
        if (!_repaint.FullIsCurrent && !_repaint.FullOnItsWay)
        {
            // The last full-detail paint failed or was dropped, and nothing else will ask for one: ask now.
            _repaint.RepaintNow();
        }

        Layers? layers = await _repaint.WaitForFullAsync(FullPaintTimeout, () => _say("Waiting for full detail…", false));
        if (layers is null)
        {
            return null;
        }

        _say(doing, false);
        return await Task.Run(() =>
            (ToneComposer.Compose(layers, Palette.Derive(s.GetColour("colour")), s.GetFloat("contrast"), s.GetFloat("drift")), layers.Size));
    }

    private async Task<T> Ask<T>(Func<Task<T>> question)
    {
        _asking++;
        try
        {
            return await question();
        }
        finally
        {
            _asking--;
        }
    }

    private static History Fresh(Settings s)
    {
        var h = new History();
        h.Push("Opened", s);
        return h;
    }

    // Every setting of the list, from the values where they have one and from the defaults where not.
    private static Settings Complete(IReadOnlyList<ParamSpec> specs, Settings values)
    {
        var s = Settings.Defaults(specs);
        foreach (ParamSpec spec in specs)
        {
            if (values.Values.TryGetValue(spec.Key, out System.Text.Json.JsonElement v))
            {
                s = s.With(spec.Key, v);
            }
        }

        // The edits to the prop's parts go with it (they have no row).
        foreach (string key in SettingsPanel.RowlessKeys)
        {
            if (values.Values.TryGetValue(key, out System.Text.Json.JsonElement parts))
            {
                s = s.With(key, parts);
            }
        }

        return s;
    }

    private string? StartFolder() => Prefs.SaveFolder ?? (FilePath is null ? null : Path.GetDirectoryName(FilePath));

    private IReadOnlyList<ParamSpec>? SpecsFor(string recipe) =>
        recipe == FloorRecipe.Id ? _floorSpecs : MeshRecipes.Find(recipe)?.Specs;

    // The piece becomes the entry: the floor's painting or the rock's building follows the panel, and the panel shows
    // the recipe's settings (rebuilt when the recipe changes; a setting the values lack keeps its value, as before, or
    // its default in a rebuilt panel).
    private void Enter(Piece piece, IReadOnlyList<ParamSpec> specs, Settings values)
    {
        bool same = piece.Recipe == _piece.Recipe;
        bool floor = piece.Recipe == FloorRecipe.Id;
        _piece = piece;
        if (MeshRecipes.Find(piece.Recipe) is { } mesh)
        {
            _rocks.Recipe = mesh;
        }

        _rocks.Active = !floor;
        _repaint.Following = floor;
        if (same)
        {
            _panel.Apply(values);
        }
        else
        {
            _panel.Rebuild(specs, Complete(specs, values));
        }
    }

    // Shows the entry: the floor painted at full detail, or the rock (cliff) built and shown (over a mesh from
    // Blender too).
    private Task ShowEntryAsync(bool frame)
    {
        if (MeshRecipes.Find(Recipe) is not null)
        {
            Refresh();
            return _rocks.ShowNow(frame);
        }

        Repaint();
        FloorShown?.Invoke();
        return Task.CompletedTask;
    }

    private void Show(Settings? target)
    {
        if (target is null)
        {
            return;
        }

        _panel.Apply(target);
        if (MeshRecipes.Find(Recipe) is not null)
        {
            _ = _rocks.Follow();
            Refresh();
            return;
        }

        Repaint();
    }

    private void Repaint()
    {
        _repaint.OnToneChanged();
        _repaint.RepaintNow();
        Refresh();
    }

    private async Task<bool> WriteAsync(string path)
    {
        try
        {
            WorkFile.Save(path, _piece, _panel.Current, History, MeshRecipes.Find(Recipe)?.Engine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Fail($"{Path.GetFileName(path)} could not be saved", e.Message);
            return false;
        }

        Saved(path);
        return true;
    }

    // The same save without a card: the problem is returned for the caller to pass on.
    private string? TryWrite(string path)
    {
        try
        {
            WorkFile.Save(path, _piece, _panel.Current, History, MeshRecipes.Find(Recipe)?.Engine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _say($"{Path.GetFileName(path)} could not be saved: {e.Message}", true);
            return $"{Path.GetFileName(path)} could not be saved: {e.Message}";
        }

        Saved(path);
        return null;
    }

    private void Saved(string path)
    {
        FilePath = Path.GetFullPath(path);
        UntitledName = null;
        _saved = _panel.Current;
        Remember(FilePath);
        _say($"Saved {Name}.", false);
        Refresh();
        _ = StorePictureAsync(FilePath);
    }

    // A picture of the view as it is at the save, the middle square at 128 px, kept for the drawer under the file's
    // path. A view that cannot be pictured (or a disk that cannot keep it) only means no picture: it is logged.
    private async Task StorePictureAsync(string fullPath)
    {
        try
        {
            if (Capture is not { } capture)
            {
                return;
            }

            (byte[] rgba, int width, int height) = await capture();
            ThumbCache thumbs = Thumbs;
            await Task.Run(() => thumbs.Put(ThumbCache.FileKey(fullPath), ViewThumb.Png(rgba, width, height)));
            Refresh();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            TraceLog.Write($"no picture kept of {Path.GetFileName(fullPath)}: {e.Message}");
        }
    }

    private void Remember(string path)
    {
        try
        {
            _library.Add(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // The library could not be updated; the file itself is fine.
            _say($"{Path.GetFileName(path)} was not added to the library: {e.Message}", true);
        }

        Prefs.SaveFolder = Path.GetDirectoryName(path);
        Prefs.Save();
        Refresh();
    }

    private async Task Fail(string title, string message)
    {
        _say($"{title}: {message}", true);
        await Ask(async () =>
        {
            await Dialogs.TellAsync(title, message);
            return true;
        });
    }
}
