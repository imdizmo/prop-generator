using PropGenerator.Nature.Cliffs;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

/// <summary>The edge collapse thins flat faces and keeps creases, chips, the ground and the mesh closed.</summary>
public class MeshSimplifyTests
{
    /// <summary>The grid spacing the test solids are meshed at.</summary>
    private const double Cell = 0.1;

    /// <summary>The error stop the cliffs use: 0.15 cells.</summary>
    private const double Tau = 0.15 * Cell;

    [Fact]
    public void AFlatFacedBoxCollapsesToFewTriangles()
    {
        // A box's six faces, meshed at 0.1 m (about 900 triangles), come down to about the 12 a box needs, its volume
        // and corners as they were.
        var half = new V3(1, 0.7, 0.45);
        var mesh = DualContour.Mesh(Box(half), -half, half, Cell);
        int before = mesh.TriangleCount;
        double volume = CliffMeshes.Volume(mesh);
        Assert.True(MeshSimplify.Simplify(mesh, Tau, []) > 0);
        Assert.True(mesh.TriangleCount <= 16, $"{before} triangles came down to {mesh.TriangleCount}");
        Assert.Null(CliffMeshes.Open(mesh));
        Assert.Equal(volume, CliffMeshes.Volume(mesh), 1e-9);
        Assert.Equal(8, mesh.Points.Count(p => Math.Abs(Math.Abs(p.X) - half.X) < 1e-9 && Math.Abs(Math.Abs(p.Y) - half.Y) < 1e-9 && Math.Abs(Math.Abs(p.Z) - half.Z) < 1e-9));
    }

    [Fact]
    public void AChippedEdgeKeepsItsChipWithinTheStop()
    {
        // A round chip bitten out of the box's top front edge: the flat faces thin out, the chip's curve stays, and the
        // two meshes lie within the stop of each other both ways.
        var (field, raw, simple) = Chipped(false);
        Assert.True(simple.TriangleCount < raw.TriangleCount / 3, $"{raw.TriangleCount} triangles came down to {simple.TriangleCount}");
        Assert.True(Farthest(raw.Points, simple) < Tau, $"a first point is {Farthest(raw.Points, simple):F4} m from the thinned mesh");
        Assert.True(Farthest(Samples(simple), raw) < Tau, $"a thinned point is {Farthest(Samples(simple), raw):F4} m from the first mesh");

        // The chip keeps its points: those inside the bite's reach, most of the first mesh's there.
        int Bitten(SolidMesh m) => m.Points.Count(p => (p - ChipCentre).Length < ChipRadius + 1e-6 && field.Distance(p) > -1e-6);
        Assert.True(Bitten(simple) > Bitten(raw) / 4, $"the chip has {Bitten(simple)} of {Bitten(raw)} points left");
    }

    [Fact]
    public void TheMeshStaysClosedAndNoTriangleTurnsOver()
    {
        var (field, raw, simple) = Chipped(false);
        Assert.Null(CliffMeshes.Open(simple));
        Assert.Equal(CliffMeshes.ShellVolumes(raw).Count, CliffMeshes.ShellVolumes(simple).Count);
        Assert.Equal(CliffMeshes.Volume(raw), CliffMeshes.Volume(simple), 0.01 * CliffMeshes.Volume(raw));
        for (int t = 0; t < simple.Indices.Count; t += 3)
        {
            var (normal, area) = CliffMeshes.Face(simple.Points, simple.Indices, t);
            var middle = (simple.Points[simple.Indices[t]] + simple.Points[simple.Indices[t + 1]] + simple.Points[simple.Indices[t + 2]]) / 3;
            Assert.True(area >= MeshMend.EmptyArea, $"triangle {t / 3} is empty");
            Assert.True(V3.Dot(normal, Slope(field, middle)) > 0, $"triangle {t / 3} faces into the solid");
        }

        // No fold is added.
        Assert.True(CliffMeshes.Folds(simple, MeshMend.FoldBend) * simple.Indices.Count <= CliffMeshes.Folds(raw, MeshMend.FoldBend) * raw.Indices.Count);
    }

