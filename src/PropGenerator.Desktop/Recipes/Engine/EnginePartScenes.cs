using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Scenes;
using PropGenerator.Engine;
using PropGenerator.Nature.Style;
using PartEdits = PropGenerator.Desktop.Scenes.PartEdits;

namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// The part scenes of one of easy-props' kinds on its engine (D1 of the phase 2 report, E1 of the phase 3 plan): the
/// scene is the engine's own generated components with our part edits (<see cref="SettingsPanel.PartEditsKey"/>, the
/// work file's diff) folded in; an edited scene is kept as the diff against the engine's components, plus a painted
/// tree's own edits; and every part edit is one of upstream's scene operations. The local ops and the generated
/// components come from the small rules engine (the UI thread may ask: a drag tick is one op); the ops that make
/// every part again (tree, seed) go to the engine's thread. Generated components and the asset's validated values are
/// kept in small LRUs keyed by a SHA-256 of the values, so a piece carrying its whole tree stays small.
/// </summary>
internal sealed class EnginePartScenes
{
    /// <summary>
    /// A test's stand-in for the rules engine's scene op (scene JSON, op JSON) → scene JSON or null, for the calls made
    /// in its own flow (an <see cref="AsyncLocal{T}"/>, so tests running beside it are not touched).
    /// </summary>
    internal static readonly AsyncLocal<Func<string, string, string?>?> OpForTests = new();

    // How many generated scenes and validated values are kept, each by its values' hash.
    private const int CacheCap = 8;

    // The asset's validated values stand in a component's parameters as this (entry.ts's compact form).
    private const string Asset = "$asset";

    // A work file is shared, and a tree's own edits are parsed on every rebuild: text or entries far beyond any tree's
    // are refused (a tree has a few hundred limbs and clumps at most).
    private const int MaxTreeEditsChars = 256 * 1024;
    private const int MaxTreeEditEntries = 4096;

    // A kept part's values beyond these are refused (a part has a few dozen; a spine at most 64 points).
    private const int MaxListItems = 64;
    private const int MaxTextChars = 64;
    private const int MaxDepth = 4;

    private const string Style = "low-poly";

