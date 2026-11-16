using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class Vec3dTests
{
    private static readonly EulerOrder[] Orders = [EulerOrder.XYZ, EulerOrder.YXZ, EulerOrder.ZYX];

    [Fact]
    public void ApplyMatrix4MatchesThree()
    {
        var family = KernelGoldens.Family("applyMatrix4");
        family.AssertAll(r =>
        {
            var m = new Mat4d().FromArray(family.Slice(r, "m0", 16));
            var v = new Vec3d(family[r, "vx"], family[r, "vy"], family[r, "vz"]).ApplyMatrix4(m);
            return [v.X, v.Y, v.Z];
        });
    }

    [Fact]
    public void VectorOperationsMatchThree()
    {
        var family = KernelGoldens.Family("vector");
        family.AssertAll(r =>
        {
            var v = new Vec3d(family[r, "vx"], family[r, "vy"], family[r, "vz"]);
            var w = new Vec3d(family[r, "wx"], family[r, "wy"], family[r, "wz"]);
            var axis = new Vec3d(family[r, "axisx"], family[r, "axisy"], family[r, "axisz"]);
            var euler = new Euler(family[r, "ex"], family[r, "ey"], family[r, "ez"], Orders[(int)family[r, "order"]]);
            var axisAngle = v.Clone().ApplyAxisAngle(axis, family[r, "angle"]);
            var byEuler = v.Clone().ApplyEuler(euler);
            var normalized = v.Clone().Normalize();
            var cross = new Vec3d().CrossVectors(v, w);
            var lerp = v.Clone().Lerp(w, family[r, "alpha"]);
            return
            [
                axisAngle.X, axisAngle.Y, axisAngle.Z, byEuler.X, byEuler.Y, byEuler.Z,
                normalized.X, normalized.Y, normalized.Z, cross.X, cross.Y, cross.Z,
                v.DistanceTo(w), v.Dot(w), v.Length(), lerp.X, lerp.Y, lerp.Z,
            ];
        });
    }

    [Fact]
    public void NormalizeLeavesTheZeroVectorZero()
    {
        var v = new Vec3d().Normalize();
        Assert.True(Exact.Same(0.0, v.X) && Exact.Same(0.0, v.Y) && Exact.Same(0.0, v.Z));
    }

    [Fact]
    public void DivideScalarMultipliesByTheReciprocal()
    {
        // three divides as multiplyScalar(1 / s); 0.1 / 7 and 0.1 * (1 / 7) differ in the last bit.
        var v = new Vec3d(0.1, 0.2, 5).DivideScalar(7);
        Assert.True(Exact.Same(0.1 * (1.0 / 7), v.X));
        Assert.True(Exact.Same(0.2 * (1.0 / 7), v.Y));
        Assert.True(Exact.Same(5 * (1.0 / 7), v.Z));
        Assert.NotEqual(0.1 / 7, v.X);
    }

    [Fact]
    public void CrossReadsItselfBeforeWritingLikeThree()
    {
        var a = new Vec3d(1, 2, 3);
        var b = new Vec3d(-4, 0.5, 2);
        var expected = new Vec3d().CrossVectors(a.Clone(), b);
        var same = a.Cross(b);
        Assert.Same(a, same);
        Assert.Equal((expected.X, expected.Y, expected.Z), (a.X, a.Y, a.Z));
    }

    [Fact]
    public void ChainsReturnTheSameObject()
    {
        var v = new Vec3d(1, 2, 3);
        Assert.Same(v, v.Add(new Vec3d(1, 1, 1)).MultiplyScalar(2).Sub(new Vec3d(0, 1, 0)).Normalize());
    }

    [Fact]
    public void FromBufferAttributeReadsTheFloatsOfOneVertex()
    {
        float[] positions = [0.1f, 0.2f, 0.3f, 1.1f, 1.2f, 1.3f];
        var v = new Vec3d().FromBufferAttribute(positions, 1);
        Assert.Equal((1.1f, 1.2f, 1.3f), ((float)v.X, (float)v.Y, (float)v.Z));
        Assert.Equal((double)1.1f, v.X);
    }
}
