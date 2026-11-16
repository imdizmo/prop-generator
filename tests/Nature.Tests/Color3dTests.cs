using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class Color3dTests
{
    [Fact]
    public void HexLerpAndScaleMatchThree()
    {
        var family = KernelGoldens.Family("color");
        family.AssertAll(r =>
        {
            var a = Color3d.FromHex((int)family[r, "hexA"]);
            var b = Color3d.FromHex((int)family[r, "hexB"]);
            double alpha = family[r, "alpha"];
            var lerp = a.Clone().Lerp(b, alpha);
            var lerpColors = new Color3d().LerpColors(a, b, alpha);
            var scaled = a.Clone().MultiplyScalar(family[r, "s"]);
            return
            [
                a.R, a.G, a.B, b.R, b.G, b.B, lerp.R, lerp.G, lerp.B,
                lerpColors.R, lerpColors.G, lerpColors.B, scaled.R, scaled.G, scaled.B,
            ];
        });
    }

    [Fact]
    public void NewColorIsWhite()
    {
        var c = new Color3d();
        Assert.Equal((1.0, 1.0, 1.0), (c.R, c.G, c.B));
    }

    [Fact]
    public void RgbIsTakenAsLinear()
    {
        var c = new Color3d(0.5, 0.25, 0.125);
        Assert.Equal((0.5, 0.25, 0.125), (c.R, c.G, c.B));
    }

    [Fact]
    public void CopyAndLerpChainOnOneObject()
    {
        var paint = new Color3d();
        var deep = Color3d.FromHex(0x203010);
        var light = Color3d.FromHex(0xa0c080);
        Assert.Same(paint, paint.Copy(deep).Lerp(light, 0.4));
        var expected = deep.Clone().Lerp(light, 0.4);
        Assert.Equal((expected.R, expected.G, expected.B), (paint.R, paint.G, paint.B));
    }

    [Fact]
    public void FromStyleReadsSixDigitHexAsFromHexDoes()
    {
        var style = Color3d.FromStyle("#6B4e35");
        var hex = Color3d.FromHex(0x6b4e35);
        Assert.Equal((hex.R, hex.G, hex.B), (style.R, style.G, style.B));
    }

    [Fact]
    public void FromStyleReadsThreeDigitHexAsDigitsOverFifteen()
    {
        // three reads #abc as 10/15, 11/15, 12/15: the same doubles as 0xaa/255, 0xbb/255, 0xcc/255.
        var style = Color3d.FromStyle("#aBc");
        var hex = Color3d.FromHex(0xaabbcc);
        Assert.Equal((hex.R, hex.G, hex.B), (style.R, style.G, style.B));
    }

    [Theory]
    [InlineData("#abcd")]
    [InlineData("red")]
    [InlineData("rgb(1, 2, 3)")]
    [InlineData("#ggg")]
    public void FromStyleRefusesWhatEasyPropsNeverHandsIt(string style) =>
        Assert.Throws<NotSupportedException>(() => Color3d.FromStyle(style));
}
