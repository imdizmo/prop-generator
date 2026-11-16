using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;

namespace PropGenerator.Engine;

/// <summary>
/// easy-props' bundle in one V8 runtime: the shims and <c>engine.js</c> loaded, and typed calls on its global
/// <c>EasyProps</c> that return plain C# data. Not thread-safe: one thread calls it at a time (see
/// <see cref="EngineThread"/> and <see cref="RulesEngine"/>).
/// <para>
/// Every call is bounded. A promise is driven to its end by pumping the shims' timer queue (<c>__runTimers()</c>)
/// on the calling thread; a call that is cancelled or runs past <see cref="EngineOptions.JobTimeout"/> sees the
/// cancel flag at its next yield, and is interrupted if it has not stopped after <see cref="EngineOptions.CancelGrace"/>.
/// A failed call's leftover timers are dropped, so they never run inside the next call.
/// A call past the heap cap throws <see cref="EngineOutOfMemoryException"/> and the runtime is thrown away; so is a
/// runtime grown past <see cref="EngineOptions.RecycleBytes"/>, after its call (once its result has been read out). The
/// next call loads a fresh one.
/// </para>
/// </summary>
public sealed class EasyPropsEngine : IDisposable
{
    private const string Api = "EasyProps.";

    private readonly EngineOptions _options;
    private readonly IReadOnlyList<EngineScript> _scripts;
    private readonly HostBridge _bridge;
    private readonly Lock _running = new();
    private V8ScriptEngine? _v8;
    private long _jobs;
    private long _job;
    private bool _broken;
    private bool _disposed;

    /// <summary>Loads the bundle.</summary>
    /// <param name="options">Scripts, caps and limits; null for the embedded bundle and the default caps.</param>
    /// <exception cref="EngineException">There is no bundle, or it failed to load.</exception>
    public EasyPropsEngine(EngineOptions? options = null)
    {
        _options = options ?? new EngineOptions();
        _scripts = _options.Scripts ?? EngineOptions.EmbeddedScripts();
        if (_scripts.Count == 0)
        {
            throw new EngineException("easy-props' bundle is not embedded (Resources/engine.js; see tools/engine).");
        }

        _bridge = new HostBridge(_options.Log);
        Load();
    }

    /// <summary>How many runtimes this engine has loaded: 1, plus one after each recovery or recycle.</summary>
    public int Generation { get; private set; }

    /// <summary>The bundle's version and generators.</summary>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The info.</returns>
    public EngineInfo Info(CancellationToken cancel = default) =>
        ScriptData.Parse<EngineInfo>(Call(Api + "info", cancel), "info");

    /// <summary>Every preset (retired ones left out).</summary>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The presets and their headings.</returns>
    public EnginePresets Presets(CancellationToken cancel = default) =>
        ScriptData.Parse<EnginePresets>(Call(Api + "presets", cancel), "presets");

    /// <summary>One preset by id, retired ones included.</summary>
    /// <param name="id">The preset's id.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The preset, or null when there is none.</returns>
    public EnginePreset? Preset(string id, CancellationToken cancel = default) =>
        Call(Api + "preset", cancel, id) is string json ? JsonSerializer.Deserialize<EnginePreset>(json, ScriptData.Json) : null;

    /// <summary>A generator's parameter definitions.</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>Its specs and colour rows.</returns>
    public EngineParameters Parameters(string generatorId, CancellationToken cancel = default) =>
        ScriptData.Parse<EngineParameters>(Call(Api + "parameters", cancel, generatorId), "parameters");

    /// <summary>The rows the panel shows for these values.</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="valuesJson">The values, as a JSON object.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The rows, in order.</returns>
    public IReadOnlyList<EngineShownRow> Shown(string generatorId, string valuesJson, CancellationToken cancel = default) =>
        ScriptData.Parse<List<EngineShownRow>>(Call(Api + "shown", cancel, generatorId, valuesJson), "shown");

