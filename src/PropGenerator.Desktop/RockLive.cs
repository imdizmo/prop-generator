using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.View;
using PropGenerator.Nature.Export;

namespace PropGenerator.Desktop;

/// <summary>
/// Keeps the 3D view's rock in step with the settings panel while a rock (or any prop of a mesh recipe, such as the
/// cliff: see <see cref="Recipe"/>) is the entry being made. Every change in the
/// panel asks for the rock to be built again, off the window's thread. One build runs at a time: changes that come
/// while it runs only leave the newest settings waiting, which are built as soon as it is done. Builds run in order, so
/// every build that finishes is newer than the rock shown, and it is shown: during a drag the rock follows the hand,
/// one build behind at most, and ends on the value let go. Never shown: a build begun before the entry was started,
/// opened, undone, redone or brought back (that newer request wins over an older slider build), and any build once
/// the rock is no longer the entry. Only the newest rock shown is kept (to show it again). While the shapes you can walk
/// into are wanted, each build works them out beside the rock, off the window's thread too. Every build, count and
/// shapes call carries a token that is cancelled once its result can no longer be shown (the kind changed, the rock is
/// no longer the entry, a start, open, undo or redo), and this window's own replace keys (<see cref="BuildKey"/>,
/// <see cref="CountKey"/>, <see cref="ShapesKey"/>), so an engine shared by every window drops only this window's older
/// jobs. A build dropped by a newer one of the same key is built again, silently.
/// </summary>
public sealed class RockLive : IDisposable
{
    // A replaced job dropped this many times in a row is reported rather than asked for again.
    private const int MostDropsInARow = 5;

    // Numbers the instances, for their replace keys.
    private static long _instances;

    private readonly SettingsPanel _panel;
    private readonly TileView _view;
    private Settings? _waiting;
    private Settings? _lastAsked;
    private long _asked;
    private long _barrier;
    private long _shownNumber;
    private Task _running = Task.CompletedTask;
    private bool _frameWanted;
    private bool _forced;
    private bool _disposed;
    private bool _shaping;
    private int _buildDrops;
    private int _shapeDrops;

    // Cancelled (and replaced) whenever the jobs started so far can no longer be shown.
    private CancellationTokenSource _jobs = new();

    // The last settings whose full-detail triangles were counted, so switching levels does not build the full rock again.
    // Written on the window's thread only.
    private Settings? _countedSettings;
    private int _countedFaces;

