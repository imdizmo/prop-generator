using System.Buffers.Binary;
using System.Text.Json;
using PropGenerator.Nature.Cliffs;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Stylized;
using PropGenerator.Nature.Tests.Support;
using PropGenerator.Nature.Textures;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

public class CliffExportTests
{
    private static CliffSettings Long => CliffPresets.All.Single(p => p.Id == "long-cliff").Settings;

    private static IReadOnlyList<ExportFile> Export() =>
        Package.Build(CliffExport.Source(Long), new ExportSettings("cliff", "unreal", 3, Collision: true), new TextureCache());

    [Fact]
    public void ExportWritesThreeLevelsWithColours()
    {
        var files = Export();
        Assert.Equal(3, files.Count(f => f.Name.Contains("_LOD", StringComparison.Ordinal)));
        foreach (var lod in files.Where(f => f.Name.Contains("_LOD", StringComparison.Ordinal)))
        {
            using var glb = GlbFile.Parse(lod.Bytes);
            var meshes = glb.Json.RootElement.GetProperty("meshes");
            Assert.Equal(1, meshes.GetArrayLength());
            Assert.All(meshes[0].GetProperty("primitives").EnumerateArray(), p => Assert.True(p.GetProperty("attributes").TryGetProperty("COLOR_0", out _)));
        }

        // A collision shape for every part the cliff is built from: every corner of every block, in the export's Y-up
        // space, is inside one of the source's upright boxes (their ring's corners round the axis from Start to End).
        using var collision = GlbFile.Parse(files.Single(f => f.Name == "cliff_collision.glb").Bytes);
        Assert.True(collision.Json.RootElement.GetProperty("nodes").GetArrayLength() >= 1);
        var shapes = CliffExport.Source(Long).Collision;
        foreach (var corner in CliffGenerator.Layout(Long).SelectMany(b => b.Block.Corners()))
        {
            var p = new Vec3d(corner.X, corner.Z, -corner.Y);
            Assert.Contains(shapes, c =>
                p.Y >= c.Start.Y - 1e-6 && p.Y <= c.End.Y + 1e-6
                && Math.Abs(p.X - c.Start.X) <= c.Ring!.Max(r => r.X) + 1e-6
                && Math.Abs(p.Z - c.Start.Z) <= c.Ring!.Max(r => r.Z) + 1e-6);
        }
    }

    [Fact]
    public void TheGlbHasThreeLevelsAndCollision()
    {
        var files = Export();
        Assert.Equal(["cliff_LOD0.glb", "cliff_LOD1.glb", "cliff_LOD2.glb"], files.Where(f => f.Name.Contains("_LOD", StringComparison.Ordinal)).Select(f => f.Name));

        // One upright box per piece of each block, more than a rock's 8, all kept: none is left without collision.
        var chunks = CliffGenerator.Build(Long, 1).Chunks;
        Assert.InRange(chunks.Count, Collision.MaxShapes + 1, CliffExport.MaxCollisionShapes);
        using var collision = GlbFile.Parse(files.Single(f => f.Name == "cliff_collision.glb").Bytes);
        Assert.Equal(chunks.Count, collision.Json.RootElement.GetProperty("nodes").GetArrayLength());

        // The pivot is the ground line, read off the files: no node is moved, the mesh spans the built cliff's own
        // heights (its flat bottom Sink below z = 0, not lifted), and the shapes reach from the blocks' lowest bottom to
        // their highest top, grown by how far the smudge pushes the rock out.
        using var lod0 = GlbFile.Parse(files.Single(f => f.Name == "cliff_LOD0.glb").Bytes);
        foreach (var glb in new[] { lod0, collision })
        {
            foreach (var node in glb.Json.RootElement.GetProperty("nodes").EnumerateArray())
            {
                Assert.True(!node.TryGetProperty("translation", out var t) || t.EnumerateArray().All(v => v.GetDouble() == 0), "a node is moved");
                Assert.False(node.TryGetProperty("matrix", out _), "a node has a matrix");
            }
        }

        float[] built = CliffGenerator.Build(Long, 1).Positions;
        var (meshLow, meshHigh) = Heights(lod0);
        Assert.Equal(built.Where((_, i) => i % 3 == 2).Min(), meshLow, 1e-4);
        Assert.Equal(built.Where((_, i) => i % 3 == 2).Max(), meshHigh, 1e-4);
        Assert.True(meshLow < -0.4, $"the bottom is at {meshLow}");
        var layout = CliffGenerator.Layout(Long);
        var (shapeLow, shapeHigh) = Heights(collision);
        Assert.Equal(layout.Min(b => b.Block.Bounds().Min.Z), shapeLow, 1e-4);
        Assert.Equal(layout.Max(b => b.Block.Bounds().Max.Z) + CliffCollision.Reach(Long), shapeHigh, 1e-4);

        // The description: the slab cliff, version 6.
        using var asset = JsonDocument.Parse(files.Single(f => f.Name == "cliff.asset.json").Bytes);
        Assert.Equal("slab-cliff", asset.RootElement.GetProperty("generator").GetString());
        Assert.Equal(6, asset.RootElement.GetProperty("generatorVersion").GetInt32());
    }

