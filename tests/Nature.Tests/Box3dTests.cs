using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class Box3dTests
{
    [Theory]
    [InlineData("ico")]
    [InlineData("icoWelded")]
    [InlineData("tube")]
    [InlineData("soup")]
    [InlineData("quirk")]
    [InlineData("nearZero")]
    public void SetFromBufferAttributeMatchesThree(string mesh)
    {
        float[] positions = KernelGoldens.RequiredMesh($"{mesh}/input").Position;
        AssertBox(new Box3d().SetFromBufferAttribute(positions), $"{mesh}/box");
    }

    [Fact]
    public void EmptyBoxMatchesThree()
    {
        var box = new Box3d().SetFromBufferAttribute([]);
        Assert.True(box.IsEmpty());
        AssertBox(box, "empty/box");
    }

    [Fact]
    public void ExpandByPointGrowsBothCorners()
    {
        var box = new Box3d().ExpandByPoint(new Vec3d(1, -2, 3)).ExpandByPoint(new Vec3d(-1, 4, 0));
        Assert.Equal((-1.0, -2.0, 0.0), (box.Min.X, box.Min.Y, box.Min.Z));
        Assert.Equal((1.0, 4.0, 3.0), (box.Max.X, box.Max.Y, box.Max.Z));
    }

    private static void AssertBox(Box3d box, string name)
    {
        double[] expected = KernelGoldens.Doubles(name, "box");
        var size = box.GetSize(new Vec3d());
        double[] actual = [box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z, size.X, size.Y, size.Z];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Exact.Same(expected[i], actual[i]), $"{name} value {i}: three {expected[i]:R}, port {actual[i]:R}");
        }

        Assert.Equal(KernelGoldens.BoxIsEmpty(name), box.IsEmpty());
    }
}