    /// <summary>Follows nothing until <see cref="Active"/> is set.</summary>
    /// <param name="panel">The settings.</param>
    /// <param name="view">Shows the rock.</param>
    public RockLive(SettingsPanel panel, TileView view)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(view);
        _panel = panel;
        _view = view;
        view.RockLost += OnRockLost;
        long id = Interlocked.Increment(ref _instances);
        BuildKey = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"view-build:{id}");
        CountKey = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"view-count:{id}");
        ShapesKey = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"view-shapes:{id}");
    }

    /// <summary>Gets the replace key of this view's level builds: a newer one drops an older one still waiting.</summary>
    internal string BuildKey { get; }

    /// <summary>Gets the replace key of this view's face counts after a drag.</summary>
    internal string CountKey { get; }

    /// <summary>Gets the replace key of this view's shapes worked out after the rock was shown.</summary>
    internal string ShapesKey { get; }

    /// <summary>Building a rock failed (the message says why).</summary>
    public event Action<string>? Failed;

    /// <summary>A rock was built and shown in the view: the milliseconds the build took.</summary>
    public event Action<double>? Built;

    /// <summary>The rock's full-detail triangles were counted again after a drag (<see cref="FullDetailFaces"/> changed).</summary>
    public event Action? Counted;

    /// <summary>
    /// A rock was shown: the mesh, and the settings it was built from. The status bar and the view toolbar follow it
    /// (the faces, the shading tool, the stats and the shapes you can walk into).
    /// </summary>
    internal event Action<ColouredMesh, Settings>? Showed;

    /// <summary>
    /// The shapes you can walk into of the rock shown were worked out after it was shown (<see cref="Shapes"/> and
    /// <see cref="ShapesOf"/> changed): they were asked for after its build began.
    /// </summary>
    internal event Action? ShapesReady;

    /// <summary>Gets the newest rock shown, or null when none is (the floor is the entry, or nothing was built yet).</summary>
    public ColouredMesh? Mesh { get; private set; }

    /// <summary>Gets the triangles of the rock at full detail (0 while none is shown), whatever detail level is on screen.</summary>
    public int FullDetailFaces
    {
        get => Mesh is null ? 0 : field;
        private set;
    }

    /// <summary>Gets the triangles of the rock at the detail level on screen (0 while none is shown).</summary>
    public int ShownFaces => Mesh is null ? 0 : Mesh.Indices.Length / 3;

    /// <summary>Gets the share of full detail <see cref="Mesh"/> was built at.</summary>
    public double ShownDetail { get; private set; } = 1;

    /// <summary>
    /// Gets or sets the share of full detail the rock is built at: 1, 0.4 or 0.12 (the detail level shown). A change
    /// builds the rock again, keeping the camera, unless a mesh from Blender is in the view.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not above 0 or above 1.</exception>
    public double Detail
    {
        get;
        set
        {
            if (value is not (> 0 and <= 1))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The share of full detail must be above 0 and at most 1.");
            }

            if (value == field)
            {
                return;
            }

            field = value;
            _ = Follow();
        }
    } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether a mouse button is down in the window. While one is, a rock below full
    /// detail keeps its last full-detail count rather than building the full rock as well; when it is let go, the rock
    /// shown is counted again if it needs to be.
    /// </summary>
    public bool PointerHeld
    {
        get;
        set
        {
            if (value == field)
            {
                return;
            }

            field = value;
            if (!value && Active && ShownSettings is Settings s && !s.Equals(_countedSettings))
            {
                _ = RecountAsync();
            }
        }
    }

    /// <summary>Gets the settings <see cref="Mesh"/> was built from.</summary>
    public Settings? ShownSettings { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the shapes you can walk into are wanted (they are shown in the view):
    /// while they are, each build works them out beside the rock, off the window's thread. Turned on, the shapes of the
    /// rock shown are worked out if they are not yet.
    /// </summary>
    internal bool WantShapes
    {
        get;
        set
        {
            if (value == field)
            {
                return;
            }

            field = value;
            if (value)
            {
                _ = WorkOutShapesAsync();
            }
        }
    }

    /// <summary>
    /// Gets the shapes you can walk into of the rock built from <see cref="ShapesOf"/>, ready for the view; null when it
    /// has none, or none were worked out.
    /// </summary>
    internal CollisionPrisms? Shapes { get; private set; }

    /// <summary>
    /// Gets the settings <see cref="Shapes"/> were worked out for, or null when none were. The shapes belong to the rock
    /// shown only while this equals <see cref="ShownSettings"/>.
    /// </summary>
    internal Settings? ShapesOf { get; private set; }

    /// <summary>
    /// Gets or sets the recipe of the entry: what builds the prop shown (the rock or the cliff) and works out its
    /// shapes, unless the tests put their own builders in. Changed while active, builds of the old recipe still running
    /// are dropped and its prop kept is let go.
    /// </summary>
    internal IMeshRecipe Recipe
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(value, field))
            {
                return;
            }

            bool active = Active;
            Active = false;
            StopJobs();
            field = value;
            Active = active;
        }
    }

    = MeshRecipes.All[0];

    /// <summary>Gets or sets what works out the shapes you can walk into instead of the recipe (null: the recipe's own; the tests watch it).</summary>
    internal Func<Settings, IReadOnlyList<CollisionShape>>? ShapeBuilder { get; set; }

    /// <summary>Gets or sets what builds the prop at full detail instead of the recipe (null: the recipe's own; the tests give a slower one).</summary>
    internal Func<Settings, ColouredMesh>? Builder { get; set; }

    /// <summary>Gets or sets what builds the prop at a lower detail level instead of the recipe (null: the recipe's own).</summary>
    internal Func<Settings, double, ColouredMesh>? LevelBuilder { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a rock is the entry: only then are the panel's changes followed. Turned
    /// off, builds still running are dropped and the rock kept is let go.
    /// </summary>
    public bool Active
    {
        get;
        set
        {
            if (value == field || _disposed)
            {
                return;
            }

            field = value;
            if (value)
            {
                _panel.PaintChanged += OnPaintChanged;
                _panel.Committed += OnCommitted;
            }
            else
            {
                _panel.PaintChanged -= OnPaintChanged;
                _panel.Committed -= OnCommitted;
                StopJobs();
                _asked++;
                _barrier = _asked;
                _waiting = null;
                _lastAsked = null;
                _frameWanted = false;
                _forced = false;
                Mesh = null;
                ShownSettings = null;
                Shapes = null;
                ShapesOf = null;
            }
        }
    }

    /// <summary>
    /// Builds the rock of the settings as they are now and shows it, even over a mesh from Blender (a rock entry
    /// started or opened, or Back to the piece).
    /// </summary>
    /// <param name="frame">True to frame the camera on the rock (a new entry); false to keep the camera.</param>
    /// <returns>Done when the rock is shown, or was overtaken by a newer start, open, undo or redo.</returns>
    public Task ShowNow(bool frame)
    {
        _forced = true;
        _frameWanted |= frame;
        return Ask(barrier: true);
    }

    /// <summary>
    /// Builds the rock of the settings as they are now and shows it, unless a mesh from Blender is in the view (as a
    /// slider change does; used after an undo or a redo).
    /// </summary>
    /// <returns>Done when the rock is built, or was overtaken by a newer start, open, undo or redo.</returns>
    public Task Follow() => Ask(barrier: true);

    /// <summary>Shows the rock kept again (Back to the piece), or builds it when there is none.</summary>
    /// <returns>Done when it is shown.</returns>
    public Task ShowAgain()
    {
        if (Mesh is ColouredMesh m && ShownSettings is Settings s && s.Equals(_panel.Current) && ShownDetail == Detail && _waiting is null && _running.IsCompleted)
        {
            _ = Show(m, s, frame: false);
            return Task.CompletedTask;
        }

        return ShowNow(frame: false);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the Far away level is shown: the prop as its far-away card, made by
    /// <see cref="FarCard"/> from the full prop. A change builds again.
    /// </summary>
    internal bool Far
    {
        get;
        set
        {
            if (value != field)
            {
                field = value;
                _ = Follow();
            }
        }
    }

    /// <summary>Gets or sets what makes the far-away card of the full prop, off the window's thread; null (or a null card) shows the prop.</summary>
    internal Func<ColouredMesh, ColouredMesh?>? FarCard { get; set; }

    /// <summary>
    /// Gets or sets what the view draws instead of each rock shown, such as the rock with its selected part lit up; null
    /// to draw the rock as built. <see cref="Mesh"/> stays the rock as built.
    /// </summary>
    internal Func<ColouredMesh, ColouredMesh>? Decorate { get; set; }

    /// <summary>Draws the rock shown again through <see cref="Decorate"/>, keeping the camera (the selection changed).</summary>
    internal void Redecorate()
    {
        if (Active && Mesh is ColouredMesh m)
        {
            _view.ShowRock(Decorated(m), frame: false);
        }
    }

    private ColouredMesh Decorated(ColouredMesh mesh) => Decorate is { } decorate ? decorate(mesh) : mesh;

    /// <summary>Stops following the panel; builds still running are dropped.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Active = false;
        StopJobs();
        _disposed = true;
        _view.RockLost -= OnRockLost;
    }

    // The jobs started so far stop: their results could no longer be shown. The old source is cancelled, not disposed:
    // a job on another thread may still read its token.
    private void StopJobs()
    {
        _jobs.Cancel();
        _jobs = new CancellationTokenSource();
    }

    private void OnPaintChanged(bool dragging)
    {
        _ = dragging;
        _ = Ask();
    }

    // A finished change whose settings were already asked for (a drag let go on its last value) needs no new build.
    private void OnCommitted(string label)
    {
        _ = label;
        if (_lastAsked is null || !_lastAsked.Equals(_panel.Current))
        {
            _ = Ask();
        }
    }

    private void OnRockLost()
    {
        if (Active && Mesh is ColouredMesh m && ShownSettings is Settings s)
        {
            _view.ShowRock(Decorated(m), frame: false);
            Showed?.Invoke(m, s);
        }
    }

    // barrier: builds begun before this request are not shown when they finish (a start, open, undo or redo must not
    // be overwritten by an older slider build).
    private Task Ask(bool barrier = false)
    {
        if (!Active || _disposed)
        {
            return Task.CompletedTask;
        }

        _asked++;
        if (barrier)
        {
            // Nothing begun before is shown: whatever still runs stops.
            _barrier = _asked;
            StopJobs();
        }

        _waiting = _panel.Current;
        _lastAsked = _waiting;
        if (_running.IsCompleted)
        {
            _running = BuildWhileWaitingAsync();
        }

        return _running;
    }

    // Builds the settings left waiting, one build after another, until nothing waits. Runs on the window's thread
    // between builds; the builds themselves run on the thread pool.
    private async Task BuildWhileWaitingAsync()
    {
        while (_waiting is Settings settings && Active && !_disposed)
        {
            _waiting = null;
            long number = _asked;
            double detail = Detail;
            Settings? countedFor = _countedSettings;
            int counted = _countedFaces;
            bool keepCount = PointerHeld && countedFor is not null;
            bool shapes = WantShapes;
            IMeshRecipe recipe = Recipe;
            CancellationToken cancel = _jobs.Token;
            ColouredMesh mesh;
            int fullFaces;
            bool exact;
            bool shaped;
            CollisionPrisms? prisms;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                (mesh, fullFaces, exact, shaped, prisms) = await Task.Run(() => BuildAt(recipe, settings, detail, countedFor, counted, keepCount, shapes, cancel));
                _buildDrops = 0;
            }
            catch (OperationCanceledException e)
            {
                // Stopped (silent), or dropped by a newer job of the same key: then built again, unless newer
                // settings already wait.
                if (!cancel.IsCancellationRequested && Active && !_disposed && number >= _barrier)
                {
                    if (++_buildDrops < MostDropsInARow)
                    {
                        _waiting ??= settings;
                    }
                    else
                    {
                        _buildDrops = 0;
                        Failed?.Invoke($"The {Kind(recipe)} could not be built: {e.Message}");
                    }
                }

                continue;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                if (number >= _barrier && Active)
                {
                    Failed?.Invoke($"The {Kind(recipe)} could not be built: {e.Message}");
                }

                continue;
            }

            if (number < _barrier || number <= _shownNumber || !Active || _disposed)
            {
                // Begun before a start, open, undo or redo (or the rock is no longer the entry): dropped.
                TraceLog.Write("rock build dropped (begun before the newest start, open, undo or redo)");
                continue;
            }

            if (exact)
            {
                _countedSettings = settings;
                _countedFaces = fullFaces;
            }

            _shownNumber = number;
            Mesh = mesh;
            FullDetailFaces = fullFaces;
            ShownDetail = detail;
            ShownSettings = settings;
            if (shaped)
            {
                Shapes = prisms;
                ShapesOf = settings;
            }

            if ((_forced || !_view.ShowingPiece) && Show(mesh, settings, _frameWanted))
            {
                Built?.Invoke(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            if (!shaped && WantShapes)
            {
                // Asked for while this build ran.
                _ = WorkOutShapesAsync();
            }
        }
    }

    // A prop's shapes you can walk into, ready for the view; null when it has none.
    private CollisionPrisms? ShapesFor(IMeshRecipe recipe, Settings settings, string? replaces, CancellationToken cancel)
    {
        IReadOnlyList<CollisionShape> shapes = ShapeBuilder is { } own ? own(settings) : recipe.Collision(settings, cancel, replaces);
        return shapes.Count > 0 ? CollisionPrisms.From(shapes) : null;
    }

    // Works out the shapes of the rock shown, off the window's thread, when they are wanted and not worked out yet. One
    // runs at a time; a result for settings no longer shown is dropped, and the rock shown by then is worked out next.
    private async Task WorkOutShapesAsync()
    {
        if (_shaping || !WantShapes || !Active || _disposed || ShownSettings is not Settings s || s.Equals(ShapesOf))
        {
            return;
        }

        _shaping = true;
        IMeshRecipe recipe = Recipe;
        CancellationToken cancel = _jobs.Token;
        CollisionPrisms? prisms;
        bool again = false;
        try
        {
            prisms = await Task.Run(() => ShapesFor(recipe, s, ShapesKey, cancel));
            _shapeDrops = 0;
        }
        catch (OperationCanceledException e)
        {
            if (!cancel.IsCancellationRequested && ++_shapeDrops >= MostDropsInARow)
            {
                _shapeDrops = 0;
                if (Active && !_disposed)
                {
                    Failed?.Invoke($"The {Kind(recipe)}'s shapes you can walk into could not be worked out: {e.Message}");
                }

                return;
            }

            // Stopped or dropped: the rock shown now (if it is still wanted) gets its shapes all the same.
            again = true;
            prisms = null;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (Active && !_disposed)
            {
                Failed?.Invoke($"The {Kind(recipe)}'s shapes you can walk into could not be worked out: {e.Message}");
            }

            return;
        }
        finally
        {
            _shaping = false;
        }

        if (again)
        {
            _ = WorkOutShapesAsync();
            return;
        }

        if (!Active || _disposed)
        {
            return;
        }

        if (s.Equals(ShownSettings) && !s.Equals(ShapesOf))
        {
            Shapes = prisms;
            ShapesOf = s;
            ShapesReady?.Invoke();
        }

        // A newer rock shown meanwhile, built without its shapes, is worked out next.
        _ = WorkOutShapesAsync();
    }

    // The rock at a detail level, its triangles at full detail, whether that count is this rock's own, and (when asked
    // for) its shapes you can walk into. Below full detail the full count comes with the build (the recipe builds the
    // full rock too, for its count only); while a button is held (a drag) the last count is kept instead, and counted
    // again once it is let go, and settings already counted are not counted again.
    private (ColouredMesh Mesh, int FullFaces, bool Exact, bool Shaped, CollisionPrisms? Shapes) BuildAt(
        IMeshRecipe recipe, Settings settings, double detail, Settings? countedFor, int counted, bool keepCount, bool shapes, CancellationToken cancel)
    {
        CollisionPrisms? prisms = shapes ? ShapesFor(recipe, settings, replaces: null, cancel) : null;
        if (Far && FarCard is { } card)
        {
            // The Far away level: the card the export makes, its pictures drawn from the full prop.
            ColouredMesh whole = Full(recipe, settings, BuildPurpose.View, BuildKey, cancel);
            return (card(whole) ?? whole, whole.Indices.Length / 3, true, shapes, prisms);
        }

        if (detail >= 1)
        {
            ColouredMesh full = Full(recipe, settings, BuildPurpose.View, BuildKey, cancel);
            return (full, full.Indices.Length / 3, true, shapes, prisms);
        }

        bool countFull = !keepCount && !settings.Equals(countedFor);
        ColouredMesh level;
        int? faces;
        if (LevelBuilder is null && Builder is null)
        {
            (level, faces) = recipe.BuildCounted(settings, detail, countFull, BuildPurpose.View, cancel, BuildKey);
        }
        else
        {
            level = LevelBuilder is { } own ? own(settings, detail) : recipe.Build(settings, detail, BuildPurpose.View, cancel, BuildKey);
            faces = countFull ? Full(recipe, settings, BuildPurpose.View, BuildKey, cancel).Indices.Length / 3 : null;
        }

        if (settings.Equals(countedFor))
        {
            return (level, counted, true, shapes, prisms);
        }

        return faces is int fullFaces ? (level, fullFaces, true, shapes, prisms) : (level, counted, false, shapes, prisms);
    }

    // What the messages call the recipe's prop: "rock" or "cliff".
    private static string Kind(IMeshRecipe recipe) => recipe.Label.ToLowerInvariant();

    // The prop at full detail: the tests' own builder when they gave one, else the recipe's.
    private ColouredMesh Full(IMeshRecipe recipe, Settings settings, BuildPurpose purpose, string replaces, CancellationToken cancel) =>
        Builder is { } own ? own(settings) : recipe.Build(settings, 1, purpose, cancel, replaces);

    // The triangles of the prop at full detail, for a count after a drag.
    private int CountFull(IMeshRecipe recipe, Settings settings, CancellationToken cancel) =>
        Builder is { } own
            ? own(settings).Indices.Length / 3
            : recipe.BuildCounted(settings, 1, countFull: true, BuildPurpose.Count, cancel, CountKey).FullFaces ?? 0;

    // Counts the full-detail triangles of the rock shown, when a drag left them uncounted: after the builds under way.
    private async Task RecountAsync()
    {
        while (!_running.IsCompleted)
        {
            await _running;
        }

        if (!Active || _disposed || PointerHeld || ShownSettings is not Settings s || s.Equals(_countedSettings))
        {
            return;
        }

        int faces;
        IMeshRecipe recipe = Recipe;
        CancellationToken cancel = _jobs.Token;
        try
        {
            faces = await Task.Run(() => CountFull(recipe, s, cancel));
        }
        catch (OperationCanceledException)
        {
            // Stopped, or dropped by a newer count of this view: that one counts.
            return;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed?.Invoke($"The {Kind(recipe)}'s faces could not be counted: {e.Message}");
            return;
        }

        if (Active && !_disposed && s.Equals(ShownSettings))
        {
            _countedSettings = s;
            _countedFaces = faces;
            FullDetailFaces = faces;
            Counted?.Invoke();
        }
    }

    // True when it is shown; a mesh the view refuses is said through Failed.
    private bool Show(ColouredMesh mesh, Settings settings, bool frame)
    {
        _forced = false;
        _frameWanted = false;
        try
        {
            _view.ShowRock(Decorated(mesh), frame);
        }
        catch (ArgumentException e)
        {
            Failed?.Invoke($"The {Kind(Recipe)} cannot be shown: {e.Message}");
            return false;
        }

        Showed?.Invoke(mesh, settings);
        return true;
    }
}
