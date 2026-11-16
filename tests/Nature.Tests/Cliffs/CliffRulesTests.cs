using PropGenerator.Nature.Cliffs;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Parameters;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

/// <summary>
/// The seed check: a cliff that breaks a rule once built is laid out again from the next sub-seed, every level from the
/// same one, with a fallback that has no cuts; and its collision covers every block.
/// </summary>
[Collection(CliffRulesHook.Name)]
public class CliffRulesTests
{
    /// <summary>The detail levels a cliff is built at, as the app and the export build them.</summary>
    private static readonly double[] Levels = [1, 0.4, 0.12];

    private static readonly string[] Presets = ["long-cliff", "tall-cliff", "cliff-top", "layered-cliff"];

    [Fact]
    public void ThePickIsTheSameAtEveryLevel()
    {
        foreach (var s in Presets.Select(id => CliffMeshes.Preset(id)).Append(CliffMeshes.Random(3)).Append(CliffMeshes.Random(17)))
        {
            var (attempt, seed, layout) = CliffRules.Choose(s);
            Assert.InRange(attempt, 0, CliffRules.Attempts - 1);
            Assert.Equal(CliffRules.SubSeed(s.Seed, attempt), seed);
            Assert.Equal(seed, CliffRules.Pick(s));
            Assert.Equal(Shapes(CliffLayout.Make(s, seed)), Shapes(layout));

            // Each level is built from the picked layout: its field's bounds (which do not depend on the grid) are the
            // layout's.
            var bounds = new SlabCliffField(layout, s, 0).Bounds();
            foreach (double level in Levels)
            {
                var (field, _) = Assert.Single(CliffGenerator.Pieces(s, level));
                Assert.Equal(bounds, field.Bounds());
            }
        }
    }

    [Fact]
    public void ABadAttemptIsSkipped()
    {
        var s = CliffMeshes.Preset("long-cliff");
        var first = new SlabCliffField(CliffLayout.Make(s, CliffRules.SubSeed(s.Seed, 0)), s, 0).Bounds();
        var second = new SlabCliffField(CliffLayout.Make(s, CliffRules.SubSeed(s.Seed, 1)), s, 0).Bounds();
        Assert.NotEqual(first, second);
        try
        {
            CliffRules.Reject = attempt => attempt == 0;
            var (attempt, seed, layout) = CliffRules.Choose(s);
            Assert.Equal(1, attempt);
            Assert.Equal(CliffRules.SubSeed(s.Seed, 1), seed);
            Assert.Equal(Shapes(CliffLayout.Make(s, seed)), Shapes(layout));
            foreach (double level in Levels)
            {
                var (field, _) = Assert.Single(CliffGenerator.Pieces(s, level));
                Assert.Equal(second, field.Bounds());
                var block = CliffGenerator.Build(s, level);
                Assert.True(block.Indices.Length > 0);
                Assert.Null(CliffMeshes.Open(block));
            }
        }
        finally
        {
            CliffRules.Reject = null;
        }
    }

