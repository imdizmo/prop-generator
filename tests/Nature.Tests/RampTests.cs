using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Style;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class RampTests
{
    [Fact]
    public void ColoursMatchThreeOnEveryCase()
    {
        using var doc = Fixture.Json("ramp.json");
        int cases = 0;
        foreach (var golden in doc.RootElement.EnumerateArray())
        {
            var stops = golden.GetProperty("stops").EnumerateArray().Select(s => s.GetString()!).ToList();
            double t = golden.GetProperty("t").GetDouble();
            double[] rgb = [.. golden.GetProperty("rgb").EnumerateArray().Select(e => e.GetDouble())];
            Color3d colour = Ramp.Colour(stops, t);
            Assert.True(
                Exact.Same(rgb[0], colour.R) && Exact.Same(rgb[1], colour.G) && Exact.Same(rgb[2], colour.B),
                $"case {cases}: {string.Join(",", stops)} at {t}: expected {string.Join(",", rgb)}, got {colour.R},{colour.G},{colour.B}");
            cases++;
        }

        Assert.Equal(500, cases);
    }

    [Fact]
    public void EachCallReturnsANewColour()
    {
        var ramp = new Ramp(["#000000", "#ffffff"]);
        var first = ramp.Colour(0.5);
        double before = first.R;
        var second = ramp.Colour(0.25);
        Assert.NotSame(first, second);
        Assert.Equal(before, first.R);
        first.R = 99;
        Assert.NotEqual(99, ramp.Colour(0.5).R);
    }

    [Fact]
    public void ValueIsClampedToTheEnds()
    {
        var ramp = new Ramp(["#102030", "#405060", "#8090a0"]);
        Assert.Equal(ramp.Colour(0).G, ramp.Colour(-3).G);
        Assert.Equal(ramp.Colour(1).B, ramp.Colour(7).B);
    }

    [Fact]
    public void FewerThanTwoStopsAndNaNThrow()
    {
        Assert.Throws<ArgumentException>(() => new Ramp(["#000000"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Ramp(["#000000", "#ffffff"]).Colour(double.NaN));
    }

    [Fact]
    public void MalformedStopThrows() => Assert.Throws<FormatException>(() => new Ramp(["#000000", "white"]));
}
