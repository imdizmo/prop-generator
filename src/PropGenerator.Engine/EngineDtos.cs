using System.Text.Json;
using System.Text.Json.Nodes;

namespace PropGenerator.Engine;

/// <summary>What the bundle is: easy-props' tag and commit, its detail levels and its generators.</summary>
public sealed record EngineInfo
{
    /// <summary>The easy-props tag the bundle was built from (such as v0.4.2).</summary>
    public string Tag { get; init; } = "";

    /// <summary>The easy-props commit the bundle was built from.</summary>
    public string Commit { get; init; } = "";

    /// <summary>easy-props' APP_VERSION (written into export files).</summary>
    public string AppVersion { get; init; } = "";

    /// <summary>The detail of each level of detail (LOD0 first).</summary>
    public IReadOnlyList<double> LevelShares { get; init; } = [];

    /// <summary>The level the far card takes.</summary>
    public int FarLevel { get; init; }

    /// <summary>The face budgets, as upstream has them.</summary>
    public JsonElement Budgets { get; init; }

    /// <summary>How many versions of one prop a batch may make (upstream's <c>MAX_VARIATIONS</c>); 0 from an older bundle.</summary>
    public int MaxVariations { get; init; }

    /// <summary>Every generator in the bundle.</summary>
    public IReadOnlyList<EngineGenerator> Generators { get; init; } = [];
}

/// <summary>One of easy-props' generators.</summary>
public sealed record EngineGenerator
{
    /// <summary>Its id (such as painted-tree).</summary>
    public string Id { get; init; } = "";

    /// <summary>Its version: old saves are read by it.</summary>
    public int Version { get; init; }

    /// <summary>Its name.</summary>
    public string Label { get; init; } = "";

    /// <summary>Whether it has a low-poly style.</summary>
    public bool LowPoly { get; init; }

    /// <summary>Whether it never sways.</summary>
    public bool Still { get; init; }

    /// <summary>Whether it sways as one piece.</summary>
    public bool SwaysEvenly { get; init; }

    /// <summary>Whether it offers collision shapes.</summary>
    public bool OffersCollision { get; init; }

    /// <summary>Whether it offers a pivot.</summary>
    public bool OffersPivot { get; init; }
}

/// <summary>Every preset, and the order of their headings.</summary>
public sealed record EnginePresets
{
    /// <summary>The headings, in upstream's order.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>The presets (retired ones left out).</summary>
    public IReadOnlyList<EnginePreset> Presets { get; init; } = [];
}

/// <summary>One of easy-props' presets.</summary>
public sealed record EnginePreset
{
    /// <summary>Its id.</summary>
    public string Id { get; init; } = "";

    /// <summary>Its name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Its one-line description.</summary>
    public string Sub { get; init; } = "";

    /// <summary>Its heading, or null for none (round-canopy).</summary>
    public string? Category { get; init; }

    /// <summary>The generator it uses.</summary>
    public string GeneratorId { get; init; } = "";

    /// <summary>Its seed.</summary>
    public long Seed { get; init; }

    /// <summary>Its parameters, as upstream has them.</summary>
    public JsonElement Parameters { get; init; }

    /// <summary>Its family (tree, rock and so on).</summary>
    public string Family { get; init; } = "";

    /// <summary>Whether its shipped thumbnail still shows it.</summary>
    public bool ThumbnailFresh { get; init; }

    /// <summary>Whether upstream has retired it (only a preset read by id can be; the list leaves them out).</summary>
    public bool Retired { get; init; }
}

/// <summary>A generator's parameter definitions, as data the panel can draw.</summary>
public sealed record EngineParameters
{
    /// <summary>Upstream's ParameterSpec list, as is.</summary>
    public IReadOnlyList<JsonElement> Specs { get; init; } = [];

    /// <summary>The colour rows (painted-tree only).</summary>
    public IReadOnlyList<JsonElement> ColourRows { get; init; } = [];
}

/// <summary>A row the panel shows for the current values.</summary>
public sealed record EngineShownRow
{
    /// <summary>The parameter's key.</summary>
    public string Key { get; init; } = "";