    [Fact]
    public void TheFallbackHasNoCuts()
    {
        // Every cut slider high, so every attempt would have cuts.
        var s = CliffMeshes.Preset("cliff-top") with { Steps = 8, Bites = 8, Grooves = 4, CutDepth = 1 };
        Assert.Contains(CliffRules.Choose(s).Layout, b => b.Block.Cuts.Count > 0);
        var tried = new List<int>();
        try
        {
            CliffRules.Reject = attempt =>
            {
                tried.Add(attempt);
                return true;
            };
            var (attempt, seed, layout) = CliffRules.Choose(s);
            Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], tried);
            Assert.Equal(CliffRules.Attempts, attempt);
            Assert.Equal(s.Seed, seed);
            Assert.Equal(s.Seed, CliffRules.Pick(s));
            Assert.All(layout, b => Assert.Empty(b.Block.Cuts));
            Assert.All(layout, b => Assert.Equal((0, 0, 0), (b.Shape.Steps, b.Shape.Bites, b.Shape.Grooves)));
            foreach (double level in Levels)
            {
                var block = CliffGenerator.Build(s, level);
                Assert.True(block.Indices.Length > 0);
                Assert.Null(CliffMeshes.Open(block));
            }
        }
        finally
        {
            CliffRules.Reject = null;
        }
    }

    [Fact]
    public void HoldsRejectsEachBrokenRule()
    {
        IReadOnlyList<LaidBlock> thick = [CliffMeshes.Laid(CliffMeshes.Box(0, 2, 0, 2, 0, 2), 0, 0, 0)];
        IReadOnlyList<LaidBlock> thin = [CliffMeshes.Laid(CliffMeshes.Box(0, 2, 0, 0.3, 0, 2), 0, 0, 0)];
        var one = Tetrahedra(new V3(0, 0, 0));
        Assert.True(CliffRules.Holds(thick, one, 0.6));
        Assert.False(CliffRules.Holds(thin, one, 0.6));

        // Two pieces of the same size; a crumb of a thousandth of the volume is no piece.
        Assert.False(CliffRules.Holds(thick, Tetrahedra(new V3(0, 0, 0), new V3(5, 0, 0)), 0.6));
        var crumb = Tetrahedra(new V3(0, 0, 0));
        var small = Tetrahedra(new V3(5, 0, 0), scale: 0.1);
        int offset = crumb.Points.Count;
        crumb.Points.AddRange(small.Points);
        crumb.Indices.AddRange(small.Indices.Select(i => i + offset));
        Assert.True(CliffRules.Holds(thick, crumb, 0.6));

        // A triangle gone leaves three open edges.
        var open = Tetrahedra(new V3(0, 0, 0));
        open.Indices.RemoveRange(0, 3);
        Assert.False(CliffRules.Holds(thick, open, 0.6));

        // Nothing at all is no cliff.
        Assert.False(CliffRules.Holds(thick, new SolidMesh(), 0.6));
    }

    [Fact]
    public void CollisionCoversEveryBlock()
    {
        var s = CliffMeshes.Preset("long-cliff");
        var layout = CliffGenerator.Layout(s);
        var chunks = CliffGenerator.Build(s, 1).Chunks;
        Assert.Equal(layout.Sum(b => CliffChunk.Of(b).Count), chunks.Count);
        Assert.True(chunks.Count > Collision.MaxShapes, $"{chunks.Count} chunks");

        // The shapes the export keeps, all of them, cover every block's whole ground footprint.
        var shapes = Collision.Choose(CliffExport.Source(s).Collision, [], CliffExport.MaxCollisionShapes);
        Assert.Equal(chunks.Count, shapes.Count);
        AssertCovered(layout, shapes);

        // A cliff of many blocks, far more prisms than 32: they merge to at most 32, and still every footprint is covered.
        var many = CliffMeshes.Preset("long-cliff") with { Blocks = 12, Tiers = 4, Layers = 12 };
        var blocks = CliffGenerator.Layout(many);
        int prisms = blocks.Sum(b => CliffChunk.Of(b).Count);
        Assert.True(prisms > 3 * CliffExport.MaxCollisionShapes, $"{prisms} prisms");
        Assert.InRange(CliffGenerator.Build(many, 0.12).Chunks.Count, 1, CliffExport.MaxCollisionShapes);
        var source = CliffExport.Source(many);
        Assert.Equal(32, source.MaxCollisionShapes);
        var kept = Collision.Choose(source.Collision, [], source.MaxCollisionShapes);
        Assert.Equal(source.Collision.Count, kept.Count);
        Assert.InRange(kept.Count, 1, CliffExport.MaxCollisionShapes);
        AssertCovered(blocks, kept);
    }

    [Fact]
    public void ThePickDoesNotDependOnDetail()
    {
        var detail = CliffParameterSet.Instance.Schema.OfType<NumberSpec>().Single(spec => spec.Key == "detail");
        foreach (var s in Presets.Select(id => CliffMeshes.Preset(id)).Concat(Enumerable.Range(1, 12).Select(CliffMeshes.Random)))
        {
            var picks = new[] { detail.Min, detail.DefaultNumber, detail.Max }.Select(d => CliffRules.Choose(s with { Detail = d })).ToList();
            Assert.All(picks, p => Assert.Equal(picks[0].Attempt, p.Attempt));
            Assert.All(picks, p => Assert.Equal(picks[0].Seed, p.Seed));
        }
    }

    [Fact]
    public void GroupsAreTheBlocksThatTouch()
    {
        var left = CliffMeshes.Laid(CliffMeshes.Box(0, 2, 0, 2, 0, 2), 0, 0, 0);
        var touching = CliffMeshes.Laid(CliffMeshes.Box(2, 4, 0, 2, 0, 2), 1, 0, 0);
        var overlapping = CliffMeshes.Laid(CliffMeshes.Box(1, 3, 0, 2, 1, 3), 1, 0, 0);
        var apart = CliffMeshes.Laid(CliffMeshes.Box(5, 7, 0, 2, 0, 2), 2, 0, 0);

        // Turned 45° about the upright, its corner points at the left box's side face and stays 0.3 m off it.
        var turned = new ConvexBlock(
            [.. Enumerable.Range(0, 4).Select(k => new Plane(new V3(Math.Cos((k + 0.5) * Math.PI / 2), Math.Sin((k + 0.5) * Math.PI / 2), 0), 1)),
             new Plane(new V3(0, 0, 1), 2), new Plane(new V3(0, 0, -1), 0)],
            []);
        var shifted = new ConvexBlock([.. turned.Shell.Select(p => new Plane(p.Normal, p.Offset + V3.Dot(p.Normal, new V3(-0.3 - Math.Sqrt(2), 1, 0))))], []);
        var diamond = CliffMeshes.Laid(shifted, 3, 0, 0);

        Assert.Equal(1, CliffRules.Groups([left, touching]));
        Assert.Equal(1, CliffRules.Groups([left, overlapping]));
        Assert.Equal(2, CliffRules.Groups([left, apart]));
        Assert.Equal(2, CliffRules.Groups([left, diamond]));
        Assert.Equal(2, CliffRules.Groups([left, touching, apart]));

        // Two pieces of rock pass when the layout is two groups.
        var two = Tetrahedra(new V3(0, 0, 0), new V3(5, 0, 0));
        Assert.False(CliffRules.Holds([left, touching], two, 0.6));
        Assert.True(CliffRules.Holds([left, apart], two, 0.6));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(14)]
    [InlineData(19)]
    [InlineData(35)]
    [InlineData(43)]
    [InlineData(47)]
    public void TouchingBlocksAreOneShell(int seed)
    {
        // The settings whose probes or builds were several shells: their uncut layouts (the fallback's), with all their
        // surface detail, are as many shells as the layout has groups of touching blocks, at every level.
        var x = CliffMeshes.Random(seed);
        var s = CliffRules.Uncut(x);
        var layout = CliffLayout.Make(s, x.Seed);
        int groups = CliffRules.Groups(layout);
        foreach (double level in Levels)
        {
            var mesh = Assert.Single(CliffGenerator.Pieces(s, layout, level)).Mesh;
            var volumes = CliffMeshes.ShellVolumes(mesh);
            Assert.Equal(groups, volumes.Count(v => v > 0.01 * volumes.Sum()));
        }
    }

    [Fact]
    public void RocksStillGetAtMostEight()
    {
        var offered = Enumerable.Range(0, 20)
            .Select(i => new CollisionShape(new Kernel.Vec3d(i, 0, 0), new Kernel.Vec3d(i, 1, 0), 0.5, 0.5))
            .ToList();
        Assert.Equal(8, Collision.MaxShapes);
        Assert.Equal(8, Collision.Choose(offered, []).Count);
        var rock = new ExportSource(_ => [], offered, new Kernel.Vec3d(0, 0, 0), false, false, new AssetDescription("rock", 1, 1, "low-poly", new Dictionary<string, object?>(), "test"));
        Assert.Equal(8, rock.MaxCollisionShapes);
    }

    [Fact]
    public void DetailNeverCutsThroughAPlate()
    {
        // The thinnest plates (Layers at its top) with every Surface and Cracks slider at its top: the detail is held to
        // a quarter of each plate's thickness, so every level is as many shells as the layout has groups.
        var raw = CliffParameterSet.Instance.Defaults.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        foreach (var (name, number) in CliffPresets.Values.Single(p => p.Id == "layered-cliff").Values)
        {
            raw[name] = number;
        }

        foreach (var spec in CliffParameterSet.Instance.Schema.OfType<NumberSpec>())
        {
            if (spec.Key == "layers" || spec.Group is "Surface" or "Cracks")
            {
                raw[spec.Key] = spec.Max;
            }
        }

        var result = CliffParameterSet.Instance.Validate(raw);
        Assert.True(result.Valid);
        var s = CliffSettings.From(result.Parameters, 4);
        var layout = CliffGenerator.Layout(s);
        int groups = CliffRules.Groups(layout);
        Assert.True(layout.Count >= 12, $"{layout.Count} blocks");
        foreach (double level in Levels)
        {
            var (field, mesh) = Assert.Single(CliffGenerator.Pieces(s, layout, level));
            Assert.True(((SlabCliffField)field).Capped);
            var volumes = CliffMeshes.ShellVolumes(mesh);
            Assert.InRange(volumes.Count(v => v > 0.01 * volumes.Sum()), 1, groups);
        }
    }

    [Fact]
    public void SmudgeNeverCutsThroughAPlate()
    {
        // The thinnest plates with the smudge at its top and no creases, ledges or lines (nor chisel, chips or wear, so
        // the smudge is all the detail there is): it pushes the surface in by up to 1.5 m, and is held, with the rest of
        // the detail, to a quarter of the plates' thickness. Laid out from the cliff's own seed, so no pick hides a split.
        var s = CliffMeshes.Preset("layered-cliff") with
        {
            Layers = 12,
            Smudge = 1.5,
            Creases = 0,
            Ledges = 0,
            Lines = 0,
            Chisel = 0,
            EdgeChips = 0,
            EdgeWear = 0,
        };
        var layout = CliffLayout.Make(s, s.Seed);
        int groups = CliffRules.Groups(layout);
        foreach (double level in Levels)
        {
            var (_, mesh) = Assert.Single(CliffGenerator.Pieces(s, layout, level));
            var volumes = CliffMeshes.ShellVolumes(mesh);
            Assert.InRange(volumes.Count(v => v > 0.01 * volumes.Sum()), 1, groups);
        }

        // Near the surface, the detail (the field less the plain blocks' field) never pushes in further than a quarter
        // of the thickest plate's thinnest width.
        var field = new SlabCliffField(layout, s, 0.15);
        var plain = new SlabCliffField(layout, CliffMeshes.Plain(s), 0.15);
        double most = 0.25 * layout.Max(b => b.Block.MinWidth());
        var (min, max) = plain.Bounds()!.Value;
        var random = new System.Random(5);
        double deepest = 0;
        for (int found = 0; found < 3000;)
        {
            var p = new V3(
                min.X + ((max.X - min.X) * random.NextDouble()),
                min.Y + ((max.Y - min.Y) * random.NextDouble()),
                min.Z + ((max.Z - min.Z) * random.NextDouble()));
            double under = plain.Distance(p);
            if (Math.Abs(under) < 0.3 && p.Z > 0)
            {
                found++;
                deepest = Math.Max(deepest, field.Distance(p) - under);
            }
        }

        Assert.True(deepest <= most + 1e-9, $"the detail pushes in {deepest:F3} m, past {most:F3} m");
    }

    /// <summary>
    /// Asserts every block's ground footprint (its shell's section 2 cm above its ground line: the corners and the
    /// middles of the edges, each pulled 2 cm in toward the middle) lies inside some exported shape (Y-up).
    /// </summary>
    private static void AssertCovered(IReadOnlyList<LaidBlock> blocks, IReadOnlyList<CollisionShape> shapes)
    {
        var hulls = shapes.Select(shape => Collision.Prism(shape, new Kernel.Vec3d(0, 0, 0))).ToList();
        foreach (var b in blocks)
        {
            double z = b.Shape.Base.Z + 0.02;
            var section = Section(b.Block, z);
            var middle = new V3(section.Average(p => p.X), section.Average(p => p.Y), z);
            var samples = section.Concat(section.Select((p, k) => (p + section[(k + 1) % section.Count]) / 2));
            foreach (var corner in samples)
            {
                var away = corner - middle;
                var p = corner - (away * (0.02 / away.Length));
                var y = new V3(p.X, p.Z, -p.Y);
                Assert.True(hulls.Any(h => Inside(h, y)), $"block c{b.Column} t{b.Tier} l{b.Layer}: {p} is outside every shape");
            }
        }
    }

    /// <summary>A block's shell cut at a height, as a polygon in x and y (its corners in order).</summary>
    private static List<V3> Section(ConvexBlock block, double z)
    {
        var (min, max) = block.Bounds();
        List<V3> polygon = [new(min.X - 1, min.Y - 1, z), new(max.X + 1, min.Y - 1, z), new(max.X + 1, max.Y + 1, z), new(min.X - 1, max.Y + 1, z)];
        foreach (var plane in block.Shell)
        {
            var kept = new List<V3>();
            for (int k = 0; k < polygon.Count; k++)
            {
                var a = polygon[k];
                var c = polygon[(k + 1) % polygon.Count];
                double fa = plane.At(a);
                double fc = plane.At(c);
                if (fa <= 0)
                {
                    kept.Add(a);
                }

                if ((fa <= 0) != (fc <= 0))
                {
                    kept.Add(a + ((c - a) * (fa / (fa - fc))));
                }
            }

            polygon = kept;
        }

        return polygon;
    }

    /// <summary>Whether a point lies inside a closed convex mesh (behind every one of its triangles, to float rounding).</summary>
    private static bool Inside(Kernel.PropMesh hull, V3 p)
    {
        float[] q = hull.Position;
        uint[] index = hull.Index!;
        for (int t = 0; t < index.Length; t += 3)
        {
            var a = new V3(q[3 * index[t]], q[(3 * index[t]) + 1], q[(3 * index[t]) + 2]);
            var b = new V3(q[3 * index[t + 1]], q[(3 * index[t + 1]) + 1], q[(3 * index[t + 1]) + 2]);
            var c = new V3(q[3 * index[t + 2]], q[(3 * index[t + 2]) + 1], q[(3 * index[t + 2]) + 2]);
            var n = V3.Cross(b - a, c - a);
            if (V3.Dot(n, p - a) > 1e-4 * n.Length)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The shapes of laid blocks, to compare two layouts by value.</summary>
    private static List<BlockShape> Shapes(IReadOnlyList<LaidBlock> layout) => [.. layout.Select(b => b.Shape)];

    /// <summary>A closed mesh of one tetrahedron at each corner given, wound counter-clockwise seen from outside.</summary>
    private static SolidMesh Tetrahedra(V3 at, V3? second = null, double scale = 1)
    {
        var mesh = new SolidMesh();
        foreach (var corner in second is { } other ? [at, other] : new[] { at })
        {
            int o = mesh.Points.Count;
            mesh.Points.AddRange([corner, corner + new V3(scale, 0, 0), corner + new V3(0, scale, 0), corner + new V3(0, 0, scale)]);
            mesh.Indices.AddRange([o, o + 2, o + 1, o, o + 1, o + 3, o, o + 3, o + 2, o + 1, o + 2, o + 3]);
        }

        return mesh;
    }
}
