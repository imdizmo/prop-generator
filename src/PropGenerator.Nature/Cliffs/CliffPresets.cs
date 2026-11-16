using System.Collections.ObjectModel;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// The slab cliff's starting points, in drawer order: Long cliff, Tall cliff, Cliff top and Layered cliff. Each is only
/// a place to start from: the same sliders drag any one of them into any other.
/// </summary>
public static class CliffPresets
{
    /// <summary>
    /// Gets the starting points as raw parameter values by key (a slider left at its default may be missing) with their
    /// seed, in drawer order: <see cref="All"/>'s settings are <see cref="CliffSettings.From"/> of these values,
    /// validated by <see cref="CliffParameterSet"/>. Each sets the spec's proportions, its blocks, tiers and layers, and
    /// its palette; the Long cliff keeps every other slider at its default (the defaults are its values), and the others
    /// set a few more for their character (tuned by eye with tools/cliff_match/seed_sheet.sh, and the surface against the hand-made cliffs with tools/cliff_match/closeups.sh).
    /// </summary>
    public static IReadOnlyList<(string Id, string Name, IReadOnlyDictionary<string, object?> Values, uint Seed)> Values { get; } =
    [
        ("long-cliff", "Long cliff", Preset(28, 9, 6, 5, 1, 0, "granite"), 1),

        // One tall face, as Cliff 01 and Cliff 02 are: its two blocks leaning less, its steps shallower and one corner
        // bite, so its seeds read as one mass (a tilted top keeps their outlines apart).
        ("tall-cliff", "Tall cliff", Preset(6.5, 13, 3.5, 2, 1, 0, "slate", ("lean", 5), ("top-tilt", 12), ("bites", 1), ("cut-depth", 0.45)), 2),

        // Set-back masses broken by more steps, bites and grooves, and rougher faces, so the crests read apart seed to seed.
        ("cliff-top", "Cliff top", Preset(14, 5, 8, 3, 3, 0, "granite", ("roughness", 0.5), ("steps", 4), ("bites", 3), ("grooves", 2)), 3),

        // Plates that differ more in thickness and in where they sit, with tilted, stepped and bitten tops; its many plate
        // rims are chipped and worn less, so they stay crisp, as the hand-made limestone's do, and its crack is shallower,
        // as it crosses every plate's rim.
        ("layered-cliff", "Layered cliff", Preset(10, 7, 6, 2, 1, 7, "lavender", ("layer-variety", 0.7), ("block-variety", 0.6), ("top-tilt", 7), ("steps", 3), ("bites", 3), ("edge-chips", 0.25), ("edge-wear", 0.08), ("crease-depth", 0.12)), 4),
    ];

    /// <summary>Gets the starting points in drawer order, as settings (see <see cref="Values"/>).</summary>
    public static IReadOnlyList<(string Id, string Name, CliffSettings Settings)> All { get; } =
        [.. Values.Select(p => (p.Id, p.Name, CliffSettings.From(CliffParameterSet.Instance.Validate(p.Values).Parameters, p.Seed)))];

    // One starting point's values by parameter key, before validation: its proportions, layout and palette, and any
    // other slider it sets away from its default.
    private static ReadOnlyDictionary<string, object?> Preset(
        double width, double height, double depth, int blocks, int tiers, int layers, string palette, params (string Key, double Value)[] more)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["width"] = width,
            ["height"] = height,
            ["depth"] = depth,
            ["blocks"] = (double)blocks,
            ["tiers"] = (double)tiers,
            ["layers"] = (double)layers,
            ["palette"] = palette,
        };
        foreach (var (key, value) in more)
        {
            values[key] = value;
        }

        return new(values);
    }
}
