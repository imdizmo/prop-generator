namespace PropGenerator;

/// <summary>A colour as lightness, chroma and hue (radians) in Oklab's polar form.</summary>
/// <param name="L">Lightness, 0 (black) to 1 (white).</param>
/// <param name="C">Chroma: how far from grey.</param>
/// <param name="H">Hue angle in radians.</param>
internal readonly record struct Oklch(double L, double C, double H)
{
    /// <summary>Converts an sRGB colour (0 to 1, gamma-encoded) with Björn Ottosson's matrices.</summary>
    /// <param name="c">The colour.</param>
    /// <returns>The colour in Oklch.</returns>
    public static Oklch FromSrgb(Rgb c)
    {
        double r = ToLinear(c.R), g = ToLinear(c.G), b = ToLinear(c.B);
        double l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
        double m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
        double s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));
        double bigL = (0.2104542553 * l) + (0.7936177850 * m) - (0.0040720468 * s);
        double a = (1.9779984951 * l) - (2.4285922050 * m) + (0.4505937099 * s);
        double bb = (0.0259040371 * l) + (0.7827717662 * m) - (0.8086757660 * s);
        return new Oklch(bigL, Math.Sqrt((a * a) + (bb * bb)), Math.Atan2(bb, a));
    }

    /// <summary>Converts back to sRGB, clipping each channel to 0 to 1.</summary>
    /// <returns>The colour.</returns>
    public Rgb ToSrgb()
    {
        (double r, double g, double b) = ToLinear();
        return new Rgb((float)FromLinear(r), (float)FromLinear(g), (float)FromLinear(b));
    }

    // Linear sRGB, not clipped (a colour outside sRGB has a channel below 0 or above 1).
    private (double R, double G, double B) ToLinear()
    {
        double a = C * Math.Cos(H), b = C * Math.Sin(H);
        double l = L + (0.3963377774 * a) + (0.2158037573 * b);
        double m = L - (0.1055613458 * a) - (0.0638541728 * b);
        double s = L - (0.0894841775 * a) - (1.2914855480 * b);
        l = l * l * l;
        m = m * m * m;
        s = s * s * s;
        return (
            (4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s),
            (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s),
            (-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s));
    }

    /// <summary>
    /// The same lightness and hue with the chroma lowered just enough for sRGB to show it without clipping a channel
    /// (found by halving the step 40 times). A colour already inside sRGB comes back as it is.
    /// </summary>
    /// <returns>The colour, inside sRGB.</returns>
    public Oklch IntoSrgbGamut()
    {
        if (InSrgbGamut())
        {
            return this;
        }

        double lo = 0, hi = C;
        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) / 2;
            if ((this with { C = mid }).InSrgbGamut())
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return this with { C = lo };
    }

    /// <summary>Whether sRGB can show this colour as it is: every linear channel from 0 to 1.</summary>
    /// <returns>True when inside sRGB.</returns>
    public bool InSrgbGamut()
    {
        const double Slack = 1e-7;
        (double r, double g, double b) = ToLinear();
        return r >= -Slack && r <= 1 + Slack && g >= -Slack && g <= 1 + Slack && b >= -Slack && b <= 1 + Slack;
    }

    private static double ToLinear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    private static double FromLinear(double v)
    {
        v = Math.Clamp(v, 0, 1);
        return v <= 0.0031308 ? 12.92 * v : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;
    }
}
