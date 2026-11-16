using Xunit;

namespace PropGenerator.Tests;

public sealed class TileMeshTests
{
    private static IEnumerable<(float X, float Y, float Z, float U, float V)> Verts(Mesh m)
    {
        for (int i = 0; i < m.Positions.Length / 3; i++)
        {
            yield return (m.Positions[3 * i], m.Positions[3 * i + 1], m.Positions[3 * i + 2], m.Uvs[2 * i], m.Uvs[2 * i + 1]);
        }
    }

    private static List<(float X, float Y, float Z, float U, float V)> TopVerts(Mesh m, int faceId)
    {
        var verts = Verts(m).ToList();
        return [.. Enumerable.Range(0, m.FaceIds.Length).Where(t => m.FaceIds[t] == faceId)
            .SelectMany(t => m.Indices.Skip(3 * t).Take(3)).Distinct().Select(i => verts[i])];
    }

    [Fact]
    public void TriangleTurnedHalfAroundMapsTexelCToMirror()
    {
        var tops = TopVerts(TileMesh.Triangle(), 101);
        Assert.Equal(3, tops.Count);
        var square = TopVerts(TileMesh.Square(), 1);
        foreach (var (X, Y, Z, U, V) in tops.Concat(square))
        {
            foreach (var b in tops.Concat(square))
            {
                if (Math.Abs(X + b.X) > 1e-6 || Math.Abs(Y + b.Y) > 1e-6)
                {
                    continue;
                }

                double ca = U * 1024 - 0.5, cb = b.U * 1024 - 0.5;
                double sum = ((ca + cb) % 1024 + 1024) % 1024;
                Assert.True(sum < 1e-3 || 1024 - sum < 1e-3, $"u {U} {b.U}");
                Assert.Equal(2 * (224 + 400 + 0.5) / 1024.0, V + b.V, 1e-6);
            }
        }
    }

    [Fact]
    public void SquareTopUvAtCentre()
    {
        // The square's vertices are at the corners, so check the formula's centre through the corner pair average.
        var tops = TopVerts(TileMesh.Square(), 1);
        double u = tops.Average(v => v.U), vv = tops.Average(v => v.V);
        Assert.Equal((224 + 400 + 0.5) / 1024.0, vv, 1e-6);
        Assert.Equal(0.5 + TileMesh.H, u, 1e-6);
    }

    [Fact]
    public void FacesAndIds()
    {
        var sq = TileMesh.Square();
        Assert.Equal(6, sq.FaceIds.Distinct().Count());
        Assert.Equal(12, sq.Indices.Length / 3);
        var tri = TileMesh.Triangle();
        Assert.Equal([100, 101, 102, 103, 104], [.. tri.FaceIds.Distinct().OrderBy(x => x)]);
        var piece = TileMesh.FloorPiece();
        Assert.Equal("floor_tile", piece.Recipe);
        string Kind(int id) => piece.Faces.Single(f => f.FaceId == id).Kind;
        Assert.Equal(["plank_top", "plank_top", "cut_end", "plank_side", "cut_end", "plank_side"],
            [.. Enumerable.Range(0, 6).Select(Kind)]);
        Assert.Equal(["plank_top", "plank_top", "cut_end", "cut_end", "plank_side"],
            [.. Enumerable.Range(100, 5).Select(Kind)]);
    }

    [Fact]
    public void SideUvsFollowTheAtlasRows()
    {
        var sq = TileMesh.Square();
        // Face 3 is the +x side, face 5 the -x side: rows 64..128 and 0..64.
        foreach (var (face, lo, hi) in new[] { (3, 64.0, 127.0), (5, 0.0, 63.0), (2, 128.0, 223.0) })
        {
            var vs = Enumerable.Range(0, sq.FaceIds.Length).Where(t => sq.FaceIds[t] == face)
                .SelectMany(t => sq.Indices.Skip(3 * t).Take(3)).Distinct().Select(i => sq.Uvs[2 * i + 1] * 1024.0).ToList();
            Assert.Equal(lo, vs.Min(), 1e-3);
            Assert.Equal(hi, vs.Max(), 1e-3);
        }
    }
}
