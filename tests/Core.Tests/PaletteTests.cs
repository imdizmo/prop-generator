using Xunit;

namespace PropGenerator.Tests;

public class PaletteTests
{
    private static readonly Rgb V5 = new(0.272f, 0.188f, 0.170f);

    private static float Luma(Rgb c) => (0.2126f * c.R) + (0.7152f * c.G) + (0.0722f * c.B);

    private static float Dist(Rgb a, Rgb b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    private static void AssertClose(Rgb actual, Rgb expected, float tol) =>
        Assert.True(Dist(actual, expected) <= tol, $"{actual} vs {expected}");

    private static IEnumerable<Rgb> All(Palette p) =>
        [p.Base, p.LitTint, p.ShadeTint, p.Redder, p.Greyer, p.EndMul, p.SideMul, p.Gap, p.SeamDeep, p.SeamLight];

    [Fact]
    public void V5ColourGivesV5Palette()
    {
        var p = Palette.Derive(V5);
        AssertClose(p.Base, V5, 1e-6f);
        AssertClose(p.LitTint, new(1.05f, 1f, .9f), 1e-3f);
        AssertClose(p.ShadeTint, new(.94f, .9f, 1f), 1e-3f);
        AssertClose(p.Redder, new(1.07f, .985f, .955f), 1e-3f);
        AssertClose(p.Greyer, new(.955f, .99f, 1.045f), 1e-3f);
        AssertClose(p.EndMul, new(1.16f, 1.12f, 1.06f), 1e-3f);
        AssertClose(p.SideMul, new(.94f, .94f, .94f), 1e-3f);
        AssertClose(p.Gap, Rgb.FromHex("#24150C"), 1e-3f);
        AssertClose(p.SeamDeep, new(.085f, .055f, .043f), 1e-3f);
        AssertClose(p.SeamLight, new(.135f, .088f, .07f), 1e-3f);
    }

    [Theory]
    [InlineData("#ffffff")]
    [InlineData("#000000")]
    [InlineData("#00c000")]
    [InlineData("#d8c6a0")]
    public void ExtremesStayFiniteAndOrdered(string hex)
    {
        var p = Palette.Derive(Rgb.FromHex(hex));
        Assert.All(All(p), c => Assert.True(float.IsFinite(c.R) && float.IsFinite(c.G) && float.IsFinite(c.B), c.ToString()));
        Assert.True(Luma(p.SeamDeep) < Luma(p.Base));
        Assert.True(Luma(p.Base * p.EndMul) >= Luma(p.Base) - 1e-3f);
    }

    [Fact]
    public void ColoursStayInRange()
    {
        foreach (string hex in new[] { "#ffffff", "#000000", "#00c000", "#d8c6a0", "#ff0000" })
        {
            var p = Palette.Derive(Rgb.FromHex(hex));
            foreach (var c in new[] { p.Base, p.Gap, p.SeamDeep, p.SeamLight })
            {
                Assert.InRange(c.R, 0f, 1f);
                Assert.InRange(c.G, 0f, 1f);
                Assert.InRange(c.B, 0f, 1f);
            }
        }
    }

    [Fact]
    public void SeamsStayDarkOnPaleWood()
    {
        var p = Palette.Derive(Rgb.FromHex("#f0e6d0"));
        Assert.True(Luma(p.SeamLight) < 0.2f);
        Assert.True(Luma(p.SeamDeep) < Luma(p.SeamLight));
    }

    [Fact]
    public void LighterPickGivesLighterBaseAndSeams()
    {
        var a = Palette.Derive(Rgb.FromHex("#4a3020"));
        var b = Palette.Derive(Rgb.FromHex("#a08060"));
        Assert.True(Luma(b.Base) > Luma(a.Base));
        Assert.True(Oklch.FromSrgb(b.SeamLight).L >= Oklch.FromSrgb(a.SeamLight).L - 1e-6, $"{a.SeamLight} {b.SeamLight}");
    }

    // Seams keep v5's chroma ratio to the wood (seam C = wood C x v5's seam C / v5's wood C) and are brought into sRGB
    // by lowering the chroma, never by clipping each channel (which turned #a07850's deep seam into (29, 11, 0)).
    [Theory]
    [InlineData("#a07850")]
    [InlineData("#d8c6a0")]
    [InlineData("#2a1d16")]
    public void SeamsAreInGamutWithoutClippingDarkerThanTheWoodAndOfItsHue(string hex)
    {
        var p = Palette.Derive(Rgb.FromHex(hex));
        var wood = Oklch.FromSrgb(p.Base);
        var v5 = Oklch.FromSrgb(V5);
        (Rgb Seam, Rgb V5Seam)[] seams =
        [
            (p.SeamDeep, new(.085f, .055f, .043f)),
            (p.SeamLight, new(.135f, .088f, .07f)),
            (p.Gap, Rgb.FromHex("#24150C")),
        ];
        foreach ((Rgb seam, Rgb v5Seam) in seams)
        {
            var s = Oklch.FromSrgb(seam);
            var v5s = Oklch.FromSrgb(v5Seam);
            double wantL = Math.Min(v5s.L, wood.L + (v5s.L - v5.L));
            Assert.True(Math.Abs(s.L - wantL) < 1e-3, $"{seam}: lightness {s.L:0.0000}, the rule's {wantL:0.0000} (a clipped channel moves it)");
            Assert.True(s.C <= (wood.C * v5s.C / v5.C) + 1e-3, $"{seam}: chroma {s.C:0.0000} above the ratio's {wood.C * v5s.C / v5.C:0.0000}");
            Assert.True(s.L < wood.L, $"{seam} is not darker than the wood");
            double dh = Math.Abs(Math.IEEERemainder(s.H - wood.H, 2 * Math.PI)) * 180 / Math.PI;
            Assert.True(s.C < 1e-3 || dh <= 25, $"{seam}: hue {dh:0.0} degrees from the wood's");
            Assert.All((float[])[seam.R, seam.G, seam.B], ch => Assert.InRange(ch, 0f, 1f));
        }
    }

    [Fact]
    public void FromHexReadsBytes()
    {
        var c = Rgb.FromHex("#ff8000");
        Assert.Equal(1f, c.R);
        Assert.Equal(128f / 255f, c.G, 1e-6f);
        Assert.Equal(0f, c.B);
    }
}
