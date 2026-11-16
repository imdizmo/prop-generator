using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class Mat4dTests
{
    [Fact]
    public void ComposeMatchesThree()
    {
        var family = KernelGoldens.Family("compose");
        family.AssertAll(r =>
        {
            var position = new Vec3d(family[r, "px"], family[r, "py"], family[r, "pz"]);
            var quaternion = new Quatd(family[r, "qx"], family[r, "qy"], family[r, "qz"], family[r, "qw"]);
            var scale = new Vec3d(family[r, "sx"], family[r, "sy"], family[r, "sz"]);
            var m = new Mat4d().Compose(position, quaternion, scale);

            // The normal matrix is checked in Mat3dTests; here three's own values pass through.
            return [.. m.ToArray(), .. family.Slice(r, "n0", 9)];
        });
    }

    [Fact]
    public void StartsAsIdentity()
    {
        Assert.Equal([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1], new Mat4d().ToArray());
    }

    [Fact]
    public void ElementsAreColumnMajor()
    {
        var m = new Mat4d().MakeTranslation(5, 6, 7);
        Assert.Equal([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 5, 6, 7, 1], m.ToArray());
        Assert.Equal([2, 0, 0, 0, 0, 3, 0, 0, 0, 0, 4, 0, 0, 0, 0, 1], new Mat4d().MakeScale(2, 3, 4).ToArray());
    }

    [Fact]
    public void FromArrayReadsSixteenFromAnOffset()
    {
        double[] values = [.. Enumerable.Range(0, 20).Select(i => i * 0.5)];
        Assert.Equal(values[2..18], new Mat4d().FromArray(values, 2).ToArray());
    }

    [Fact]
    public void RotationFromQuaternionIsComposeWithoutMoveOrScale()
    {
        var q = new Quatd().SetFromEuler(new Euler(0.3, -1.2, 2.2));
        var expected = new Mat4d().Compose(new Vec3d(0, 0, 0), q, new Vec3d(1, 1, 1));
        Assert.Equal(expected.ToArray(), new Mat4d().MakeRotationFromQuaternion(q).ToArray());
    }
}