    /// <summary>Its label for these values.</summary>
    public string Label { get; init; } = "";

    /// <summary>Its group for these values, or null when its spec has none.</summary>
    public string? Group { get; init; }
}

/// <summary>Whether a set of values is valid, and the values made valid.</summary>
public sealed record EngineValidation
{
    /// <summary>Whether the values were valid as given.</summary>
    public bool Valid { get; init; }

    /// <summary>The values, made valid.</summary>
    public JsonElement Parameters { get; init; }

    /// <summary>What was wrong.</summary>
    public IReadOnlyList<EngineIssue> Issues { get; init; } = [];
}

/// <summary>One thing wrong with a value.</summary>
public sealed record EngineIssue
{
    /// <summary>The parameter's key.</summary>
    public string Key { get; init; } = "";

    /// <summary>What is wrong.</summary>
    public string Message { get; init; } = "";
}

/// <summary>What a prop's values say about it.</summary>
public sealed record EngineTraits
{
    /// <summary>Whether it gets a far card.</summary>
    public bool FarAway { get; init; }

    /// <summary>Whether it sways.</summary>
    public bool Sways { get; init; }

    /// <summary>Whether it sways as one piece.</summary>
    public bool SwaysEvenly { get; init; }
}

/// <summary>A scene's collision: what its generator offers, and what the export writes.</summary>
public sealed record EngineCollision
{
    /// <summary>The capsules the generator offers itself (none for a generator that offers none).</summary>
    public IReadOnlyList<EngineCapsule> Offered { get; init; } = [];

    /// <summary>The capsules the export writes: the offered ones, or ones made from the solid parts.</summary>
    public IReadOnlyList<EngineCapsule> Shapes { get; init; } = [];
}

/// <summary>A collision capsule, in easy-props' Y-up metres.</summary>
public sealed record EngineCapsule
{
    /// <summary>One end.</summary>
    public IReadOnlyList<double> Start { get; init; } = [];

    /// <summary>The other end.</summary>
    public IReadOnlyList<double> End { get; init; } = [];

    /// <summary>The radius at <see cref="Start"/>.</summary>
    public double StartRadius { get; init; }

    /// <summary>The radius at <see cref="End"/>.</summary>
    public double EndRadius { get; init; }
}

/// <summary>A built prop at one detail: its parts.</summary>
/// <param name="Parts">The parts, in upstream's order.</param>
/// <param name="FullFaces">The face count at full detail, when it was asked for.</param>
/// <param name="Meta">The whole meta object, for anything not read into fields.</param>
public sealed record EngineMesh(IReadOnlyList<EnginePart> Parts, int? FullFaces, JsonElement Meta);

/// <summary>
/// One placed part of a built prop: its vertex attributes (easy-props' Y-up metres, float32 as built) and how it is
/// drawn.
/// </summary>
public sealed record EnginePart
{
    /// <summary>The scene component it came from.</summary>
    public string? ComponentId { get; init; }

    /// <summary>The generator of that component.</summary>
    public string? GeneratorId { get; init; }

    /// <summary>Its material's name.</summary>
    public string? Material { get; init; }

    /// <summary>Its role (wood, foliage and so on), if it has one.</summary>
    public string? Role { get; init; }

    /// <summary>Its role within its part.</summary>
    public string? RoleOfPart { get; init; }

    /// <summary>Whether it is drawn flat-shaded.</summary>
    public bool FlatShading { get; init; }

    /// <summary>Whether it is a stand-in (proxy) part.</summary>
    public bool Proxy { get; init; }

    /// <summary>Its placement, a column-major 4x4 matrix.</summary>
    public IReadOnlyList<double> Matrix { get; init; } = [];

    /// <summary>Its tint (linear RGB), when it has one.</summary>
    public IReadOnlyList<double>? Tint { get; init; }

    /// <summary>How much its placement scales it against its component's own scale.</summary>
    public double Growth { get; init; } = 1;

    /// <summary>How its surface is drawn (default when it has no surface of its own).</summary>
    public EngineSurface Surface { get; init; } = new();