    /// <summary>What else changes when the values change (such as a palette's colours).</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="beforeJson">The values before.</param>
    /// <param name="afterJson">The values after.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The patch: key to new value.</returns>
    public IReadOnlyDictionary<string, JsonElement> Adjust(string generatorId, string beforeJson, string afterJson, CancellationToken cancel = default) =>
        ScriptData.Parse<Dictionary<string, JsonElement>>(Call(Api + "adjust", cancel, generatorId, beforeJson, afterJson), "adjust");

    /// <summary>Checks a set of values.</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="valuesJson">The values.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>Whether they are valid, the values made valid, and what was wrong.</returns>
    public EngineValidation Validate(string generatorId, string valuesJson, CancellationToken cancel = default) =>
        ScriptData.Parse<EngineValidation>(Call(Api + "validate", cancel, generatorId, valuesJson), "validate");

    /// <summary>What these values say about the prop (far card, sway).</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="valuesJson">The values.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The traits.</returns>
    public EngineTraits Traits(string generatorId, string valuesJson, CancellationToken cancel = default) =>
        ScriptData.Parse<EngineTraits>(Call(Api + "traits", cancel, generatorId, valuesJson), "traits");

    /// <summary>The scene's components, in compact form (parameters equal to the asset's written as "$asset").</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="valuesJson">The values.</param>
    /// <param name="seed">The seed.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The components, as JSON.</returns>
    public string Components(string generatorId, string valuesJson, long seed, CancellationToken cancel = default) =>
        ScriptData.Text(Call(Api + "components", cancel, generatorId, valuesJson, (double)seed), "components");

    /// <summary>Builds a scene at one detail (upstream's <c>sceneExportSource(...).buildAtDetail</c>).</summary>
    /// <param name="sceneJson">The scene: generatorId, seed, parameters, components.</param>
    /// <param name="style">The style profile (low-poly or detailed).</param>
    /// <param name="detail">The detail (1, 0.4, 0.12 for LOD0 to LOD2).</param>
    /// <param name="fullFaces">Also count the faces at full detail (<see cref="EngineMesh.FullFaces"/>).</param>
    /// <param name="cancel">Stops the build.</param>
    /// <returns>The parts.</returns>
    public EngineMesh Build(string sceneJson, string style, double detail, bool fullFaces = false, CancellationToken cancel = default) =>
        Call(Api + "build", ScriptData.Mesh, cancel, sceneJson, style, detail, fullFaces ? """{"fullFaces":true}""" : "{}");

    /// <summary>A texture's pixels by its key (bark:print:strength, relief:print, dab, leaf, white).</summary>
    /// <param name="key">The key a part's surface names.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The pixels.</returns>
    public EngineTexture Texture(string key, CancellationToken cancel = default) =>
        Call(Api + "texture", result => ScriptData.Texture(result, key), cancel, key);

    /// <summary>Upstream's export package (GLBs, textures, asset.json).</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="settingsJson">The export settings.</param>
    /// <param name="cancel">Stops the export.</param>
    /// <returns>The files.</returns>
    public IReadOnlyList<EngineFile> ExportPackage(string sceneJson, string style, string settingsJson, CancellationToken cancel = default) =>
        ExportPackage(sceneJson, style, settingsJson, null, cancel);

    /// <summary>Upstream's export package (GLBs, textures, asset.json), with its progress.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="settingsJson">The export settings.</param>
    /// <param name="progress">
    /// Gets the export's progress, a share from 0 to 1, ending at 1 when it succeeds. It is called on this engine's
    /// thread: it may only report on (post), never wait for another thread.
    /// </param>
    /// <param name="cancel">Stops the export.</param>
    /// <returns>The files.</returns>
    public IReadOnlyList<EngineFile> ExportPackage(string sceneJson, string style, string settingsJson, IProgress<double>? progress,
        CancellationToken cancel = default)
    {
        MakeRoomForAnExport();
        _bridge.JobProgress = progress;
        _bridge.LastProgress = -1;
        try
        {
            IReadOnlyList<EngineFile> files = Call(Api + "exportPackage", ScriptData.Files, cancel, sceneJson, style, settingsJson);
            if (progress is not null && _bridge.LastProgress < 1)
            {
                progress.Report(1);
            }

            return files;
        }
        finally
        {
            _bridge.JobProgress = null;
        }
    }

