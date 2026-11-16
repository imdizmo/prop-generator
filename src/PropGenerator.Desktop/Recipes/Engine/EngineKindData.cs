using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PropGenerator.Desktop.Engine;
using PropGenerator.Engine;

namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// What one of easy-props' kinds says about its settings, from its engine: the panel's rows, the drawer's starting
/// points, the detail levels, and the rules the panel asks on the UI thread (rows hidden or relabelled, what a change
/// adjusts, the far card, the sway). The rules go to the small rules engine, never the build thread, and are cached by
/// a hash of the values in a 64-entry LRU. A rule call that fails is answered as if the rule said nothing, so the panel stays usable.
/// </summary>
internal sealed class EngineKindData
{
    /// <summary>How many rule answers are kept (by method and values).</summary>
    internal const int CacheCap = 64;

    private readonly EngineCatalog _catalog;
    private readonly RulesEngine _rules;
    private readonly Lazy<IReadOnlyList<ParamSpec>> _specs;
    private readonly Lazy<IReadOnlyList<JsonElement>> _rows;
    private readonly Lazy<IReadOnlyList<StartingPoint>> _points;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, object Value)>> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, object Value)> _recent = new();
    private int _failed;

    /// <summary>Reads a kind's data from the engine (lazily: nothing is asked until a member is read).</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="catalog">The catalog its parameters and presets are read from.</param>
    /// <param name="rules">The rules engine its rules are asked of.</param>
    internal EngineKindData(EngineKind kind, EngineCatalog catalog, RulesEngine rules)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rules);
        Kind = kind;
        _catalog = catalog;
        _rules = rules;

        // Publication only: a failed read is not kept, so the next read asks again.
        _specs = new(() => EngineSpecs.From(catalog.Parameters(kind.Id), kind), LazyThreadSafetyMode.PublicationOnly);
        _rows = new(() => { EngineParameters p = catalog.Parameters(kind.Id); return [.. p.Specs, .. p.ColourRows]; }, LazyThreadSafetyMode.PublicationOnly);
        _points = new(MakeStartingPoints, LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>Gets the kind.</summary>
    internal EngineKind Kind { get; }

    /// <summary>Gets the settings in panel order: upstream's specs, its colour rows, then the seed.</summary>
    internal IReadOnlyList<ParamSpec> Specs => _specs.Value;

    /// <summary>
    /// Gets the starting points: the kind's presets in upstream's order (by heading in upstream's heading order for a
    /// kind that sorts by categories), each from the defaults with the preset's validated parameters and seed; none for a
    /// kind the drawer does not list (a tree's pieces).
    /// </summary>
    internal IReadOnlyList<StartingPoint> StartingPoints => _points.Value;

    /// <summary>Gets the share of full detail each detail level is built at, full detail first.</summary>
    internal IReadOnlyList<double> DetailShares => _catalog.Info.LevelShares;

    /// <summary>Gets a value indicating whether the prop can also be made low-poly.</summary>
    internal bool LowPoly => Generator.LowPoly;

    /// <summary>Gets the generator's version.</summary>
    internal int Version => Generator.Version;

    /// <summary>Gets the own-colour rows by the choice they belong to and the colours they are.</summary>
    internal IReadOnlyList<OwnColourGroup> OwnColourGroups =>
        [.. _rows.Value
            .Where(r => Text(r, "kind") == "colour" && Text(r, "ownOf") is not null && Text(r, "set") is not null)
            .GroupBy(r => (OwnOf: Text(r, "ownOf")!, Set: Text(r, "set")!))
            .Select(g => new OwnColourGroup(g.Key.Set, OwnColourGroup.TitleOf(g.Key.Set), g.Key.OwnOf, [.. g.Select(r => Text(r, "key")!)]))];

    /// <summary>Gets how many rule answers are cached now (at most <see cref="CacheCap"/>).</summary>
    internal int Cached
    {
        get
        {
            lock (_gate)
            {
                return _cache.Count;
            }
        }
    }

    /// <summary>Gets the keys of the rule answers cached now, newest first (method and a SHA-256 of the values).</summary>
    internal IReadOnlyList<string> CachedKeys
    {
        get
        {
            lock (_gate)
            {
                return [.. _recent.Select(e => e.Key)];
            }
        }
    }

    private EngineGenerator Generator => _catalog.Info.Generators.First(g => g.Id == Kind.Id);

    /// <summary>The rows these settings have no use for (upstream's <c>shown()</c> leaves them out); none when the rule fails.</summary>
    /// <param name="s">The settings.</param>
    /// <returns>The keys to hide.</returns>
    internal IReadOnlyCollection<string> HiddenKeys(Settings s)
    {
        if (Shown(s) is not { } shown)
        {
            return [];
        }

        var showing = shown.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        return [.. Specs.Where(spec => !SettingsPanel.IsHidden(spec) && !showing.Contains(spec.Key)).Select(spec => spec.Key)];
    }

    /// <summary>
    /// The rows these settings show, under the labels and groups they go by, when one differs from its setting's (a
    /// palm-form painted tree's branch rows under Leaves); null when none does, or when the rule fails.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <returns>The rows in upstream's order, or null.</returns>
    internal IReadOnlyList<ShownRow>? ShownRows(Settings s)
    {
        if (Shown(s) is not { } shown)
        {
            return null;
        }

        var specs = Specs.ToDictionary(spec => spec.Key, StringComparer.Ordinal);
        bool differs = shown.Any(r => !specs.TryGetValue(r.Key, out ParamSpec? spec) || spec.Label != r.Label || spec.Group != r.Group);
        return differs ? [.. shown.Select(r => new ShownRow(r.Key, r.Label, r.Group))] : null;
    }

    /// <summary>
    /// What else changes with a change just made: a choice switched to My own colours starts its colours from those
    /// that were showing (upstream's <c>adjust</c>). Only a changed choice asks the engine; a failed call keeps the change as made.
    /// </summary>
    /// <param name="before">The settings before the change.</param>
    /// <param name="after">The settings with the change.</param>
    /// <returns>The settings to keep.</returns>
    internal Settings Adjust(Settings before, Settings after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        IReadOnlyList<ParamSpec> specs;
        try
        {
            specs = Specs;
        }
        catch (Exception e) when (Expected(e))
        {
            Failed(e);
            return after;
        }

        bool choiceChanged = specs.Any(spec => spec.Type == "choice"
            && after.Values.TryGetValue(spec.Key, out JsonElement now)
            && (!before.Values.TryGetValue(spec.Key, out JsonElement was) || was.ToString() != now.ToString()));
        if (!choiceChanged)
        {
            return after;
        }

        try
        {
            IReadOnlyDictionary<string, JsonElement> patch = _rules.Adjust(Kind.Id, EngineSpecs.ValuesJson(before, Kind, specs), EngineSpecs.ValuesJson(after, Kind, specs));
            return EngineSpecs.ApplyPatch(after, patch);
        }
        catch (Exception e) when (Expected(e))
        {
            Failed(e);
            return after;
        }
    }

    /// <summary>Whether these settings offer a far-away card (upstream's <c>offersFarAway</c>); false when the rule fails.</summary>
    /// <param name="s">The settings.</param>
    /// <returns>True when the Far away level is offered.</returns>
    internal bool FarAway(Settings s) => Traits(s)?.FarAway ?? false;

    /// <summary>Whether the prop sways as one piece; the kind's own wind when the rule fails.</summary>
    /// <param name="s">The settings.</param>
    /// <returns>True when every part moves about alike.</returns>
    internal bool SwaysEvenly(Settings s) => Traits(s)?.SwaysEvenly ?? Kind.Wind?.Evenly ?? false;

    // A failure the engine is expected to give (D10): a script error, a timeout, a host shut down.
    private static bool Expected(Exception e) =>
        e is EngineException or TimeoutException or ObjectDisposedException or OperationCanceledException;

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private IReadOnlyList<EngineShownRow>? Shown(Settings s) => Ask("shown", s, json => _rules.Shown(Kind.Id, json));

    private EngineTraits? Traits(Settings s) => Ask("traits", s, json => _rules.Traits(Kind.Id, json));

    // One rule answer: from the cache, or asked of the rules engine (outside the lock) and kept; null when it failed.
    private T? Ask<T>(string method, Settings s, Func<string, T> call)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(s);
        string json;
        try
        {
            json = EngineSpecs.ValuesJson(s, Kind, Specs);
        }
        catch (Exception e) when (Expected(e))
        {
            Failed(e);
            return null;
        }

        // Keyed by a hash of the values, not the text: a piece carries its whole tree (E12), so 64 keys of its text
        // would hold megabytes.
        string key = method + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out LinkedListNode<(string Key, object Value)>? hit))
            {
                _recent.Remove(hit);
                _recent.AddFirst(hit);
                return (T)hit.Value.Value;
            }
        }

        T answer;
        try
        {
            answer = call(json);
        }
        catch (Exception e) when (Expected(e))
        {
            Failed(e);
            return null;
        }

        lock (_gate)
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

    // Writes the first failure of this kind's rules to the trace; later ones are answered quietly.
    private void Failed(Exception e)
    {
        if (Interlocked.Exchange(ref _failed, 1) == 0)
        {
            TraceLog.Write($"engine rules: {Kind.Id} answered without its rules ({e.GetType().Name}: {e.Message})");
        }
    }

    // A preset's parameters made valid (upstream's validate fills the values its panel does not show, as the port's
    // preset store has them); the preset's own when the rule fails.
    private JsonElement Validated(EnginePreset p)
    {
        try
        {
            return _rules.Validate(Kind.Id, p.Parameters.GetRawText()).Parameters;
        }
        catch (Exception e) when (Expected(e))
        {
            Failed(e);
            return p.Parameters;
        }
    }

    private List<StartingPoint> MakeStartingPoints()
    {
        IReadOnlyList<ParamSpec> specs = Specs;
        var defaults = Settings.Defaults(specs);
        var list = new List<StartingPoint>();
        if (!Kind.InDrawer)
        {
            // A tree's piece starts only from a tree (ruling 5).
            return list;
        }

        foreach (EnginePreset p in _catalog.Presets.Presets)
        {
            if (p.GeneratorId != Kind.Id || p.Retired)
            {
                continue;
            }

            Settings settings = EngineSpecs.WithValues(defaults, Validated(p), specs).With("seed", p.Seed);
            list.Add(new StartingPoint(p.Id, p.Name, p.Category ?? Kind.FallbackCategory, Kind.Id, settings));
        }

        if (!Kind.SortByCategories)
        {
            return list;
        }

        // A stable sort, so the presets keep their own order under each heading; unknown headings last.
        IReadOnlyList<string> order = _catalog.Presets.Categories;
        int Rank(string group) => order.Contains(group) ? order.ToList().IndexOf(group) : order.Count;
        return [.. list.OrderBy(p => Rank(p.Group))];
    }
}
