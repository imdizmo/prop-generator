using PropGenerator.Nature.Export;
using PropGenerator.Nature.Kernel;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class WindTests
{
    [Fact]
    public void ARigidPointSwaysButNeverFlutters()
    {
        var mesh = new PropMesh { Position = [0, 1, 0, 0, 1, 0, 0, 1, 0] };
        var part = new GeneratedPart(mesh, "leaves", "foliage", null, ["position"], new Mat4d(), 1);
        float[] free = Wind.Uv1(mesh, part, new Vec3d(0, 0, 0), new WindExtent(2, 1));
        float[] fixedPoints = Wind.Uv1(mesh, part, new Vec3d(0, 0, 0), new WindExtent(2, 1), [0, 1, 0.5f]);
        Assert.Equal([free[0], free[2], free[4]], [fixedPoints[0], fixedPoints[2], fixedPoints[4]]);
        Assert.Equal(free[1], fixedPoints[1]);
        Assert.Equal(0f, fixedPoints[3]);
        Assert.Equal((float)(free[5] * 0.5), fixedPoints[5]);
    }

    [Fact]
    public void SwayGrowsWithHeightAndReachAndFlutterFollowsTheRole()
    {
        var extent = new WindExtent(2, 1);
        Assert.Equal((0.0, 0.0), Wind.Weight(0, 0, 0, "solid", extent));
        Assert.Equal((0.6, 0.25), Wind.Weight(0, 2, 0, "thin", extent));
        Assert.Equal((1.0, 1.0), Wind.Weight(3, 5, 4, "foliage", extent));
        Assert.Equal(0.5, Wind.Weight(0, -1, 0, "foliage", extent).Flutter);
    }

    [Fact]
    public void TheExtentSkipsProxiesAndIsNeverBelowAMillimetre()
    {
        var point = new GeneratedPart(new PropMesh { Position = [0, 0, 0] }, "bark", null, null, ["position"], new Mat4d(), 1);
        var proxy = point with
        {
            Mesh = new PropMesh { Position = [9, 9, 9] },
            Surface = new PaintedSurface(false, Proxy: true),
        };
        Assert.Equal(new WindExtent(1e-3, 1e-3), Wind.MeasureExtent([point, proxy], new Vec3d(0, 0, 0)));
    }

    [Fact]
    public void AnExportOnlyMeasuresWindForAPropThatSways()
    {
        var part = new GeneratedPart(new PropMesh { Position = [3, 2, 4] }, "bark", null, null, ["position"], new Mat4d(), 1);
        var origin = new Vec3d(0, 0, 0);
        Assert.Null(Wind.ForExport([part], origin, wind: false, sways: true, swaysEvenly: false));
        Assert.Null(Wind.ForExport([part], origin, wind: true, sways: false, swaysEvenly: false));
        Assert.Equal(new WindExtent(2, 5), Wind.ForExport([part], origin, wind: true, sways: true, swaysEvenly: false));
        Assert.Equal(new WindExtent(2, Wind.EvenReach), Wind.ForExport([part], origin, wind: true, sways: true, swaysEvenly: true));
    }
}
