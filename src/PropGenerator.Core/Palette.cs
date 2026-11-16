namespace PropGenerator;

/// <summary>
/// Every tone the painting uses, worked out from one picked wood colour. Each is the picked colour moved by the same
/// Oklch step the v5 painting has from its own base colour, so the v5 base gives back v5's numbers exactly.
/// </summary>
/// <param name="Base">The wood colour (a very dark pick is lifted a little so the seams can be darker).</param>
/// <param name="LitTint">Per-channel multiplier where the plank is lit.</param>
/// <param name="ShadeTint">Per-channel multiplier where the plank is in shade.</param>
/// <param name="Redder">Per-channel multiplier for a drift towards red.</param>
/// <param name="Greyer">Per-channel multiplier for a drift towards grey.</param>
/// <param name="EndMul">Per-channel multiplier for the cut ends (end grain).</param>
/// <param name="SideMul">Per-channel multiplier for the long sides.</param>
/// <param name="Gap">The colour of the gap between planks.</param>
/// <param name="SeamDeep">The seam colour at its deepest.</param>
/// <param name="SeamLight">The seam colour at its shallowest.</param>
public sealed record Palette(
    Rgb Base,
    Rgb LitTint,
    Rgb ShadeTint,
    Rgb Redder,
    Rgb Greyer,
    Rgb EndMul,
    Rgb SideMul,
    Rgb Gap,
    Rgb SeamDeep,
    Rgb SeamLight)
{
    private const double MinL = 0.02;
    private const double MaxL = 0.98;
    private const double MinBaseL = 0.05;
    private const double MinRatio = 0.5;
    private const double MaxRatio = 2.0;

    // v5's own colours: the base the steps are measured from, and what each tone was in v5.
    private static readonly Rgb V5Base = new(0.272f, 0.188f, 0.170f);
    private static readonly Oklch V5BaseLch = Oklch.FromSrgb(V5Base);

    private static readonly Oklch LitStep = Step(V5Base * new Rgb(1.05f, 1f, .9f));
    private static readonly Oklch ShadeStep = Step(V5Base * new Rgb(.94f, .9f, 1f));
    private static readonly Oklch RedderStep = Step(V5Base * new Rgb(1.07f, .985f, .955f));
    private static readonly Oklch GreyerStep = Step(V5Base * new Rgb(.955f, .99f, 1.045f));
    private static readonly Oklch EndStep = Step(V5Base * new Rgb(1.16f, 1.12f, 1.06f));
    private static readonly Oklch SideStep = Step(V5Base * new Rgb(.94f, .94f, .94f));
    private static readonly Oklch GapLch = Oklch.FromSrgb(Rgb.FromHex("#24150C"));
    private static readonly Oklch DeepLch = Oklch.FromSrgb(new Rgb(0.085f, 0.055f, 0.043f));
    private static readonly Oklch LightLch = Oklch.FromSrgb(new Rgb(0.135f, 0.088f, 0.07f));

    /// <summary>Works out every tone from the wood colour the user picked.</summary>
    /// <param name="pick">The picked colour.</param>
    /// <returns>The palette.</returns>
    public static Palette Derive(Rgb pick)
    {
        var lch = Oklch.FromSrgb(pick);
        if (lch.L < MinBaseL)
        {
            lch = lch with { L = MinBaseL };
        }
        else if (lch.L > MaxL)
        {
            lch = lch with { L = MaxL };
        }

        Rgb baseColour = lch == Oklch.FromSrgb(pick) ? pick : lch.ToSrgb();
        lch = Oklch.FromSrgb(baseColour);
        return new Palette(
            baseColour,
            Ratio(baseColour, lch, LitStep),
            Ratio(baseColour, lch, ShadeStep),
            Ratio(baseColour, lch, RedderStep),
            Ratio(baseColour, lch, GreyerStep),
            Ratio(baseColour, lch, EndStep),
            Ratio(baseColour, lch, SideStep),
            Seam(lch, GapLch),
            Seam(lch, DeepLch),
            Seam(lch, LightLch));
    }

    private static Oklch Step(Rgb target)
    {
        var t = Oklch.FromSrgb(target);
        return new Oklch(t.L - V5BaseLch.L, t.C - V5BaseLch.C, HueDelta(t.H, V5BaseLch.H));
    }

    private static double HueDelta(double to, double from)
    {
        double d = to - from;
        while (d > Math.PI)
        {
            d -= 2 * Math.PI;
        }

        while (d <= -Math.PI)
        {
            d += 2 * Math.PI;
        }

        return d;
    }

    // The pick moved by the step, divided by the pick: the tint as a per-channel multiplier.
    private static Rgb Ratio(Rgb pick, Oklch pickLch, Oklch step)
    {
        Rgb moved = new Oklch(
            Math.Clamp(pickLch.L + step.L, MinL, MaxL),
            Math.Max(0, pickLch.C + step.C),
            pickLch.H + step.H).ToSrgb();
        return new Rgb(Div(moved.R, pick.R), Div(moved.G, pick.G), Div(moved.B, pick.B));
    }

    private static float Div(float moved, float pick) =>
        pick < 1e-4f ? 1f : (float)Math.Clamp(moved / (double)pick, MinRatio, MaxRatio);

    // Seams keep v5's own lightness (an absolute step), lowered further only when the pick is darker than v5's wood, so
    // they stay dark on pale woods. Their chroma is the pick's times v5's ratio of seam chroma to wood chroma, and their
    // hue is the pick's turned as v5's was. A dark seam of a strong colour can fall outside sRGB: then its chroma is
    // lowered until it fits, which keeps its lightness and hue (clipping each channel would shift both). v5's pick gives
    // v5's seams back exactly: they are sRGB colours, so nothing is lowered.
    private static Rgb Seam(Oklch pickLch, Oklch v5Seam)
    {
        double l = Math.Min(v5Seam.L, pickLch.L + (v5Seam.L - V5BaseLch.L));
        return new Oklch(
            Math.Clamp(l, MinL, MaxL),
            pickLch.C * (v5Seam.C / V5BaseLch.C),
            pickLch.H + HueDelta(v5Seam.H, V5BaseLch.H)).IntoSrgbGamut().ToSrgb();
    }
}