    /// <summary>The scene's collision: what the generator offers, and what the export writes.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The capsules.</returns>
    public EngineCollision Collision(string sceneJson, string style, CancellationToken cancel = default) =>
        ScriptData.Parse<EngineCollision>(Call(Api + "collision", cancel, sceneJson, style), "collision");

    /// <summary>The scene's pivot, in easy-props' Y-up metres.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>x, y, z.</returns>
    public IReadOnlyList<double> Pivot(string sceneJson, CancellationToken cancel = default) =>
        ScriptData.Parse<double[]>(Call(Api + "pivot", cancel, sceneJson), "pivot");

    /// <summary>
    /// One of upstream's scene operations (copy, remove, restore, position, transform, parameters, part, break, tree,
    /// seed; see <c>entry.ts</c> <c>sceneOp</c>). tree and seed make every component again.
    /// </summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="opJson">The operation: <c>{kind, componentId, ...}</c>.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The scene after it, or null when upstream refused (left the scene as it was).</returns>
    public string? SceneOp(string sceneJson, string opJson, string style, CancellationToken cancel = default) =>
        ScriptData.TextOrNull(Call(Api + "sceneOp", cancel, sceneJson, opJson, style), "sceneOp");

    /// <summary>What can be done to one part: is it painted, a heavy limb, and can it break now.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="componentId">The part.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The facts (all false for a part the scene does not have).</returns>
    public EnginePartFacts PartFacts(string sceneJson, string componentId, CancellationToken cancel = default) =>
        ScriptData.Parse<EnginePartFacts>(Call(Api + "partFacts", cancel, sceneJson, componentId), "partFacts");

    /// <summary>A scene as upstream opens a saved one (<c>parseProject</c>: earlier looks and part migration).</summary>
    /// <param name="sceneJson">The scene as saved.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The scene, filled.</returns>
    public string EarlierLooks(string sceneJson, CancellationToken cancel = default) =>
        ScriptData.Text(Call(Api + "earlierLooks", cancel, sceneJson), "earlierLooks");

    /// <summary>What <see cref="EarlierLooks"/> fills for one generator.</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The settings it fills, in the asset and in a part's rock.</returns>
    public EngineEarlierLooks EarlierLooksOf(string generatorId, CancellationToken cancel = default) =>
        ScriptData.Parse<EngineEarlierLooks>(Call(Api + "earlierLooksOf", cancel, generatorId), "earlierLooksOf");

    /// <summary>Upstream's <c>mergeGenerated</c>: freshly generated components folded into the kept ones.</summary>
    /// <param name="keptJson">The kept components.</param>
    /// <param name="generatedJson">The generated components.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The components, as JSON.</returns>
    public string MergeGenerated(string keptJson, string generatedJson, CancellationToken cancel = default) =>
        ScriptData.Text(Call(Api + "mergeGenerated", cancel, keptJson, generatedJson), "mergeGenerated");

    /// <summary>The controls Make it fit may lower for this scene.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The controls.</returns>
    public IReadOnlyList<EngineFitControl> FitControls(string sceneJson, CancellationToken cancel = default) =>
        ScriptData.Parse<List<EngineFitControl>>(Call(Api + "fitControls", cancel, sceneJson), "fitControls");

    /// <summary>The faces at full detail, as upstream's status bar counts them.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>Wood and leaf faces.</returns>
    public EngineFaceCounts CountFaces(string sceneJson, string style, CancellationToken cancel = default) =>
        ScriptData.Parse<EngineFaceCounts>(Call(Api + "countFaces", cancel, sceneJson, style), "countFaces");

