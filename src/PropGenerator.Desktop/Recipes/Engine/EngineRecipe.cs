using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Scenes;
using PropGenerator.Engine;
using PropGenerator.Nature.Export;

namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// One of easy-props' kinds as the app makes it, from easy-props' own code run on its engine: its settings, starting
/// points and rules from <see cref="EngineKindData"/>, its meshes, shapes and GLB files from the engine's build thread
/// (<see cref="EngineMeshes"/>), and its part scenes from the engine too (<see cref="EnginePartScenes"/>: its components,
/// and every part edit one of upstream's scene ops). The build, count, export and shapes block on an engine job, so they
/// are refused on the UI thread; every other member answers from cached data or the small rules engine.
/// </summary>
internal sealed class EngineRecipe : IMeshRecipe
{
    // The style profile every kind is built in: its painted look, as easy-props' default.
    private const string Style = "low-poly";

    // Gives a call with no replace key a key of its own, so it replaces nothing (EngineThread's default live key would
    // let any two keyless builds drop each other).
    private static long _ownKeys;

    private readonly EnginePartScenes _scenes;
    private readonly EngineHost _host;
    private readonly Lazy<EngineKindData> _data;

    // What upstream fills in an old file of this kind: a constant per generator, asked once (not cached on failure).
    private EngineEarlierLooks? _earlier;
    private int _earlierAsked;
    private long _earlierTicks;

    /// <summary>Makes the recipe of one of easy-props' kinds.</summary>
    /// <param name="kind">What is ours about the kind.</param>
    /// <param name="host">The engine host it builds on (the app's <see cref="EngineHost.Default"/>).</param>
    internal EngineRecipe(EngineKind kind, EngineHost host)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(host);
        Kind = kind;
        _host = host;
        _scenes = new EnginePartScenes(kind, () => Specs, host);

