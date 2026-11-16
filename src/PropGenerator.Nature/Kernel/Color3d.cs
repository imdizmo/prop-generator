// Ported from three.js r186 src/math/Color.js and src/math/ColorManagement.js (MIT, © 2010-2026 three.js authors).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Kernel;

/// <summary>
/// three's Color: linear red, green and blue in doubles. A mutable object whose methods change it and return it, so
/// easy-props' shared paint colours (<c>paint.Copy(deep).Lerp(light, t)</c>) behave as in three. Only the methods
/// easy-props calls are here.
/// </summary>
public sealed class Color3d
{
    /// <summary>White, as <c>new Color()</c>.</summary>
    public Color3d()
        : this(1, 1, 1)
    {
    }

    /// <summary>A colour from linear components (three's working colour space; nothing is converted).</summary>
    public Color3d(double r, double g, double b)
    {
        R = r;
        G = g;
        B = b;
    }

    /// <summary>Linear red.</summary>
    public double R { get; set; }

    /// <summary>Linear green.</summary>
    public double G { get; set; }

    /// <summary>Linear blue.</summary>
    public double B { get; set; }

    /// <summary>
    /// <c>new Color(0xrrggbb)</c> (and <c>new Color('#rrggbb')</c>, which parses to the same number): each byte over 255,
    /// converted from sRGB to linear with three's constants.
    /// </summary>
    public static Color3d FromHex(int hex) => new Color3d(
        ((hex >> 16) & 255) / 255.0,
        ((hex >> 8) & 255) / 255.0,
        (hex & 255) / 255.0).SrgbToLinear();

    /// <summary>
    /// <c>new Color('#rgb')</c> or <c>new Color('#rrggbb')</c> (three's setStyle for hex text): each digit over 15 or each
    /// byte over 255, converted from sRGB to linear. Any other text throws <see cref="NotSupportedException"/>: easy-props
    /// only ever hands hex colours to the export.
    /// </summary>
    public static Color3d FromStyle(string style)
    {
        ArgumentNullException.ThrowIfNull(style);
        string hex = style.StartsWith('#') ? style[1..] : "";
        if (hex.Length == 0 || !hex.All(char.IsAsciiHexDigit) || hex.Length is not (3 or 6))
        {
            throw new NotSupportedException($"Only #rgb and #rrggbb colours are read, not '{style}'");
        }

        if (hex.Length == 3)
        {
            return new Color3d(
                Convert.ToInt32(hex[..1], 16) / 15.0,
                Convert.ToInt32(hex[1..2], 16) / 15.0,
                Convert.ToInt32(hex[2..], 16) / 15.0).SrgbToLinear();
        }

        return FromHex(Convert.ToInt32(hex, 16));
    }

    /// <summary>A new colour with the same components.</summary>
    public Color3d Clone() => new(R, G, B);

    /// <summary>Takes the components of <paramref name="color"/>.</summary>
    public Color3d Copy(Color3d color)
    {
        R = color.R;
        G = color.G;
        B = color.B;
        return this;
    }

    /// <summary>Multiplies every component by <paramref name="s"/>.</summary>
    public Color3d MultiplyScalar(double s)
    {
        R *= s;
        G *= s;
        B *= s;
        return this;
    }

    /// <summary>Moves toward <paramref name="color"/> by <paramref name="alpha"/>.</summary>
    public Color3d Lerp(Color3d color, double alpha)
    {
        R += (color.R - R) * alpha;
        G += (color.G - G) * alpha;
        B += (color.B - B) * alpha;
        return this;
    }

    /// <summary>Becomes the colour <paramref name="alpha"/> of the way from <paramref name="color1"/> to <paramref name="color2"/>.</summary>
    public Color3d LerpColors(Color3d color1, Color3d color2, double alpha)
    {
        R = color1.R + ((color2.R - color1.R) * alpha);
        G = color1.G + ((color2.G - color1.G) * alpha);
        B = color1.B + ((color2.B - color1.B) * alpha);
        return this;
    }

    /// <summary>three's SRGBToLinear (ColorManagement.js), on each component.</summary>
    private Color3d SrgbToLinear()
    {
        R = SrgbToLinear(R);
        G = SrgbToLinear(G);
        B = SrgbToLinear(B);
        return this;
    }

    private static double SrgbToLinear(double c) =>
        c < 0.04045 ? c * 0.0773993808 : JsMath.Pow((c * 0.9478672986) + 0.0521327014, 2.4);
}
