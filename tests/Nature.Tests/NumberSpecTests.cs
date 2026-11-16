using PropGenerator.Nature.Parameters;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class NumberSpecTests
{
    private static NumberSpec Slider(bool whole = false) => new("k", "K", null, null, 1, 10, 1, 4, whole: whole);

    [Fact]
    public void InRangeValueIsKept() => Assert.Equal(7.25, Slider().Clamp(7.25));

    [Theory]
    [InlineData(-5.0, 1.0)]
    [InlineData(11.0, 10.0)]
    [InlineData(1e300, 10.0)]
    public void OutOfRangeIsPulledIn(double raw, double expected) => Assert.Equal(expected, Slider().Clamp(raw));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NotFiniteBecomesTheDefault(double raw) => Assert.Equal(4.0, Slider().Clamp(raw));

    [Fact]
    public void NotANumberOrMissingBecomesTheDefault()
    {
        var spec = Slider();
        Assert.Equal(4.0, spec.Clamp("7"));
        Assert.Equal(4.0, spec.Clamp(true));
        Assert.Equal(4.0, spec.Clamp(null));
        Assert.Equal(4.0, spec.Clamp(PropGenerator.Nature.Js.JsUndefined.Value));
        Assert.Equal(4.0, spec.Clamp(new List<object?> { 7.0 }));
    }

    [Fact]
    public void IntegersOfOtherTypesCount() => Assert.Equal(7.0, Slider().Clamp(7));

    [Theory]
    [InlineData(2.5, 3.0)]
    [InlineData(2.4999999999999996, 2.0)]
    [InlineData(9.5, 10.0)]
    [InlineData(0.5, 1.0)]
    public void WholeNumbersRoundTiesUpAfterClamping(double raw, double expected) =>
        Assert.Equal(expected, Slider(whole: true).Clamp(raw));

    [Fact]
    public void NegativeZeroInRangeBecomesPositiveZero()
    {
        var spec = new NumberSpec("k", "K", null, null, 0, 1, 0.1, 0.5);
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(spec.Clamp(-0.0)));
    }
}
