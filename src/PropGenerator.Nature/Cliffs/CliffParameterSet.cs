using PropGenerator.Nature.Parameters;
using PropGenerator.Nature.Style;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// The slab cliff's settings: ranges, defaults, groups and the tooltip of each, in panel order (the seed is added by the
/// app). The defaults are the Long cliff's.
/// </summary>
public static class CliffParameterSet
{
    /// <summary>The id of the slab cliff generator.</summary>
    public const string Id = "slab-cliff";

    /// <summary>The parameter set, every slider in panel order.</summary>
    public static ParameterSet Instance { get; } = Build();

    private static ParameterSet Build()
    {
        IReadOnlyList<ParameterSpec> schema =
        [
        Number("Width", "Proportions", 2, 60, 0.1, 28, "m"),
        Number("Height", "Proportions", 1, 40, 0.1, 9, "m"),
        Number("Depth", "Proportions", 1, 25, 0.1, 6, "m"),
        Number("Blocks", "Blocks", 1, 12, 1, 5, null, true),
        Number("Block variety", "Blocks", 0, 1, 0.01, 0.5, null),
        Number("Tiers", "Blocks", 1, 4, 1, 1, null, true),
        Number("Layers", "Blocks", 0, 12, 1, 0, null, true),
        Number("Layer variety", "Blocks", 0, 1, 0.01, 0.5, null),
        Number("Joint crack", "Blocks", 0, 0.2, 0.005, 0.08, "m"),
        Number("Lean", "Shape", -10, 30, 0.5, 8, "°"),
        Number("Top tilt", "Shape", 0, 60, 0.5, 4, "°"),
        Number("Taper", "Shape", 0, 0.6, 0.01, 0, null),
        Number("End turn", "Shape", 0, 50, 0.5, 50, "°"),
        Number("Foot", "Shape", 0, 0.4, 0.01, 0, null),
        Number("Underside tilt", "Shape", 0, 30, 0.5, 0, "°"),
        Number("Faces", "Shape", 4, 16, 1, 8, null, true),
        Number("Roughness", "Shape", 0, 1.5, 0.01, 0.3, "m"),
        Number("Bevel", "Shape", 0, 1.5, 0.01, 0.3, "m"),
        Number("Sink", "Shape", 0, 2, 0.05, 0.5, "m"),
        Number("Steps", "Cuts", 0, 8, 1, 2, null, true),
        Number("Bites", "Cuts", 0, 8, 1, 2, null, true),
        Number("Grooves", "Cuts", 0, 4, 1, 1, null, true),
        Number("Cut depth", "Cuts", 0, 1, 0.01, 0.6, null),
        Number("Chisel", "Surface", 0, 0.5, 0.005, 0.03, "m"),
        Number("Chisel size", "Surface", 0.2, 5, 0.05, 2.5, "m"),
        Number("Edge chips", "Surface", 0, 1, 0.01, 0.3, "m"),
        Number("Chip size", "Surface", 0.1, 3, 0.05, 1.5, "m"),
        Number("Edge wear", "Surface", 0, 1, 0.01, 0.15, "m"),
        Number("Wear width", "Surface", 0.1, 3, 0.05, 0.8, "m"),
        Number("Wear size", "Surface", 0.1, 5, 0.05, 2.0, "m"),
        Number("Smudge", "Surface", 0, 1.5, 0.01, 0.02, "m"),
        Number("Smudge size", "Surface", 0.3, 10, 0.1, 5, "m"),
        Number("Creases", "Cracks", 0, 4, 1, 1, null, true),
        Number("Crease depth", "Cracks", 0, 2, 0.01, 0.2, "m"),
        Number("Crease width", "Cracks", 0.05, 2, 0.01, 0.1, "m"),
        Number("Crease step", "Cracks", 0, 1.5, 0.01, 0, "m"),
        Number("Crease angle", "Cracks", 0, 90, 1, 10, "°"),
        Number("Crease wobble", "Cracks", 0, 1.5, 0.01, 0.15, "m"),
        Number("Crease seed", "Cracks", 0, 9999, 1, 31, null, true),
        Number("Ledges", "Cracks", 0, 3, 1, 0, null, true),
        Number("Ledge depth", "Cracks", 0, 2, 0.01, 0.3, "m"),
        Number("Ledge step", "Cracks", 0, 1.5, 0.01, 0.2, "m"),
        Number("Lines", "Cracks", 0, 40, 1, 0, null, true),
        Number("Line depth", "Cracks", 0, 0.5, 0.005, 0.05, "m"),
        Number("Line wobble", "Cracks", 0, 10, 0.1, 2, null),
        Number("Detail", "Mesh", 2, 24, 0.5, 8, null),
        Number("Sharp angle", "Mesh", 5, 90, 1, 30, "°"),
        new ChoiceSpec(
            "palette",
            "Palette",
            CliffTooltips.For("palette"),
            "Colour",
            [.. Palettes.RockRamps.Keys.Select(name => new ChoiceOption(name, char.ToUpperInvariant(name[0]) + name[1..]))],
            "granite"),
        Number("Variation", "Colour", 0, 1, 0.01, 0.5, null),
        ];
        return new ParameterSet(Id, schema, [.. schema.Select(spec => spec.Key)]);
    }

    /// <summary>One slider: its key is the label in lower case with hyphens, its help the tooltip for that key.</summary>
    private static NumberSpec Number(string label, string group, double min, double max, double step, double defaultValue, string? unit, bool whole = false)
    {
        string key = label.ToLowerInvariant().Replace(' ', '-');
        return new NumberSpec(key, label, CliffTooltips.For(key), group, min, max, step, defaultValue, unit, whole);
    }
}
