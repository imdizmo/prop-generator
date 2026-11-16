using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class QuatdTests
{
    private static readonly EulerOrder[] Orders = [EulerOrder.XYZ, EulerOrder.YXZ, EulerOrder.ZYX];

    [Fact]
    public void SetFromEulerMatchesThree()
    {
        var family = KernelGoldens.Family("setFromEuler");
        family.AssertAll(r =>
        {
            var euler = new Euler(family[r, "ex"], family[r, "ey"], family[r, "ez"], Orders[(int)family[r, "order"]]);
            var q = new Quatd().SetFromEuler(euler);
            return [q.X, q.Y, q.Z, q.W];
        });
    }

    [Fact]
    public void SetFromUnitVectorsMatchesThree()
    {
        var family = KernelGoldens.Family("setFromUnitVectors");
        family.AssertAll(r =>
        {
            var from = new Vec3d(family[r, "fx"], family[r, "fy"], family[r, "fz"]);
            var to = new Vec3d(family[r, "tx"], family[r, "ty"], family[r, "tz"]);
            var q = new Quatd().SetFromUnitVectors(from, to);
            return [q.X, q.Y, q.Z, q.W];
        });
    }

    [Fact]
    public void MultiplyAndSetFromAxisAngleMatchThree()
    {
        var family = KernelGoldens.Family("quaternion");
        family.AssertAll(r =>
        {
            var a = new Quatd(family[r, "ax"], family[r, "ay"], family[r, "az"], family[r, "aw"]);
            var b = new Quatd(family[r, "bx"], family[r, "by"], family[r, "bz"], family[r, "bw"]);
            var axis = new Vec3d(family[r, "axisx"], family[r, "axisy"], family[r, "axisz"]);
            var product = a.Multiply(b);
            var turn = new Quatd().SetFromAxisAngle(axis, family[r, "angle"]);
            return [product.X, product.Y, product.Z, product.W, turn.X, turn.Y, turn.Z, turn.W];
        });
    }

    [Fact]
    public void MultiplyReadsBothBeforeWritingLikeThree()
    {
        var q = new Quatd(0.1, 0.2, 0.3, 0.9);
        var expected = new Quatd().MultiplyQuaternions(new Quatd(0.1, 0.2, 0.3, 0.9), new Quatd(0.1, 0.2, 0.3, 0.9));
        q.MultiplyQuaternions(q, q);
        Assert.Equal((expected.X, expected.Y, expected.Z, expected.W), (q.X, q.Y, q.Z, q.W));
    }

    [Fact]
    public void NormalizeOfZeroIsIdentity()
    {
        var q = new Quatd(0, 0, 0, 0).Normalize();
        Assert.Equal((0.0, 0.0, 0.0, 1.0), (q.X, q.Y, q.Z, q.W));
    }
}
