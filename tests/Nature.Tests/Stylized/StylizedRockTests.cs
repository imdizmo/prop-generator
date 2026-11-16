using PropGenerator.Nature.Cliffs;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Stylized;
using PropGenerator.Nature.Textures;
using Xunit;

namespace PropGenerator.Nature.Tests.Stylized;

public class StylizedRockTests
{
    private static StylizedRockSettings Defaults => StylizedRockSettings.Defaults;

    private static void AssertClosed(SolidMesh mesh)
    {
        var edges = new Dictionary<(int, int), int>();
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                var edge = (mesh.Indices[t + k], mesh.Indices[t + ((k + 1) % 3)]);
                Assert.NotEqual(edge.Item1, edge.Item2);
                edges[edge] = edges.GetValueOrDefault(edge) + 1;
            }
        }

        foreach (var ((a, b), count) in edges)
        {
            Assert.Equal(1, count);
            Assert.True(edges.ContainsKey((b, a)), $"edge {a}-{b} has no opposite");
        }
    }

    [Fact]
    public void TheSphereIsClosedAndWoundOutward()
    {
        SolidMesh sphere = StylizedRockGenerator.Sphere(4);
        Assert.Equal(6 * 4 * 4 * 2, sphere.TriangleCount);
        Assert.Equal((6 * 4 * 4) + 2, sphere.Points.Count);
        AssertClosed(sphere);
        Assert.All(sphere.Points, p => Assert.Equal(1, p.Length, 9));
        for (int t = 0; t < sphere.Indices.Count; t += 3)
        {
            V3 a = sphere.Points[sphere.Indices[t]], b = sphere.Points[sphere.Indices[t + 1]], c = sphere.Points[sphere.Indices[t + 2]];
            Assert.True(V3.Dot(V3.Cross(b - a, c - a), a + b + c) > 0);
        }
    }

    [Fact]
    public void TheRockIsClosedSitsOnTheGroundAndFitsItsSize()
    {
        SolidMesh rock = StylizedRockGenerator.Solid(Defaults, 1);
        AssertClosed(rock);
        Assert.Equal(0, rock.Points.Min(p => p.Z), 9);
        double reach = Defaults.Size / 2 * (1 + (Defaults.Bumpiness / 2));
        Assert.All(rock.Points, p => Assert.True(Math.Abs(p.X) <= reach && Math.Abs(p.Y) <= reach, $"{p}"));
        Assert.InRange(rock.Points.Max(p => p.Z), Defaults.Size * Defaults.Height * 0.6, Defaults.Size * Defaults.Height * 1.3);

        // Thinned well below the sphere's 6912 triangles, but still a rock, not a crystal.
        Assert.InRange(rock.TriangleCount, 60, 1500);
    }

    [Fact]
    public void TheSameSeedGivesTheSameRockAndAnotherSeedAnother()
    {
        StylizedRock a = StylizedRockGenerator.Build(Defaults, 1);
        StylizedRock again = StylizedRockGenerator.Build(Defaults, 1);
        StylizedRock other = StylizedRockGenerator.Build(Defaults with { Seed = 2 }, 1);
        Assert.Equal(a.Positions, again.Positions);
        Assert.Equal(a.Colours, again.Colours);
        Assert.NotEqual(a.Positions, other.Positions);
        Assert.Equal(a.Positions.Length / 3, a.Indices.Length);
        Assert.All(a.Normals.Chunk(3), n => Assert.Equal(1, Math.Sqrt(n.Sum(x => (double)x * x)), 4));
    }

    [Fact]
    public void ALowerDetailLeavesFewerTriangles()
    {
        int full = StylizedRockGenerator.Solid(Defaults, 1).TriangleCount;
        int middle = StylizedRockGenerator.Solid(Defaults, 0.4).TriangleCount;
        int far = StylizedRockGenerator.Solid(Defaults, 0.12).TriangleCount;
        Assert.True(full > middle && middle > far, $"{full} {middle} {far}");
    }

    [Fact]
    public void EveryStartingPointBuildsAndExports()
    {
        foreach ((string id, _, StylizedRockSettings settings) in StylizedRockPresets.All)
        {
            ExportSource source = StylizedRockGenerator.Source(settings);
            var shape = Assert.Single(source.Collision);
            Assert.True(shape.End.Y > 0 && shape.R0 > 0);
            var files = Package.Build(source, new ExportSettings(id, "unreal", 3, Collision: true), new TextureCache());
            Assert.Contains(files, f => f.Name.EndsWith(".glb", StringComparison.Ordinal));
        }
    }
}
