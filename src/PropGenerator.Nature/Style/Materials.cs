// Ported from easy-props frontend/src/assets/style/materials.ts and materialColor in
// frontend/src/viewport/scene/component-renderer.ts (MIT, © 2026 AHS12).
using System.Globalization;
using System.Text;
using PropGenerator.Nature.Js;
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Style;

/// <summary>
/// The material palette: the colour each material kind is painted with, from the scene's two colours. The viewport and
/// the export use the same colours, so an exported prop looks like what the user saw.
/// </summary>
public static class Materials
{
    /// <summary>The bark colour a new prop starts with.</summary>
    public const string BarkColour = "#6b4e35";

    /// <summary>The leaf colour a new prop starts with.</summary>
    public const string LeavesColour = "#4e7d3e";

    /// <summary>The coconut colour, which no scene colour changes.</summary>
    public const string CoconutColour = "#7a5c3e";

    /// <summary>
    /// easy-props' <c>materialColor</c>: the colour a material kind is painted with, as <c>#rrggbb</c> text. Lighter bark is
    /// the bark lightened by 0.2, dark leaves the leaves darkened by 0.24; "painted" is white, its colour being in its
    /// vertices. Any other kind throws.
    /// </summary>
    public static string ColourOf(string material, SceneMaterials materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        return material switch
        {
            "bark" => materials.Bark,
            "bark-light" => Lighten(materials.Bark, 0.2),
            "leaves" => materials.Leaves,
            "leaves-dark" => Darken(materials.Leaves, 0.24),
            "coconut" => CoconutColour,
            "painted" => "#ffffff",
            _ => throw new ArgumentException($"Unknown material kind '{material}'", nameof(material)),
        };
    }

    /// <summary>The linear tint a part of <paramref name="material"/> is multiplied by: three's <c>new Color(ColourOf(…))</c>.</summary>
    public static Color3d Tint(string material, SceneMaterials materials) => Color3d.FromStyle(ColourOf(material, materials));

    /// <summary>Mixes a hex colour towards black by <paramref name="amount"/> (0 to 1), each channel rounded as JavaScript rounds.</summary>
    public static string Darken(string hex, double amount) => Mix(hex, "#000000", amount);

    /// <summary>Mixes a hex colour towards white by <paramref name="amount"/> (0 to 1), each channel rounded as JavaScript rounds.</summary>
    public static string Lighten(string hex, double amount) => Mix(hex, "#ffffff", amount);

    /// <summary>easy-props' <c>mix</c>: text it cannot read as hex comes back unchanged.</summary>
    private static string Mix(string hex, string towards, double amount)
    {
        if (Parse(hex) is not { } from || Parse(towards) is not { } to)
        {
            return hex;
        }

        var mixed = new StringBuilder("#");
        for (int i = 0; i < 3; i++)
        {
            double channel = JsMath.Round(from[i] + ((to[i] - from[i]) * amount));
            mixed.Append(Hex(channel).PadLeft(2, '0'));
        }

        return mixed.ToString();
    }

    /// <summary>
    /// The channels of <c>#rgb</c> or <c>#rrggbb</c> as mix reads them: the first '#' dropped, three digits doubled, then
    /// <c>Number.parseInt(text, 16)</c> and its three low bytes; null when parseInt finds no number.
    /// </summary>
    private static int[]? Parse(string value)
    {
        int hash = value.IndexOf('#', StringComparison.Ordinal);
        string stripped = hash < 0 ? value : value.Remove(hash, 1);
        string full = stripped.Length == 3 ? string.Concat(stripped.Select(c => $"{c}{c}")) : stripped;
        double number = ParseIntHex(full);
        if (double.IsNaN(number))
        {
            return null;
        }

        int bits = JsNumber.ToInt32(number);
        return [(bits >> 16) & 0xff, (bits >> 8) & 0xff, bits & 0xff];
    }

    /// <summary>
    /// <c>Number.parseInt(text, 16)</c>: leading white space and one sign skipped, an optional <c>0x</c>, then as many hex
    /// digits as there are; NaN for none.
    /// </summary>
    private static double ParseIntHex(string text)
    {
        int at = 0;
        while (at < text.Length && (char.IsWhiteSpace(text[at]) || text[at] == '\uFEFF'))
        {
            at++;
        }

        double sign = 1;
        if (at < text.Length && text[at] is '+' or '-')
        {
            sign = text[at] == '-' ? -1 : 1;
            at++;
        }

        if (at + 1 < text.Length && text[at] == '0' && text[at + 1] is 'x' or 'X')
        {
            at += 2;
        }

        int start = at;
        double value = 0;
        while (at < text.Length && char.IsAsciiHexDigit(text[at]))
        {
            value = (value * 16) + Convert.ToInt32(text[at].ToString(), 16);
            at++;
        }

        return at == start ? double.NaN : sign * value;
    }

    /// <summary><c>Number.prototype.toString(16)</c> for a whole number, as a channel is.</summary>
    private static string Hex(double whole) =>
        whole < 0
            ? "-" + ((long)-whole).ToString("x", CultureInfo.InvariantCulture)
            : ((long)whole).ToString("x", CultureInfo.InvariantCulture);
}
