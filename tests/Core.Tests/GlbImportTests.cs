using System.Numerics;
using SharpGLTF.Schema2;
using Xunit;

namespace PropGenerator.Tests;

public sealed class GlbImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "prop-generator", "scratch",
        "glbin-" + Guid.NewGuid().ToString("N"));

    public GlbImportTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string WriteSquare(string name = "sq.glb")
    {
        string path = Path.Combine(_dir, name);
        GlbExport.Write(path, TileMesh.Square(), new byte[4 * 4 * 4], 4, "sq");
        return path;
    }

    private static List<(Vector3, Vector3, Vector3)> Triangles(Mesh m) =>
        [.. Enumerable.Range(0, m.Indices.Length / 3).Select(t =>
        {
            Vector3 P(int k) => new(m.Positions[3 * m.Indices[(3 * t) + k]], m.Positions[(3 * m.Indices[(3 * t) + k]) + 1], m.Positions[(3 * m.Indices[(3 * t) + k]) + 2]);
            return (P(0), P(1), P(2));
        })];

    [Fact]
    public void ReadingGivesBackTheMeshInTheZUpFrame()
    {
        Mesh original = TileMesh.Square();
        GlbImport.Result r = GlbImport.Read(WriteSquare());

        var a = Triangles(original);
        var b = Triangles(r.Mesh);
        Assert.Equal(a.Count, b.Count);
        for (int t = 0; t < a.Count; t++)
        {
            Assert.True(Vector3.Distance(a[t].Item1, b[t].Item1) < 1e-5f, $"triangle {t}");
            Assert.True(Vector3.Distance(a[t].Item2, b[t].Item2) < 1e-5f);
            Assert.True(Vector3.Distance(a[t].Item3, b[t].Item3) < 1e-5f);
        }

        Assert.Equal(r.Mesh.Positions.Length / 3 * 2, r.Mesh.Uvs.Length);
        Assert.Equal(original.Uvs[1], r.Mesh.Uvs[1], 5);
    }

    private string WriteWithSecondUvSet(string name, float[] u, float v = 1f)
    {
        string path = Path.Combine(_dir, name);
        var model = ModelRoot.CreateModel();
        var prim = model.CreateMesh("m").CreatePrimitive();
        prim.WithVertexAccessor("POSITION", new Vector3[] { new(0, 0, 0), new(1, 0, 0), new(0, 0, -1), new(2, 0, 0), new(3, 0, 0), new(2, 0, -1) });
        prim.WithVertexAccessor("TEXCOORD_1", u.Select(x => new Vector2(x, v)).ToArray());
        prim.WithIndicesAccessor(PrimitiveType.TRIANGLES, [0, 1, 2, 3, 4, 5]);
        model.UseScene("s").CreateNode("n").WithMesh(model.LogicalMeshes[0]);
        model.SaveGLB(path);
        return path;
    }

    [Fact]
    public void FaceNumbersComeFromTheSecondUvSet()
    {
        GlbImport.Result r = GlbImport.Read(WriteWithSecondUvSet("uv.glb", [4, 4, 4, 9, 9, 9]));

        Assert.Equal([4, 9], r.Mesh.FaceIds);
        Assert.Equal([4, 9], r.Piece.Faces.Select(f => f.FaceId));
        Assert.All(r.Piece.Faces, f => Assert.Equal("unmarked", f.Kind));
        Assert.Null(r.Warning);
        // glTF (1, 0, 0) and (0, 0, -1) are (1, 0, 0) and (0, 1, 0) in the Z-up frame.
        Assert.Equal(new Vector3(0, 1, 0), new Vector3(r.Mesh.Positions[6], r.Mesh.Positions[7], r.Mesh.Positions[8]));
    }

    [Fact]
    public void ACleanFaceIdAttributeIsReadToo()
    {
        string path = Path.Combine(_dir, "attr.glb");
        var model = ModelRoot.CreateModel();
        var prim = model.CreateMesh("m").CreatePrimitive();
        prim.WithVertexAccessor("POSITION", new Vector3[] { new(0, 0, 0), new(1, 0, 0), new(0, 0, -1), new(2, 0, 0), new(3, 0, 0), new(2, 0, -1) });
        prim.WithVertexAccessor("_PG_FACE_ID", new float[] { 4, 4, 4, 9, 9, 9 });
        prim.WithIndicesAccessor(PrimitiveType.TRIANGLES, [0, 1, 2, 3, 4, 5]);
        model.UseScene("s").CreateNode("n").WithMesh(model.LogicalMeshes[0]);
        model.SaveGLB(path);

        Assert.Equal([4, 9], GlbImport.Read(path).Mesh.FaceIds);
    }

    [Fact]
    public void BlendedFaceNumbersAreNotTrustedEachTriangleBecomesAFaceWithAWarning()
    {
        GlbImport.Result r = GlbImport.Read(WriteWithSecondUvSet("blend.glb", [4, 5, 4, 9, 9, 9]));

        Assert.NotNull(r.Warning);
        Assert.Equal([0, 1], r.Mesh.FaceIds);
    }

    [Fact]
    public void ASecondUvSetThatIsNotFaceNumbersIsNotTakenForThem()
    {
        GlbImport.Result r = GlbImport.Read(WriteWithSecondUvSet("lightmap.glb", [4, 4, 4, 9, 9, 9], 0.25f));

        Assert.NotNull(r.Warning);
    }

    [Fact]
    public void WithoutFaceNumbersEachTriangleIsItsOwnFaceAndTheWarningSaysSo()
    {
        GlbImport.Result r = GlbImport.Read(WriteSquare());

        Assert.NotNull(r.Warning);
        Assert.Equal(Enumerable.Range(0, r.Mesh.Indices.Length / 3), r.Mesh.FaceIds);
    }

    [Fact]
    public void AFileThatIsNotAGlbIsAnInvalidDataError()
    {
        string path = Path.Combine(_dir, "bad.glb");
        File.WriteAllText(path, "this is not a model");

        InvalidDataException e = Assert.Throws<InvalidDataException>(() => GlbImport.Read(path));

        Assert.Contains("bad.glb", e.Message);
    }
}
