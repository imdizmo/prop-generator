using SharpGLTF.Schema2;
using Xunit;

namespace PropGenerator.Tests;

public sealed class GlbExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "prop-generator", "scratch",
        "glb-" + Guid.NewGuid().ToString("N"));

    public GlbExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private static byte[] Picture(int size)
    {
        byte[] px = new byte[size * size * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = (byte)(i / 7); px[i + 1] = 90; px[i + 2] = 40; px[i + 3] = 255; }
        return px;
    }

    [Fact]
    public void ExportReadsBack()
    {
        var mesh = TileMesh.Triangle();
        string path = Path.Combine(_dir, "t.glb");
        GlbExport.Write(path, mesh, Picture(1024), 1024, "pg_x_floor_tri");
        var model = ModelRoot.Load(path);
        var prim = model.LogicalMeshes.Single().Primitives.Single();
        Assert.Equal(mesh.Positions.Length / 3, prim.GetVertexAccessor("POSITION").Count);
        Assert.Equal(mesh.Indices.Length, prim.GetIndexAccessor().Count);
        var image = model.LogicalImages.Single();
        Assert.True(image.Content.IsPng);
        byte[] bytes = image.Content.Content.ToArray();
        Assert.Equal(1024, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)));
        Assert.Equal(1024, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)));
        Assert.Equal("pg_x_floor_tri", model.LogicalNodes.Single().Name);
        Assert.Equal(0.9f, model.LogicalMaterials.Single().FindChannel("MetallicRoughness")!.Value.Parameters[1].Value);
        // Y up: the tile's top (z = T) becomes y = T.
        var pos = prim.GetVertexAccessor("POSITION").AsVector3Array();
        Assert.Equal(TileMesh.T, pos.Max(p => p.Y), 1e-5);
        Assert.Equal(0f, pos.Min(p => p.Y), 1e-5);
        Assert.Equal(-TileMesh.S / 2, pos.Min(p => p.Z), 1e-4);
    }
}
