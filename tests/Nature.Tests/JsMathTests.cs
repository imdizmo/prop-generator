using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using PropGenerator.Nature.Js;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class JsMathTests
{
    /// <summary>Every function is checked against at least this many V8 results (its edge values plus a seeded sample of random inputs).</summary>
    private const long MinRecords = 1_500;

    private delegate double Fn(ReadOnlySpan<double> args);

    public static TheoryData<string> Functions =>
        ["sin", "cos", "tan", "atan", "atan2", "exp", "log", "log10", "pow", "asin", "acos", "sqrt", "hypot2", "hypot3"];

    private static Fn For(string name) => name switch
    {
        "sin" => static a => JsMath.Sin(a[0]),
        "cos" => static a => JsMath.Cos(a[0]),
        "tan" => static a => JsMath.Tan(a[0]),
        "atan" => static a => JsMath.Atan(a[0]),
        "atan2" => static a => JsMath.Atan2(a[0], a[1]),
        "exp" => static a => JsMath.Exp(a[0]),
        "log" => static a => JsMath.Log(a[0]),
        "log10" => static a => JsMath.Log10(a[0]),
        "pow" => static a => JsMath.Pow(a[0], a[1]),
        "asin" => static a => JsMath.Asin(a[0]),
        "acos" => static a => JsMath.Acos(a[0]),
        "sqrt" => static a => JsMath.Sqrt(a[0]),
        "hypot2" => static a => JsMath.Hypot(a[0], a[1]),
        "hypot3" => static a => JsMath.Hypot(a[0], a[1], a[2]),
        _ => throw new ArgumentException(name),
    };

    [Fact]
    public void TheIndexListsExactlyTheFourteenFunctions()
    {
        using var index = Fixture.Json("jsmath-sample.json");
        string?[] listed = [.. index.RootElement.GetProperty("functions").EnumerateArray().Select(f => f.GetProperty("name").GetString())];
        Assert.Equal(Functions.Select(d => d.Data).Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Every record of the function's sample in Fixtures/jsmath-sample.json (the inputs, then V8's output) gives the same
    /// bits from JsMath. NaN matches any NaN, since V8's NaN bit patterns are its own.
    /// </summary>
    [Theory]
    [MemberData(nameof(Functions))]
    public void MatchesV8BitForBit(string name)
    {
        Assert.True(BitConverter.IsLittleEndian);
        using var index = Fixture.Json("jsmath-sample.json");
        var entry = index.RootElement.GetProperty("functions").EnumerateArray()
            .Single(f => f.GetProperty("name").GetString() == name);
        int arity = entry.GetProperty("arity").GetInt32();
        long records = entry.GetProperty("records").GetInt64();
        ReadOnlySpan<double> data = MemoryMarshal.Cast<byte, double>(Convert.FromBase64String(entry.GetProperty("data").GetString()!));
        int width = arity + 1;
        Assert.Equal(records * width, data.Length);
        Assert.True(records >= MinRecords, $"the {name} sample holds {records} records, fewer than {MinRecords}");

        Fn fn = For(name);
        long bad = 0;
        ulong maxUlps = 0;
        var report = new StringBuilder();
        for (int at = 0; at < data.Length; at += width)
        {
            ReadOnlySpan<double> args = data.Slice(at, arity);
            double expected = data[at + arity];
            double actual = fn(args);
            if (Same(expected, actual))
            {
                continue;
            }

            bad++;
            ulong ulps = Ulps(expected, actual);
            maxUlps = Math.Max(maxUlps, ulps);
            if (bad <= 20)
            {
                report.Append(CultureInfo.InvariantCulture, $"\n  #{at / width} ({Show(args)}): V8 {Show(expected)}, ours {Show(actual)}, {ulps} ulps");
            }
        }

        Assert.True(bad == 0, $"{name}: {bad} of {records} records differ, max {maxUlps} ulps.{report}");
    }

    [Fact]
    public void RoundTiesTowardPlusInfinity()
    {
        Assert.Equal(-2.0, JsMath.Round(-2.5));
        Assert.Equal(3.0, JsMath.Round(2.5));
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(JsMath.Round(-0.5)));
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(JsMath.Round(-0.0)));
        Assert.Equal(BitConverter.DoubleToInt64Bits(0.0), BitConverter.DoubleToInt64Bits(JsMath.Round(0.25)));
        Assert.Equal(0.0, JsMath.Round(0.49999999999999994));
        Assert.Equal(-1.0, JsMath.Round(-0.5000000000000001));
        Assert.Equal(4503599627370497.0, JsMath.Round(4503599627370497.0));
        Assert.Equal(double.PositiveInfinity, JsMath.Round(double.PositiveInfinity));
        Assert.True(double.IsNaN(JsMath.Round(double.NaN)));
    }

    private static bool Same(double expected, double actual) =>
        double.IsNaN(expected)
            ? double.IsNaN(actual)
            : BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual);

    /// <summary>The distance in units in the last place, counting -0 and +0 one apart; NaN against a number is the most.</summary>
    private static ulong Ulps(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b))
        {
            return ulong.MaxValue;
        }

        long ka = Ordered(a), kb = Ordered(b);
        return ka > kb ? (ulong)ka - (ulong)kb : (ulong)kb - (ulong)ka;
    }

    private static long Ordered(double d)
    {
        long bits = BitConverter.DoubleToInt64Bits(d);
        return bits < 0 ? -1 - (bits & long.MaxValue) : bits;
    }

    private static string Show(double d) =>
        string.Create(CultureInfo.InvariantCulture, $"{d:R} [{BitConverter.DoubleToInt64Bits(d):X16}]");

    private static string Show(ReadOnlySpan<double> args)
    {
        string[] parts = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            parts[i] = Show(args[i]);
        }

        return string.Join(", ", parts);
    }
}