        // Publication only: a read that failed (the engine not loaded yet) is asked again next time.
        _data = new(() => new EngineKindData(kind, host.Catalog, host.Rules), LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>Gets what is ours about the kind.</summary>
    internal EngineKind Kind { get; }

    /// <inheritdoc/>
    public string Id => Kind.Id;

    /// <inheritdoc/>
    public string Label => Kind.Label;

    /// <inheritdoc/>
    public string ExportName => Kind.ExportName;

    /// <inheritdoc/>
    public int Version => Data.Version;

    /// <inheritdoc/>
    public IReadOnlyList<ParamSpec> Specs => Data.Specs;

    /// <inheritdoc/>
    public IReadOnlyList<StartingPoint> StartingPoints => Data.StartingPoints;

    /// <inheritdoc/>
    public IReadOnlyList<double> DetailShares => Data.DetailShares;

    /// <inheritdoc/>
    public WindSway? Wind => Kind.Wind;

    /// <inheritdoc/>
    public bool LowPoly => Data.LowPoly;

    /// <inheritdoc/>
    public double PictureDetail => Kind.PictureDetail;

    /// <inheritdoc/>
    public IReadOnlyList<OwnColourGroup> OwnColourGroups => Data.OwnColourGroups;

    /// <inheritdoc/>
    public EngineStamp? Engine
    {
        get
        {
            try
            {
                EngineInfo info = _host.Catalog.Info;
                return new EngineStamp(info.Tag, info.Commit);
            }
            catch (Exception e) when (e is EngineException or ObjectDisposedException)
            {
                return null;
            }
        }
    }

    /// <summary>Gets how many times the recipe asked the engine for its earlier looks (once a session when it answers).</summary>
    internal int EarlierLooksAsked => Volatile.Read(ref _earlierAsked);

    /// <summary>Gets how long those asks took in all.</summary>
    internal TimeSpan EarlierLooksTime => TimeSpan.FromTicks(Interlocked.Read(ref _earlierTicks));

    private EngineKindData Data => _data.Value;

    /// <summary>
    /// Fills what a saved prop of this kind lacks as easy-props' <c>withEarlierLooks</c> does when it opens a file
    /// (E2): each setting upstream gives an old file, missing from the settings, and from each kept part's <c>rock</c>
    /// in the part edits. Upstream's answer is a constant per generator, asked of the rules engine once a session; the
    /// fill itself is C#, so a file of 200 undo steps costs one engine call.
    /// </summary>
    /// <param name="s">The settings as saved.</param>
    /// <returns>The settings, filled.</returns>
    /// <exception cref="WorkFileException">The engine could not say what an old file looked like.</exception>
    public Settings EarlierLooks(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        EngineEarlierLooks looks = EarlierLooksOfKind();
        if (looks.IsEmpty)
        {
            return s;
        }

        Settings filled = s;
        foreach ((string key, JsonElement value) in looks.Asset)
        {
            if (!s.Values.ContainsKey(key))
            {
                filled = filled.With(key, value);
            }
        }

        if (looks.Rock.Count > 0 && s.Values.TryGetValue(SettingsPanel.PartEditsKey, out JsonElement edits)
            && JsonNode.Parse(edits.GetRawText()) is JsonObject parts && parts["kept"] is JsonArray kept)
        {
            bool changed = false;
            foreach (JsonNode? component in kept)
            {
                if (component?["parameters"] is JsonObject parameters && parameters["rock"] is JsonObject rock)
                {
                    changed |= looks.FillRock(rock);
                }
            }

            if (changed)
            {
                filled = filled.With(SettingsPanel.PartEditsKey, JsonSerializer.SerializeToElement(parts));
            }
        }

        return filled;
    }

    // Upstream's earlier looks of this kind, asked once; a failure is not kept, so the next open asks again.
    private EngineEarlierLooks EarlierLooksOfKind()
    {
        if (Volatile.Read(ref _earlier) is { } known)
        {
            return known;
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            EngineEarlierLooks asked = _host.Rules.EarlierLooksOf(Id);
            return Interlocked.CompareExchange(ref _earlier, asked, null) ?? asked;
        }
        catch (Exception e) when (e is EngineException or TimeoutException or OperationCanceledException or ObjectDisposedException)
        {
            throw new WorkFileException($"easy-props' engine could not say how a {Label.ToLowerInvariant()} saved by an earlier version looked: {e.Message}", e);
        }
        finally
        {
            _ = Interlocked.Increment(ref _earlierAsked);
            _ = Interlocked.Add(ref _earlierTicks, Stopwatch.GetElapsedTime(start).Ticks);
        }
    }

    /// <inheritdoc/>
    public bool FarAway(Settings s) => Data.FarAway(s);

    /// <inheritdoc/>
    public IReadOnlyList<ShownRow>? ShownRows(Settings s) => Data.ShownRows(s);

    /// <inheritdoc/>
    public IReadOnlyCollection<string> HiddenKeys(Settings s) => Data.HiddenKeys(s);

    /// <inheritdoc/>
    public Settings Adjust(Settings before, Settings after) => Data.Adjust(before, after);

    /// <summary>
    /// Builds the prop on the engine, in the queue its purpose says (<see cref="KindOf"/>), as one coloured mesh, Z-up,
    /// in metres. Blocks on the engine's job, so it is refused on the UI thread.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="detail">The share of full detail.</param>
    /// <param name="purpose">What the build is for.</param>
    /// <param name="cancel">Stops the build.</param>
    /// <param name="replaces">The caller's replace key, or null to replace nothing.</param>
    /// <returns>The mesh.</returns>
    /// <exception cref="InvalidOperationException">Called on the UI thread.</exception>
    /// <exception cref="EngineException">The build failed in the engine.</exception>
    /// <exception cref="OperationCanceledException">The build was stopped, or dropped for a newer one with its key.</exception>
    public ColouredMesh Build(Settings s, double detail, BuildPurpose purpose = BuildPurpose.View, CancellationToken cancel = default, string? replaces = null) =>
        BuildCounted(s, detail, countFull: false, purpose, cancel, replaces).Mesh;

    /// <summary>
    /// Builds the prop and, when asked, has the engine count its faces at full detail in the same job (proxies left
    /// out). Blocks on the engine's job, so it is refused on the UI thread.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="detail">The share of full detail.</param>
    /// <param name="countFull">True to count the full-detail faces as well.</param>
    /// <param name="purpose">What the build is for.</param>
    /// <param name="cancel">Stops the build.</param>
    /// <param name="replaces">The caller's replace key, or null to replace nothing.</param>
    /// <returns>The mesh, and the full-detail faces (null when not asked).</returns>
    /// <exception cref="InvalidOperationException">Called on the UI thread.</exception>
    /// <exception cref="EngineException">The build failed in the engine.</exception>
    /// <exception cref="OperationCanceledException">The build was stopped, or dropped for a newer one with its key.</exception>
    public (ColouredMesh Mesh, int? FullFaces) BuildCounted(Settings s, double detail, bool countFull, BuildPurpose purpose, CancellationToken cancel, string? replaces = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        OffTheUiThread(nameof(Build));
        EngineJobKind kind = KindOf(purpose);
        string scene = SceneJson(s);
        EngineThread builds = _host.Builds;
        EngineMesh mesh = builds.BuildAsync(scene, Style, detail, kind, countFull, KeyOf(kind, replaces), cancel).GetAwaiter().GetResult();
        ColouredMesh drawn = EngineMeshes.ToColoured(mesh, _host.Textures, kind, cancel, PieceFrames.Of(s), Kind.Wind?.ByPart == true);
        return (drawn, countFull ? mesh.FullFaces ?? (drawn.Indices.Length / 3) : null);
    }

    /// <summary>Builds the prop's GLB file with upstream's export (one level, no collision), on the engine's Export turn.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="modelName">The model's name, already safe as a file name.</param>
    /// <param name="options">How to export it (null: <see cref="ExportOptions.Default"/>): look, picture size and set.</param>
    /// <param name="cancel">Stops the export.</param>
    /// <returns>The bytes of the .glb.</returns>
    /// <exception cref="InvalidOperationException">Called on the UI thread.</exception>
    /// <exception cref="EngineException">The export failed in the engine.</exception>
    /// <exception cref="OperationCanceledException">The export was stopped.</exception>
    public byte[] Glb(Settings s, string modelName, ExportOptions? options = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(s);
        OffTheUiThread(nameof(Glb));
        return EngineMeshes.Glb(_host.Builds, SceneJson(s), modelName, options ?? ExportOptions.Default, cancel);
    }

    /// <summary>
    /// Every file of the prop's export package (its .glb and its asset description), as upstream's batch writes them
    /// into a set's folder: one job on the engine's Export turn; the scene is made on the pool.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="name">The name the files are written under, already export-safe (upstream's item name).</param>
    /// <param name="options">The look, picture size and set.</param>
    /// <param name="cancel">Stops the export.</param>
    /// <returns>The files.</returns>
    /// <exception cref="EngineException">The export failed in the engine.</exception>
    /// <exception cref="OperationCanceledException">The export was stopped.</exception>
    internal async Task<IReadOnlyList<EngineFile>> ExportFilesAsync(Settings s, string name, ExportOptions options, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(options);
        string scene = await Task.Run(() => SceneJson(s), cancel).ConfigureAwait(false);
        return await _host.Builds.ExportAsync(scene, Style, EngineMeshes.ExportSettings(name, options), null, cancel).ConfigureAwait(false);
    }

    /// <summary>The shapes a player bumps into, as upstream's export writes them, in the prop's own Y-up space.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="cancel">Stops the work.</param>
    /// <param name="replaces">The caller's replace key, or null to replace nothing.</param>
    /// <returns>The shapes; empty when the prop has none.</returns>
    /// <exception cref="InvalidOperationException">Called on the UI thread.</exception>
    /// <exception cref="EngineException">The engine failed.</exception>
    /// <exception cref="OperationCanceledException">The work was stopped, or dropped for a newer one with its key.</exception>
    public IReadOnlyList<CollisionShape> Collision(Settings s, CancellationToken cancel = default, string? replaces = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        OffTheUiThread(nameof(Collision));
        EngineCollision collision = _host.Builds.CollisionAsync(SceneJson(s), Style, EngineJobKind.Live, replaces, cancel).GetAwaiter().GetResult();
        return EngineMeshes.ToShapes(collision);
    }

    /// <summary>The prop as easy-props' scene holds it: the components its generator makes, with the user's part edits folded in.</summary>
    /// <param name="s">The settings, with the part edits if there are any.</param>
    /// <returns>The scene.</returns>
    public Scene SceneOf(Settings s) => _scenes.SceneOf(s);

    /// <summary>The settings with the parts as this scene has them.</summary>
    /// <param name="s">The settings the scene was made from.</param>
    /// <param name="scene">The scene after a part edit.</param>
    /// <returns>The settings to keep.</returns>
    public Settings WithScene(Settings s, Scene scene) => _scenes.WithScene(s, scene);

    /// <summary>
    /// Why what these settings carry cannot be used: a piece's tree text past any tree's
    /// (<see cref="EngineKinds.MaxTreeChars"/>), which is never sent and is left out on open
    /// (<see cref="PartEditsSettings.Without"/>), or part edits that cannot be (see <see cref="EditsProblem"/>).
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <returns>The problem, or null.</returns>
    public string? EditsProblem(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (Kind.CarriedKeys.Contains(EngineKinds.TreeKey) && PartEditsSettings.OversizedTree(s) is int length)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"The tree it was made from is {length} characters long, more than any tree's ({EngineKinds.MaxTreeChars}), so it is made from the default tree.");
        }

