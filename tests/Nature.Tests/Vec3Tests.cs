using PropGenerator.Nature.Rocks;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class Vec3Tests
{
    [Fact]
    public void NormalizeKeepsAZeroVectorZero() => Assert.Equal(new Vec3(0, 0, 0), Vec3.Normalize(new Vec3(0, 0, 0)));

    [Fact]
    public void NormalizeDividesByTheLength() => Assert.Equal(new Vec3(0.6, 0, 0.8), Vec3.Normalize(new Vec3(3, 0, 4)));

    [Fact]
    public void CrossIsRightHanded() => Assert.Equal(new Vec3(0, 0, 1), Vec3.Cross(new Vec3(1, 0, 0), new Vec3(0, 1, 0)));

    [Fact]
    public void DotAddsLeftToRight()
    {
        // (1e16 + 1) - 1e16 is 0 in doubles, while 1e16 + (1 - 1e16) is 2: the order is easy-props'.
        Assert.Equal(0, Vec3.Dot(new Vec3(1e16, 1, -1e16), new Vec3(1, 1, 1)));
    }

    [Fact]
    public void IndexerReadsTheAxes()
    {
        var v = new Vec3(1, 2, 3);
        Assert.Equal((1.0, 2.0, 3.0), (v[0], v[1], v[2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => v[3]);
    }
}
