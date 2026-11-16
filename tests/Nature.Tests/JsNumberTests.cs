using PropGenerator.Nature.Js;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class JsNumberTests
{
    [Theory]
    [InlineData(2147483648.0, -2147483648)]
    [InlineData(4294967296.0, 0)]
    [InlineData(4294967297.5, 1)]
    [InlineData(-1.9, -1)]
    [InlineData(-2147483649.0, 2147483647)]
    [InlineData(1e10, 1410065408)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    public void ToInt32WrapsLikeJs(double value, int expected) => Assert.Equal(expected, JsNumber.ToInt32(value));

    [Theory]
    [InlineData(-1.0, 4294967295u)]
    [InlineData(4294967295.0, 4294967295u)]
    [InlineData(4294967296.0, 0u)]
    [InlineData(-2147483649.0, 2147483647u)]
    [InlineData(2147483648.5, 2147483648u)]
    public void ToUint32WrapsLikeJs(double value, uint expected) => Assert.Equal(expected, JsNumber.ToUint32(value));

    [Theory]
    [InlineData(-0.0, 5, "0.00000")]
    [InlineData(0.0, 2, "0.00")]
    [InlineData(1.005, 2, "1.00")]
    [InlineData(2.5, 0, "3")]
    [InlineData(-2.5, 0, "-3")]
    [InlineData(0.125, 2, "0.13")]
    [InlineData(-0.000001, 2, "-0.00")]
    [InlineData(123.456, 5, "123.45600")]
    [InlineData(1e21, 2, "1e+21")]
    [InlineData(-1.5e22, 3, "-1.5e+22")]
    [InlineData(1e20, 2, "100000000000000000000.00")]
    [InlineData(0.1, 20, "0.10000000000000000555")]
    [InlineData(double.NaN, 2, "NaN")]
    public void ToFixedMatchesJs(double value, int digits, string expected) =>
        Assert.Equal(expected, JsNumber.ToFixed(value, digits));

    [Theory]
    [InlineData(1e-7, "1e-7")]
    [InlineData(123456789012345680000.0, "123456789012345680000")]
    [InlineData(0.30000000000000004, "0.30000000000000004")]
    [InlineData(0.0, "0")]
    [InlineData(-1.5, "-1.5")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1.2345e21, "1.2345e+21")]
    [InlineData(0.000001, "0.000001")]
    [InlineData(1.5e-7, "1.5e-7")]
    [InlineData(100.0, "100")]
    [InlineData(5e-324, "5e-324")]
    [InlineData(double.MaxValue, "1.7976931348623157e+308")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    [InlineData(double.NaN, "NaN")]
    public void ToJsStringMatchesJs(double value, string expected) => Assert.Equal(expected, JsNumber.ToJsString(value));

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(255.0, 255)]
    [InlineData(256.0, 0)]
    [InlineData(300.9, 44)]
    [InlineData(-1.0, 255)]
    [InlineData(-0.5, 0)]
    [InlineData(-257.0, 255)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(4294967551.0, 255)]
    public void ToUint8WrapsLikeAUint8ArrayStore(double value, byte expected) => Assert.Equal(expected, JsNumber.ToUint8(value));

    [Fact]
    public void ToJsStringOfASum() => Assert.Equal("0.30000000000000004", JsNumber.ToJsString(0.1 + 0.2));

    [Fact]
    public void ToJsStringOfMinusZeroIsZero() => Assert.Equal("0", JsNumber.ToJsString(-0.0));
}