        return _scenes.EditsProblem(s);
    }

    /// <summary>
    /// A prop's scene as these settings: the defaults, then each of the scene's values the panel draws or the kind
    /// carries (a piece's tree), and its seed. How a piece made from a tree (upstream's <c>makePieces</c>) becomes a prop.
    /// </summary>
    /// <param name="sceneJson">The scene: <c>{generatorId, seed, parameters}</c>, as the engine gives a piece.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentException">The scene is another kind's, or has no parameters.</exception>
    /// <exception cref="JsonException">The scene is not JSON.</exception>
    internal Settings SettingsOf(string sceneJson)
    {
        ArgumentNullException.ThrowIfNull(sceneJson);
        using var scene = JsonDocument.Parse(sceneJson);
        JsonElement root = scene.RootElement;
        if (!root.TryGetProperty("generatorId", out JsonElement id) || id.GetString() != Id)
        {
            throw new ArgumentException($"The scene is not a {Label.ToLowerInvariant()}'s.", nameof(sceneJson));
        }

        if (!root.TryGetProperty("parameters", out JsonElement parameters) || parameters.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The scene has no parameters.", nameof(sceneJson));
        }

        IReadOnlyList<ParamSpec> specs = Specs;
        var keep = new HashSet<string>(specs.Select(spec => spec.Key).Concat(Kind.CarriedKeys), StringComparer.Ordinal);
        JsonElement mine = JsonSerializer.SerializeToElement(parameters.EnumerateObject().Where(p => keep.Contains(p.Name)).ToDictionary(p => p.Name, p => p.Value));
        Settings s = EngineSpecs.WithValues(Settings.Defaults(specs), mine, specs);
        return root.TryGetProperty("seed", out JsonElement seed) && seed.ValueKind == JsonValueKind.Number ? s.With("seed", seed.GetDouble()) : s;
    }

    /// <summary>
    /// The controls Make it fit may lower for these settings (upstream's <c>fitControlsOf</c>, on the rules engine, so
    /// the UI thread may ask). Upstream reads the generator and its values only, so the scene asked about has no
    /// components and the kind's own seed: a new seed or a part edit asks nothing new of the rules cache.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <returns>The controls, in upstream's order.</returns>
    /// <exception cref="EngineException">The rules engine failed.</exception>
    internal IReadOnlyList<EngineFitControl> FitControls(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return _host.Rules.FitControls(EngineScenes.Of(Kind, ValuesJson(s), Kind.DefaultSeed, null));
    }

    /// <summary>
    /// The prop's damage settings for these values (upstream's <c>damageSettings</c>, from the rules engine, cached):
    /// none for a prop that cannot be damaged.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <returns>The settings, in upstream's order.</returns>
    /// <exception cref="EngineException">The rules engine failed.</exception>
    internal IReadOnlyList<EngineDamageSetting> DamageSettings(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return _host.Rules.DamageSettings(Id, ValuesJson(s));
    }

    /// <summary>Gets the rules engine the recipe asks its quick questions of.</summary>
    internal RulesEngine Rules => _host.Rules;

    /// <summary>
    /// Upstream's Make it fit on these settings (their part edits folded in), one job on the engine's export turn; the
    /// scene is made on the pool, so nothing of it runs on the caller's thread.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="budget">The faces the prop may have.</param>
    /// <param name="deadline">How long it may try: null for upstream's 5 s, <see cref="Timeout.InfiniteTimeSpan"/> for no limit.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>What it changed and the faces it came to.</returns>
    /// <exception cref="EngineException">The engine failed.</exception>
    /// <exception cref="OperationCanceledException">It was stopped.</exception>
    internal async Task<EngineFit> MakeItFitAsync(Settings s, int budget, TimeSpan? deadline, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(s);
        string scene = await Task.Run(() => SceneJson(s), cancel).ConfigureAwait(false);
        return await _host.Builds.MakeItFitAsync(scene, Style, budget, EngineJobKind.Export, deadline, cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// One of upstream's scene operations on the prop's parts, on the calling thread (the UI thread may call it: these
    /// touch one part). The heavy ones (<see cref="SceneOp.Heavy"/>: tree, seed) are refused here; see <see cref="ApplyAsync"/>.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="op">The operation.</param>
    /// <returns>The scene after it, or null when upstream refused (left the prop as it was).</returns>
    /// <exception cref="InvalidOperationException">The op is a heavy one.</exception>
    /// <exception cref="EngineException">The engine failed.</exception>
    /// <exception cref="TimeoutException">The engine did not answer in time.</exception>
    public Scene? Apply(Settings s, SceneOp op) => _scenes.Apply(s, op);

    /// <summary>
    /// Any of upstream's scene operations; the heavy ones (tree, seed) run on the engine's thread, so the caller keeps
    /// the window usable meanwhile, as it does for a rebuild.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="op">The operation.</param>
    /// <param name="cancel">Stops a heavy op.</param>
    /// <returns>The scene after it, or null when upstream refused.</returns>
    public Task<Scene?> ApplyAsync(Settings s, SceneOp op, CancellationToken cancel = default) => _scenes.ApplyAsync(s, op, cancel);

    /// <summary>What can be done to one part: is it a painted tree's, a heavy limb, and can it break now.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="componentId">The part.</param>
    /// <returns>The facts.</returns>
    /// <exception cref="EngineException">The engine failed.</exception>
    public EnginePartFacts Facts(Settings s, string componentId) => _scenes.Facts(s, componentId);

    /// <summary>Gets how many of upstream's scene ops the part edits have run (the drag's cost, for the tests).</summary>
    internal int SceneOpsRun => _scenes.OpsRun;

    /// <summary>Gets how long those ops took in the engine, in all.</summary>
    internal TimeSpan SceneOpTime => _scenes.OpTime;

    /// <summary>The scene a part edit is run on: the kind's values and seed, and the scene's components as they are now.</summary>
    /// <param name="s">The settings.</param>
    /// <returns>The scene JSON.</returns>
    internal string OpSceneJson(Settings s) => _scenes.OpSceneJson(s);

    /// <summary>The values the engine reads from these settings, a JSON object (<see cref="EngineSpecs.ValuesJson"/>).</summary>
    /// <param name="s">The settings.</param>
    /// <returns>The values.</returns>
    internal string ValuesJson(Settings s) => EngineSpecs.ValuesJson(s, Kind, Specs);

    /// <summary>The engine's queue a build for this purpose goes in.</summary>
    /// <param name="purpose">What the build is for.</param>
    /// <returns>The view's own builds, counts and shapes go Live; the others their own turns.</returns>
    internal static EngineJobKind KindOf(BuildPurpose purpose) => purpose switch
    {
        BuildPurpose.Export => EngineJobKind.Export,
        BuildPurpose.ShowMore => EngineJobKind.ShowMore,
        BuildPurpose.Picture => EngineJobKind.Thumbnail,
        _ => EngineJobKind.Live,
    };

    /// <summary>
    /// The scene the engine builds these settings from: the kind's values and seed, and, when the settings hold part
    /// edits, the engine's components with the edits folded in.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <returns>The scene JSON.</returns>
    internal string SceneJson(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        uint seed = s.Values.ContainsKey("seed") ? s.GetSeed("seed") : Kind.DefaultSeed;
        Scene? edited = s.Values.ContainsKey(SettingsPanel.PartEditsKey) ? _scenes.SceneOf(s) : null;
        return EngineScenes.Of(Kind, EngineSpecs.ValuesJson(s, Kind, Specs), seed, edited);
    }

    // A live build with no key of the caller's gets one of its own; the other turns replace nothing without one.
    private static string? KeyOf(EngineJobKind kind, string? replaces) =>
        replaces ?? (kind == EngineJobKind.Live ? "own:" + Interlocked.Increment(ref _ownKeys).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);

    // A real check (not Debug.Assert, compiled out in Release and fatal in a test host): the UI thread never waits on the
    // engine. The UI thread is the one running Avalonia's synchronization context (the app's, or a headless test
    // session's); a pool thread never has it. Not Dispatcher.UIThread.CheckAccess() nor Dispatcher.FromThread: while
    // no UI thread is set up (between headless sessions), reading Dispatcher.UIThread on a pool thread makes a
    // dispatcher for that thread and claims it as the UI thread, so both would answer true there.
    private void OffTheUiThread(string member)
    {
        if (SynchronizationContext.Current is AvaloniaSynchronizationContext)
        {
            throw new InvalidOperationException($"The {Label.ToLowerInvariant()}'s {member} waits on easy-props' engine, so it is never called on the UI thread.");
        }
    }
}
