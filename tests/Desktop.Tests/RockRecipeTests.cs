using System.Diagnostics;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Nature.Export;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>The cut rock in the app, from easy-props' engine: its settings, starting points, levels, builds and files.</summary>
[Collection(TimedAlone.Name)]
public class RockRecipeTests(ITestOutputHelper output)
{
    [Fact]
    public void SpecsAreTheRockSettingsThenSeed()
    {
        var specs = RockRecipe.Specs;
        Assert.Equal(
            ["build", "kind", "width", "height", "depth", "chunks", "faces", "cuts", "layers", "overhang", "dents", "rubble",
             "edges", "rounded", "blend", "bend", "variation", "grain", "lichen", "palette",
             "stoneShadow", "stoneDark", "stoneLight", "stoneHighlight", "snow", "moss", "mossFrom", "wet", "cracks", "crumble", "seed"],
            specs.Select(s => s.Key));
        Assert.Equal("int", specs.Single(s => s.Key == "chunks").Type);
        foreach (string k in new[] { "build", "kind", "palette" })
        {
            var s = specs.Single(x => x.Key == k);
            Assert.Equal("choice", s.Type);
            Assert.True(s.Options!.Count >= 2);
        }

        Assert.Contains(specs.Single(s => s.Key == "palette").Options!, o => o.Value == "custom" && o.Label == "My own colours");
        Assert.All(specs.Where(s => s.Key.StartsWith("stone", StringComparison.Ordinal)), s =>
        {
            Assert.Equal("colour", s.Type);
            Assert.Equal(8, s.Swatches!.Count);
        });
        Assert.Equal("seed", specs[^1].Type);
        Assert.Equal(
            [.. Enumerable.Repeat("Shape", 12), .. Enumerable.Repeat("Surface", 12), .. Enumerable.Repeat("Weather", 4), .. Enumerable.Repeat("Damage", 2), "Surface"],
            specs.Select(s => s.Group));
        Assert.Equal("greystone", specs.Single(s => s.Key == "palette").Default.GetString());
        Assert.All(specs, s => Assert.Equal("paint", s.Kind));
    }

    [Fact]
    public void EveryTooltipIsOursAndLongerThanEasyPropsHelpButTheWeathers()
    {
        var help = EngineHost.Default.Catalog.Parameters(RockRecipe.Id).Specs.ToDictionary(
            s => s.GetProperty("key").GetString()!,
            s => s.TryGetProperty("help", out System.Text.Json.JsonElement h) ? h.GetString() ?? "" : "");

        // Snow, wet and where moss grows read upstream's own help: the app has nothing to add to them.
        string[] upstreams = ["snow", "wet", "mossFrom"];
        foreach (string key in upstreams)
        {
            Assert.Null(RockTooltips.TryFor(key));
            Assert.Equal(help[key], RockRecipe.Specs.Single(s => s.Key == key).Tooltip);
        }

        // Moss can grow on one side now: the tip no longer says it grows only on faces that look up at the sky.
        Assert.DoesNotContain("faces that look up at the sky", RockTooltips.For("moss"), StringComparison.Ordinal);
        Assert.Contains("Greystone is the default", RockTooltips.For("palette"), StringComparison.Ordinal);
        foreach (var spec in RockRecipe.Specs.Where(s => !upstreams.Contains(s.Key)))
        {
            string tip = RockTooltips.For(spec.Key);
            Assert.Equal(tip, spec.Tooltip);
            Assert.EndsWith(".", tip, StringComparison.Ordinal);
            string theirs = help.GetValueOrDefault(spec.Key, "");
            Assert.True(tip.Length > theirs.Length, spec.Key);
        }
    }

    [Fact]
    public void TwentyStartingPointsInFourGroups()
    {
        var pts = RockRecipe.StartingPoints;
        Assert.Equal(20, pts.Count);
        Assert.Equal(EngineHost.Default.Catalog.Presets.Presets.Where(p => p.GeneratorId == "cut-rock").Select(p => p.Id), pts.Select(p => p.Id));
        Assert.Equal(
            new[] { ("Boulders", 6), ("Outcrops", 3), ("Stones", 8), ("Cliffs", 3) },
            pts.GroupBy(p => p.Group).Select(g => (g.Key, g.Count())).ToArray());
        Assert.Contains(pts, p => p.Name.Contains("Boulder", StringComparison.Ordinal));
        Assert.Contains(pts, p => p.Name == "Sea stack");
        Assert.All(pts, p => Assert.Equal("cut-rock", p.Recipe));
    }

    [Fact]
    public void LodBuildsAtTheExportShares()
    {
        Assert.Equal([1, 0.4, 0.12], RockRecipe.DetailShares);
        var boulder = RockRecipe.StartingPoints.Single(p => p.Id == "boulder").Settings;
        int lod0 = RockRecipe.Build(boulder).Indices.Length / 3;
        Assert.Equal(lod0, RockRecipe.Build(boulder, 1).Indices.Length / 3);
        int lod1 = RockRecipe.Build(boulder, RockRecipe.DetailShares[1]).Indices.Length / 3;
        int lod2 = RockRecipe.Build(boulder, RockRecipe.DetailShares[2]).Indices.Length / 3;
        output.WriteLine($"boulder faces: {lod0}, {lod1}, {lod2}");
        Assert.True(lod1 < lod0, $"{lod1} < {lod0}");
        Assert.True(lod2 < lod1, $"{lod2} < {lod1}");
        Assert.True(lod2 > 0);
    }