    [Fact]
    public void GroundPointsStayOnTheGround()
    {
        // The chipped box standing on the ground z = 0, its chip at the ground: every point left is one the mesh had,
        // where it was, and the footprint flat on the ground keeps its area to within the stop along its outline.
        var (_, raw, simple) = Chipped(true);
        var first = raw.Points.ToHashSet();
        Assert.All(simple.Points, p => Assert.Contains(p, first));
        Assert.True(Footprint(raw) > 1, $"the footprint is {Footprint(raw):F3} m²");
        Assert.Equal(Footprint(raw), Footprint(simple), 2 * (2 + 1.4) * Tau);
        Assert.True(simple.TriangleCount < raw.TriangleCount / 3);

        // A held height across flat faces: the box centred on the origin has a ring of points round its sides at the
        // height of the row nearest its middle, which go when nothing is held and stay, on every side, when it is.
        var half = new V3(1, 0.7, 0.45);
        var free = DualContour.Mesh(Box(half), -half, half, Cell);
        var held = DualContour.Mesh(Box(half), -half, half, Cell);
        double height = free.Points.Where(p => Math.Abs(p.X - half.X) < 1e-9 && Math.Abs(p.Z) < 0.4).MinBy(p => Math.Abs(p.Z)).Z;
        int ring = free.Points.Count(OnRing);
        MeshSimplify.Simplify(free, Tau, []);
        MeshSimplify.Simplify(held, Tau, [height]);
        Assert.True(ring >= 4, $"{ring} points on the ring");
        Assert.Equal(0, free.Points.Count(OnRing));
        V3[] sides = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0)];
        Assert.All(sides, side =>
            Assert.Contains(held.Points, p => OnRing(p) && V3.Dot(p, side) > 0.5 * V3.Dot(CliffMeshes.Abs(side), half)));
        Assert.Null(CliffMeshes.Open(held));

        bool OnRing(V3 p) => Math.Abs(p.Z - height) < 1e-9;

        static double Footprint(SolidMesh m)
        {
            double area = 0;
            for (int t = 0; t < m.Indices.Count; t += 3)
            {
                if (Math.Abs(m.Points[m.Indices[t]].Z) < 1e-9 && Math.Abs(m.Points[m.Indices[t + 1]].Z) < 1e-9 && Math.Abs(m.Points[m.Indices[t + 2]].Z) < 1e-9)
                {
                    area += CliffMeshes.Face(m.Points, m.Indices, t).Area;
                }
            }

            return area;
        }
    }

    [Fact]
    public void AMeshWithNothingToCollapseIsLeftAsItIs()
    {
        // An octahedron: every collapse would move a corner far off its faces.
        var mesh = new SolidMesh();
        mesh.Points.AddRange([new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)]);
        mesh.Tags.AddRange([0, 1, 2, 3, 4, 5]);
        mesh.Indices.AddRange([0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5]);
        var points = mesh.Points.ToList();
        var indices = mesh.Indices.ToList();
        Assert.Null(CliffMeshes.Open(mesh));
        Assert.Equal(0, MeshSimplify.Simplify(mesh, Tau, [0]));
        Assert.Equal(points, mesh.Points);
        Assert.Equal(indices, mesh.Indices);
        Assert.Equal([0, 1, 2, 3, 4, 5], mesh.Tags);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0.4)]
    public void TheLongCliffKeepsItsShapeWithinTheStop(double level)
    {
        // The Long cliff as meshed and as thinned, at the stop it is thinned to (0.15 of its grid cell): every point of
        // the first mesh lies within the stop of the thinned one, and every corner, edge middle and triangle middle of
        // the thinned one within the stop of the first. Thinning twice gives the same mesh.
        var s = CliffMeshes.Preset("long-cliff");
        var layout = CliffGenerator.Layout(s);
        var (_, raw, cell) = Assert.Single(CliffGenerator.Unsimplified(s, layout, level));
        double tau = CliffGenerator.SimplifyCells * cell;
        var (_, simple) = Assert.Single(CliffGenerator.Finish(s, CliffGenerator.Unsimplified(s, layout, level)));
        var (_, again) = Assert.Single(CliffGenerator.Pieces(s, layout, level));
        Assert.Equal(simple.Points, again.Points);
        Assert.Equal(simple.Indices, again.Indices);
        Assert.True(simple.TriangleCount < raw.TriangleCount / 2, $"{raw.TriangleCount} triangles came down to {simple.TriangleCount}");
        Assert.Null(CliffMeshes.Open(simple));

        var near = new Near(simple, tau);
        var far = raw.Points.Select(p => (p, near.Distance(p))).MaxBy(pair => pair.Item2);
        Assert.True(far.Item2 < tau, $"the first mesh's point {far.p} is {far.Item2:F4} m from the thinned one (stop {tau:F4} m)");
        near = new Near(raw, tau);
        far = Samples(simple).Select(p => (p, near.Distance(p))).MaxBy(pair => pair.Item2);
        Assert.True(far.Item2 < tau, $"the thinned mesh's point {far.p} is {far.Item2:F4} m from the first one (stop {tau:F4} m)");
    }

    /// <summary>Where the chip's bite is centred: on the box's top front edge.</summary>
    private static readonly V3 ChipCentre = new(0.2, -0.7, 0.45);

    /// <summary>The bite's radius.</summary>
    private const double ChipRadius = 0.3;

    /// <summary>A box's field.</summary>
    private static FunctionField Box(V3 half) =>
        new(p => Math.Max(Math.Max(Math.Abs(p.X) - half.X, Math.Abs(p.Y) - half.Y), Math.Abs(p.Z) - half.Z));

    /// <summary>
    /// A box (2 × 1.4 × 0.9 m) with a round chip bitten out of its top front edge, centred on the origin, or standing on
    /// the ground z = 0 with the chip at its bottom front edge; meshed at 0.1 m, and the same mesh thinned at the stop,
    /// holding the ground z = 0.
    /// </summary>
    private static (FunctionField Field, SolidMesh Raw, SolidMesh Simple) Chipped(bool grounded)
    {
        var half = new V3(1, 0.7, 0.45);
        var up = new V3(0, 0, grounded ? half.Z : 0);
        var centre = grounded ? new V3(ChipCentre.X, ChipCentre.Y, -half.Z) : ChipCentre;
        var field = new FunctionField(q =>
        {
            var p = q - up;
            double box = Math.Max(Math.Max(Math.Abs(p.X) - half.X, Math.Abs(p.Y) - half.Y), Math.Abs(p.Z) - half.Z);
            return Math.Max(box, ChipRadius - (p - centre).Length);
        });
        var raw = DualContour.Mesh(field, up - half, up + half, Cell);
        var simple = DualContour.Mesh(field, up - half, up + half, Cell);
        MeshSimplify.Simplify(simple, Tau, [0]);
        return (field, raw, simple);
    }

    private static V3 Slope(FunctionField field, V3 p)
    {
        const double h = 1e-4;
        return new V3(
            field.Distance(p + new V3(h, 0, 0)) - field.Distance(p - new V3(h, 0, 0)),
            field.Distance(p + new V3(0, h, 0)) - field.Distance(p - new V3(0, h, 0)),
            field.Distance(p + new V3(0, 0, h)) - field.Distance(p - new V3(0, 0, h)));
    }

    /// <summary>Points over a mesh's triangles: each corner, each edge's middle and each triangle's middle.</summary>
    internal static List<V3> Samples(SolidMesh mesh)
    {
        var samples = new List<V3>(mesh.Points);
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            var a = mesh.Points[mesh.Indices[t]];
            var b = mesh.Points[mesh.Indices[t + 1]];
            var c = mesh.Points[mesh.Indices[t + 2]];
            samples.AddRange([(a + b) / 2, (b + c) / 2, (c + a) / 2, (a + b + c) / 3]);
        }

        return samples;
    }

    /// <summary>The farthest any of the points is from the mesh's surface.</summary>
    internal static double Farthest(IEnumerable<V3> points, SolidMesh mesh)
    {
        double farthest = 0;
        foreach (var p in points)
        {
            double nearest = double.PositiveInfinity;
            for (int t = 0; t < mesh.Indices.Count && nearest > farthest; t += 3)
            {
                nearest = Math.Min(nearest, (MeshSimplify.Closest(p, mesh.Points[mesh.Indices[t]], mesh.Points[mesh.Indices[t + 1]], mesh.Points[mesh.Indices[t + 2]]) - p).Length);
            }

            farthest = Math.Max(farthest, nearest);
        }

        return farthest;
    }

    /// <summary>
    /// A mesh's triangles binned in cubes, each triangle in every cube its bounds (grown by a reach) touch, so the
    /// triangles within the reach of a point are all in the point's cube.
    /// </summary>
    private sealed class Near
    {
        private const double Size = 0.25;
        private readonly SolidMesh _mesh;
        private readonly Dictionary<(long, long, long), List<int>> _cubes = [];

        public Near(SolidMesh mesh, double reach)
        {
            _mesh = mesh;
            for (int t = 0; t < mesh.Indices.Count; t += 3)
            {
                var a = mesh.Points[mesh.Indices[t]];
                var b = mesh.Points[mesh.Indices[t + 1]];
                var c = mesh.Points[mesh.Indices[t + 2]];
                var (x0, y0, z0) = Cube(new V3(Math.Min(a.X, Math.Min(b.X, c.X)), Math.Min(a.Y, Math.Min(b.Y, c.Y)), Math.Min(a.Z, Math.Min(b.Z, c.Z))) - new V3(reach, reach, reach));
                var (x1, y1, z1) = Cube(new V3(Math.Max(a.X, Math.Max(b.X, c.X)), Math.Max(a.Y, Math.Max(b.Y, c.Y)), Math.Max(a.Z, Math.Max(b.Z, c.Z))) + new V3(reach, reach, reach));
                for (long x = x0; x <= x1; x++)
                {
                    for (long y = y0; y <= y1; y++)
                    {
                        for (long z = z0; z <= z1; z++)
                        {
                            if (!_cubes.TryGetValue((x, y, z), out var list))
                            {
                                _cubes[(x, y, z)] = list = [];
                            }

                            list.Add(t);
                        }
                    }
                }
            }
        }

        /// <summary>The distance from a point to the nearest triangle within the reach, or infinity.</summary>
        public double Distance(V3 p)
        {
            double nearest = double.PositiveInfinity;
            if (_cubes.TryGetValue(Cube(p), out var list))
            {
                foreach (int t in list)
                {
                    var q = MeshSimplify.Closest(p, _mesh.Points[_mesh.Indices[t]], _mesh.Points[_mesh.Indices[t + 1]], _mesh.Points[_mesh.Indices[t + 2]]);
                    nearest = Math.Min(nearest, (q - p).Length);
                }
            }

            return nearest;
        }

        private static (long, long, long) Cube(V3 p) => ((long)Math.Floor(p.X / Size), (long)Math.Floor(p.Y / Size), (long)Math.Floor(p.Z / Size));
    }
}
