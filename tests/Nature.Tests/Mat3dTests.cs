using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class Mat3dTests
{
    [Fact]
    public void GetNormalMatrixMatchesThree()
    {
        var family = KernelGoldens.Family("compose");
        family.AssertAll(r =>
        {
            // three's composed matrix, so this checks only getNormalMatrix (setFromMatrix4, invert, transpose).
            var m = new Mat4d().FromArray(family.Slice(r, "m0", 16));
            var n = new Mat3d().GetNormalMatrix(m);
            return [.. family.Slice(r, "m0", 16), .. n.Elements];
        });
    }

    [Fact]
    public void InvertOfASingularMatrixIsZero()
    {
        var m = new Mat3d().Set(1, 2, 3, 2, 4, 6, 0, 1, 0).Invert();
        Assert.All(m.Elements, e => Assert.True(Exact.Same(0.0, e)));
    }

    [Fact]
    public void SetTakesRowsAndStoresColumns()
    {
        var m = new Mat3d().Set(1, 2, 3, 4, 5, 6, 7, 8, 9);
        Assert.Equal([1, 4, 7, 2, 5, 8, 3, 6, 9], m.Elements);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], m.Transpose().Elements);
    }
}
