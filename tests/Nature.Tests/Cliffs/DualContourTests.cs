using PropGenerator.Nature.Cliffs;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

public class DualContourTests
{
    [Fact]
    public void ASphereIsClosedAndRound()
    {
        var sphere = new FunctionField(p => p.Length - 1);
        var mesh = DualContour.Mesh(sphere, new V3(-1, -1, -1), new V3(1, 1, 1), 0.1);
        Assert.Null(CliffMeshes.Open(mesh));
        Assert.All(mesh.Points, p => Assert.InRange(p.Length, 0.99, 1.01));
        Assert.Equal(4 * Math.PI / 3, CliffMeshes.Volume(mesh), 0.02);
        Assert.Equal(mesh.Points.Count, mesh.Tags.Count);
    }

    [Fact]
    public void ABoxKeepsItsFacesEdgesAndCornersExactly()
    {
        var half = new V3(1, 0.7, 0.45);
        var box = new FunctionField(p => Math.Max(Math.Max(Math.Abs(p.X) - half.X, Math.Abs(p.Y) - half.Y), Math.Abs(p.Z) - half.Z));
        var mesh = DualContour.Mesh(box, -half, half, 0.1);
        Assert.Null(CliffMeshes.Open(mesh));
        Assert.Equal(2 * 1.4 * 0.9, CliffMeshes.Volume(mesh), 1e-9);
        Assert.Equal(half.X, mesh.Points.Max(p => p.X), 1e-9);
        Assert.Equal(-half.Z, mesh.Points.Min(p => p.Z), 1e-9);

        // Every triangle lies on a face of the box.
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            var n = CliffMeshes.Abs(CliffMeshes.Face(mesh.Points, mesh.Indices, t).Normal);
            Assert.True(Math.Max(Math.Max(n.X, n.Y), n.Z) > 1 - 1e-9, $"triangle {t / 3} faces {n}");
        }
    }

    [Fact]
    public void ThePocketsAndCrumbsOfAPartAreDropped()
    {
        // A box with a pocket sealed inside it and a crumb beside it: the pocket's inside-out shell and the crumb go,
        // and the box stays, closed. Two shells of a size both stay.
        var field = new FunctionField(p => Math.Min(
            Math.Max(Box(p), 0.2 - (p - new V3(0.1, 0.05, 0.02)).Length),
            (p - new V3(1.5, 0.03, 0.01)).Length - 0.08));
        var mesh = DualContour.Mesh(field, new V3(-1.1, -0.8, -0.6), new V3(1.7, 0.8, 0.6), 0.05);
        Assert.Equal(3, CliffMeshes.ShellVolumes(mesh).Count);
        Assert.Equal(2, mesh.DropLooseShells(0.01));
        var left = CliffMeshes.ShellVolumes(mesh);
        Assert.Single(left);
        Assert.Equal(2 * 1.4 * 1.0, left[0], 1e-6);
        Assert.Null(CliffMeshes.Open(mesh));
        Assert.Equal(mesh.Points.Count, mesh.Tags.Count);

        var two = DualContour.Mesh(new FunctionField(p => Math.Min(Box(p), Box(p - new V3(2.5, 0, 0)))), new V3(-1.1, -0.8, -0.6), new V3(3.6, 0.8, 0.6), 0.05);
        Assert.Equal(0, two.DropLooseShells(0.01));
        Assert.Equal(2, CliffMeshes.ShellVolumes(two).Count);

        static double Box(V3 p) => Math.Max(Math.Max(Math.Abs(p.X) - 1, Math.Abs(p.Y) - 0.7), Math.Abs(p.Z) - 0.5);
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(8.0)]
    [InlineData(20.0)]
    public void AGentleCreaseRunsStraight(double degrees)
    {
        // Two flat faces meeting at a few degrees along a line slanted across the grid (a chiselled facet's edge): the
        // vertices of the cells the crease crosses sit on it, so every triangle lies on one face or the other instead of
        // cutting the crease off in steps.
        const double Cell = 0.1;
        double t = Math.Tan(degrees * Math.PI / 360);
        var across = new V3(0.906307787, 0.422618262, 0);
        var one = new V3(t * across.X, t * across.Y, 1).Normalized();
        var two = new V3(-t * across.X, -t * across.Y, 1).Normalized();
        var ridge = new FunctionField(p => Math.Max(Math.Max(V3.Dot(p, one), V3.Dot(p, two)), Math.Max(Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) - 1, -p.Z - 0.6)));
        var mesh = DualContour.Mesh(ridge, new V3(-1.1, -1.1, -0.7), new V3(1.1, 1.1, 0.4), Cell);
        Assert.Null(CliffMeshes.Open(mesh));
        double worst = 0;
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            var a = mesh.Points[mesh.Indices[i]];
            var b = mesh.Points[mesh.Indices[i + 1]];
            var c = mesh.Points[mesh.Indices[i + 2]];
            var middle = (a + b + c) / 3;
            if (Math.Max(Math.Abs(middle.X), Math.Abs(middle.Y)) < 0.8 && middle.Z > -0.3)
            {
                worst = Math.Max(worst, Math.Abs(Math.Max(V3.Dot(middle, one), V3.Dot(middle, two))));
            }
        }

        Assert.True(worst < 4e-3 * Cell, $"a triangle is {worst / Cell} cells off the faces");
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.7)]
    [InlineData(1.3)]
    public void APlateThinnerThanACellStaysClosed(double thickness)
    {
        // A tilted plate a fraction of a cell thick: the grid points straddle it unevenly.
        const double Cell = 0.1;
        var normal = new V3(0.3, 0.2, 1).Normalized();
        var plate = new FunctionField(p => Math.Max(Math.Abs(V3.Dot(p, normal)) - (thickness * Cell / 2), Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) - 1));
        var mesh = DualContour.Mesh(plate, new V3(-1.2, -1.2, -0.6), new V3(1.2, 1.2, 0.6), Cell);
        Assert.Null(CliffMeshes.Open(mesh));
    }

    [Fact]
    public void SpheresThatTouchStayManifold()
    {
        // Two balls meeting at a point, and two a hair apart: one vertex may not serve both.
        foreach (double gap in new[] { 0, 0.004, 0.03 })
        {
            var balls = new FunctionField(p => Math.Min((p - new V3(-0.5 - gap, 0.013, 0.021)).Length - 0.5, (p - new V3(0.5 + gap, 0.013, 0.021)).Length - 0.5));
            var mesh = DualContour.Mesh(balls, new V3(-1.1, -0.6, -0.6), new V3(1.1, 0.6, 0.6), 0.07);
            Assert.Null(CliffMeshes.Open(mesh));
        }
    }

    [Fact]
    public void RoughNoisyFieldsStayClosed()
    {
        // Noise much finer than the grid: thin sheets, pinches and specks everywhere.
        foreach (double scale in new[] { 2.0, 6.0, 15.0 })
        {
            var blob = new FunctionField(p => p.Length - 1 + (0.4 * ((CliffNoise.Fbm(p * scale, scale, 3) * 2) - 1)));
            var mesh = DualContour.Mesh(blob, new V3(-1.5, -1.5, -1.5), new V3(1.5, 1.5, 1.5), 0.12);
            Assert.Null(CliffMeshes.Open(mesh));
        }
    }

    [Fact]
    public void AnEstimateFarFromZeroStandsInForTheField()
    {
        // The field is read only near the surface: an estimate that is sure elsewhere gives the same mesh.
        var exact = new FunctionField(p => p.Length - 1);
        var estimated = new FunctionField(p => p.Length - 1, p => (p.Length - 1 + 0.05, 0.1));
        var a = DualContour.Mesh(exact, new V3(-1, -1, -1), new V3(1, 1, 1), 0.1);
        var b = DualContour.Mesh(estimated, new V3(-1, -1, -1), new V3(1, 1, 1), 0.1);
        Assert.Equal(a.Points, b.Points);
        Assert.Equal(a.Indices, b.Indices);
        var (_, nx, ny, nz) = DualContour.Grid(new V3(-1, -1, -1), new V3(1, 1, 1), 0.1);
        Assert.True(exact.Distances - estimated.Distances > nx * ny * nz * 0.6, $"{estimated.Distances} vs {exact.Distances} evaluations on {nx * ny * nz} points");
    }

    [Fact]
    public void BoundsTooTightStillGiveAClosedMesh()
    {
        // The padding is outside whatever the field says, so a solid cut by its bounds is capped, not opened.
        var sphere = new FunctionField(p => p.Length - 1);
        var mesh = DualContour.Mesh(sphere, new V3(-0.5, -0.5, -0.5), new V3(0.5, 0.5, 0.5), 0.1);
        Assert.Null(CliffMeshes.Open(mesh));
    }

    [Fact]
    public void HasInsideFindsTheSolidOnTheMeshesGrid()
    {
        var sphere = new FunctionField(p => p.Length - 1);
        Assert.True(DualContour.HasInside(sphere, new V3(-1, -1, -1), new V3(1, 1, 1), 0.1));
        // A ball wider than a cell's half diagonal always holds a grid point.
        var speck = new FunctionField(p => (p - new V3(0.71, -0.33, 0.2)).Length - 0.09);
        Assert.True(DualContour.HasInside(speck, new V3(-1, -1, -1), new V3(1, 1, 1), 0.1));
        var none = new FunctionField(p => p.Length + 0.5);
        Assert.False(DualContour.HasInside(none, new V3(-1, -1, -1), new V3(1, 1, 1), 0.1));
        Assert.Equal(0, DualContour.Mesh(none, new V3(-1, -1, -1), new V3(1, 1, 1), 0.1).TriangleCount);
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    [InlineData(7, true)]
    public void EveryPinchIsUndone(int opposite, bool bubbles)
    {
        // Two grid points of one cell, corner 0 and either the opposite corner of a face (6) or of the cube (7): tiny
        // balls around them make only those two points inside, or tiny bubbles in a large ball only those two outside.
        // Meshed as sampled, each pattern would pinch (four quads on one edge, or two sheets on one vertex).
        const double Cell = 0.2;
        var min = new V3(-1, -1, -1);
        var max = new V3(1, 1, 1);
        var (origin, nx, ny, nz) = DualContour.Grid(min, max, Cell);
        V3 Corner(int c) => origin + (new V3((nx / 2) + (c & 1), (ny / 2) + ((c >> 1) & 1), (nz / 2) + ((c >> 2) & 1)) * Cell);
        double Balls(V3 p) => Math.Min((p - Corner(0)).Length, (p - Corner(opposite)).Length) - (0.3 * Cell);
        var field = new FunctionField(bubbles ? p => Math.Max(p.Length - 0.9, -Balls(p)) : Balls);

        for (int c = 0; c < 8; c++)
        {
            bool marked = c == 0 || c == opposite;
            Assert.Equal(marked != bubbles, field.Distance(Corner(c)) < 0);
        }

        var mesh = DualContour.Mesh(field, min, max, Cell);
        Assert.True(mesh.TriangleCount > 0);
        Assert.Null(CliffMeshes.Open(mesh));
    }
}
