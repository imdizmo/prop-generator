// Ported from V8 (BSD-3-Clause) — Math.pow special cases; see NOTICE.md.
namespace PropGenerator.Nature.Js;

/// <summary>
/// JavaScript's Math functions, giving the same bits as V8 (Node 26, V8 14.6). The generators branch on the signs of
/// computed values, so one bit of difference can change a mesh. Each function was measured against a million V8
/// results (tests/Nature.Tests/JsMathTests.cs): sqrt is .NET's own, pow is the C library's behind V8's own special cases,
/// hypot is V8's builtin, and the rest are V8's fdlibm code (<see cref="Ieee754"/>).
/// </summary>
public static class JsMath
{
    /// <summary>Math.sin.</summary>
    public static double Sin(double x) => Ieee754.Sin(x);

    /// <summary>Math.cos.</summary>
    public static double Cos(double x) => Ieee754.Cos(x);

    /// <summary>Math.tan.</summary>
    public static double Tan(double x) => Ieee754.Tan(x);

    /// <summary>Math.atan.</summary>
    public static double Atan(double x) => Ieee754.Atan(x);

    /// <summary>Math.atan2(y, x).</summary>
    public static double Atan2(double y, double x) => Ieee754.Atan2(y, x);

    /// <summary>Math.exp.</summary>
    public static double Exp(double x) => Ieee754.Exp(x);

    /// <summary>Math.log.</summary>
    public static double Log(double x) => Ieee754.Log(x);

    /// <summary>Math.log10.</summary>
    public static double Log10(double x) => Ieee754.Log10(x);

    /// <summary>
    /// Math.pow and the ** operator, as V8's math::pow computes them with its default --use-std-math-pow: the C
    /// library's pow, after the cases where JavaScript's answer differs from C's or V8 takes a shortcut.
    /// </summary>
    /// <remarks>
    /// The C library's pow is glibc's here, and .NET's Math.Pow calls the same one on Linux. On another C library
    /// (Windows, macOS) the last bit can differ; the conformance test is the check.
    /// </remarks>
    public static double Pow(double x, double y)
    {
        if (double.IsNaN(y))
        {
            return double.NaN;
        }

        if (double.IsInfinity(y) && (x == 1 || x == -1))
        {
            return double.NaN;
        }

        if (y == 2.0)
        {
            return x * x;
        }

        if (y == 0.5)
        {
            // sqrt(-0 + 0) is +0, and pow(-Infinity, 0.5) is +Infinity where sqrt would give NaN.
            return double.IsInfinity(x) ? double.PositiveInfinity : Math.Sqrt(x + 0.0);
        }

        // The one allowed use of Math.Pow (BannedSymbols.txt): it is glibc's pow, which is V8's here.
#pragma warning disable RS0030
        return Math.Pow(x, y);
#pragma warning restore RS0030
    }

    /// <summary>Math.asin.</summary>
    public static double Asin(double x) => Ieee754.Asin(x);

    /// <summary>Math.acos.</summary>
    public static double Acos(double x) => Ieee754.Acos(x);

    /// <summary>Math.sqrt: correctly rounded in both, so .NET's own.</summary>
    public static double Sqrt(double x) => Math.Sqrt(x);

    /// <summary>Math.hypot of two numbers.</summary>
    public static double Hypot(double a, double b) => Ieee754.Hypot(a, b);

    /// <summary>Math.hypot of three numbers.</summary>
    public static double Hypot(double a, double b, double c) => Ieee754.Hypot(a, b, c);

    /// <summary>
    /// Math.round: halves go toward +infinity (-2.5 gives -2), and a negative number that rounds to zero gives -0.
    /// .NET's Math.Round rounds halves to even or away from zero, which is wrong for one or the other sign.
    /// </summary>
    public static double Round(double x)
    {
        double f = Math.Floor(x);
        double r = x - f >= 0.5 ? f + 1 : f;
        return r == 0 && x < 0 ? -0.0 : r;
    }
}
