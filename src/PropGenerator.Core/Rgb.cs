using System.Globalization;

namespace PropGenerator;

/// <summary>A colour as sRGB, each channel 0 to 1 (tints and multipliers may go above 1).</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
public readonly record struct Rgb(float R, float G, float B)
{
    /// <summary>Reads "#rrggbb" (the # is optional).</summary>
    /// <param name="hex">Six hex digits.</param>
    /// <returns>The colour.</returns>
    public static Rgb FromHex(string hex)
    {
        string h = hex.TrimStart('#');
        if (h.Length != 6)
        {
            throw new FormatException($"'{hex}' is not a #rrggbb colour.");
        }

        float Byte(int at) => int.Parse(h.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
        return new Rgb(Byte(0), Byte(2), Byte(4));
    }

    /// <summary>Multiplies channel by channel (no clipping).</summary>
    /// <param name="a">The first colour.</param>
    /// <param name="b">The second colour.</param>
    /// <returns>The product.</returns>
    public static Rgb operator *(Rgb a, Rgb b) => new(a.R * b.R, a.G * b.G, a.B * b.B);
}