    public static TheoryData<string> PointIds()
    {
        var d = new TheoryData<string>();
        foreach (var p in RockRecipe.StartingPoints)
        {
            d.Add(p.Id);
        }

        return d;
    }

    [Theory]
    [MemberData(nameof(PointIds))]
    public void EveryStartingPointBuildsAStandingRock(string id)
    {
        var p = RockRecipe.StartingPoints.Single(x => x.Id == id);
        var m = RockRecipe.Build(p.Settings);
        Assert.Equal(0, m.Indices.Length % 3);
        Assert.NotEmpty(m.Indices);
        Assert.Equal(m.Positions.Length, m.Normals.Length);
        Assert.Equal(m.Positions.Length, m.Colours.Length);
        Assert.All(m.Positions, v => Assert.True(float.IsFinite(v)));
        Assert.All(m.Normals, v => Assert.True(float.IsFinite(v)));
        Assert.All(m.Colours, v => Assert.InRange(v, 0f, 1f));
        Assert.All(m.Indices, i => Assert.InRange(i, 0, (m.Positions.Length / 3) - 1));
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 2; i < m.Positions.Length; i += 3)
        {
            minZ = Math.Min(minZ, m.Positions[i]);
            maxZ = Math.Max(maxZ, m.Positions[i]);
        }

        Assert.True(minZ <= 0.05f, $"min Z {minZ}");
        float want = p.Settings.GetFloat("height");
        Assert.InRange(maxZ - minZ, want * 0.75f, want * 1.25f);
    }

    [Fact]
    public void WindingMatchesNormals()
    {
        var m = RockRecipe.Build(RockRecipe.StartingPoints[0].Settings);
        int agree = 0, total = 0;
        for (int t = 0; t < m.Indices.Length; t += 3)
        {
            int a = m.Indices[t] * 3, b = m.Indices[t + 1] * 3, c = m.Indices[t + 2] * 3;
            float ux = m.Positions[b] - m.Positions[a], uy = m.Positions[b + 1] - m.Positions[a + 1], uz = m.Positions[b + 2] - m.Positions[a + 2];
            float vx = m.Positions[c] - m.Positions[a], vy = m.Positions[c + 1] - m.Positions[a + 1], vz = m.Positions[c + 2] - m.Positions[a + 2];
            float fx = (uy * vz) - (uz * vy), fy = (uz * vx) - (ux * vz), fz = (ux * vy) - (uy * vx);
            float dot = (fx * m.Normals[a]) + (fy * m.Normals[a + 1]) + (fz * m.Normals[a + 2]);
            total++;
            if (dot >= 0)
            {
                agree++;
            }
        }

        Assert.True(agree > total * 0.95, $"{agree}/{total}");
    }

    // Each preset is built three times and the fastest counts: a build slowed once by the machine's other work (a
    // collection, another process) says nothing about the build itself.
    [Fact]
    public void BuildIsFastEnoughToDrag()
    {
        _ = RockRecipe.Build(RockRecipe.StartingPoints[0].Settings);
        foreach (var p in RockRecipe.StartingPoints)
        {
            double best = double.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                var sw = Stopwatch.StartNew();
                _ = RockRecipe.Build(p.Settings);
                sw.Stop();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }

            output.WriteLine($"{p.Id}: {best:F1} ms (fastest of 3)");
            Assert.True(best < 150, $"{p.Id} took {best} ms at best of 3");
        }
    }

    [Fact]
    public void GlbIsTheEnginesExportPackages()
    {
        var p = RockRecipe.StartingPoints.Single(x => x.Id == "boulder");
        byte[] ours = RockRecipe.Glb(p.Settings, "rock");
        var files = EngineKit.Package(RockRecipe.Engine, p.Settings, "rock");
        byte[] want = files.Single(f => f.Name.EndsWith(".glb", StringComparison.Ordinal)).Bytes;
        Assert.True(want.AsSpan().SequenceEqual(ours));
        Assert.Equal("glTF", System.Text.Encoding.ASCII.GetString(ours, 0, 4));
    }

    [Fact]
    public void AComicExportCutsTheRocksColoursToAFewFlatTones()
    {
        var boulder = RockRecipe.StartingPoints[0].Settings;
        static (int Colours, int Vertices) Read(byte[] glb)
        {
            var model = SharpGLTF.Schema2.ModelRoot.ParseGLB(glb);
            var primitive = model.LogicalMeshes[0].Primitives[0];
            var colours = primitive.GetVertexAccessor("COLOR_0").AsVector3Array();
            return (colours.Distinct().Count(), colours.Count);
        }

        var (litColours, litVertices) = Read(RockRecipe.Glb(boulder, "rock", ExportLook.Lit));
        var (comicColours, comicVertices) = Read(RockRecipe.Glb(boulder, "rock", ExportLook.Cel));
        Assert.Equal(litVertices, comicVertices);
        Assert.InRange(comicColours, 1, 6);
        Assert.True(litColours > comicColours, $"lit {litColours} colours, comic {comicColours}");

        // Cartoon draws in the engine: the file is the Lit one.
        Assert.Equal(RockRecipe.Glb(boulder, "rock", ExportLook.Lit), RockRecipe.Glb(boulder, "rock", ExportLook.Cartoon));
    }
}
