// Adapted from easy-props (MIT, © 2026 AHS12).
namespace PropGenerator.Desktop.Commands;

/// <summary>Where the sun stands and how it lights the prop, as easy-props' sun and shadow popover sets it.</summary>
/// <param name="Azimuth">The sun's direction around the prop, in degrees from 0 to 360.</param>
/// <param name="Elevation">The sun's height above the ground, in degrees from 4 to 88.</param>
/// <param name="Warmth">How warm the light is, from 0 (white) to 1 (golden).</param>
/// <param name="Shadow">How dark the shadows are, from 0 (none) to 1 (full).</param>
public readonly record struct Lighting(double Azimuth, double Elevation, double Warmth, double Shadow)
{
    /// <summary>Gets the light the app starts with (easy-props' default: 52°, 46°, warmth 0.55, shadow 0.8).</summary>
    public static Lighting Default => new(52, 46, 0.55, 0.8);

    /// <summary>Gets a value indicating whether every part is inside its range.</summary>
    public bool IsValid =>
        Azimuth is >= 0 and <= 360 && Elevation is >= 4 and <= 88 && Warmth is >= 0 and <= 1 && Shadow is >= 0 and <= 1;
}
