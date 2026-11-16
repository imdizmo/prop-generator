// Ported from easy-props frontend/src/assets/style/toon-looks.ts (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Style;

/// <summary>
/// The numbers behind the Cartoon and Comic looks, kept in one place so the view and an engine's shaders made at export
/// draw a prop the same way. Brightness and colour values are linear, from 0 to 1.
/// </summary>
public static class ToonLooks
{
    /// <summary>The soft look: shadow, mid and lit bands.</summary>
    public static IReadOnlyList<double> CartoonLevels { get; } = [0.42, 0.7, 1];

    /// <summary>Where light moves from one Cartoon band to the next.</summary>
    public static IReadOnlyList<double> CartoonSteps { get; } = [0.3, 0.62];

    /// <summary>How wide each soft Cartoon step is, as a share of the whole range.</summary>
    public const double CartoonSoftness = 0.09;

    /// <summary>The Cartoon look's line weight.</summary>
    public const double CartoonLineWeight = 0.75;

    /// <summary>The Cartoon look's line strength.</summary>
    public const double CartoonLineStrength = 0.45;

    /// <summary>The Comic look's shadow and lit bands.</summary>
    public static IReadOnlyList<double> ComicBands { get; } = [80 / 255.0, 1];

    /// <summary>Where the light falls between shadow and lit, as how bright a point is next to its own colour.</summary>
    public const double ComicShadeLevel = 0.55;

    /// <summary>The lit level.</summary>
    public const double ComicLitLevel = 1.05;

    /// <summary>Half the width of the one step between shadow and lit.</summary>
    public const double ComicStepSoftness = 0.03;

    /// <summary>How much richer colours are than the painted ones.</summary>
    public const double ComicVivid = 1.2;

    /// <summary>How colourful a colour must be before it gets richer colour and deeper shade, between these two.</summary>
    public static IReadOnlyList<double> ComicColourful { get; } = [0.3, 0.6];

    /// <summary>How far shade deepens a colour.</summary>
    public const double ComicShadowDepth = 1.7;

    /// <summary>How far shade deepens a grey.</summary>
    public const double ComicGreyShadowDepth = 0.7;

    /// <summary>How dark shade goes.</summary>
    public const double ComicShadowDarkness = 0.8;

    /// <summary>How far the lit side keeps the light's tint.</summary>
    public const double ComicTint = 0.6;

    /// <summary>The most the lit side may tint.</summary>
    public static IReadOnlyList<double> ComicTintRange { get; } = [0.7, 1.4];

    /// <summary>How far a leaf card's light follows the round shape of its whole clump rather than its own lumps.</summary>
    public const double ComicRounding = 0.85;

    /// <summary>A grey print darker than this is a stroke.</summary>
    public const double ComicStrokeLevel = 0.8;

    /// <summary>The tone a stroke is drawn at.</summary>
    public const double ComicStrokeTone = 0.62;

    /// <summary>A print counts as grey when its brightest and dimmest channels are closer than this.</summary>
    public const double ComicGreyPrint = 0.02;

    /// <summary>The Comic look's line weight.</summary>
    public const double ComicLineWeight = 1.5;

    /// <summary>The Comic look's line strength.</summary>
    public const double ComicLineStrength = 1;

    /// <summary>Tones nearer than this count as one colour (easy-props' <c>FLAT_COLOURS.sameColour</c>).</summary>
    public const double FlatSameColour = 0.3;

    /// <summary>How much brightness counts next to hue when deciding two tones are one colour.</summary>
    public const double FlatBrightnessWeight = 0.3;

    /// <summary>How far a joined tone leans to the colour of its lighter half.</summary>
    public const double FlatLean = 0.75;

    /// <summary>The most tones a part keeps.</summary>
    public const int FlatMaxTones = 6;

    /// <summary>The grouping rounds.</summary>
    public const int FlatRounds = 8;

    /// <summary>The ink lines' colour.</summary>
    public const string InkColour = "#1c1814";

    /// <summary>The ink line width in screen pixels at weight 1, on the outside of a shape.</summary>
    public const double InkOuter = 2;

    /// <summary>The ink line width where shapes overlap.</summary>
    public const double InkInner = 1.25;

    /// <summary>Gaps in a crown narrower than this many pixels go without a line.</summary>
    public const double InkHoleReach = 5;

    /// <summary>How bright the Cartoon look draws each amount of light: flat bands joined by short soft steps.</summary>
    /// <param name="light">The amount of light.</param>
    public static double CartoonLevel(double light)
    {
        double level = CartoonLevels[0];
        for (int index = 0; index < CartoonSteps.Count; index++)
        {
            double step = CartoonSteps[index];
            level += (CartoonLevels[index + 1] - CartoonLevels[index]) * Smoothstep(step - (CartoonSoftness / 2), step + (CartoonSoftness / 2), light);
        }

        return level;
    }

    private static double Smoothstep(double edge0, double edge1, double x)
    {
        double t = Math.Min(Math.Max((x - edge0) / (edge1 - edge0), 0), 1);
        return t * t * (3 - (2 * t));
    }
}
