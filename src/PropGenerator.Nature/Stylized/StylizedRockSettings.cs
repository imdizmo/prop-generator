using System.Collections.ObjectModel;
using PropGenerator.Nature.Parameters;
using PropGenerator.Nature.Style;

namespace PropGenerator.Nature.Stylized;

/// <summary>
/// The stylized rock's settings: ranges, defaults, groups and the tooltip of each, in panel order (the seed is added by
/// the app). The recipe is Blender Artifex's one-minute stylized rock: a sphere pushed in and out by Voronoi noise, then
/// thinned until only large flat facets are left.
/// </summary>
public static class StylizedRockParameterSet
{
    /// <summary>The id of the stylized rock generator.</summary>
    public const string Id = "stylized-rock";

    /// <summary>The parameter set, every slider in panel order.</summary>
    public static ParameterSet Instance { get; } = Build();

    private static ParameterSet Build()
    {
        IReadOnlyList<ParameterSpec> schema =
        [
            new NumberSpec("size", "Size", "How wide the rock is across, in metres, from 0.2 to 10. A pebble is 0.4 m, a rock to sit on 1.2 m, a boulder 3 m.", "Shape", 0.2, 10, 0.05, 1.2, "m"),
            new NumberSpec("height", "Height", "How tall the rock stands as a share of its width, from 0.3 (a flat slab) to 2 (a standing stone). 0.8 is a settled rock.", "Shape", 0.3, 2, 0.01, 0.8),
            new NumberSpec("bumps", "Bumps", "How many bumps and hollows fit across the rock, from 1 (one big lump) to 6 (many small ones). About 2 gives the look of the video.", "Shape", 1, 6, 0.1, 2.2),
            new NumberSpec("bumpiness", "Bumpiness", "How far the bumps push out and the hollows sink in, from 0 (a smooth egg) to 1 (deep craters and ridges). 0.5 is a lumpy rock.", "Shape", 0, 1, 0.01, 0.5),
            new NumberSpec("facets", "Facets", "How big the flat facets are, as a share of the rock's width, from 0.01 (a smooth, dense mesh) to 0.25 (a few large planes). The mesh is thinned until a facet would stray this far from the bumpy surface.", "Mesh", 0.01, 0.25, 0.005, 0.1),
            new ChoiceSpec(
                "palette",
                "Palette",
                "The stone's colours, dark to light: the ramp the facets are coloured from by how far they face up.",
                "Colour",
                [.. Palettes.RockRamps.Keys.Select(name => new ChoiceOption(name, char.ToUpperInvariant(name[0]) + name[1..]))],
                "granite"),
            new NumberSpec("variation", "Variation", "How much the facets differ in colour from their neighbours, from 0 (shaded by facing alone) to 1 (a patchwork).", "Colour", 0, 1, 0.01, 0.5),
        ];
        return new ParameterSet(Id, schema, [.. schema.Select(spec => spec.Key)]);
    }
}

/// <summary>The stylized rock's settings as typed values: one property per slider and the seed. Make one with <see cref="From"/>.</summary>
public sealed record StylizedRockSettings
{
    /// <summary>Gets the settings every slider's default gives, with seed 1.</summary>
    public static StylizedRockSettings Defaults { get; } = From(StylizedRockParameterSet.Instance.Defaults, 1);

    /// <summary>Gets how wide the rock is across, in metres.</summary>
    public double Size { get; init; }

    /// <summary>Gets how tall the rock stands as a share of its width.</summary>
    public double Height { get; init; }

    /// <summary>Gets how many bumps fit across the rock.</summary>
    public double Bumps { get; init; }

    /// <summary>Gets how far the bumps push out, 0 to 1.</summary>
    public double Bumpiness { get; init; }

    /// <summary>Gets how far a facet may stray from the bumpy surface, as a share of the width.</summary>
    public double Facets { get; init; }

    /// <summary>Gets the rock palette's name (a key of <see cref="Palettes.RockRamps"/>).</summary>
    public string Palette { get; init; } = "granite";

    /// <summary>Gets how much the facets differ in colour, 0 to 1.</summary>
    public double Variation { get; init; }

    /// <summary>Gets the seed.</summary>
    public uint Seed { get; init; }

    /// <summary>The settings from validated parameters (see <see cref="StylizedRockParameterSet"/>) and a seed.</summary>
    /// <param name="p">The parameters, as <see cref="ParameterSet.Validate(object?)"/> gives them.</param>
    /// <param name="seed">The seed.</param>
    public static StylizedRockSettings From(ParameterDictionary p, uint seed)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new StylizedRockSettings
        {
            Size = p.Number("size"),
            Height = p.Number("height"),
            Bumps = p.Number("bumps"),
            Bumpiness = p.Number("bumpiness"),
            Facets = p.Number("facets"),
            Palette = p.Text("palette"),
            Variation = p.Number("variation"),
            Seed = seed,
        };
    }

    /// <summary>The settings by their slider keys, then the seed, as an export's description lists them.</summary>
    public IReadOnlyDictionary<string, object?> Parameters() => new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["size"] = Size,
        ["height"] = Height,
        ["bumps"] = Bumps,
        ["bumpiness"] = Bumpiness,
        ["facets"] = Facets,
        ["palette"] = Palette,
        ["variation"] = Variation,
        ["seed"] = (double)Seed,
    };
}

/// <summary>The stylized rock's starting points, in drawer order: a pebble, a rock and a boulder.</summary>
public static class StylizedRockPresets
{
    /// <summary>Gets the starting points as raw parameter values by key (a slider left at its default is missing) with their seed.</summary>
    public static IReadOnlyList<(string Id, string Name, IReadOnlyDictionary<string, object?> Values, uint Seed)> Values { get; } =
    [
        ("stylized-pebble", "Pebble", Preset(0.4, 0.7, 2.0, "slate"), 11),
        ("stylized-rock", "Stylized rock", Preset(1.2, 0.8, 2.2, "granite"), 12),
        ("stylized-boulder", "Stylized boulder", Preset(3, 0.9, 2.8, "sandstone"), 13),
    ];

    /// <summary>Gets the starting points in drawer order, as settings.</summary>
    public static IReadOnlyList<(string Id, string Name, StylizedRockSettings Settings)> All { get; } =
        [.. Values.Select(p => (p.Id, p.Name, StylizedRockSettings.From(StylizedRockParameterSet.Instance.Validate(p.Values).Parameters, p.Seed)))];

    private static ReadOnlyDictionary<string, object?> Preset(double size, double height, double bumps, string palette) =>
        new(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["size"] = size,
            ["height"] = height,
            ["bumps"] = bumps,
            ["palette"] = palette,
        });
}