    /// <summary>Its vertex count.</summary>
    public int VertexCount { get; init; }

    /// <summary>Its triangle count.</summary>
    public int Faces { get; init; }

    /// <summary>Every vertex attribute, by three.js name, in the order built.</summary>
    public IReadOnlyDictionary<string, EngineVertexData> Attributes { get; init; } = new Dictionary<string, EngineVertexData>();

    /// <summary>Positions, xyz per vertex.</summary>
    public float[] Positions => Attributes.TryGetValue("position", out EngineVertexData? a) ? a.Data : [];

    /// <summary>Normals, xyz per vertex (empty when it has none).</summary>
    public float[] Normals => Attributes.TryGetValue("normal", out EngineVertexData? a) ? a.Data : [];

    /// <summary>Vertex colours, when it has them (see their item size).</summary>
    public float[]? Colors => Attributes.GetValueOrDefault("color")?.Data;

    /// <summary>Texture coordinates, uv per vertex, when it has them.</summary>
    public float[]? Uvs => Attributes.GetValueOrDefault("uv")?.Data;

    /// <summary>Second texture coordinates, uv per vertex, when it has them.</summary>
    public float[]? Uv1 => Attributes.GetValueOrDefault("uv1")?.Data;

    /// <summary>The sway weight per vertex, when it has one.</summary>
    public float[]? Rigid => Attributes.GetValueOrDefault("rigid")?.Data;

    /// <summary>The triangle index (widened to 32 bits), or null when the part is not indexed.</summary>
    public uint[]? Index { get; init; }

    /// <summary>Whether the index came as 16-bit.</summary>
    public bool Index16 { get; init; }

    /// <summary>The part's whole meta object, for anything not read into fields.</summary>
    public JsonElement Meta { get; init; }
}

/// <summary>A vertex attribute, copied out as float32 component by component.</summary>
/// <param name="Name">Its three.js name (position, normal, color, uv, uv1, rigid and so on).</param>
/// <param name="ItemSize">Components per vertex.</param>
/// <param name="Normalized">Whether three.js marks it normalized.</param>
/// <param name="Data">Its values, <paramref name="ItemSize"/> per vertex.</param>
public sealed record EngineVertexData(string Name, int ItemSize, bool Normalized, float[] Data);

/// <summary>How a part's surface is drawn: texture keys (see <see cref="EasyPropsEngine.Texture"/>) and alpha.</summary>
public sealed record EngineSurface
{
    /// <summary>Whether its vertex colours are used.</summary>
    public bool VertexColors { get; init; }

    /// <summary>Its colour map's key, if any.</summary>
    public string? Map { get; init; }

    /// <summary>Its normal map's key, if any.</summary>
    public string? NormalMap { get; init; }

    /// <summary>Its alpha cut-off, if any.</summary>
    public double? AlphaTest { get; init; }

    /// <summary>Its own roughness (a wet surface shines: below 1), or null when it has none and is fully rough.</summary>
    public double? Roughness { get; init; }

    /// <summary>The material name upstream's export gives it (such as a wet copy's <c>_wet40</c>), or null for the usual.</summary>
    public string? ExportName { get; init; }

    /// <summary>Whether it receives shadows (false when upstream leaves it unset).</summary>
    public bool ReceiveShadow => Flag(Meta, "receiveShadow");

    /// <summary>Whether it is a stand-in (proxy) surface: picked, never drawn.</summary>
    public bool Proxy => Flag(Meta, "proxy");

    /// <summary>The whole surface object.</summary>
    public JsonElement Meta { get; init; }

