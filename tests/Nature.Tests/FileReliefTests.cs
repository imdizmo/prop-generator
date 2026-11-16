using PropGenerator.Nature.Textures;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class FileReliefTests
{
    [Fact]
    public void FlipsGreenOnlyAndLeavesTheInputAlone()
    {
        var relief = new RgbaImage(2, 2, [10, 20, 30, 40, 11, 21, 31, 41, 12, 22, 32, 42, 13, 23, 33, 43]);
        RgbaImage full = FileRelief.Build(relief, full: true);
        Assert.Equal([10, 235, 30, 40, 11, 234, 31, 41, 12, 233, 32, 42, 13, 232, 33, 43], full.Rgba);
        Assert.Equal(20, relief.Rgba[1]);
    }

    [Fact]
    public void HalvesWithJsRounding()
    {
        // Channel sums 2, 255, 10 and 1019: Math.round(sum / 4) takes halves up (0.5 -> 1, 2.5 -> 3), then green flips.
        var relief = new RgbaImage(2, 2, [0, 0, 1, 255, 1, 255, 3, 255, 0, 0, 3, 255, 1, 0, 3, 254]);
        RgbaImage half = FileRelief.Build(relief, full: false);
        Assert.Equal((1, 1), (half.W, half.H));
        Assert.Equal([1, 191, 3, 255], half.Rgba);
    }
}
