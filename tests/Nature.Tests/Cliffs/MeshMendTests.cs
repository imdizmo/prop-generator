using PropGenerator.Nature.Cliffs;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

/// <summary>The mesh clean-up mends folds and empty triangles and keeps the mesh closed.</summary>
public class MeshMendTests
{
    [Fact]
    public void ThePlatesWithNoDetailHaveFewFolds()
    {
        // The Layered cliff's seed 4 with every Surface and Cracks slider at 0: its plates' joints and cuts folded 13
        // edges in 10,000 triangles (DC alone), past the round-8 block's 3.9. Mended it is within that budget, still
        // closed and every triangle facing out with area.
        var s = CliffMeshes.Preset("layered-cliff") with { Seed = 4 };
        var plain = CliffMeshes.Plain(s);
        var (_, mesh) = Assert.Single(CliffGenerator.Pieces(plain, CliffGenerator.Layout(s), 1));
        double folds = CliffMeshes.Folds(mesh, MeshMend.FoldBend) * mesh.Indices.Count / 2;
        Assert.True(folds <= 4.0 * mesh.TriangleCount / 10_000, $"{folds:F0} folds in {mesh.TriangleCount} triangles");
        Assert.Null(CliffMeshes.Open(mesh));
    }

    [Fact]
    public void NoTriangleIsEmpty()
    {
        // The Long cliff's seed 6 at full detail had one triangle of 9e-15 m², its corners in a line along the ground
        // line's crease (Ruling 31): a quad neither diagonal splits into two triangles with area is fanned round a point
        // of its own instead.
        var (_, mesh) = Assert.Single(CliffGenerator.Pieces(CliffMeshes.Preset("long-cliff") with { Seed = 6 }, 1));
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            Assert.True(CliffMeshes.Face(mesh.Points, mesh.Indices, t).Area >= MeshMend.EmptyArea, $"triangle {t / 3} is empty");
        }

        Assert.Null(CliffMeshes.Open(mesh));
    }

    [Fact]
    public void MendingMovesNoPointFarAndKeepsTheMeshClosed()
    {
        // The Cliff top's seed 3 meshed with and without the clean-up: the same triangle count, closed, and no point more
        // than one cell from where the mesher put it.
        var s = CliffMeshes.Preset("cliff-top") with { Seed = 3 };
        var layout = CliffGenerator.Layout(s);
        double cell = CliffGenerator.StartCell(s, 1, layout);
        var field = new SlabCliffField(layout, s, 1.5 * cell);
        var (min, max) = field.Bounds()!.Value;
        field.Prepare();
        var raw = DualContour.Mesh(field, min, max, cell);
        var mended = DualContour.Mesh(field, min, max, cell);
        int changes = MeshMend.Mend(mended, field, cell);
        Assert.True(changes > 0);
        Assert.Equal(raw.TriangleCount, mended.TriangleCount);
        Assert.Null(CliffMeshes.Open(mended));
        Assert.True(CliffMeshes.Folds(mended, MeshMend.FoldBend) < CliffMeshes.Folds(raw, MeshMend.FoldBend));
        for (int i = 0; i < raw.Points.Count; i++)
        {
            Assert.True((mended.Points[i] - raw.Points[i]).Length <= cell + 1e-9, $"point {i} moved {(mended.Points[i] - raw.Points[i]).Length:F3} m");
        }
    }
}
