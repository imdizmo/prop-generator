using PropGenerator.Nature.Export;
using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Tests.Support;
using PropGenerator.Nature.Textures;
using SharpGLTF.Schema2;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class GlbWriterTests
{
    [Fact]
    public void AMeshRoundTripsThroughSharpGltf()
    {
        var mesh = Triangle(wide: false);
        byte[] glb = GlbWriter.Write([GlbNode.Of(Merged("p_wood", mesh, "white"))], "p", new TextureCache());
        var model = ModelRoot.ParseGLB(glb);
        var node = Assert.Single(model.LogicalNodes);
        Assert.Equal("p_wood", node.Name);
        Assert.Equal("p_wood", node.Mesh.Name);
        var primitive = Assert.Single(node.Mesh.Primitives);
        Assert.Equal(mesh.Position, primitive.GetVertexAccessor("POSITION").AsVector3Array().SelectMany(v => new[] { v.X, v.Y, v.Z }));
        Assert.Equal(mesh.Uv, primitive.GetVertexAccessor("TEXCOORD_0").AsVector2Array().SelectMany(v => new[] { v.X, v.Y }));
        Assert.Equal(mesh.Index!, primitive.GetIndices());
        Assert.Equal("p_wood", primitive.Material.Name);
        Assert.Equal("white", Assert.Single(model.LogicalTextures).Name);
    }

    [Fact]
    public void NormalsOffUnitLengthAreNormalisedAsGltfExporterDoes()
    {
        var mesh = Triangle(false);
        mesh.Normal = [0, 0, 0, 0, 2, 0, 3, 0, 4];
        float[] written = WrittenNormals(mesh);
        Assert.Equal([1, 0, 0, 0, 1, 0, (float)(3 * (1 / 5.0)), 0, (float)(4 * (1 / 5.0))], written);
    }

    [Fact]
    public void NormalsWithinTheValidatorsToleranceAreWrittenAsTheyAre()
    {
        var mesh = Triangle(false);
        mesh.Normal = [0, 0, 1.0004f, 0, 1, 0, 0, 0.9996f, 0];
        Assert.Equal(mesh.Normal, WrittenNormals(mesh));
    }

    [Theory]
    [InlineData(false, 5123)]
    [InlineData(true, 5125)]
    public void TheIndexKeepsItsWidth(bool wide, int componentType)
    {
        using var file = GlbFile.Parse(GlbWriter.Write([GlbNode.Of(Merged("p_wood", Triangle(wide), "white"))], "p", new TextureCache()));
        var root = file.Json.RootElement;
        int indices = root.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("indices").GetInt32();
        var accessor = root.GetProperty("accessors")[indices];
        Assert.Equal(componentType, accessor.GetProperty("componentType").GetInt32());
        Assert.Equal([0.0], accessor.GetProperty("min").EnumerateArray().Select(v => v.GetDouble()));
        Assert.Equal([2.0], accessor.GetProperty("max").EnumerateArray().Select(v => v.GetDouble()));
    }

    [Fact]
    public void AMaterialSharedByTwoNodesIsWrittenOnce()
    {
        var wood = Merged("p", Triangle(false), "white");
        var shape = GlbNode.Shape("UCX_p_00", Collision.Prism(new CollisionShape(new Vec3d(0, 0, 0), new Vec3d(0, 1, 0), 0.2, 0.1), new Vec3d(0, 0, 0)), wood.Material);
        using var file = GlbFile.Parse(GlbWriter.Write([GlbNode.Of(wood), shape], "p", new TextureCache()));
        var root = file.Json.RootElement;
        Assert.Single(root.GetProperty("materials").EnumerateArray());
        Assert.Equal([0, 0], root.GetProperty("meshes").EnumerateArray().Select(m => m.GetProperty("primitives")[0].GetProperty("material").GetInt32()));
        Assert.Equal(["POSITION", "NORMAL"], root.GetProperty("meshes")[1].GetProperty("primitives")[0].GetProperty("attributes").EnumerateObject().Select(a => a.Name));
    }

    [Fact]
    public void ACollisionFilesOwnMaterialIsPlain()
    {
        var material = new ShapeMaterial("p_collision");
        var prism = Collision.Prism(new CollisionShape(new Vec3d(0, 0, 0), new Vec3d(0, 1, 0), 0.2, 0.1), new Vec3d(0, 0, 0));
        using var file = GlbFile.Parse(GlbWriter.Write([GlbNode.Shape("p_collision_0", prism, material), GlbNode.Shape("p_collision_1", prism.Clone(), material)], "p_collision", new TextureCache()));
        var root = file.Json.RootElement;
        var only = Assert.Single(root.GetProperty("materials").EnumerateArray());
        Assert.Equal("p_collision", only.GetProperty("name").GetString());
        var pbr = only.GetProperty("pbrMetallicRoughness");
        Assert.Equal(0, pbr.GetProperty("metallicFactor").GetDouble());
        Assert.False(pbr.TryGetProperty("baseColorTexture", out _));
        Assert.False(root.TryGetProperty("textures", out _));
        Assert.False(root.TryGetProperty("images", out _));
    }

    [Fact]
    public void AJoinedMeshHasAPrimitivePerGroupSharingItsAttributes()
    {
        var a = Merged("p_wood", Triangle(false), "white");
        var b = Merged("p_leaves", Triangle(false), "white", alphaTest: 0.5);
        var joined = Assert.Single(Merge.Join([a, b], "p"));
        using var file = GlbFile.Parse(GlbWriter.Write([GlbNode.Of(joined)], "p", new TextureCache()));
        var root = file.Json.RootElement;
        var primitives = root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray().ToList();
        Assert.Equal(2, primitives.Count);
        Assert.Equal(primitives[0].GetProperty("attributes").GetRawText(), primitives[1].GetProperty("attributes").GetRawText());
        Assert.Equal([0, 1], primitives.Select(p => p.GetProperty("material").GetInt32()));
        var second = root.GetProperty("accessors")[primitives[1].GetProperty("indices").GetInt32()];
        Assert.Equal(3, second.GetProperty("count").GetInt32());
        Assert.Equal([3.0], second.GetProperty("min").EnumerateArray().Select(v => v.GetDouble()));
        Assert.Equal([5.0], second.GetProperty("max").EnumerateArray().Select(v => v.GetDouble()));
    }

    [Fact]
    public void OnlyTheWhiteTextureIsKnown()
    {
        // easy-props' kinds (leaf, dab, bark and relief maps) write their GLBs on the engine; a key here is "white" or refused.
        var leaves = Merged("p_leaves", Triangle(false), "leaf");
        Assert.Contains("'leaf'", Assert.Throws<ArgumentException>(() => GlbWriter.Write([GlbNode.Of(leaves)], "p", new TextureCache())).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABoundThatWouldHoldNanIsLeftOutAndMinusZeroIsWrittenAsZero()
    {
        var mesh = Triangle(false);
        mesh.Position[0] = float.NaN;
        mesh.Uv = [-0f, -0f, 1, -0f, -0f, 1];
        using var file = GlbFile.Parse(GlbWriter.Write([GlbNode.Of(Merged("p_wood", mesh, "white"))], "p", new TextureCache()));
        var root = file.Json.RootElement;
        var attributes = root.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes");
        var position = root.GetProperty("accessors")[attributes.GetProperty("POSITION").GetInt32()];
        Assert.False(position.TryGetProperty("min", out _));
        Assert.False(position.TryGetProperty("max", out _));
        var uv = root.GetProperty("accessors")[attributes.GetProperty("TEXCOORD_0").GetInt32()];
        Assert.Equal(["0", "0"], uv.GetProperty("min").EnumerateArray().Select(v => v.GetRawText()));
    }

    private static float[] WrittenNormals(PropMesh mesh)
    {
        using var file = GlbFile.Parse(GlbWriter.Write([GlbNode.Of(Merged("p_wood", mesh, "white"))], "p", new TextureCache()));
        var root = file.Json.RootElement;
        int normal = root.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes").GetProperty("NORMAL").GetInt32();
        byte[] bytes = file.AccessorBytes(root.GetProperty("accessors")[normal]);
        return [.. System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes)];
    }

    private static MergedMesh Merged(string name, PropMesh mesh, string map, string? relief = null, bool full = true, double alphaTest = 0) =>
        new(name, mesh, new MergedMaterial(name, map == "white" ? 0.85 : 1, map, relief, full, alphaTest), mesh.Index!.Length / 3);

    /// <summary>One triangle with every attribute a merged mesh has but wind, in the order the merge leaves them.</summary>
    private static PropMesh Triangle(bool wide)
    {
        var mesh = new PropMesh
        {
            Position = [0, 0, 0, 1, 0, -0.5f, 0, 1, 0.25f],
            Color = [1, 0.5f, 0.25f, 1, 1, 1, 0, 0, 0],
            Normal = [0, 0, 1, 0, 0, 1, 0, 0, 1],
            Uv = [0, 0, 1, 0, 0, 1],
        };
        mesh.SetIndex([0, 1, 2], wide);
        return mesh;
    }
}