    private static bool Flag(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>A texture's pixels.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Rgba">
/// RGBA8 pixels as three's <c>DataTexture</c> holds them (its <c>image.data</c>): the row at v = 0 first.
/// </param>
/// <param name="Meta">The texture's meta object (colour space, wrap and so on).</param>
public sealed record EngineTexture(int Width, int Height, byte[] Rgba, JsonElement Meta)
{
    /// <summary>three's colour space: "srgb" for colour maps, "" (none, linear data) for relief and the like.</summary>
    public string ColorSpace =>
        Meta.ValueKind == JsonValueKind.Object && Meta.TryGetProperty("colorSpace", out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    /// <summary>Whether it repeats (three's RepeatWrapping) rather than clamping at its edges.</summary>
    public bool Repeat =>
        Meta.ValueKind == JsonValueKind.Object && Meta.TryGetProperty("repeat", out JsonElement v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>A file of an export package.</summary>
/// <param name="Name">Its name in the package.</param>
/// <param name="Bytes">Its bytes.</param>
/// <param name="Faces">Its triangle count, for a mesh file.</param>
public sealed record EngineFile(string Name, byte[] Bytes, int? Faces = null);

/// <summary>The engine's memory, as V8 reports it.</summary>
/// <param name="UsedBytes">Heap in use.</param>
/// <param name="TotalBytes">Heap reserved.</param>
/// <param name="ExternalBytes">Memory outside the heap (ArrayBuffers).</param>
/// <param name="LimitBytes">The heap's hard limit.</param>
public sealed record EngineHeap(long UsedBytes, long TotalBytes, long ExternalBytes, long LimitBytes);

/// <summary>What can be done to one part of a scene.</summary>
public sealed record EnginePartFacts
{
    /// <summary>Whether it is a painted tree's part (a limb, a clump or the bark), edited through part actions.</summary>
    public bool Painted { get; init; }

    /// <summary>Whether it is a heavy limb (the trunk's own limbs and their big branches): one that can break.</summary>
    public bool Heavy { get; init; }

    /// <summary>Whether it can break now (a heavy limb not already broken).</summary>
    public bool CanBreak { get; init; }
}

/// <summary>
/// What upstream fills when it opens a file saved before a setting existed (<c>withEarlierLooks</c>), for one
/// generator: the asset's settings, and each part's <c>rock</c> settings. Both empty for a generator it leaves alone.
/// </summary>
public sealed record EngineEarlierLooks
{
    /// <summary>The asset's settings filled when missing, by key.</summary>
    public IReadOnlyDictionary<string, JsonElement> Asset { get; init; } = new Dictionary<string, JsonElement>();

    /// <summary>A part's <c>parameters.rock</c> settings filled when missing, by key.</summary>
    public IReadOnlyDictionary<string, JsonElement> Rock { get; init; } = new Dictionary<string, JsonElement>();

    /// <summary>Whether it fills nothing.</summary>
    public bool IsEmpty => Asset.Count == 0 && Rock.Count == 0;

    /// <summary>
    /// Fills a scene in C# as upstream's <c>withEarlierLooks</c> does, with no engine call: the asset's settings
    /// missing a key get it, and so does every part whose <c>parameters.rock</c> is an object (one written
    /// <c>"$asset"</c> is left as it is, as upstream leaves a value that is not an object).
    /// </summary>
    /// <param name="sceneJson">The scene: <c>{generatorId, seed, parameters, components?}</c>.</param>
    /// <returns>The scene, filled (the same text when nothing was missing).</returns>
    public string FillScene(string sceneJson)
    {
        if (IsEmpty)
        {
            return sceneJson;
        }

        JsonObject scene = JsonNode.Parse(sceneJson)?.AsObject() ?? throw new ArgumentException("The scene is not a JSON object.", nameof(sceneJson));
        bool changed = false;
        if (scene["parameters"] is JsonObject parameters)
        {
            changed |= FillAsset(parameters);
        }
        else if (scene["parameters"] is null && Asset.Count > 0)
        {
            var filled = new JsonObject();
            _ = FillAsset(filled);
            scene["parameters"] = filled;
            changed = true;
        }

        if (scene["components"] is JsonArray components)
        {
            foreach (JsonNode? component in components)
            {
                if (component?["parameters"] is JsonObject p && p["rock"] is JsonObject rock)
                {
                    changed |= FillRock(rock);
                }
            }
        }

        return changed ? scene.ToJsonString() : sceneJson;
    }

    /// <summary>Fills an asset's settings: each key of <see cref="Asset"/> it lacks.</summary>
    /// <param name="parameters">The settings, changed in place.</param>
    /// <returns>Whether anything was added.</returns>
    public bool FillAsset(JsonObject parameters) => Fill(parameters, Asset);

    /// <summary>Fills a part's rock settings: each key of <see cref="Rock"/> it lacks.</summary>
    /// <param name="rock">The part's <c>parameters.rock</c>, changed in place.</param>
    /// <returns>Whether anything was added.</returns>
    public bool FillRock(JsonObject rock) => Fill(rock, Rock);

    private static bool Fill(JsonObject values, IReadOnlyDictionary<string, JsonElement> earlier)
    {
        ArgumentNullException.ThrowIfNull(values);
        bool changed = false;
        foreach ((string key, JsonElement value) in earlier)
        {
            if (!values.ContainsKey(key))
            {
                values[key] = JsonNode.Parse(value.GetRawText());
                changed = true;
            }
        }

        return changed;
    }
}

/// <summary>A control Make it fit may lower, and how far.</summary>
public sealed record EngineFitControl
{
    /// <summary>The setting's key.</summary>
    public string Key { get; init; } = "";

    /// <summary>Its label.</summary>
    public string Label { get; init; } = "";

    /// <summary>Its least value.</summary>
    public double Min { get; init; }

    /// <summary>Its greatest value.</summary>
    public double Max { get; init; }

    /// <summary>Its step.</summary>
    public double Step { get; init; }

    /// <summary>How low Make it fit takes it at most.</summary>
    public double Limit { get; init; }
}

/// <summary>A prop's faces at full detail, as upstream's status bar counts them.</summary>
public sealed record EngineFaceCounts
{
    /// <summary>The faces of trunk, limbs and every other solid part.</summary>
    public int Wood { get; init; }

    /// <summary>The faces of the leaves.</summary>
    public int Leaves { get; init; }

    /// <summary>Every face.</summary>
    public int Total => Wood + Leaves;
}

/// <summary>What Make it fit did: the scene, what it changed, and the faces it came to.</summary>
/// <param name="Scene">The scene after (JSON, compact components).</param>
/// <param name="Changes">The settings it lowered, in the order it lowered them.</param>
/// <param name="Description">Those changes in words.</param>
/// <param name="Faces">The faces the scene has now.</param>
/// <param name="Budget">The budget it fitted to.</param>
/// <param name="Fits">Whether it came under the budget.</param>
/// <param name="Stopped">Whether it ran out of time before trying every control.</param>
public sealed record EngineFit(string Scene, IReadOnlyList<EngineFitChange> Changes, string Description, int Faces, int Budget, bool Fits, bool Stopped);

/// <summary>One setting Make it fit lowered.</summary>
public sealed record EngineFitChange
{
    /// <summary>The setting's key.</summary>
    public string Key { get; init; } = "";

    /// <summary>Its label.</summary>
    public string Label { get; init; } = "";

    /// <summary>Its value before.</summary>
    public double From { get; init; }

    /// <summary>Its value after.</summary>
    public double To { get; init; }
}

/// <summary>The pieces a tree can make, in upstream's order, with upstream's names and help.</summary>
/// <param name="Kinds">stump, log, branch, litter.</param>
public sealed record EnginePieceLabels(IReadOnlyList<EnginePieceLabel> Kinds)
{
    /// <summary>One piece's label by its kind.</summary>
    /// <param name="kind">stump, log, branch or litter.</param>
    /// <returns>Its label, or null for a kind upstream does not have.</returns>
    public EnginePieceLabel? Of(string kind) => Kinds.FirstOrDefault(k => k.Kind == kind);
}

/// <summary>A piece's name and help.</summary>
/// <param name="Kind">stump, log, branch or litter.</param>
/// <param name="Name">Its name (Fallen log).</param>
/// <param name="Help">A line on what it is.</param>
public sealed record EnginePieceLabel(string Kind, string Name, string Help);

/// <summary>One piece made from a tree, as a prop of its own.</summary>
/// <param name="Kind">stump, log, branch or litter.</param>
/// <param name="Name">The name upstream gives it (the tree's name and the piece's).</param>
/// <param name="Scene">Its scene (JSON, compact components), the tree it came from carried in its parameters.</param>
public sealed record EnginePiece(string Kind, string Name, string Scene);

/// <summary>One prop of a batch, named as upstream numbers it.</summary>
public sealed record EngineBatchItem
{
    /// <summary>Its name (oak_01).</summary>
    public string Name { get; init; } = "";

    /// <summary>Its own folder, when each prop gets one.</summary>
    public string Folder { get; init; } = "";

    /// <summary>The name its files are written under.</summary>
    public string ExportName { get; init; } = "";
}

/// <summary>The names a typed name makes.</summary>
public sealed record EngineExportNames
{
    /// <summary>The folder's name (upstream's <c>folderNameOf</c>: the name as typed, made safe).</summary>
    public string Folder { get; init; } = "";

    /// <summary>The files' name (upstream's <c>exportNameOf</c>: old_oak_1).</summary>
    public string Export { get; init; } = "";
}

/// <summary>One colour setting that props in a set share, and the choice the set starts on.</summary>
public sealed record EngineColourGroup
{
    /// <summary>The group's id: the setting and the choices it offers.</summary>
    public string Id { get; init; } = "";

    /// <summary>The setting's key (palette, barkTone, flowerColour).</summary>
    public string Key { get; init; } = "";

    /// <summary>What it is called, with the kind of prop ("Tree: Palette").</summary>
    public string Label { get; init; } = "";

    /// <summary>The choices it offers (own colours left out).</summary>
    public IReadOnlyList<EngineColourOption> Options { get; init; } = [];

    /// <summary>The choice most of the set uses, which the set starts on.</summary>
    public string Usual { get; init; } = "";

    /// <summary>How many props in the set have this setting.</summary>
    public int Props { get; init; }
}

/// <summary>A choice of a colour setting.</summary>
public sealed record EngineColourOption
{
    /// <summary>Its value.</summary>
    public string Value { get; init; } = "";

    /// <summary>Its label.</summary>
    public string Label { get; init; } = "";
}

/// <summary>One of a prop's damage settings (an upstream number parameter in the Damage group).</summary>
public sealed record EngineDamageSetting
{
    /// <summary>Its key.</summary>
    public string Key { get; init; } = "";

    /// <summary>Its label.</summary>
    public string Label { get; init; } = "";

    /// <summary>Its help, if any.</summary>
    public string? Help { get; init; }

    /// <summary>Its group (Damage).</summary>
    public string? Group { get; init; }

    /// <summary>Its least value.</summary>
    public double Min { get; init; }

    /// <summary>Its greatest value.</summary>
    public double Max { get; init; }

    /// <summary>Its step.</summary>
    public double Step { get; init; }

    /// <summary>Its default.</summary>
    public double Default { get; init; }

    /// <summary>The whole spec, as upstream has it (passed back to the engine as is).</summary>
    public JsonElement Spec { get; init; }

    /// <summary>Settings as the engine takes them back (pageDamages, damageNames): their specs as upstream gave them.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>A JSON array.</returns>
    public static string ToJson(IEnumerable<EngineDamageSetting> settings) =>
        "[" + string.Join(",", settings.Select(static s => s.Spec.ValueKind == JsonValueKind.Undefined
            ? JsonSerializer.Serialize(new { kind = "number", key = s.Key, label = s.Label, help = s.Help, group = s.Group, min = s.Min, max = s.Max, step = s.Step, @default = s.Default })
            : s.Spec.GetRawText())) + "]";
}

/// <summary>One version of a prop: on another seed, or on its own seed with damage.</summary>
public sealed record EngineVersion
{
    /// <summary>Its seed.</summary>
    public uint Seed { get; init; }

    /// <summary>Its damage settings, by key; null for the prop's own.</summary>
    public IReadOnlyDictionary<string, double>? Damage { get; init; }

    /// <summary>The version as the engine takes it.</summary>
    /// <returns>JSON: <c>{seed, damage?}</c>.</returns>
    public string ToJson() => JsonSerializer.Serialize(
        Damage is null ? new { seed = Seed } : (object)new { seed = Seed, damage = Damage });
}
