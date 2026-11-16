using System.Collections.Concurrent;
using PropGenerator.Engine;

namespace PropGenerator.Desktop.Engine;

/// <summary>
/// What easy-props says about itself, read once from the rules engine: its info and its presets when made (at launch,
/// under the loading window), and each generator's parameters on first ask. Read from the UI thread and from pool
/// threads.
/// </summary>
internal sealed class EngineCatalog
{
    private readonly RulesEngine _rules;
    private readonly ConcurrentDictionary<string, Lazy<EngineParameters>> _parameters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, EnginePreset> _retired = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EnginePreset> _byId;

    /// <summary>Reads the info and the presets.</summary>
    /// <param name="rules">The rules engine they are read from.</param>
    /// <exception cref="EngineException">The engine failed to answer.</exception>
    public EngineCatalog(RulesEngine rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
        Info = rules.Info();
        Presets = rules.Presets();
        _byId = Presets.Presets.GroupBy(p => p.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    /// <summary>Gets the bundle's version (tag and commit) and generators.</summary>
    public EngineInfo Info { get; }

    /// <summary>Gets every preset (retired ones left out) and the order of their headings.</summary>
    public EnginePresets Presets { get; }

    /// <summary>A generator's parameter definitions, read once (one entry per generator, nine at most).</summary>
    /// <param name="generatorId">The generator, such as "bush".</param>
    /// <returns>Its specs and colour rows.</returns>
    /// <exception cref="EngineException">The engine failed to answer (asked again next time).</exception>
    public EngineParameters Parameters(string generatorId)
    {
        ArgumentNullException.ThrowIfNull(generatorId);
        Lazy<EngineParameters> entry = _parameters.GetOrAdd(generatorId, id => new Lazy<EngineParameters>(() => _rules.Parameters(id)));
        try
        {
            return entry.Value;
        }
        catch (Exception)
        {
            // A Lazy keeps its failure: let this one go, so the next ask reads again (and an unknown id is not kept).
            _parameters.TryRemove(new KeyValuePair<string, Lazy<EngineParameters>>(generatorId, entry));
            throw;
        }
    }

    /// <summary>One preset by id: from the list, or a retired one asked of the engine (and then kept).</summary>
    /// <param name="id">The preset's id.</param>
    /// <returns>The preset, or null when there is none.</returns>
    /// <exception cref="EngineException">The engine failed to answer.</exception>
    public EnginePreset? Preset(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_byId.TryGetValue(id, out EnginePreset? listed) || _retired.TryGetValue(id, out listed))
        {
            return listed;
        }

        // Only presets the bundle has are kept, so the cache is bounded by its retired presets.
        EnginePreset? asked = _rules.Preset(id);
        return asked is null ? null : _retired.GetOrAdd(id, asked);
    }
}