    private readonly EngineKind _kind;
    private readonly Func<IReadOnlyList<ParamSpec>> _specs;
    private readonly EngineHost _host;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, object Value)>> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, object Value)> _recent = new();
    private (Settings Settings, Scene Scene)? _last;
    private (Settings Settings, string Id, EnginePartFacts Facts)? _lastFacts;
    private int _ops;
    private long _opTicks;

    /// <summary>The part scenes of one kind.</summary>
    /// <param name="kind">The kind; its id is the upstream generator.</param>
    /// <param name="specs">The kind's settings (read when first needed: they come from the engine).</param>
    /// <param name="host">The engine host.</param>
    internal EnginePartScenes(EngineKind kind, Func<IReadOnlyList<ParamSpec>> specs, EngineHost host)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(specs);
        ArgumentNullException.ThrowIfNull(host);
        _kind = kind;
        _specs = specs;
        _host = host;
    }

    /// <summary>Gets how many scene ops were run on the rules engine (a drag tick is one).</summary>
    internal int OpsRun => Volatile.Read(ref _ops);

    /// <summary>Gets how long those ops took in all, the engine call alone.</summary>
    internal TimeSpan OpTime => TimeSpan.FromTicks(Interlocked.Read(ref _opTicks));

    // Whether the kind keeps edits to its own parts in an edits parameter (the painted tree; EngineSpecs.ValuesJson writes it).
    private bool HasTreeEdits => _kind.Id == EngineKinds.PaintedTree;

    /// <summary>The prop as easy-props' scene holds it: the engine's components with the user's part edits folded in.</summary>
    /// <param name="s">The settings, with the part edits if there are any.</param>
    /// <returns>The scene.</returns>
    /// <exception cref="EngineException">The engine failed.</exception>
    public Scene SceneOf(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        lock (_gate)
        {
            if (_last is { } last && ReferenceEquals(last.Settings, s))
            {
                return last.Scene;
            }
        }

        Scene generated = Generated(s);
        Scene scene = generated;
        if (s.Values.ContainsKey(SettingsPanel.PartEditsKey))
        {
            try
            {
                PartEdits edits = EditsOf(s, generated.Components);
                scene = edits.IsEmpty ? generated : generated with { Components = edits.Apply(generated.Components, MergeGenerated) };
            }
            catch (FormatException)
            {
                // Edits out of bounds are left out (opening the file says so; see EditsProblem).
            }
        }

        lock (_gate)
        {
            _last = (s, scene);
        }

        return scene;
    }

    // easy-props' own mergeGenerated, on the rules engine: the kept parts folded into what the generator makes now (the
    // moves, and a reshaped part's own shape); then ours, which easy-props does not have: the turns and sizes.
    private IReadOnlyList<SceneComponent> MergeGenerated(IReadOnlyList<SceneComponent> kept, IReadOnlyList<SceneComponent> generated)
    {
        static string Json(IReadOnlyList<SceneComponent> list) => new JsonArray([.. list.Select(c => (JsonNode)ComponentJson.Write(c))]).ToJsonString();
        string json = _host.Rules.MergeGenerated(Json(kept), Json(generated));
        JsonArray merged = JsonNode.Parse(json) as JsonArray ?? throw new EngineException("mergeGenerated gave no list of components.");
        var bySlot = new Dictionary<string, SceneComponent>(StringComparer.Ordinal);
        foreach (SceneComponent k in kept.Where(k => k.Slot is not null))
        {
            bySlot[k.Slot!] = k;
        }

        return
        [
            .. merged.Select(node =>
            {
                SceneComponent c = ComponentJson.Read(node ?? throw new EngineException("mergeGenerated gave an empty component."));
                return c.Slot is { } slot && bySlot.TryGetValue(slot, out SceneComponent? previous) ? SceneOps.TurnedAndSized(c, previous) : c;
            }),
        ];
    }

    /// <summary>
    /// Why the part edits these settings hold cannot be used (a tree's own edits far beyond any tree's; part edits
    /// unreadable, or out of bounds against the engine's components: <see cref="PartEditsCheck"/>), or null when they
    /// can, there are none, or the engine cannot say (the edits are then kept).
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <returns>The problem, or null.</returns>
    public string? EditsProblem(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (HasTreeEdits && TreeEditsProblem(TreeEditsOf(s)) is string tree)
        {
            return tree;
        }

        if (!s.Values.ContainsKey(SettingsPanel.PartEditsKey))
        {
            return null;
        }

        try
        {
            _ = EditsOf(s, Generated(s).Components);
            return null;
        }
        catch (FormatException e)
        {
            return e.Message;
        }
        catch (Exception e) when (IsEngineFailure(e))
        {
            Trace.TraceWarning($"The part edits could not be checked: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// The settings with the parts as this scene has them: a painted tree's own edits (its <c>edits</c>, which an op
    /// on one of its limbs or clumps changes), then only what differs from the engine's components. A kept part
    /// carries its whole component, the asset's values written out where the engine wrote <c>"$asset"</c>, so a copy
    /// keeps the settings it was made with, as in easy-props.
    /// </summary>
    /// <param name="s">The settings the scene was made from.</param>
    /// <param name="scene">The scene after a part edit.</param>
    /// <returns>The settings to keep.</returns>
    /// <exception cref="EngineException">The engine failed.</exception>
    public Settings WithScene(Settings s, Scene scene)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(scene);
        if (HasTreeEdits && scene.Parameters.TryGetValue(SettingsPanel.TreeEditsKey, out object? e) && e is string text && text != TreeEditsOf(s))
        {
            s = s.With(SettingsPanel.TreeEditsKey, text);
        }

        var edits = PartEdits.Of(Generated(s).Components, scene.Components);
        if (edits.IsEmpty)
        {
            return s.Without(SettingsPanel.PartEditsKey);
        }

        JsonObject json = edits.ToJson();
        if (json["kept"] is JsonArray kept && kept.Any(HoldsAsset))
        {
            JsonObject values = ValidatedValues(s);
            foreach (JsonNode? part in kept)
            {
                if (part?["parameters"] is JsonObject parameters)
                {
                    foreach (string key in parameters.Where(kv => IsAsset(kv.Value)).Select(kv => kv.Key).ToList())
                    {
                        parameters[key] = values.DeepClone();
                    }
                }
            }
        }

        return s.With(SettingsPanel.PartEditsKey, JsonSerializer.SerializeToElement(json));
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
    public Scene? Apply(Settings s, SceneOp op)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(op);
        if (op.Heavy)
        {
            throw new InvalidOperationException($"The {op.ToJson()["kind"]} op makes every part again: it runs on the engine's thread (ApplyAsync).");
        }

        string sceneJson = OpSceneJson(s, op);
        string opJson = op.ToJson().ToJsonString();
        long start = Stopwatch.GetTimestamp();
        string? after;
        try
        {
            after = OpForTests.Value is { } stand ? stand(sceneJson, opJson) : _host.Rules.SceneOp(sceneJson, opJson, Style);
        }
        finally
        {
            _ = Interlocked.Add(ref _opTicks, Stopwatch.GetElapsedTime(start).Ticks);
            _ = Interlocked.Increment(ref _ops);
        }

        return after is null ? null : FromJson(after);
    }

    /// <summary>
    /// Any of upstream's scene operations; the heavy ones (tree, seed) run on the engine's thread, so the caller keeps
    /// the window usable meanwhile, as it does for a rebuild.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="op">The operation.</param>
    /// <param name="cancel">Stops a heavy op.</param>
    /// <returns>The scene after it, or null when upstream refused.</returns>
    public async Task<Scene?> ApplyAsync(Settings s, SceneOp op, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(op);
        if (!op.Heavy)
        {
            return Apply(s, op);
        }

        string? after = await _host.Builds.SceneOpAsync(OpSceneJson(s, op), op.ToJson().ToJsonString(), Style, cancel).ConfigureAwait(false);
        return after is null ? null : FromJson(after);
    }

    /// <summary>What can be done to one part: is it a painted tree's, a heavy limb, and can it break now.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="componentId">The part.</param>
    /// <returns>The facts.</returns>
    /// <exception cref="EngineException">The engine failed.</exception>
    public EnginePartFacts Facts(Settings s, string componentId)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(componentId);
        lock (_gate)
        {
            if (_lastFacts is { } last && ReferenceEquals(last.Settings, s) && last.Id == componentId)
            {
                return last.Facts;
            }
        }

        EnginePartFacts facts = _host.Rules.PartFacts(OpSceneJson(s), componentId);
        lock (_gate)
        {
            _lastFacts = (s, componentId, facts);
        }

        return facts;
    }

    /// <summary>The scene a part edit runs on: the kind's values and seed, and the scene's components as they are now.</summary>
    /// <param name="s">The settings.</param>
    /// <returns>The scene JSON.</returns>
    internal string OpSceneJson(Settings s) => EngineScenes.Of(_kind, ValuesJson(s), SeedOf(s), SceneOf(s));

    /// <summary>Whether an exception is the engine failing (phase 2 D10): each caller of an engine call catches these.</summary>
    /// <param name="e">The exception.</param>
    /// <returns>True for a failure, a timeout, a stop or a shut-down engine.</returns>
    internal static bool IsEngineFailure(Exception e) =>
        e is EngineException or TimeoutException or OperationCanceledException or ObjectDisposedException;

    // A transform op carries ours beside it (how the user turned and sized the part): set on the part before the op,
    // so upstream, which spreads the part, carries them through.
    private string OpSceneJson(Settings s, SceneOp op)
    {
        Scene scene = SceneOf(s);
        if (op is SceneOp.Transform { Turned: not null } or SceneOp.Transform { Sized: not null })
        {
            var t = (SceneOp.Transform)op;
            scene = scene with
            {
                Components = [.. scene.Components.Select(c => c.Id == t.ComponentId ? c with { Turned = t.Turned ?? c.Turned, Sized = t.Sized ?? c.Sized } : c)],
            };
        }

        return EngineScenes.Of(_kind, ValuesJson(s), SeedOf(s), scene);
    }

    private string ValuesJson(Settings s) => EngineSpecs.ValuesJson(s, _kind, _specs());

    private uint SeedOf(Settings s) => s.Values.ContainsKey("seed") ? s.GetSeed("seed") : _kind.DefaultSeed;

    // The components the engine makes for these settings (part edits left out), kept by their values' hash.
    private Scene Generated(Settings s)
    {
        string values = ValuesJson(s);
        uint seed = SeedOf(s);
        string key = Key("components", seed.ToString(CultureInfo.InvariantCulture), values);
        if (Cached<Scene>(key) is { } hit)
        {
            return hit;
        }

        string json = _host.Rules.Components(_kind.Id, values, seed);
        var components = JsonNode.Parse(json) as JsonArray ?? throw new EngineException("The engine's components are not a list.");
        var scene = new Scene(_kind.Id, ParametersOf(values), seed, Style, [.. components.Select(ReadPart)], SceneMaterials.Default);
        Keep(key, scene);
        return scene;
    }

    // The asset's values as the engine validates them: what "$asset" stands for.
    private JsonObject ValidatedValues(Settings s) => Validated(ValuesJson(s));

    // Kept as text, so each caller gets a node of its own.
    private JsonObject Validated(string values)
    {
        string key = Key("validated", values);
        if (Cached<string>(key) is not { } text)
        {
            text = _host.Rules.Validate(_kind.Id, values).Parameters.GetRawText();
            Keep(key, text);
        }

        return JsonNode.Parse(text) as JsonObject ?? throw new EngineException("The engine's validated values are not an object.");
    }

    // A scene as the engine returns it from an op.
    private Scene FromJson(string json)
    {
        JsonNode root = JsonNode.Parse(json) ?? throw new EngineException("The engine's scene is empty.");
        var components = root["components"] as JsonArray ?? throw new EngineException("The engine's scene has no components.");
        return new Scene(
            root["generatorId"]?.GetValue<string>() ?? _kind.Id,
            ComponentJson.ReadParameters(root["parameters"] as JsonObject ?? []),
            root["seed"]?.GetValue<double>() ?? 0,
            Style,
            [.. components.Select(ReadPart)],
            SceneMaterials.Default);
    }

    private static Nature.Parameters.ParameterDictionary ParametersOf(string valuesJson) =>
        ComponentJson.ReadParameters(JsonNode.Parse(valuesJson) as JsonObject ?? []);

    private static SceneComponent ReadPart(JsonNode? node)
    {
        try
        {
            return ComponentJson.Read(node ?? throw new FormatException("A part is empty."));
        }
        catch (FormatException e)
        {
            throw new EngineException($"The engine made a part that cannot be read: {e.Message}", e);
        }
    }

    // The part edits the settings hold, checked against the engine's components (a work file is not trusted).
    private PartEdits EditsOf(Settings s, IReadOnlyList<SceneComponent> generated)
    {
        if (!s.Values.TryGetValue(SettingsPanel.PartEditsKey, out JsonElement e))
        {
            return PartEdits.None;
        }

        if (e.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The part edits were left out: they are not an object.");
        }

        try
        {
            return PartEditsCheck.Validate(PartEdits.FromJson(JsonNode.Parse(e.GetRawText())), generated, CheckedParameters);
        }
        catch (JsonException x)
        {
            throw new FormatException($"The part edits were left out: {x.Message}", x);
        }
    }

    // A kept part's parameters, checked against its fresh twin's (the engine's own part of the same generator): only
    // the twin's keys; where the twin holds the asset's values ("$asset") the part holds "$asset" or values the engine
    // validates as the asset's; otherwise values of the twin's kind, finite and small. A restored part (no parameters)
    // stays so.
    private Nature.Parameters.ParameterDictionary CheckedParameters(SceneComponent part, IReadOnlyList<SceneComponent> generated)
    {
        var twin = ComponentJson.WriteParameters(generated.First(c => c.GeneratorId == part.GeneratorId).Parameters);
        var own = ComponentJson.WriteParameters(part.Parameters);
        var result = new JsonObject();
        foreach ((string key, JsonNode? value) in own)
        {
            if (!twin.TryGetPropertyValue(key, out JsonNode? like))
            {
                continue;
            }

            result[key] = IsAsset(like)
                ? IsAsset(value) ? JsonValue.Create(Asset) : value is JsonObject values ? Validated(values.ToJsonString()) : throw Refused(part, key)
                : Shaped(part, key, value, like, 0);
        }

        return ComponentJson.ReadParameters(result);
    }

    // A value shaped like the twin's: a finite number, short text, true or false, null where the twin has null, or a
    // short list or an object of such values.
    private static JsonNode? Shaped(SceneComponent part, string key, JsonNode? value, JsonNode? like, int depth)
    {
        if (depth > MaxDepth)
        {
            throw Refused(part, key);
        }

        switch (value)
        {
            case null:
                return like is null ? null : throw Refused(part, key);
            case JsonArray list when list.Count <= MaxListItems:
                return new JsonArray([.. list.Select(item => Shaped(part, key, item, (like as JsonArray)?.FirstOrDefault() ?? item, depth + 1))]);
            case JsonObject o when o.Count <= MaxListItems:
                return new JsonObject(o.Select(kv => KeyValuePair.Create(kv.Key, Shaped(part, key, kv.Value, (like as JsonObject)?[kv.Key] ?? kv.Value, depth + 1))));
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number && double.IsFinite(v.GetValue<double>()):
                double d = v.GetValue<double>();
                return key == "controlPoints" && d is < PartEditsCheck.MinControlPoints or > PartEditsCheck.MaxControlPoints ? throw Refused(part, key) : d;
            case JsonValue v when v.GetValueKind() == JsonValueKind.String && v.GetValue<string>().Length <= MaxTextChars:
                return v.GetValue<string>();
            case JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False:
                return v.GetValue<bool>();
            default:
                throw Refused(part, key);
        }
    }

    private static FormatException Refused(SceneComponent part, string key) =>
        new($"The part edits were left out: part '{part.Id}' has a '{key}' its generator cannot take.");

    private static bool IsAsset(JsonNode? node) => node is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == Asset;

    private static bool HoldsAsset(JsonNode? part) => part?["parameters"] is JsonObject parameters && parameters.Any(kv => IsAsset(kv.Value));

    private static string? TreeEditsProblem(string text)
    {
        if (text.Length > MaxTreeEditsChars)
        {
            return string.Create(CultureInfo.InvariantCulture, $"The tree's own edits are {text.Length} characters long, more than any tree's ({MaxTreeEditsChars}).");
        }

        var edits = PaintedEdits.Parse(text);
        int entries = edits.Limbs.Count + edits.Clumps.Count;
        return entries > MaxTreeEditEntries
            ? string.Create(CultureInfo.InvariantCulture, $"The tree's own edits name {entries} parts, more than any tree has ({MaxTreeEditEntries}).")
            : null;
    }

    // A painted tree's edits as the settings hold them: "" for none.
    private static string TreeEditsOf(Settings s) =>
        s.Values.TryGetValue(SettingsPanel.TreeEditsKey, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString()! : string.Empty;

    private static string Key(string what, params string[] parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string part in parts)
        {
            hash.AppendData(BitConverter.GetBytes(Encoding.UTF8.GetByteCount(part)));
            hash.AppendData(Encoding.UTF8.GetBytes(part));
        }

        return what + ":" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private T? Cached<T>(string key)
        where T : class
    {
        lock (_gate)
        {
            if (!_cache.TryGetValue(key, out LinkedListNode<(string Key, object Value)>? hit))
            {
                return null;
            }

            _recent.Remove(hit);
            _recent.AddFirst(hit);
            return (T)hit.Value.Value;
        }
    }

    private void Keep(string key, object value)
    {
        lock (_gate)
        {
            if (_cache.ContainsKey(key))
            {
                return;
            }

            _cache[key] = _recent.AddFirst((key, value));
            while (_cache.Count > CacheCap)
            {
                LinkedListNode<(string Key, object Value)> oldest = _recent.Last!;
                _recent.RemoveLast();
                _ = _cache.Remove(oldest.Value.Key);
            }
        }
    }
}
