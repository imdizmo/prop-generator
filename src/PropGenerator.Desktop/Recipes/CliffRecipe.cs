using System.Text.Json;
using PropGenerator.Nature.Cliffs;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Parameters;
using PropGenerator.Nature.Textures;
using NatureParameterSpec = PropGenerator.Nature.Parameters.ParameterSpec;

namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// The slab cliff as an app recipe: its settings (the cliff's own, then the seed), its four starting points in the
/// drawer's Cliffs group (after easy-props' cliffs there), its coloured mesh at each detail level, its GLB file and its
/// shapes you can walk into.
/// </summary>
internal sealed class CliffRecipe : IMeshRecipe
{
    /// <summary>The seed used by a cliff whose settings do not say (the Long cliff's).</summary>
    private const double DefaultSeed = 1;

    private const string SeedTooltip =
        "A whole number from 0 to 4294967295 that picks which cliff you get from the other settings: each block's width, "
        + "height and set-back, the angle and inset of every plane, where the steps, bites and grooves land, the end turns "
        + "and the surface noise. Two seeds give different cliffs, not the same one jittered; the same seed and settings "
        + "always give the same cliff. Crease seed picks the cracks on their own. The starting points use seeds 1 to 4.";

    private readonly Lazy<IReadOnlyList<ParamSpec>> _specs;
    private readonly Lazy<IReadOnlyList<StartingPoint>> _points;

    private CliffRecipe()
    {
        _specs = new(MakeSpecs);
        _points = new(MakeStartingPoints);
    }

    /// <summary>Gets the slab cliff as a mesh recipe.</summary>
    internal static CliffRecipe Instance { get; } = new();

    /// <inheritdoc/>
    public string Id => CliffParameterSet.Id;

    /// <inheritdoc/>
    public string Label => "Cliff";

    /// <inheritdoc/>
    public string ExportName => "cliff";

    /// <inheritdoc/>
    public int Version => CliffExport.Version;

    /// <inheritdoc/>
    public IReadOnlyList<ParamSpec> Specs => _specs.Value;

    /// <inheritdoc/>
    public IReadOnlyList<StartingPoint> StartingPoints => _points.Value;

    /// <summary>Gets the share of full detail each detail level is built at: 1, 0.4 and 0.12, as the export's levels.</summary>
    public IReadOnlyList<double> DetailShares => Package.LevelShares;

    /// <inheritdoc/>
    public ColouredMesh Build(Settings s, double detail, BuildPurpose purpose = BuildPurpose.View, CancellationToken cancel = default, string? replaces = null)
    {
        CliffBlock block = CliffGenerator.Build(Inputs(s), detail);
        return new ColouredMesh(block.Positions, block.Normals, block.Colours, block.Indices);
    }

    /// <inheritdoc/>
    public byte[] Glb(Settings s, string modelName, ExportOptions? options = null, CancellationToken cancel = default)
    {
        var settings = new ExportSettings(modelName, "unreal", 1, Collision: false, Look: (options ?? ExportOptions.Default).Look);
        IReadOnlyList<ExportFile> files = Package.Build(CliffExport.Source(Inputs(s), BuildInfo.Version), settings, new TextureCache());
        return files.Single(f => f.Name.EndsWith(".glb", StringComparison.Ordinal)).Bytes;
    }

    /// <summary>
    /// The shapes a player bumps into, as the export chooses them from the cliff's own offer: its blocks' upright
    /// boxes, heaviest first, up to <see cref="CliffExport.MaxCollisionShapes"/>, in the prop's own Y-up space.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="cancel">Not used: the cliff is worked out at once.</param>
    /// <param name="replaces">Not used.</param>
    /// <returns>The shapes.</returns>
    public IReadOnlyList<CollisionShape> Collision(Settings s, CancellationToken cancel = default, string? replaces = null) =>
        PropGenerator.Nature.Export.Collision.Choose(CliffExport.Source(Inputs(s)).Collision, [], CliffExport.MaxCollisionShapes);

    // The settings as the generator reads them: the cliff's own parameters, validated, and the seed.
    private static CliffSettings Inputs(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var raw = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (NatureParameterSpec spec in CliffParameterSet.Instance.Controls)
        {
            if (s.Values.TryGetValue(spec.Key, out JsonElement e))
            {
                raw[spec.Key] = e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetDouble();
            }
        }

        ParameterDictionary parameters = CliffParameterSet.Instance.Validate(raw).Parameters;
        uint seed = s.Values.ContainsKey("seed") ? s.GetSeed("seed") : (uint)DefaultSeed;
        return CliffSettings.From(parameters, seed);
    }

    private static List<ParamSpec> MakeSpecs()
    {
        var list = new List<ParamSpec>();
        foreach (NatureParameterSpec spec in CliffParameterSet.Instance.Controls)
        {
            list.Add(spec switch
            {
                NumberSpec n => new ParamSpec(
                    n.Key, n.Label, CliffTooltips.For(n.Key), "paint", n.Whole ? "int" : "float",
                    n.Min, n.Max, n.Step, JsonSerializer.SerializeToElement(n.DefaultNumber), n.Group, null, n.Unit),
                ChoiceSpec c => new ParamSpec(
                    c.Key, c.Label, CliffTooltips.For(c.Key), "paint", "choice", null, null, null,
                    JsonSerializer.SerializeToElement(c.DefaultChoice), c.Group,
                    [.. c.Options.Select(o => new ChoiceItem(o.Value, o.Label))]),
                _ => throw new InvalidOperationException($"Unexpected cliff setting '{spec.Key}'."),
            });
        }

        list.Add(new ParamSpec(
            "seed", "Seed", SeedTooltip, "paint", "seed", 0, uint.MaxValue, 1, JsonSerializer.SerializeToElement(DefaultSeed), "Seed"));
        return list;
    }

    private List<StartingPoint> MakeStartingPoints()
    {
        var list = new List<StartingPoint>();
        foreach ((string id, string name, IReadOnlyDictionary<string, object?> values, uint seed) in CliffPresets.Values)
        {
            var settings = Settings.Defaults(Specs);
            foreach (KeyValuePair<string, object?> kv in values)
            {
                settings = settings.With(kv.Key, kv.Value!);
            }

            list.Add(new StartingPoint(id, name, "Cliffs", Id, settings.With("seed", seed)));
        }

        return list;
    }
}
