using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PropGenerator.Engine;

/// <summary>
/// A second, small runtime for the panel's quick synchronous questions (info, presets, shown rows, adjust, validate,
/// traits, components), so they never wait behind a build on <see cref="EngineThread"/>. Callers on any thread take turns.
/// <para>
/// It also runs upstream's cheap scene calls: the part edits that touch one part (<see cref="SceneOp"/>; never the
/// tree and seed ops, which make every part again and go to <see cref="EngineThread.SceneOpAsync"/>), part facts,
/// earlier looks, fit controls, piece labels, set colours, damage and version names. The small answers that depend
/// only on their arguments are kept in an LRU of <see cref="CacheCap"/> entries, keyed by a SHA-256 of the
/// arguments (a piece's settings carry its whole tree), never holding the lock across an engine call.
/// </para>
/// </summary>
public sealed class RulesEngine : IDisposable
{
    /// <summary>How many cached answers are kept.</summary>
    public const int CacheCap = 64;

    private readonly Lock _lock = new();
    private readonly Lock _cacheGate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, object Value)>> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, object Value)> _recent = new();
    private readonly EasyPropsEngine _engine;

    /// <summary>Loads the bundle into its own runtime.</summary>
    /// <param name="options">Scripts, caps and limits; null for the embedded bundle and <see cref="EngineOptions.ForRules"/>.</param>
    public RulesEngine(EngineOptions? options = null) => _engine = new EasyPropsEngine(options ?? EngineOptions.ForRules);

    /// <summary>The bundle's version and generators.</summary>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The info.</returns>
    public EngineInfo Info(CancellationToken cancel = default)
    {
        lock (_lock)
        {
            return _engine.Info(cancel);
        }
    }

    /// <summary>Every preset (retired ones left out), and the order of their headings.</summary>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The presets.</returns>
    public EnginePresets Presets(CancellationToken cancel = default)
    {
        lock (_lock)
        {
            return _engine.Presets(cancel);
        }
    }

    /// <summary>One preset by id, retired ones included (see <see cref="EnginePreset.Retired"/>).</summary>
    /// <param name="id">The preset's id.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The preset, or null when there is none.</returns>
    public EnginePreset? Preset(string id, CancellationToken cancel = default)
    {
        lock (_lock)
        {
            return _engine.Preset(id, cancel);
        }
    }

    /// <summary>The rows the panel shows for these values.</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="valuesJson">The values.</param>
    /// <returns>The rows.</returns>
    public IReadOnlyList<EngineShownRow> Shown(string generatorId, string valuesJson)
    {
        lock (_lock)
        {
            return _engine.Shown(generatorId, valuesJson);
        }
    }

    /// <summary>What else changes when the values change.</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="beforeJson">The values before.</param>
    /// <param name="afterJson">The values after.</param>
    /// <returns>The patch.</returns>
    public IReadOnlyDictionary<string, JsonElement> Adjust(string generatorId, string beforeJson, string afterJson)
    {
        lock (_lock)
        {
            return _engine.Adjust(generatorId, beforeJson, afterJson);
        }
    }

    /// <summary>Checks a set of values.</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="valuesJson">The values.</param>
    /// <returns>The check.</returns>
    public EngineValidation Validate(string generatorId, string valuesJson)
    {
        lock (_lock)
        {
            return _engine.Validate(generatorId, valuesJson);
        }
    }

    /// <summary>What these values say about the prop.</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="valuesJson">The values.</param>
    /// <returns>The traits.</returns>
    public EngineTraits Traits(string generatorId, string valuesJson)
    {
        lock (_lock)
        {
            return _engine.Traits(generatorId, valuesJson);
        }
    }

    /// <summary>The scene's components, in compact form.</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="valuesJson">The values.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The components, as JSON.</returns>
    public string Components(string generatorId, string valuesJson, long seed)
    {
        lock (_lock)
        {
            return _engine.Components(generatorId, valuesJson, seed);
        }
    }

    /// <summary>A generator's parameter definitions.</summary>
    /// <param name="generatorId">The generator.</param>
    /// <returns>Its specs and colour rows.</returns>
    public EngineParameters Parameters(string generatorId)
    {
        lock (_lock)
        {
            return _engine.Parameters(generatorId);
        }
    }

    /// <summary>How many answers are cached now (at most <see cref="CacheCap"/>).</summary>
    public int Cached
    {
        get
        {
            lock (_cacheGate)
            {
                return _cache.Count;
            }
        }
    }

    /// <summary>
    /// One of upstream's part edits (copy, remove, restore, position, transform, parameters, part, break). The tree
    /// and seed ops make every part again: they are refused here and run on <see cref="EngineThread.SceneOpAsync"/>.
    /// </summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="opJson">The operation: <c>{kind, componentId, ...}</c>.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The scene after it, or null when upstream refused.</returns>
    /// <exception cref="ArgumentException">The op is tree or seed (or names no kind).</exception>
    public string? SceneOp(string sceneJson, string opJson, string style, CancellationToken cancel = default)
    {
        string kind = OpKind(opJson);
        if (EngineThread.IsHeavyOp(kind))
        {
            throw new ArgumentException($"The {kind} op makes every part again: run it on the engine thread (EngineThread.SceneOpAsync).", nameof(opJson));
        }

        lock (_lock)
        {
            return _engine.SceneOp(sceneJson, opJson, style, cancel);
        }
    }

    /// <summary>What can be done to one part: painted, heavy, can break now.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="componentId">The part.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The facts.</returns>
    public EnginePartFacts PartFacts(string sceneJson, string componentId, CancellationToken cancel = default)
    {
        lock (_lock)
        {
            return _engine.PartFacts(sceneJson, componentId, cancel);
        }
    }

    /// <summary>A scene as upstream opens a saved one (earlier looks filled, parts migrated).</summary>
    /// <param name="sceneJson">The scene as saved.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The scene, filled.</returns>
    public string EarlierLooks(string sceneJson, CancellationToken cancel = default)
    {
        lock (_lock)
        {
            return _engine.EarlierLooks(sceneJson, cancel);
        }
    }

    /// <summary>What upstream fills in an old file of this generator (cached).</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The settings filled, in the asset and in a part's rock.</returns>
    public EngineEarlierLooks EarlierLooksOf(string generatorId, CancellationToken cancel = default) =>
        Cachedly(nameof(EarlierLooksOf), [generatorId], () => _engine.EarlierLooksOf(generatorId, cancel));

    /// <summary>Upstream's <c>mergeGenerated</c>.</summary>
    /// <param name="keptJson">The kept components.</param>
    /// <param name="generatedJson">The generated components.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The components, as JSON.</returns>
    public string MergeGenerated(string keptJson, string generatedJson, CancellationToken cancel = default)
    {
        lock (_lock)
        {
            return _engine.MergeGenerated(keptJson, generatedJson, cancel);
        }
    }

    /// <summary>The controls Make it fit may lower for this scene (cached).</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The controls.</returns>
    public IReadOnlyList<EngineFitControl> FitControls(string sceneJson, CancellationToken cancel = default) =>
        Cachedly(nameof(FitControls), [sceneJson], () => _engine.FitControls(sceneJson, cancel));

    /// <summary>Upstream's names and help for each piece (cached).</summary>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The labels.</returns>
    public EnginePieceLabels PieceLabels(CancellationToken cancel = default) =>
        Cachedly(nameof(PieceLabels), [], () => _engine.PieceLabels(cancel));

    /// <summary>The colour settings a set of props shares (cached).</summary>
    /// <param name="propsJson">The props: <c>[{generatorId, parameters}]</c>.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The groups.</returns>
    public IReadOnlyList<EngineColourGroup> ColourGroups(string propsJson, CancellationToken cancel = default) =>
        Cachedly(nameof(ColourGroups), [propsJson], () => _engine.ColourGroups(propsJson, cancel));

    /// <summary>The set's starting colours (cached).</summary>
    /// <param name="groupsJson">The groups.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The colours by group id, as JSON.</returns>
    public string UsualColours(string groupsJson, CancellationToken cancel = default) =>
        Cachedly(nameof(UsualColours), [groupsJson], () => _engine.UsualColours(groupsJson, cancel));

    /// <summary>A prop's damage settings for these values (cached).</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="valuesJson">The values.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The settings; none for a prop that cannot be damaged.</returns>
    public IReadOnlyList<EngineDamageSetting> DamageSettings(string generatorId, string valuesJson, CancellationToken cancel = default) =>
        Cachedly(nameof(DamageSettings), [generatorId, valuesJson], () => _engine.DamageSettings(generatorId, valuesJson, cancel));

    /// <summary>The eight seeds of a page of Show me more (cached).</summary>
    /// <param name="seed">The prop's seed.</param>
    /// <param name="page">The page, from 0.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The seeds.</returns>
    public IReadOnlyList<uint> PageSeeds(uint seed, int page, CancellationToken cancel = default) =>
        Cachedly(nameof(PageSeeds), [Invariant(seed), Invariant(page)], () => _engine.PageSeeds(seed, page, cancel));

    /// <summary>The eight damaged versions of a page (cached).</summary>
    /// <param name="seed">The prop's seed.</param>
    /// <param name="page">The page, from 0.</param>
    /// <param name="settingsJson">The damage settings (<see cref="EngineDamageSetting.ToJson"/>).</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The versions.</returns>
    public IReadOnlyList<EngineVersion> PageDamages(uint seed, int page, string settingsJson, CancellationToken cancel = default) =>
        Cachedly(nameof(PageDamages), [Invariant(seed), Invariant(page), settingsJson], () => _engine.PageDamages(seed, page, settingsJson, cancel));

    /// <summary>What a damaged version has, in words (cached).</summary>
    /// <param name="versionJson">The version.</param>
    /// <param name="settingsJson">The damage settings.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The words.</returns>
    public string DamageNames(string versionJson, string settingsJson, CancellationToken cancel = default) =>
        Cachedly(nameof(DamageNames), [versionJson, settingsJson], () => _engine.DamageNames(versionJson, settingsJson, cancel));

    /// <summary>A version's name that is the same each time (cached).</summary>
    /// <param name="versionJson">The version.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The key.</returns>
    public string VersionKey(string versionJson, CancellationToken cancel = default) =>
        Cachedly(nameof(VersionKey), [versionJson], () => _engine.VersionKey(versionJson, cancel));

    /// <summary>The folder and file names a typed name makes (cached).</summary>
    /// <param name="name">The name as typed.</param>
    /// <param name="cancel">Stops the call.</param>
    /// <returns>The names.</returns>
    public EngineExportNames ExportNames(string name, CancellationToken cancel = default) =>
        Cachedly(nameof(ExportNames), [name], () => _engine.ExportNames(name, cancel));

    /// <summary>The cache key of a call: its method, then a SHA-256 of its arguments.</summary>
    /// <param name="method">The method.</param>
    /// <param name="args">Its arguments.</param>
    /// <returns>The key (short whatever the arguments' size).</returns>
    internal static string KeyOf(string method, IReadOnlyList<string> args)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string arg in args)
        {
            hash.AppendData(BitConverter.GetBytes(Encoding.UTF8.GetByteCount(arg)));
            hash.AppendData(Encoding.UTF8.GetBytes(arg));
        }

        return method + ":" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string Invariant(long n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string OpKind(string opJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(opJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("kind", out JsonElement kind) && kind.ValueKind == JsonValueKind.String
                ? kind.GetString() ?? ""
                : throw new ArgumentException("The op names no kind.", nameof(opJson));
        }
        catch (JsonException e)
        {
            throw new ArgumentException("The op is not JSON: " + e.Message, nameof(opJson), e);
        }
    }

    private T Cachedly<T>(string method, IReadOnlyList<string> args, Func<T> call)
        where T : notnull
    {
        string key = KeyOf(method, args);
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out LinkedListNode<(string Key, object Value)>? hit))
            {
                _recent.Remove(hit);
                _recent.AddFirst(hit);
                return (T)hit.Value.Value;
            }
        }

        T answer;
        lock (_lock)
        {
            answer = call();
        }

        lock (_cacheGate)
        {
            if (!_cache.ContainsKey(key))
            {
                _cache[key] = _recent.AddFirst((key, answer));
                while (_cache.Count > CacheCap)
                {
                    LinkedListNode<(string Key, object Value)> oldest = _recent.Last!;
                    _recent.RemoveLast();
                    _ = _cache.Remove(oldest.Value.Key);
                }
            }
        }

        return answer;
    }

    /// <summary>Frees the runtime.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _engine.Dispose();
        }
    }
}
