using PropGenerator.Nature.Cliffs;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

/// <summary>MeshFold: the sliver folds it collapses away, the collapses it refuses, and the mesh it leaves.</summary>
public class MeshFoldTests
{
    /// <summary>The unit cube the test meshes are of.</summary>
    private static readonly ConvexBlock Cube = CliffMeshes.Box(0, 1, 0, 1, 0, 1);

    [Fact]
    public void ASliverFoldIsCollapsedAway()
    {
        // A unit cube whose top is a 4 x 4 grid, one point of the grid's second row pulled 2 cm past the front edge (as a
        // sub-cell sliver of a chip's side leaves it): the triangles at it fold back over their neighbours. One collapse
        // takes the fold away; the mesh stays closed and 2-manifold, and nothing moves off the surface further than tau.
        var mesh = TopGrid(4, null, 0);
        mesh.Points[Top(4, 2, 1)] = new V3(0.5, -0.02, 1);
        double before = Folds(mesh);
        Assert.True(before >= 1, $"the test mesh has {before} folds");

        int made = MeshFold.Collapse(mesh, Cube, 0.25, 0.0375, []);
        Assert.True(made >= 1);
        Assert.Equal(0, Folds(mesh));
        Assert.Null(CliffMeshes.Open(mesh));
    }

    [Fact]
    public void ACollapseThatWouldMakeAFoldIsRefused()
    {
        // A cube whose top grid's inner points are thrown about (seed 19377, as found by a search over thousands): the
        // collapses that would take its two folds away make others among the triangles they move (the old count read
        // those triangles from one side only and let one through, leaving two folds). So every one is refused: none is
        // made (pinned, so a collapse let through fails here; review-final Minor 6), the two folds stay, and the mesh
        // stays closed.
        var mesh = TopGrid(4, new System.Random(19377), 1.7);
        double before = Folds(mesh);
        Assert.Equal(2, before);

        int made = MeshFold.Collapse(mesh, Cube, 0.25, 0.25, []);
        double after = Folds(mesh);
        Assert.Equal(0, made);
        Assert.Equal(before, after);
        Assert.Null(CliffMeshes.Open(mesh));
    }

    [Fact]
    public void TheSameMeshComesOutTheSame()
    {
        var a = TopGrid(4, new System.Random(6298), 1.1);
        var b = TopGrid(4, new System.Random(6298), 1.1);
        MeshFold.Collapse(a, Cube, 0.25, 0.25, []);
        MeshFold.Collapse(b, Cube, 0.25, 0.25, []);
        Assert.Equal(a.Indices, b.Indices);
        Assert.Equal(a.Points, b.Points);
    }

    [Fact]
    public void AHeldPointStaysOnItsHeight()
    {
        // The pulled point of the sliver test, with the top held: the collapse may move it only onto another top point.
        var mesh = TopGrid(4, null, 0);
        mesh.Points[Top(4, 2, 1)] = new V3(0.5, -0.02, 1);
        MeshFold.Collapse(mesh, Cube, 0.25, 0.0375, [1]);
        Assert.All(mesh.Points, p => Assert.True(Math.Abs(p.Z - 1) < 1e-12 || Math.Abs(p.Z) < 1e-12));
        Assert.Null(CliffMeshes.Open(mesh));
    }

    [Fact]
    public void ARingEdgeLongerThanTheStopIsNotCollapsed()
    {
        // A 5 x 5 top grid thrown about (seed 109, a share of 0.9): its one fold is taken away only by collapsing an edge
        // of the triangles around the folded pair. With the stop at 0.1 m every such edge that would do is longer than
        // it, so nothing is collapsed and the mesh is as it was (with the ring held to no stop, one was: the
        // round 4 search); with the stop at 0.25 m one is, and the fold goes.
        var mesh = TopGrid(5, new System.Random(109), 0.9);
        var was = TopGrid(5, new System.Random(109), 0.9);
        Assert.Equal(1, Folds(mesh));
        Assert.Equal(0, MeshFold.Collapse(mesh, Cube, 0.2, 0.1, []));
        Assert.Equal(was.Indices, mesh.Indices);
        Assert.Equal(was.Points, mesh.Points);

        Assert.Equal(1, MeshFold.Collapse(mesh, Cube, 0.2, 0.25, []));
        Assert.Equal(0, Folds(mesh));
        Assert.Null(CliffMeshes.Open(mesh));
    }

    [Fact]
    public void AnEdgeOfTheFoldedPairIsNotHeldToTheStop()
    {
        // A 4 x 4 top grid thrown about (seed 7, a share of 0.9) with one fold, every edge of its folded pair longer than
        // the 0.1 m stop: one of them is still collapsed, as the fold it takes away is no surface to keep (Ruling 41),
        // and the fold goes (held to the stop as the ring is, nothing was collapsed: the round 4 search).
        var mesh = TopGrid(4, new System.Random(7), 0.9);
        Assert.Equal(1, Folds(mesh));
        Assert.All(FoldedEdges(mesh), length => Assert.True(length > 0.1, $"an edge of the folded pair is {length:F3} m"));
        Assert.Equal(1, MeshFold.Collapse(mesh, Cube, 0.25, 0.1, []));
        Assert.Equal(0, Folds(mesh));
        Assert.Null(CliffMeshes.Open(mesh));
    }