    [Theory]
    [InlineData("pg_untitled_cliff", "cliff")]
    [InlineData("granite_wall", "cliff")]
    [InlineData("pg_untitled_rock", "rock")]
    [InlineData("cliff", "cliff")]
    public void TheNamesInTheGlbSayWhatThePropIs(string name, string what)
    {
        // A cliff's or stylized rock's mesh, node and material are named for it, never a tree's "wood" (Merge's default): the
        // name as given where it ends so already or is just that word (no "cliff_cliff"), else with _cliff or _rock
        // added.
        var source = what == "cliff"
            ? CliffExport.Source(Long)
            : StylizedRockGenerator.Source(StylizedRockSettings.Defaults, "test");
        var file = Package.Build(source, new ExportSettings(name, "godot", 1, Collision: false), new TextureCache())
            .Single(f => f.Name == $"{name}.glb");
        using var glb = GlbFile.Parse(file.Bytes);
        var root = glb.Json.RootElement;
        string want = name == what || name.EndsWith($"_{what}", StringComparison.Ordinal) ? name : $"{name}_{what}";
        foreach (string group in new[] { "nodes", "meshes", "materials" })
        {
            var names = root.GetProperty(group).EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToList();
            Assert.Equal([want], names);
        }
    }

    /// <summary>The lowest and highest y (up) of every mesh in a GLB, from its position accessors' bounds.</summary>
    private static (double Low, double High) Heights(GlbFile glb)
    {
        var root = glb.Json.RootElement;
        double low = double.MaxValue;
        double high = double.MinValue;
        foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
        {
            foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                var accessor = root.GetProperty("accessors")[primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                low = Math.Min(low, accessor.GetProperty("min")[1].GetDouble());
                high = Math.Max(high, accessor.GetProperty("max")[1].GetDouble());
            }
        }

        return (low, high);
    }

    [Fact]
    public void DescriptionParametersAreTheParameterSetKeysAndTheSeed()
    {
        var description = CliffExport.Source(Long, "9.9").Description;
        var parameters = Assert.IsType<Dictionary<string, object?>>(description.Parameters);
        string[] expected = [.. CliffParameterSet.Instance.Controls.Select(c => c.Key), "seed"];
        Assert.Equal(expected.Order(StringComparer.Ordinal), parameters.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("9.9", description.AppVersion);
        Assert.Equal("slab-cliff", description.Generator);
        Assert.Equal(6, description.GeneratorVersion);
        Assert.Equal(6, CliffExport.Version);
    }

    [Fact]
    public void ExportIsYUpWithItsPivotOnTheGroundLine()
    {
        var lod0 = Export().Single(f => f.Name == "cliff_LOD0.glb");
        using var glb = GlbFile.Parse(lod0.Bytes);
        var root = glb.Json.RootElement;
        int at = root.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes").GetProperty("POSITION").GetInt32();
        var accessor = root.GetProperty("accessors")[at];

        // The built cliff's own top and bottom, turned Y-up: the bottom is Sink below the ground line, not lifted to it.
        float[] built = CliffGenerator.Build(Long, 1).Positions;
        double top = built.Where((_, i) => i % 3 == 2).Max();
        double bottom = built.Where((_, i) => i % 3 == 2).Min();
        Assert.True(bottom < -0.4, $"the bottom is at {bottom}");
        Assert.Equal(top, accessor.GetProperty("max")[1].GetDouble(), 1e-4);
        Assert.Equal(bottom, accessor.GetProperty("min")[1].GetDouble(), 1e-4);
        byte[] bytes = glb.AccessorBytes(accessor);
        double lowest = double.MaxValue;
        for (int i = 4; i < bytes.Length; i += 12)
        {
            lowest = Math.Min(lowest, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i)));
        }

        Assert.Equal(bottom, lowest, 1e-5);
    }

    [Fact]
    public void ThePresetsAreTheSpecsFour()
    {
        Assert.Equal(["long-cliff", "tall-cliff", "cliff-top", "layered-cliff"], CliffPresets.All.Select(p => p.Id));
        Assert.Equal(["Long cliff", "Tall cliff", "Cliff top", "Layered cliff"], CliffPresets.All.Select(p => p.Name));
        Assert.Equal(CliffPresets.All.Select(p => (p.Id, p.Name)), CliffPresets.Values.Select(p => (p.Id, p.Name)));
        (double W, double H, double D, int Blocks, int Tiers, int Layers, string Palette, uint Seed)[] expected =
        [
            (28, 9, 6, 5, 1, 0, "granite", 1),
            (6.5, 13, 3.5, 2, 1, 0, "slate", 2),
            (14, 5, 8, 3, 3, 0, "granite", 3),
            (10, 7, 6, 2, 1, 7, "lavender", 4),
        ];
        Assert.Equal(
            expected,
            CliffPresets.All.Select(p => p.Settings).Select(s => (s.Width, s.Height, s.Depth, s.Blocks, s.Tiers, s.Layers, s.Palette, s.Seed)));
        Assert.Equal(CliffSettings.Defaults, Long);
    }

    [Fact]
    public void NoExportedTriangleIsEmpty()
    {
        var part = CliffExport.Source(Long).BuildAtDetail(1).Single().Mesh;
        float[] p = part.Position;
        uint[] index = part.Index!;
        for (int t = 0; t < index.Length; t += 3)
        {
            double[] a = [p[3 * index[t]], p[(3 * index[t]) + 1], p[(3 * index[t]) + 2]];
            double[] b = [p[3 * index[t + 1]] - a[0], p[(3 * index[t + 1]) + 1] - a[1], p[(3 * index[t + 1]) + 2] - a[2]];
            double[] c = [p[3 * index[t + 2]] - a[0], p[(3 * index[t + 2]) + 1] - a[1], p[(3 * index[t + 2]) + 2] - a[2]];
            double x = (b[1] * c[2]) - (b[2] * c[1]), y = (b[2] * c[0]) - (b[0] * c[2]), z = (b[0] * c[1]) - (b[1] * c[0]);
            Assert.True(0.5 * Math.Sqrt((x * x) + (y * y) + (z * z)) >= 1e-12);
        }
    }
}