    /// <summary>Upstream's Make it fit: lowers the scene's controls until it is under the budget.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="budget">The faces it may have.</param>
    /// <param name="deadline">
    /// How long it may try: null for upstream's own (5 s), <see cref="Timeout.InfiniteTimeSpan"/> for no limit (the
    /// result is then the same each time).
    /// </param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The scene and what changed.</returns>
    public EngineFit MakeItFit(string sceneJson, string style, int budget, TimeSpan? deadline = null, CancellationToken cancel = default)
    {
        string options = deadline is not { } d ? "{}"
            : d == Timeout.InfiniteTimeSpan ? """{"deadlineMs":null}"""
            : "{\"deadlineMs\":" + Math.Max(0, d.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
        return Call(Api + "makeItFit", static result => ScriptData.Fit(result), cancel, sceneJson, style, (double)budget, options);
    }

    /// <summary>A batch's names, as upstream numbers them (names only: each item's scene is made at its turn).</summary>
    /// <param name="choicesJson">The choices: <c>[{kind: preset, id} | {kind: scene, name} | {kind: versions, name, versions}]</c>.</param>
    /// <param name="variations">Variations of each preset or scene.</param>
    /// <param name="contextJson">The context: <c>{style}</c>.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The items.</returns>
    public IReadOnlyList<EngineBatchItem> BatchItems(string choicesJson, int variations, string contextJson, CancellationToken cancel = default) =>
        ScriptData.Parse<List<EngineBatchItem>>(Call(Api + "batchItems", cancel, choicesJson, (double)variations, contextJson), "batchItems");

    /// <summary>The names a typed name makes (upstream's <c>folderNameOf</c> and <c>exportNameOf</c>).</summary>
    /// <param name="name">The name as typed.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The folder and file names.</returns>
    public EngineExportNames ExportNames(string name, CancellationToken cancel = default) =>
        ScriptData.Parse<EngineExportNames>(Call(Api + "exportNames", cancel, name), "exportNames");

    /// <summary>The eight seeds of a page of Show me more.</summary>
    /// <param name="seed">The prop's seed.</param>
    /// <param name="page">The page, from 0.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The seeds.</returns>
    public IReadOnlyList<uint> PageSeeds(uint seed, int page, CancellationToken cancel = default) =>
        ScriptData.Parse<List<uint>>(Call(Api + "pageSeeds", cancel, (double)seed, (double)page), "pageSeeds");

    /// <summary>A prop's damage settings for these values; none for a prop that cannot be damaged.</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="valuesJson">The values.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The settings.</returns>
    public IReadOnlyList<EngineDamageSetting> DamageSettings(string generatorId, string valuesJson, CancellationToken cancel = default) =>
        ScriptData.DamageSettings(Call(Api + "damageSettings", cancel, generatorId, valuesJson), "damageSettings");

    /// <summary>The eight damaged versions of a page, on the prop's own seed.</summary>
    /// <param name="seed">The prop's seed.</param>
    /// <param name="page">The page, from 0.</param>
    /// <param name="settingsJson">The damage settings (<see cref="EngineDamageSetting.ToJson"/>).</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The versions.</returns>
    public IReadOnlyList<EngineVersion> PageDamages(uint seed, int page, string settingsJson, CancellationToken cancel = default) =>
        ScriptData.Parse<List<EngineVersion>>(Call(Api + "pageDamages", cancel, (double)seed, (double)page, settingsJson), "pageDamages");

    /// <summary>What a damaged version has, in its settings' words ("Snapped top, Split trunk").</summary>
    /// <param name="versionJson">The version.</param>
    /// <param name="settingsJson">The damage settings.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The words.</returns>
    public string DamageNames(string versionJson, string settingsJson, CancellationToken cancel = default) =>
        ScriptData.JsonString(Call(Api + "damageNames", cancel, versionJson, settingsJson), "damageNames");

    /// <summary>A version's name that is the same each time.</summary>
    /// <param name="versionJson">The version.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The key (the seed, then the damage).</returns>
    public string VersionKey(string versionJson, CancellationToken cancel = default) =>
        ScriptData.JsonString(Call(Api + "versionKey", cancel, versionJson), "versionKey");

    /// <summary>The prop in one version: on its seed, damaged as it says.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="versionJson">The version.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The version's scene.</returns>
    public string VersionScene(string sceneJson, string versionJson, string style, CancellationToken cancel = default) =>
        ScriptData.Text(Call(Api + "versionScene", cancel, sceneJson, versionJson, style), "versionScene");

    /// <summary>Upstream's names and help for each piece a tree can make.</summary>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The labels, in upstream's order.</returns>
    public EnginePieceLabels PieceLabels(CancellationToken cancel = default)
    {
        Dictionary<string, EnginePieceLabel> labels = ScriptData.Parse<Dictionary<string, EnginePieceLabel>>(Call(Api + "pieceLabels", cancel), "pieceLabels");
        return new EnginePieceLabels([.. labels.Select(static p => p.Value with { Kind = p.Key })]);
    }

    /// <summary>The pieces this tree can make (none for anything but a painted tree). Plans the tree: heavy.</summary>
    /// <param name="sceneJson">The tree's scene.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The kinds, in upstream's order.</returns>
    public IReadOnlyList<string> PiecesOffered(string sceneJson, CancellationToken cancel = default) =>
        ScriptData.Parse<List<string>>(Call(Api + "piecesOffered", cancel, sceneJson), "piecesOffered");

    /// <summary>Pieces of a tree, each as a prop's scene.</summary>
    /// <param name="sceneJson">The tree's scene.</param>
    /// <param name="treeName">The tree's name (the pieces are named after it).</param>
    /// <param name="kindsJson">The kinds to make: <c>["stump", ...]</c>.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The pieces.</returns>
    public IReadOnlyList<EnginePiece> MakePieces(string sceneJson, string treeName, string kindsJson, string style, CancellationToken cancel = default) =>
        Call(Api + "makePieces", static result => ScriptData.Pieces(result), cancel, sceneJson, treeName, kindsJson, style);

    /// <summary>The colour settings a set of props shares.</summary>
    /// <param name="propsJson">The props: <c>[{generatorId, parameters}]</c>.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The groups.</returns>
    public IReadOnlyList<EngineColourGroup> ColourGroups(string propsJson, CancellationToken cancel = default) =>
        ScriptData.Parse<List<EngineColourGroup>>(Call(Api + "colourGroups", cancel, propsJson), "colourGroups");

    /// <summary>The set's starting colours: each group's usual choice.</summary>
    /// <param name="groupsJson">The groups, as upstream gave them.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The colours, by group id, as JSON.</returns>
    public string UsualColours(string groupsJson, CancellationToken cancel = default) =>
        ScriptData.Text(Call(Api + "usualColours", cancel, groupsJson), "usualColours");

    /// <summary>The prop in the set's colours (made again with its colour settings changed).</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="coloursJson">The set's colours, by group id.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The scene.</returns>
    public string InSetColours(string sceneJson, string coloursJson, string style, CancellationToken cancel = default) =>
        ScriptData.Text(Call(Api + "inSetColours", cancel, sceneJson, coloursJson, style), "inSetColours");

    /// <summary>The list a set ends with (upstream's <c>setListFile</c>).</summary>
    /// <param name="name">The set's export name.</param>
    /// <param name="entriesJson">The props: <c>[{name, files: [{name, json?}]}]</c>, json a GLB's JSON chunk.</param>
    /// <param name="groupsJson">The colour groups.</param>
    /// <param name="coloursJson">The set's colours.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The file.</returns>
    public EngineFile SetList(string name, string entriesJson, string groupsJson, string coloursJson, CancellationToken cancel = default)
    {
        using var doc = JsonDocument.Parse(ScriptData.Text(Call(Api + "setList", cancel, name, entriesJson, groupsJson, coloursJson), "setList"));
        string file = doc.RootElement.GetProperty("name").GetString() ?? throw new EngineScriptException("setList returned no name.");
        string text = doc.RootElement.GetProperty("text").GetString() ?? "";
        return new EngineFile(file, System.Text.Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// The largest ArrayBuffer memory the runtime was seen holding (sampled at the end of each call, before a
    /// recycle, and at each picture an export encodes), since the engine loaded or <see cref="ResetPeak"/>.
    /// </summary>
    public long PeakExternalBytes { get; private set; }

    /// <summary>Starts <see cref="PeakExternalBytes"/> again from 0.</summary>
    public void ResetPeak() => PeakExternalBytes = 0;

    /// <summary>The runtime's memory now.</summary>
    /// <returns>Heap and external sizes.</returns>
    public EngineHeap Heap()
    {
        V8RuntimeHeapInfo h = Ready().GetRuntimeHeapInfo();
        return new EngineHeap((long)h.UsedHeapSize, (long)h.TotalHeapSize, (long)h.TotalExternalSize, (long)h.HeapSizeLimit);
    }

    /// <summary>
    /// Calls a function of the bundle by its global path (such as EasyProps.build), drives a returned promise to its
    /// end, and turns script failures into <see cref="EngineException"/>s.
    /// </summary>
    /// <param name="function">The function's path from the global object.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <param name="args">Its arguments (strings and numbers).</param>
    /// <returns>Its result (a promise's value), as ClearScript gives it; null for undefined.</returns>
    /// <remarks>
    /// A script object in the result belongs to a runtime that may be recycled as the call ends: read it inside the
    /// call with the overload that takes a reader.
    /// </remarks>
    internal object? Call(string function, CancellationToken cancel, params object?[] args) =>
        Call(function, static result => result, cancel, args);

    /// <summary>
    /// Calls a function of the bundle as <see cref="Call(string, CancellationToken, object?[])"/> does, and reads its
    /// result with <paramref name="read"/> before the call ends: a runtime grown past the recycle size is thrown away
    /// at the end of the call, and the script objects of the result with it.
    /// </summary>
    /// <typeparam name="T">What the reader makes of the result.</typeparam>
    /// <param name="function">The function's path from the global object.</param>
    /// <param name="read">Copies the result (null for undefined) out of the runtime.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <param name="args">Its arguments (strings and numbers).</param>
    /// <returns>What the reader made of the result.</returns>
    internal T Call<T>(string function, Func<object?, T> read, CancellationToken cancel, params object?[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancel.ThrowIfCancellationRequested();
        V8ScriptEngine v8 = Ready();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        long job = Interlocked.Increment(ref _jobs);
        lock (_running)
        {
            _job = job;
            v8.CancelInterrupt();
        }

        using var interrupt = new Timer(_ => InterruptIf(v8, job));
        using CancellationTokenRegistration stopping = stop.Token.Register(
            () => interrupt.Change(_options.CancelGrace, Timeout.InfiniteTimeSpan));
        stop.CancelAfter(_options.JobTimeout);
        _bridge.Stop = stop.Token;
        try
        {
            object? result = Invoke(v8, function, args);
            if (result is Task<object> task)
            {
                result = Pump(v8, task, stop.Token);
            }

            return read(result is Undefined ? null : result);
        }
        catch (Exception e) when (e is IScriptEngineException or OperationCanceledException)
        {
            throw Failure(e, function, cancel, stop.Token);
        }
        finally
        {
            lock (_running)
            {
                _job = 0;
            }

            _bridge.Stop = default;
            AfterCall();
        }
    }

    /// <summary>Evaluates a short expression in the runtime (for checks; jobs go through <see cref="Call"/>).</summary>
    /// <param name="code">The expression.</param>
    /// <returns>Its value.</returns>
    internal object? Evaluate(string code)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Ready().Evaluate(code);
    }

    /// <summary>Calls a function of the bundle by its path from the global object (such as EasyProps.build).</summary>
    /// <remarks>
    /// Every script object read on the way is disposed once used: a proxy left to the finalizer keeps its V8 object
    /// alive until then, and under load the finalizer falls behind (see <see cref="ScriptData"/>).
    /// </remarks>
    private static object? Invoke(V8ScriptEngine v8, string function, object?[] args)
    {
        string[] path = function.Split('.');
        ScriptObject target = v8.Global;
        var owned = new List<ScriptObject>();
        try
        {
            for (int i = 0; i < path.Length - 1; i++)
            {
                target = target.GetProperty(path[i]) as ScriptObject
                    ?? throw new EngineScriptException($"{function}: {path[i]} is not defined in the bundle.");
                owned.Add(target);
            }

            string name = path[^1];
            object member = target.GetProperty(name);
            (member as IDisposable)?.Dispose();
            return member is ScriptObject
                ? target.InvokeMethod(name, args)
                : throw new EngineScriptException($"{function} is not a function in the bundle.");
        }
        finally
        {
            foreach (ScriptObject o in owned)
            {
                o.Dispose();
            }
        }
    }

    /// <summary>Runs the shims' timers until the promise settles; microtasks run as each call returns.</summary>
    private static object? Pump(V8ScriptEngine v8, Task<object> task, CancellationToken stop)
    {
        using var runTimers = v8.Global.GetProperty("__runTimers") as ScriptObject;
        while (!task.IsCompleted)
        {
            stop.ThrowIfCancellationRequested();
            bool busy = runTimers?.Invoke(false) is true;
            if (!task.IsCompleted)
            {
                Thread.Sleep(busy ? 0 : 1);
            }
        }

        if (task.IsFaulted)
        {
            ExceptionDispatchInfo.Throw(task.Exception!.InnerException ?? task.Exception);
        }

        return task.IsCanceled ? throw new OperationCanceledException(stop) : task.Result;
    }

    private Exception Failure(Exception e, string function, CancellationToken cancel, CancellationToken stop)
    {
        // A fatal error leaves the runtime unusable: it is thrown away once the call has ended (AfterCall).
        bool fatal = e is IScriptEngineException { IsFatal: true };
        _broken |= fatal;
        if (!fatal)
        {
            ClearTimers();
        }

        if (cancel.IsCancellationRequested)
        {
            return new OperationCanceledException($"{function} was cancelled.", e, cancel);
        }

        if (stop.IsCancellationRequested)
        {
            return new TimeoutException($"{function} took longer than {_options.JobTimeout.TotalSeconds:0.#} s.", e);
        }

        if (fatal)
        {
            return new EngineOutOfMemoryException(
                $"{function} ran past the engine's memory cap ({_options.MaxHeapBytes >> 20} MiB); the engine was reloaded.", e);
        }

        string details = e is IScriptEngineException { ErrorDetails: { Length: > 0 } d } ? d : e.Message;
        return new EngineScriptException($"{function}: {details}", e);
    }

    /// <summary>Drops the timers a failed call left queued (shims' <c>__clearTimers()</c>), so the next call's pump never runs them.</summary>
    private void ClearTimers()
    {
        try
        {
            if (_v8?.Global.GetProperty("__clearTimers") is ScriptObject clear)
            {
                clear.Invoke(false);
            }
        }
        catch (Exception e) when (e is IScriptEngineException)
        {
            // A late interrupt from the failed call stopped it: the runtime is thrown away instead.
            _broken = true;
        }
    }

    private void InterruptIf(V8ScriptEngine v8, long job)
    {
        lock (_running)
        {
            // Only the call that asked, and only while it runs: once it has ended, its runtime may be gone.
            if (_job == job)
            {
                v8.Interrupt();
            }
        }
    }

    /// <summary>
    /// Rebuilds a runtime whose ArrayBuffers (upstream's picture caches, mostly) leave an export less than
    /// <see cref="EngineOptions.ExportHeadroomBytes"/> under the cap, so a big export never runs out of room mid-way.
    /// </summary>
    private void MakeRoomForAnExport()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_v8 is not null && _options.ExportHeadroomBytes > 0
            && (long)_v8.GetRuntimeHeapInfo().TotalExternalSize > _options.MaxArrayBufferBytes - _options.ExportHeadroomBytes)
        {
            Drop();
        }
    }

    /// <summary>Throws away a runtime grown past the recycle size, so the next call starts clean.</summary>
    private void AfterCall()
    {
        if (_broken)
        {
            _broken = false;
            Drop();
        }

        if (_v8 is null)
        {
            return;
        }

        V8RuntimeHeapInfo h = _v8.GetRuntimeHeapInfo();
        PeakExternalBytes = Math.Max(PeakExternalBytes, (long)h.TotalExternalSize);
        if ((long)(h.TotalHeapSize + h.TotalExternalSize) > _options.RecycleBytes)
        {
            Drop();
        }
    }

    private V8ScriptEngine Ready() => _v8 ?? Load();

    private V8ScriptEngine Load()
    {
        var constraints = new V8RuntimeConstraints
        {
            MaxOldSpaceSize = _options.MaxOldSpaceMiB,
            MaxArrayBufferAllocation = (ulong)_options.MaxArrayBufferBytes,
        };
        V8ScriptEngine v8 = Native(() => new V8ScriptEngine("easy-props", constraints, V8ScriptEngineFlags.EnableTaskPromiseConversion));
        try
        {
            v8.MaxRuntimeHeapSize = (UIntPtr)_options.MaxHeapBytes;
            v8.RuntimeHeapSizeSampleInterval = _options.HeapSampleInterval;
            v8.AddHostObject("__host", _bridge);
            foreach (EngineScript script in _scripts)
            {
                v8.Execute(new DocumentInfo(script.Name), script.Source);
            }

            _bridge.Attach(v8);
            _bridge.Sample = () => PeakExternalBytes = Math.Max(PeakExternalBytes, (long)v8.GetRuntimeHeapInfo().TotalExternalSize);
        }
        catch (Exception e) when (e is IScriptEngineException)
        {
            v8.Dispose();
            string details = e is IScriptEngineException { ErrorDetails: { Length: > 0 } d } ? d : e.Message;
            throw new EngineScriptException("easy-props' bundle failed to load: " + details, e);
        }
        catch
        {
            v8.Dispose();
            throw;
        }

        _v8 = v8;
        Generation++;
        return v8;
    }

    /// <summary>
    /// Makes something that needs ClearScript's native V8 library, saying a library that is missing or cannot load
    /// (ClearScript throws a bare <see cref="TypeLoadException"/>; the runtime a <see cref="DllNotFoundException"/>,
    /// <see cref="TypeInitializationException"/> or <see cref="BadImageFormatException"/>) as an
    /// <see cref="EngineException"/>, which every engine-load catch takes (the app then falls back to the C# port).
    /// </summary>
    /// <typeparam name="T">What is made.</typeparam>
    /// <param name="make">Makes it.</param>
    /// <returns>What was made.</returns>
    /// <exception cref="EngineException">The native V8 library could not be loaded.</exception>
    internal static T Native<T>(Func<T> make)
    {
        try
        {
            return make();
        }
        catch (Exception e) when (e is TypeLoadException or DllNotFoundException or TypeInitializationException or BadImageFormatException)
        {
            throw new EngineException("ClearScript's V8 library could not be loaded: " + e.Message, e);
        }
    }

    private void Drop()
    {
        _v8?.Dispose();
        _v8 = null;
    }

    /// <summary>Frees the runtime.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Drop();
    }
}
