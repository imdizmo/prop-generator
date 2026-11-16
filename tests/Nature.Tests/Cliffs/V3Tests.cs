using PropGenerator.Nature.Cliffs;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

public class V3Tests
{
    [Fact]
    public void ArithmeticWorksPerComponent()
    {
        var a = new V3(1, 2, 3);
        var b = new V3(4, -5, 6);
        Assert.Equal(new V3(5, -3, 9), a + b);
        Assert.Equal(new V3(-3, 7, -3), a - b);
        Assert.Equal(new V3(-1, -2, -3), -a);
        Assert.Equal(new V3(2, 4, 6), a * 2);
        Assert.Equal(new V3(2, 4, 6), 2 * a);
        Assert.Equal(new V3(0.5, 1, 1.5), a / 2);
    }

    [Fact]
    public void DotAndCrossAreRightHanded()
    {
        Assert.Equal(32, V3.Dot(new V3(1, 2, 3), new V3(4, 5, 6)));
        Assert.Equal(new V3(0, 0, 1), V3.Cross(new V3(1, 0, 0), new V3(0, 1, 0)));
    }

    [Fact]
    public void NormalizedHasLengthOneAndKeepsZeroZero()
    {
        Assert.Equal(5, new V3(3, 0, 4).Length);
        Assert.Equal(new V3(0.6, 0, 0.8), new V3(3, 0, 4).Normalized());
        Assert.Equal(new V3(0, 0, 0), new V3(0, 0, 0).Normalized());
    }
}
