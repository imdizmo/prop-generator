// Ported from easy-props frontend/src/assets/style/ramp.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Style;

/// <summary>
/// A ramp of colours, read by a value from 0 to 1 (easy-props' <c>colourRamp</c>). The stops are <c>#rrggbb</c> text,
/// turned linear the way three does, and blended with three's <c>Color.lerp</c>. Easy-props hands back one shared
/// colour object; every call here returns a new one holding the same numbers.
/// </summary>
public sealed class Ramp
{
    private readonly Color3d[] colours;

    /// <summary>A ramp through two or more stops.</summary>
    public Ramp(IReadOnlyList<string> stops)
    {
        if (stops.Count < 2)
        {
            throw new ArgumentException("A ramp needs at least two stops", nameof(stops));
        }

        colours = [.. stops.Select(ParseHex)];
    }

    /// <summary>The colour at <paramref name="value"/>, which is clamped to 0..1. NaN throws.</summary>
    public Color3d Colour(double value)
    {
        if (double.IsNaN(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A ramp has no colour at NaN");
        }

        double at = Math.Min(1, Math.Max(0, value)) * (colours.Length - 1);
        int below = (int)Math.Min(Math.Floor(at), colours.Length - 2);
        return colours[below].Clone().Lerp(colours[below + 1], at - below);
    }

    /// <summary>The colour at <paramref name="value"/> on a ramp through <paramref name="stops"/>.</summary>
    public static Color3d Colour(IReadOnlyList<string> stops, double value) => new Ramp(stops).Colour(value);

    private static Color3d ParseHex(string stop)
    {
        string digits = stop.StartsWith('#') ? stop[1..] : throw new FormatException($"Colour '{stop}' does not start with #");
        return digits.Length == 6 && int.TryParse(digits, System.Globalization.NumberStyles.AllowHexSpecifier, null, out int hex)
            ? Color3d.FromHex(hex)
            : throw new FormatException($"Colour '{stop}' is not #rrggbb");
    }
}