    [Fact]
    public void ACollapseThatWouldTurnATriangleOverIsRefused()
    {
        // A 4 x 4 top grid thrown about (seed 741, a share of 1.3) with one fold: the only collapse that takes it away
        // turns a triangle that was in no fold further than MeshSimplify lets a collapse turn one (past about 78°), so
        // nothing is collapsed, whatever the stop (without the limit it was: the round 4 search).
        var mesh = TopGrid(4, new System.Random(741), 1.3);
        var was = TopGrid(4, new System.Random(741), 1.3);
        Assert.Equal(1, Folds(mesh));
        Assert.Equal(0, MeshFold.Collapse(mesh, Cube, 0.25, 1, []));
        Assert.Equal(was.Indices, mesh.Indices);
        Assert.Equal(was.Points, mesh.Points);
    }

    [Fact]
    public void ACollapseThatWouldLeaveASliverAngleIsRefused()
    {
        // A 4 x 4 top grid thrown about (seed 924, a share of 1.3) with one fold: the only collapse that takes it away
        // leaves a triangle with an angle under MeshSimplify's 3°, so nothing is collapsed, whatever the stop (without
        // the check it was: the round 4 search).
        var mesh = TopGrid(4, new System.Random(924), 1.3);
        var was = TopGrid(4, new System.Random(924), 1.3);
        Assert.Equal(1, Folds(mesh));
        Assert.Equal(0, MeshFold.Collapse(mesh, Cube, 0.25, 1, []));
        Assert.Equal(was.Indices, mesh.Indices);
        Assert.Equal(was.Points, mesh.Points);
    }

    /// <summary>How many edges fold over 150°.</summary>
    private static double Folds(SolidMesh mesh) => Math.Round(CliffMeshes.Folds(mesh, 150) * mesh.Indices.Count / 2);

    /// <summary>The lengths of the edges of every triangle in a fold over 150° (a pair of neighbours whose faces bend that far).</summary>
    private static List<double> FoldedEdges(SolidMesh mesh)
    {
        var lengths = new List<double>();
        int count = mesh.Indices.Count / 3;
        double cos = Math.Cos(150 * Math.PI / 180);
        for (int t = 0; t < count; t++)
        {
            for (int u = 0; u < count; u++)
            {
                if (u != t && Shared(t, u) == 2 && V3.Dot(Normal(t), Normal(u)) < cos)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        lengths.Add((Corner(t, k) - Corner(t, (k + 1) % 3)).Length);
                    }

                    break;
                }
            }
        }

        return lengths;

        V3 Corner(int t, int k) => mesh.Points[mesh.Indices[(3 * t) + k]];
        V3 Normal(int t) => V3.Cross(Corner(t, 1) - Corner(t, 0), Corner(t, 2) - Corner(t, 0)).Normalized();
        int Shared(int t, int u) => Enumerable.Range(0, 3).Count(k => Enumerable.Range(0, 3).Any(m => mesh.Indices[(3 * t) + k] == mesh.Indices[(3 * u) + m]));
    }

    /// <summary>The index of the top grid's point (i along x, j along y) in <see cref="TopGrid"/>'s mesh.</summary>
    private static int Top(int n, int i, int j) => (i * (n + 1)) + j;

    /// <summary>
    /// A closed unit cube whose top is an n x n grid of quads (two triangles each, the diagonal alternating) and whose
    /// sides are fans from the bottom corners; with a random, the top's inner points are moved up to a share of a grid
    /// step in x and y and half that in z.
    /// </summary>
    private static SolidMesh TopGrid(int n, System.Random? random, double jitter)
    {
        var m = new SolidMesh();
        int[,] top = new int[n + 1, n + 1];
        for (int i = 0; i <= n; i++)
        {
            for (int j = 0; j <= n; j++)
            {
                double x = (double)i / n;
                double y = (double)j / n;
                double z = 1;
                if (random is not null && i > 0 && i < n && j > 0 && j < n)
                {
                    x += (random.NextDouble() - 0.5) * 2 * jitter / n;
                    y += (random.NextDouble() - 0.5) * 2 * jitter / n;
                    z += (random.NextDouble() - 0.5) * jitter / n;
                }

                top[i, j] = Add(x, y, z);
            }
        }

        int b00 = Add(0, 0, 0);
        int b10 = Add(1, 0, 0);
        int b11 = Add(1, 1, 0);
        int b01 = Add(0, 1, 0);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                int a = top[i, j];
                int b = top[i + 1, j];
                int c = top[i + 1, j + 1];
                int d = top[i, j + 1];
                if ((i + j) % 2 == 0)
                {
                    T(a, b, c);
                    T(a, c, d);
                }
                else
                {
                    T(a, b, d);
                    T(b, c, d);
                }
            }
        }

        T(b00, b11, b10);
        T(b00, b01, b11);
        for (int i = 0; i < n; i++)
        {
            T(top[i, 0], b10, top[i + 1, 0]);
        }

        T(top[0, 0], b00, b10);
        for (int j = 0; j < n; j++)
        {
            T(top[n, j], b11, top[n, j + 1]);
        }

        T(top[n, 0], b10, b11);
        for (int i = 0; i < n; i++)
        {
            T(top[i + 1, n], b01, top[i, n]);
        }

        T(top[n, n], b11, b01);
        for (int j = 0; j < n; j++)
        {
            T(top[0, j + 1], b00, top[0, j]);
        }

        T(top[0, n], b01, b00);
        return m;

        int Add(double x, double y, double z)
        {
            m.Points.Add(new V3(x, y, z));
            return m.Points.Count - 1;
        }

        void T(int a, int b, int c)
        {
            m.Indices.Add(a);
            m.Indices.Add(b);
            m.Indices.Add(c);
        }
    }
}
