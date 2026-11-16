using PropGenerator.Nature.Style;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// The cliff's vertex colours: a place on the rock palette's four-colour ramp set by how far the vertex faces up, shifted
/// by broad patches of noise read where the vertex lies (so the patches follow the rock, not the blocks, and no colour
/// line shows where blocks fuse), and a little lower where the surface was cut in.
/// </summary>
internal static class CliffColours
{
    /// <summary>The ramp's middle, where a side facing straight out sits: between its two middle colours.</summary>
    private const double Middle = 0.5;

    /// <summary>How far up (or down) the ramp a vertex facing straight up (or down) sits from the middle.</summary>
    private const double Facing = 0.45;

    /// <summary>The farthest the colours are shifted along the ramp, at Variation 1.</summary>
    private const double MaxShift = 0.12;

    /// <summary>How big the colour patches are, in metres.</summary>
    private const double PatchSize = 8;

    /// <summary>
    /// The patch noise's distance from a half is scaled by this before it is held to ±1: the noise mostly stays within
    /// 0.3 of a half (twice its spread), so the patches span most of the shift and few are held.
    /// </summary>
    private const double PatchContrast = 3;

    /// <summary>Cuts deeper than this, in metres, darken no more.</summary>
    private const double DeepestCut = 0.3;

    /// <summary>How far down the ramp the deepest cut moves a vertex.</summary>
    private const double CutDarkening = 0.12;

    // The salt of the patch noise.
    private const int ShiftSalt = 600;

    /// <summary>The rock palette's ramp, by name; an unknown name throws.</summary>
    /// <param name="palette">A key of <see cref="Palettes.RockRamps"/>.</param>
    public static Ramp RampFor(string palette) =>
        Palettes.RockRamps.TryGetValue(palette, out var stops)
            ? new Ramp(stops)
            : throw new ArgumentException($"Unknown rock palette '{palette}'", nameof(palette));

    /// <summary>
    /// The seeded shift along the ramp of the colour at a point, up to ± 0.12 × variation: low-frequency noise in
    /// cliff space, in patches about 8 m across.
    /// </summary>
    /// <param name="seed">The cliff's seed.</param>
    /// <param name="p">The point in cliff space.</param>
    /// <param name="variation">The Variation slider, 0 to 1.</param>
    public static double Shift(uint seed, V3 p, double variation)
    {
        if (variation == 0)
        {
            return 0;
        }

        double noise = CliffNoise.Fbm(p / PatchSize, CliffNoise.Hash01(seed, ShiftSalt) * 1000, 2);
        return Math.Clamp((noise - 0.5) * PatchContrast, -1, 1) * MaxShift * variation;
    }

    /// <summary>
    /// A vertex's place on the ramp (the ramp clamps it to 0..1): toward the darkest facing down, the middle two
    /// colours on the sides, toward the lightest facing up; moved by the patch's shift, and down by the cut's depth.
    /// </summary>
    /// <param name="normalZ">The up part of the vertex's unit normal.</param>
    /// <param name="shift">The shift where the vertex lies, from <see cref="Shift"/>.</param>
    /// <param name="cutIn">How far the surface was cut in at the vertex, in metres.</param>
    public static double Value(double normalZ, double shift, double cutIn) =>
        Middle + (normalZ * Facing) + shift - (Math.Clamp(cutIn, 0, DeepestCut) / DeepestCut * CutDarkening);
}
