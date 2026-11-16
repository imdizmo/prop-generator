using System.Text.Json;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Parameters;
using PropGenerator.Nature.Stylized;
using PropGenerator.Nature.Textures;
using NatureParameterSpec = PropGenerator.Nature.Parameters.ParameterSpec;

namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// The stylized rock as an app recipe: its settings (the rock's own, then the seed), its three starting points in the
/// drawer's Stylized group, its faceted mesh at each detail level, its GLB file and the prism you bump into.
/// </summary>
internal sealed class StylizedRockRecipe : IMeshRecipe
{
    /// <summary>The seed used by a rock whose settings do not say.</summary>
    private const double DefaultSeed = 12;

    private const string SeedTooltip =
        "A whole number from 0 to 4294967295 that picks which rock you get from the other settings: where its bumps and "
        + "hollows lie and how its facets are coloured. Two seeds give different rocks; the same seed and settings always "
        + "give the same rock. The starting points use seeds 11 to 13.";

    private readonly Lazy<IReadOnlyList<ParamSpec>> _specs;
    private readonly Lazy<IReadOnlyList<StartingPoint>> _points;

    private StylizedRockRecipe()
    {
        _specs = new(MakeSpecs);
        _points = new(MakeStartingPoints);
    }

    /// <summary>Gets the stylized rock as a mesh recipe.</summary>
    internal static StylizedRockRecipe Instance { get; } = new();

    /// <inheritdoc/>
    public string Id => StylizedRockParameterSet.Id;

    /// <inheritdoc/>
    public string Label => "Stylized rock";

    /// <inheritdoc/>
    public string ExportName => "rock";

    /// <inheritdoc/>
    public int Version => StylizedRockGenerator.Version;

    /// <inheritdoc/>
    public IReadOnlyList<ParamSpec> Specs => _specs.Value;

    /// <inheritdoc/>
    public IReadOnlyList<StartingPoint> StartingPoints => _points.Value;

    /// <summary>Gets the share of full detail each detail level is built at: 1, 0.4 and 0.12, as the export's levels.</summary>
    public IReadOnlyList<double> DetailShares => Package.LevelShares;

    /// <inheritdoc/>
    public ColouredMesh Build(Settings s, double detail, BuildPurpose purpose = BuildPurpose.View, CancellationToken cancel = default, string? replaces = null)
    {
        StylizedRock rock = StylizedRockGenerator.Build(Inputs(s), detail);
        return new ColouredMesh(rock.Positions, rock.Normals, rock.Colours, rock.Indices);
    }

    /// <inheritdoc/>
    public byte[] Glb(Settings s, string modelName, ExportOptions? options = null, CancellationToken cancel = default)
    {
        var settings = new ExportSettings(modelName, "unreal", 1, Collision: false, Look: (options ?? ExportOptions.Default).Look);
        IReadOnlyList<ExportFile> files = Package.Build(StylizedRockGenerator.Source(Inputs(s), BuildInfo.Version), settings, new TextureCache());
        return files.Single(f => f.Name.EndsWith(".glb", StringComparison.Ordinal)).Bytes;
    }

    /// <inheritdoc/>
    public IReadOnlyList<CollisionShape> Collision(Settings s, CancellationToken cancel = default, string? replaces = null) =>
        PropGenerator.Nature.Export.Collision.Choose(StylizedRockGenerator.Source(Inputs(s)).Collision, []);

    // The settings as the generator reads them: the rock's own parameters, validated, and the seed.
    private static StylizedRockSettings Inputs(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var raw = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (NatureParameterSpec spec in StylizedRockParameterSet.Instance.Controls)
        {
            if (s.Values.TryGetValue(spec.Key, out JsonElement e))
            {
                raw[spec.Key] = e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetDouble();
            }
        }

        ParameterDictionary parameters = StylizedRockParameterSet.Instance.Validate(raw).Parameters;
        uint seed = s.Values.ContainsKey("seed") ? s.GetSeed("seed") : (uint)DefaultSeed;
        return StylizedRockSettings.From(parameters, seed);
    }

    private static List<ParamSpec> MakeSpecs()
    {
        var list = new List<ParamSpec>();
        foreach (NatureParameterSpec spec in StylizedRockParameterSet.Instance.Controls)
        {
            list.Add(spec switch
            {
                NumberSpec n => new ParamSpec(
                    n.Key, n.Label, n.Help ?? string.Empty, "paint", n.Whole ? "int" : "float",
                    n.Min, n.Max, n.Step, JsonSerializer.SerializeToElement(n.DefaultNumber), n.Group, null, n.Unit),
                ChoiceSpec c => new ParamSpec(
                    c.Key, c.Label, c.Help ?? string.Empty, "paint", "choice", null, null, null,
                    JsonSerializer.SerializeToElement(c.DefaultChoice), c.Group,
                    [.. c.Options.Select(o => new ChoiceItem(o.Value, o.Label))]),
                _ => throw new InvalidOperationException($"Unexpected stylized rock setting '{spec.Key}'."),
            });
        }

        list.Add(new ParamSpec(
            "seed", "Seed", SeedTooltip, "paint", "seed", 0, uint.MaxValue, 1, JsonSerializer.SerializeToElement(DefaultSeed), "Seed"));
        return list;
    }

    private List<StartingPoint> MakeStartingPoints()
    {
        var list = new List<StartingPoint>();
        foreach ((string id, string name, IReadOnlyDictionary<string, object?> values, uint seed) in StylizedRockPresets.Values)
        {
            var settings = Settings.Defaults(Specs);
            foreach (KeyValuePair<string, object?> kv in values)
            {
                settings = settings.With(kv.Key, kv.Value!);
            }

            list.Add(new StartingPoint(id, name, "Stylized", Id, settings.With("seed", seed)));
        }

        return list;
    }
}
