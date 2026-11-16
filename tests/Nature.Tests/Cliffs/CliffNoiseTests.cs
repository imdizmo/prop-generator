using PropGenerator.Nature.Cliffs;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

public class CliffNoiseTests
{
    [Fact]
    public void NoiseIsDeterministicAndSmooth()
    {
        var p = new V3(1.37, -2.9, 0.41);
        Assert.Equal(CliffNoise.Fbm(p, 0.3, 3), CliffNoise.Fbm(p, 0.3, 3));
        Assert.Equal(CliffNoise.Hash01(7, 3), CliffNoise.Hash01(7, 3));

        double sum = 0;
        double worst = 0;
        for (int i = 0; i < 10_000; i++)
        {
            var q = new V3((i * 0.173) % 40 - 20, (i * 0.611) % 30 - 15, (i * 0.287) % 25);
            double w = (i % 97) * 0.13;
            double a = CliffNoise.Fbm(q, w, 3);
            double b = CliffNoise.Fbm(q + new V3(1e-4, 0, 0), w, 3);
            Assert.InRange(a, 0.0, 1.0);
            worst = Math.Max(worst, Math.Abs(a - b));
            sum += a;
        }

        Assert.True(worst < 1e-3, $"largest step {worst}");
        Assert.InRange(sum / 10_000, 0.45, 0.55);
    }

    [Fact]
    public void WAndOctavesChangeTheValue()
    {
        var p = new V3(1.37, -2.9, 0.41);
        Assert.NotEqual(CliffNoise.Fbm(p, 0.0, 3), CliffNoise.Fbm(p, 1.5, 3));
        Assert.NotEqual(CliffNoise.Fbm(p, 0.3, 1), CliffNoise.Fbm(p, 0.3, 3));
    }

    [Fact]
    public void HashSpreadsEvenly()
    {
        int[] tenths = new int[10];
        for (int salt = 0; salt < 10_000; salt++)
        {
            double h = CliffNoise.Hash01(12345, salt);
            Assert.InRange(h, 0.0, 1.0);
            tenths[Math.Min(9, (int)(h * 10))]++;
        }

        Assert.All(tenths, count => Assert.InRange(count, 800, 1200));
    }
}
