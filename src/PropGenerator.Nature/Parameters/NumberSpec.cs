// Ported from easy-props frontend/src/assets/shared/parameters.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Parameters;

/// <summary>
/// A number that is clamped into range rather than rejected (easy-props' <c>numberSchema</c>). NaN, the infinities,
/// anything that is not a number, and a missing value fall back to the default. A <see cref="Whole"/> number is then
/// rounded the way JavaScript's <c>Math.round</c> does it.
/// </summary>
public sealed class NumberSpec : ParameterSpec
{
    /// <summary>A number control. <paramref name="whole"/> is true where the schema rounds the clamped value.</summary>
    public NumberSpec(
        string key,
        string label,
        string? help,
        string? group,
        double min,
        double max,
        double step,
        double defaultValue,
        string? unit = null,
        bool whole = false)
        : base(key, label, help, group)
    {
        Min = min;
        Max = max;
        Step = step;
        DefaultNumber = defaultValue;
        Unit = unit;
        Whole = whole;
    }

    /// <summary>The smallest legal value.</summary>
    public double Min { get; }

    /// <summary>The largest legal value.</summary>
    public double Max { get; }

    /// <summary>The slider's step.</summary>
    public double Step { get; }

    /// <summary>"m", "deg", "°" (cliffs) or "count", or null.</summary>
    public string? Unit { get; }

    /// <summary>True when the clamped value is rounded to a whole number.</summary>
    public bool Whole { get; }

    /// <summary>The default as a number.</summary>
    public double DefaultNumber { get; }

    /// <inheritdoc />
    public override object DefaultValue => DefaultNumber;

    /// <summary>The clamped (and, for a whole number, rounded) value.</summary>
    public double Clamp(object? raw)
    {
        double value = JsValue.TryFiniteNumber(raw, out double number) ? number : DefaultNumber;
        double clamped = Math.Min(Max, Math.Max(Min, value));
        return Whole ? JsMath.Round(clamped) : clamped;
    }

    /// <inheritdoc />
    public override object Coerce(object? raw) => Clamp(raw);
}
