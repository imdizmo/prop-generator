using PropGenerator.Nature.Cliffs;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

/// <summary>How the blocks of a cliff are laid out: side by side, in tiers and in plates, with turned-back ends.</summary>
public class CliffLayoutTests
{
    private static readonly string[] Presets = ["long-cliff", "tall-cliff", "cliff-top", "layered-cliff"];

    /// <summary>A hundred cliffs: each preset at 25 seeds.</summary>
    private static IEnumerable<CliffSettings> Hundred() =>
        Enumerable.Range(0, 100).Select(i => CliffMeshes.Preset(Presets[i % 4]) with { Seed = (uint)(i + 1) });

    /// <summary>The hundred preset cliffs and twenty with every slider drawn across its range.</summary>
    private static IEnumerable<CliffSettings> Many() => Hundred().Concat(Enumerable.Range(1, 20).Select(CliffMeshes.Random));

    private static double MinThickness(CliffSettings s) => Math.Min(0.6, s.Depth / 3);

    private static double Top(LaidBlock b) => b.Shape.Base.Z + b.Shape.Height;

    /// <summary>The blocks a block rests on: the plates below it in its tier, or the tier below it; none on the ground.</summary>
    private static List<LaidBlock> Below(IReadOnlyList<LaidBlock> layout, LaidBlock b) =>
        b.Layer > 0 ? [.. layout.Where(o => o.Tier == b.Tier && o.Layer == b.Layer - 1)]
        : b.Tier > 0 ? [.. layout.Where(o => o.Tier == b.Tier - 1)]
        : [];

    /// <summary>
    /// The share of a block's footprint (sampled on a grid over its section 2 cm below its ground line) where the
    /// blocks below it overlap it by at least 2 cm in z: at some height of its sunk part, from 2 cm above its flat
    /// bottom up to its ground line, both it and a block below fill the point.
    /// </summary>
    private static double Supported(LaidBlock b, List<LaidBlock> below)
    {
        const int N = 24;
        const int Levels = 11;
        var shape = b.Shape;
        double low = shape.Base.Z - shape.Sink + 0.02;
        double span = Math.Max(shape.Base.Z - 0.02 - low, 0);
        int footprint = 0;
        int held = 0;
        var (min, max) = b.Block.Bounds();
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                double x = min.X + ((max.X - min.X) * (i + 0.5) / N);
                double y = min.Y + ((max.Y - min.Y) * (j + 0.5) / N);
                if (b.Block.Distance(new V3(x, y, shape.Base.Z - 0.02)) < 0)
                {
                    footprint++;
                    held += Enumerable.Range(0, Levels)
                        .Select(l => new V3(x, y, low + (span * l / (Levels - 1))))
                        .Any(p => b.Block.Distance(p) < 0 && below.Any(o => o.Block.Distance(p) < 0)) ? 1 : 0;
                }
            }
        }

        return footprint == 0 ? 0 : (double)held / footprint;
    }

    /// <summary>Where a block's shell crosses the line along x at y and z: the start and end of that x span.</summary>
    private static (double Lo, double Hi) Span(LaidBlock b, double y, double z)
    {
        double lo = double.NegativeInfinity;
        double hi = double.PositiveInfinity;
        foreach (Plane plane in b.Block.Shell)
        {
            double a = plane.Normal.X;
            double c = (plane.Normal.Y * y) + (plane.Normal.Z * z) - plane.Offset;
            if (Math.Abs(a) < 1e-12)
            {
                if (c > 0)
                {
                    return (0, 0);
                }

                continue;
            }

            if (a > 0)
            {
                hi = Math.Min(hi, -c / a);
            }
            else
            {
                lo = Math.Max(lo, -c / a);
            }
        }

        return (lo, Math.Max(lo, hi));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(12)]
    public void BlocksIsAnExactCount(int blocks)
    {
        for (uint seed = 1; seed <= 30; seed++)
        {
            var s = CliffMeshes.Preset("long-cliff", "width", 60) with { Blocks = blocks, Seed = seed };
            var layout = CliffLayout.Make(s, s.Seed);
            Assert.Equal(blocks, layout.Select(b => b.Column).Distinct().Count());
            Assert.Equal(Enumerable.Range(0, blocks), layout.Select(b => b.Column).Distinct().Order());
        }
    }

    [Fact]
    public void TheSameSettingsGiveTheSameLayout()
    {
        foreach (var s in Many().Take(40))
        {
            var a = CliffLayout.Make(s, s.Seed);
            var b = CliffLayout.Make(s, s.Seed);
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].Shape, b[i].Shape);
                Assert.Equal(a[i].Block.Shell, b[i].Block.Shell);
                Assert.Equal(a[i].Block.Cuts.Count, b[i].Block.Cuts.Count);
            }
        }
    }

    [Fact]
    public void NeighboursOverlapByAFifth()
    {
        // Measured on the solids themselves, at the higher block's ground line, along the best of nine lines across
        // the depth both blocks share.
        var failures = new List<string>();
        foreach (var s in Hundred())
        {
            var layout = CliffLayout.Make(s, s.Seed);
            foreach (var a in layout)
            {
                var b = layout.SingleOrDefault(o => o.Tier == a.Tier && o.Layer == a.Layer && o.Column == a.Column + 1);
                if (b is null)
                {
                    continue;
                }

                double z = Math.Max(a.Shape.Base.Z, b.Shape.Base.Z) + 0.02;
                if (z >= Math.Min(Top(a), Top(b)))
                {
                    continue;
                }

                double y0 = Math.Max(a.Shape.Base.Y - (a.Shape.Depth / 2), b.Shape.Base.Y - (b.Shape.Depth / 2));
                double y1 = Math.Min(a.Shape.Base.Y + (a.Shape.Depth / 2), b.Shape.Base.Y + (b.Shape.Depth / 2));
                double best = double.NegativeInfinity;
                for (int k = 0; k < 9; k++)
                {
                    double y = y0 + ((y1 - y0) * (k + 0.5) / 9);
                    var (alo, ahi) = Span(a, y, z);
                    var (blo, bhi) = Span(b, y, z);
                    double narrower = Math.Min(ahi - alo, bhi - blo);
                    if (narrower > 0)
                    {
                        best = Math.Max(best, (Math.Min(ahi, bhi) - Math.Max(alo, blo)) / narrower);
                    }
                }

                if (best < 0.2)
                {
                    failures.Add($"seed {s.Seed} tier {a.Tier} layer {a.Layer} columns {a.Column}–{b.Column}: {best:F3}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void EveryBlockIsGrounded()
    {
        var failures = new List<string>();
        foreach (var s in Many())
        {
            var layout = CliffLayout.Make(s, s.Seed);
            foreach (var b in layout)
            {
                if (b.Tier == 0 && b.Layer == 0)
                {
                    Assert.Equal(0, b.Shape.Base.Z);
                    Assert.True(b.Block.Bounds().Min.Z <= 0);
                    Assert.True(b.Block.Distance(new V3(b.Shape.Base.X, b.Shape.Base.Y, 0)) < 0);
                    continue;
                }

                double share = Supported(b, Below(layout, b));
                if (share < 0.5)
                {
                    failures.Add($"seed {s.Seed} column {b.Column} tier {b.Tier} layer {b.Layer}: {share:F2} held");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void TiersStepBackAndDown()
    {
        for (uint seed = 1; seed <= 30; seed++)
        {
            var s = CliffMeshes.Preset("cliff-top") with { Tiers = 3, Seed = seed };
            var layout = CliffLayout.Make(s, s.Seed);
            Assert.Equal([0, 1, 2], layout.Select(b => b.Tier).Distinct().Order());
            foreach (var group in layout.GroupBy(b => b.Column))
            {
                var tiers = group.OrderBy(b => b.Tier).ToList();
                Assert.Equal(3, tiers.Count);
                for (int k = 1; k < tiers.Count; k++)
                {
                    var (lower, upper) = (tiers[k - 1].Shape, tiers[k].Shape);
                    Assert.True(upper.Height < lower.Height, $"seed {seed} tier {k} is {upper.Height} m tall over {lower.Height} m");
                    Assert.True(upper.Base.Z > lower.Base.Z);
                    double setBack = (upper.Base.Y - (upper.Depth / 2)) - (lower.Base.Y - (lower.Depth / 2));
                    Assert.True(setBack >= 0.15 * lower.Depth, $"seed {seed} tier {k} is set back {setBack} m of {lower.Depth} m");
                    Assert.True(tiers[k].Block.Bounds().Min.Y > tiers[k - 1].Block.Bounds().Min.Y);
                }
            }
        }
    }

    [Fact]
    public void LayersStackFlat()
    {
        bool varied = false;
        for (uint seed = 1; seed <= 30; seed++)
        {
            var s = CliffMeshes.Preset("layered-cliff") with { Layers = 6, Seed = seed };
            var layout = CliffLayout.Make(s, s.Seed);
            double most = (1 + (s.LayerVariety / 2)) / (1 - (s.LayerVariety / 2));
            foreach (var group in layout.GroupBy(b => (b.Column, b.Tier)))
            {
                var plates = group.OrderBy(b => b.Layer).ToList();
                Assert.Equal([0, 1, 2, 3, 4, 5], plates.Select(b => b.Layer));
                foreach (var plate in plates)
                {
                    double up = plate.Block.Shell.Max(p => p.Normal.Z);
                    Assert.True(up >= Math.Cos(6 * Math.PI / 180), $"seed {seed} plate {plate.Layer} top tilts {Math.Acos(up) * 180 / Math.PI:F1}°");
                }

                var thick = plates.Select(b => b.Shape.Height).ToList();
                Assert.True(thick.Max() / thick.Min() <= most + 1e-9, $"seed {seed}: plates {string.Join(", ", thick.Select(t => t.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}");
                varied |= thick.Max() / thick.Min() > 1.05;
                for (int j = 1; j < plates.Count; j++)
                {
                    Assert.Equal(Top(plates[j - 1]), plates[j].Shape.Base.Z, 9);
                }
            }
        }

        Assert.True(varied);
    }

    [Fact]
    public void TheEndsTurnBack()
    {
        foreach (var s in Many())
        {
            var layout = CliffLayout.Make(s, s.Seed);
            int last = layout.Max(b => b.Column);
            foreach (var b in layout)
            {
                if (b.Column == 0)
                {
                    Assert.InRange(b.Shape.TurnLeft, 20, 50);
                    Assert.True(Finished(b, -1), $"seed {s.Seed} column {b.Column} has no bite or bevel at its left end");
                }
                else
                {
                    Assert.Equal(0, b.Shape.TurnLeft);
                }

                if (b.Column == last)
                {
                    Assert.InRange(b.Shape.TurnRight, 20, 50);
                    Assert.True(Finished(b, 1), $"seed {s.Seed} column {b.Column} has no bite or bevel at its right end");
                }
                else
                {
                    Assert.Equal(0, b.Shape.TurnRight);
                }
            }
        }

        // A bite whose pushed-in plane faces the end, or a chamfer plane across the end's top.
        static bool Finished(LaidBlock b, int end) =>
            b.Block.Cuts.Any(cut => cut.Any(p => end * p.Normal.X > 0.6))
            || b.Block.Shell.Any(p => end * p.Normal.X > 0.35 && p.Normal.Z > 0.35);
    }

    [Fact]
    public void EndTurnZeroLeavesTheEndCornersSquare()
    {
        // With plain blocks (no roughness, bevel or cuts), a point just inside each end block's front end corner, at its
        // ground line: the turned end face cuts it away at the default End turn, and at End turn 0 it is rock.
        foreach (string id in new[] { "long-cliff", "tall-cliff", "cliff-top" })
        {
            for (uint seed = 1; seed <= 5; seed++)
            {
                var plain = CliffMeshes.Preset(id, "roughness", 0) with { Bevel = 0, Steps = 0, Bites = 0, Grooves = 0, Seed = seed };
                var square = plain with { EndTurn = 0 };
                foreach (var (s, inside) in new[] { (plain, false), (square, true) })
                {
                    var layout = CliffLayout.Make(s, s.Seed);
                    int last = layout.Max(b => b.Column);
                    foreach (var b in layout.Where(b => b.Tier == 0 && b.Layer == 0 && (b.Column == 0 || b.Column == last)))
                    {
                        foreach (int end in new[] { -1, 1 }.Where(e => (e < 0 ? b.Column == 0 : b.Column == last)))
                        {
                            var shape = b.Shape;
                            double inset = 0.15 * Math.Min(shape.Width, shape.Depth);
                            var corner = new V3(
                                shape.Base.X + (end * ((shape.Width / 2) - inset)),
                                shape.Base.Y - (shape.Depth / 2) + inset,
                                shape.Base.Z + 0.05);
                            Assert.True(
                                (b.Block.Distance(corner) < 0) == inside,
                                $"{id} seed {seed} End turn {s.EndTurn}: the front corner at the {(end < 0 ? "left" : "right")} end is {(inside ? "cut away" : "rock")}");
                        }

                        if (inside)
                        {
                            Assert.Equal((0.0, 0.0), (b.Shape.TurnLeft, b.Shape.TurnRight));
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void EndTurnScalesEachEndsDrawnTurn()
    {
        // End turn is the most an end turns back: each end's seeded turn (20° to 50° at the default, 50°) is scaled by End
        // turn / 50, so 25 halves every turn.
        foreach (string id in Presets)
        {
            var full = CliffMeshes.Preset(id);
            Assert.Equal(50, full.EndTurn);
            var half = CliffMeshes.Preset(id, "end-turn", 25);
            var a = CliffLayout.Make(full, full.Seed);
            var b = CliffLayout.Make(half, half.Seed);
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].Shape.TurnLeft / 2, b[i].Shape.TurnLeft, 9);
                Assert.Equal(a[i].Shape.TurnRight / 2, b[i].Shape.TurnRight, 9);
                if (a[i].Tier == 0 && a[i].Layer == 0)
                {
                    // Blocks on the ground are not settled, so their shapes differ in the turns alone.
                    Assert.Equal(a[i].Shape with { TurnLeft = 0, TurnRight = 0 }, b[i].Shape with { TurnLeft = 0, TurnRight = 0 });
                }
            }
        }
    }

    [Fact]
    public void FootNarrowsTheBaseOfTheBlocksOnTheGround()
    {
        // Foot 0.4: at the ground line each block on the ground is 40% of its depth narrower front to back (a fifth at
        // the front, a fifth at the back), and the row's end blocks a fifth of their width narrower at their outer end;
        // the narrowing runs out at the block's top. Blocks off the ground are left as they were.
        var front = new V3(0, -1, 0);
        foreach (string id in Presets)
        {
            for (uint seed = 1; seed <= 3; seed++)
            {
                var flat = CliffMeshes.Preset(id) with { Seed = seed };
                var footed = flat with { Foot = 0.4 };
                var a = CliffLayout.Make(flat, flat.Seed);
                var b = CliffLayout.Make(footed, footed.Seed);
                int last = a.Max(o => o.Column);
                Assert.Equal(a.Count, b.Count);
                for (int i = 0; i < a.Count; i++)
                {
                    var shape = a[i].Shape;
                    string where = $"{id} seed {seed} block c{a[i].Column} t{a[i].Tier} l{a[i].Layer}";
                    if (a[i].Tier > 0 || a[i].Layer > 0)
                    {
                        continue;
                    }

                    double ground = shape.Base.Z;
                    foreach (var y in new[] { front, -front })
                    {
                        double narrowed = CliffMeshes.SectionReach(a[i].Block, ground, y) - CliffMeshes.SectionReach(b[i].Block, ground, y);
                        Assert.True(Math.Abs(narrowed - (0.2 * shape.Depth)) < 1e-5, $"{where}: narrowed {narrowed:F6} m along {y}");
                    }
                    // A plane at the front, one at the back and one at each outer end; none where a block meets its neighbour.
                    int outers = (a[i].Column == 0 ? 1 : 0) + (a[i].Column == last ? 1 : 0);
                    Assert.Equal(a[i].Block.Shell.Count + 2 + outers, b[i].Block.Shell.Count);
                    foreach (int end in new[] { -1, 1 }.Where(e => e < 0 ? a[i].Column == 0 : a[i].Column == last))
                    {
                        var x = new V3(end, 0, 0);
                        double narrowed = CliffMeshes.SectionReach(a[i].Block, ground, x) - CliffMeshes.SectionReach(b[i].Block, ground, x);
                        Assert.True(Math.Abs(narrowed - (0.2 * shape.Width)) < 1e-5, $"{where}: narrowed {narrowed:F3} m at {end}");
                    }

                    Assert.True(b[i].Block.MinWidth() >= CliffLayout.MinThickness(footed) - 1e-9, where);
                }
            }
        }
    }

    [Fact]
    public void AFootNeverLeavesABaseThinnerThanMinThickness()
    {
        // On a short, deeply sunk, thin cliff the whole Foot would leave the bottom of the sunk part under Min thickness:
        // it is held so the base there keeps a fifth more than Min thickness across, front to back and end to end.
        int held = 0;
        foreach (string id in Presets)
        {
            var flat = CliffMeshes.Preset(id) with { Depth = 1.6, Width = 3, Height = 1.5, Sink = 1.5, Layers = 0, Tiers = 1 };
            var s = flat with { Foot = 0.4 };
            double thin = CliffLayout.MinThickness(s);
            var before = CliffLayout.Make(flat, flat.Seed);
            var layout = CliffLayout.Make(s, s.Seed);
            int last = layout.Max(b => b.Column);
            for (int i = 0; i < layout.Count; i++)
            {
                var b = layout[i];
                double bottom = b.Shape.Base.Z - b.Shape.Sink + 1e-6;
                double run = (b.Shape.Height + b.Shape.Sink) / b.Shape.Height;
                int ends = (b.Column == 0 ? 1 : 0) + (b.Column == last ? 1 : 0);
                foreach (var (along, narrowing, name) in new[]
                {
                    (new V3(0, 1, 0), 0.4 * b.Shape.Depth * run, "front to back"),
                    (new V3(1, 0, 0), 0.2 * ends * b.Shape.Width * run, "end to end"),
                })
                {
                    double was = CliffMeshes.SectionWidth(before[i].Block, bottom, along);
                    held += was - narrowing < 1.2 * thin && narrowing > 0 ? 1 : 0;
                    Assert.True(CliffMeshes.SectionWidth(b.Block, bottom, along) >= Math.Min(was, 1.2 * thin) - 1e-6, $"{id} c{b.Column}: {name}");
                }

                Assert.True(b.Block.MinWidth() >= thin - 1e-9, $"{id} c{b.Column}: min width");
            }
        }

        // The whole foot would have gone under the floor somewhere, so the hold was tested.
        Assert.True(held > 0);
    }

    [Fact]
    public void UndersideTiltLiftsTheBottomOffTheGroundButOneEdge()
    {
        // Underside tilt 30: one plane under the blocks on the ground, rising from the cliff's lowest corner, so the cliff
        // still reaches its flat bottom (Sink under the ground line) there, while just above that bottom far less of
        // its footprint is rock than with a flat bottom.
        foreach (string id in Presets)
        {
            for (uint seed = 1; seed <= 3; seed++)
            {
                var flat = CliffMeshes.Preset(id) with { Seed = seed };
                var tilted = flat with { UndersideTilt = 30 };
                var a = CliffLayout.Make(flat, flat.Seed).Where(b => b.Tier == 0 && b.Layer == 0).ToList();
                var b = CliffLayout.Make(tilted, tilted.Seed).Where(b => b.Tier == 0 && b.Layer == 0).ToList();
                Assert.Equal(-flat.Sink, b.Min(o => o.Block.Bounds().Min.Z), 9);
                double z = -flat.Sink + 0.05;
                double before = Footprint(a, z);
                double after = Footprint(b, z);
                Assert.True(after < 0.25 * before, $"{id} seed {seed}: {after:F2} of {before:F2} still on the bottom");
                Assert.All(b, o => Assert.True(o.Block.MinWidth() >= CliffLayout.MinThickness(tilted) - 1e-9));
            }
        }

        // The share of a grid over the blocks' bounds, at a height, inside any of them.
        static double Footprint(List<LaidBlock> blocks, double z)
        {
            const int N = 60;
            var min = new V3(blocks.Min(o => o.Block.Bounds().Min.X), blocks.Min(o => o.Block.Bounds().Min.Y), 0);
            var max = new V3(blocks.Max(o => o.Block.Bounds().Max.X), blocks.Max(o => o.Block.Bounds().Max.Y), 0);
            int inside = 0;
            for (int i = 0; i < N; i++)
            {
                for (int j = 0; j < N; j++)
                {
                    var p = new V3(min.X + ((max.X - min.X) * (i + 0.5) / N), min.Y + ((max.Y - min.Y) * (j + 0.5) / N), z);
                    inside += blocks.Any(o => new ConvexBlock(o.Block.Shell, []).Distance(p) < 0) ? 1 : 0;
                }
            }

            return (double)inside / (N * N);
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(45)]
    [InlineData(60)]
    public void TaperNarrowsTheTopBlocksTowardTheirTop(double tilt)
    {
        // Taper 0.4 adds a plane at the front and at the back of each top block (the top plate of its column's top
        // tier), and at the row's outer ends, each running from the block's side planes at its ground line to a fifth of
        // its depth (or width) inside them at its top: measured on the side planes, so a lean adds to it and a steep top,
        // which cuts into a short block's sections, changes nothing. A top that would end under 1.2 × Min thickness
        // across is narrowed less. The blocks under them are left as they were.
        foreach (string id in Presets)
        {
            for (uint seed = 1; seed <= 3; seed++)
            {
                var plain = CliffMeshes.Preset(id) with { TopTilt = tilt, Seed = seed };
                var tapered = plain with { Taper = 0.4 };
                var a = CliffLayout.Make(plain, plain.Seed);
                var b = CliffLayout.Make(tapered, tapered.Seed);
                int last = a.Max(o => o.Column);
                Assert.Equal(a.Count, b.Count);
                for (int i = 0; i < a.Count; i++)
                {
                    var shape = a[i].Shape;
                    string where = $"{id} seed {seed} tilt {tilt} block c{a[i].Column} t{a[i].Tier} l{a[i].Layer}";
                    bool top = !a.Any(o => o.Column == a[i].Column && (o.Tier, o.Layer).CompareTo((a[i].Tier, a[i].Layer)) > 0);
                    if (!top)
                    {
                        Assert.Equal(a[i].Block.Shell, b[i].Block.Shell);
                        continue;
                    }

                    Plane[] sides = [.. b[i].Block.Shell.Take(Math.Max(shape.Faces, 4))];
                    Plane[] added = [.. b[i].Block.Shell.Where(p => p.Normal.Z > 0 && !a[i].Block.Shell.Contains(p))];
                    int outers = (a[i].Column == 0 ? 1 : 0) + (a[i].Column == last ? 1 : 0);
                    Assert.Equal(2 + outers, added.Length);
                    var bare = new ConvexBlock(sides, []);
                    var narrowed = new ConvexBlock([.. sides, .. added], []);
                    double high = shape.Base.Z + shape.Height;
                    foreach (var (along, size, other, outer) in new[]
                    {
                        (new V3(0, -1, 0), shape.Depth, new V3(0, 1, 0), true),
                        (new V3(0, 1, 0), shape.Depth, new V3(0, -1, 0), true),
                        (new V3(-1, 0, 0), shape.Width, new V3(1, 0, 0), a[i].Column == 0),
                        (new V3(1, 0, 0), shape.Width, new V3(-1, 0, 0), a[i].Column == last),
                    })
                    {
                        if (!outer)
                        {
                            continue;
                        }

                        double less = CliffMeshes.SectionReach(bare, high, along) - CliffMeshes.SectionReach(narrowed, high, along);
                        double across = CliffMeshes.SectionWidth(narrowed, high, along);
                        bool held = less < (0.2 * size) - 1e-6 && across <= (1.2 * CliffLayout.MinThickness(plain)) + 1e-6;
                        Assert.True(held || Math.Abs(less - (0.2 * size)) < 1e-6, $"{where} along {along}: {less:F4} m narrower, not {0.2 * size:F4} m");
                    }
                }
            }
        }
    }

    [Fact]
    public void ATaperNeverLeavesATopThinnerThanMinThickness()
    {
        // On a thin cliff the whole Taper would leave the top under Min thickness: it is held so each top block's top
        // keeps a fifth more than Min thickness across, front to back and end to end (or, where the untapered top is
        // under that already, is not narrowed at all); and with the steepest Top tilt too, no block is thinner than Min
        // thickness.
        foreach (string id in Presets)
        {
            var plain = CliffMeshes.Preset(id) with { Depth = 1.2, Width = 3, TopTilt = 0, Bevel = 0 };
            var s = plain with { Taper = 0.6 };
            double thin = CliffLayout.MinThickness(s);
            var before = CliffLayout.Make(plain, plain.Seed);
            var layout = CliffLayout.Make(s, s.Seed);
            for (int i = 0; i < layout.Count; i++)
            {
                var b = layout[i];
                if (layout.Any(o => o.Column == b.Column && (o.Tier, o.Layer).CompareTo((b.Tier, b.Layer)) > 0))
                {
                    continue;
                }

                double z = b.Shape.Base.Z + b.Shape.Height - 1e-4;
                foreach (var (along, name) in new[] { (new V3(0, 1, 0), "front to back"), (new V3(1, 0, 0), "end to end") })
                {
                    double was = CliffMeshes.SectionWidth(before[i].Block, z, along);
                    double now = CliffMeshes.SectionWidth(b.Block, z, along);
                    Assert.True(now >= Math.Min(was, 1.2 * thin) - 1e-3, $"{id} c{b.Column}: {name} {now:F3} m, was {was:F3} m");
                }
            }

            var steep = s with { TopTilt = 60 };
            Assert.All(CliffLayout.Make(steep, steep.Seed), b => Assert.True(b.Block.MinWidth() >= thin - 1e-9));
        }
    }

    [Fact]
    public void OnlyTheTopBlocksTiltSteeplyAndNoneMakesAKnifeEdge()
    {
        // The steepest Top tilt over the most Lean, with the most Taper: no two faces of any block's shell meet along an
        // edge at under 30° (a mesh folds back along a knife edge), and only the blocks on top tilt past 25°.
        foreach (string id in Presets)
        {
            for (uint seed = 1; seed <= 5; seed++)
            {
                var s = CliffMeshes.Preset(id) with { TopTilt = 60, Lean = 30, Taper = 0.6, Seed = seed };
                var layout = CliffLayout.Make(s, s.Seed);
                foreach (var b in layout)
                {
                    var shell = b.Block.Shell;
                    var corners = b.Block.Corners();
                    for (int i = 0; i < shell.Count; i++)
                    {
                        for (int j = i + 1; j < shell.Count; j++)
                        {
                            int shared = corners.Count(c => Math.Abs(shell[i].At(c)) < 1e-6 && Math.Abs(shell[j].At(c)) < 1e-6);
                            double angle = 180 - (Math.Acos(Math.Clamp(V3.Dot(shell[i].Normal, shell[j].Normal), -1, 1)) * 180 / Math.PI);
                            Assert.True(shared < 2 || angle >= 30 - 1e-6, $"{id} seed {seed} c{b.Column} t{b.Tier} l{b.Layer}: planes {i} and {j} meet at {angle:F1}°");
                        }
                    }

                    bool top = !layout.Any(o => o.Column == b.Column && (o.Tier, o.Layer).CompareTo((b.Tier, b.Layer)) > 0);
                    Assert.True(top || b.Shape.TopTilt <= 25, $"{id} seed {seed} c{b.Column} t{b.Tier} l{b.Layer} carries a block and tilts {b.Shape.TopTilt}°");
                }
            }
        }
    }

    [Fact]
    public void CutCountsVaryByOneAroundTheSliders()
    {
        var counts = new List<(int Steps, int Bites, int Grooves)>();
        for (uint seed = 1; seed <= 30; seed++)
        {
            var s = CliffMeshes.Preset("long-cliff") with { Steps = 3, Bites = 1, Grooves = 0, Seed = seed };
            counts.AddRange(CliffLayout.Make(s, s.Seed).Select(b => (b.Shape.Steps, b.Shape.Bites, b.Shape.Grooves)));
        }

        Assert.All(counts, c => Assert.InRange(c.Steps, 2, 4));
        Assert.All(counts, c => Assert.InRange(c.Bites, 0, 2));
        Assert.All(counts, c => Assert.Equal(0, c.Grooves));
        Assert.Equal([2, 3, 4], counts.Select(c => c.Steps).Distinct().Order());
        Assert.Equal([0, 1, 2], counts.Select(c => c.Bites).Distinct().Order());
        Assert.InRange(counts.Average(c => c.Steps), 2.8, 3.2);
    }

    [Fact]
    public void MostOfTheCutsAskedForAreMade()
    {
        // The cuts each block really carries (after the fin rule, the shallowest-cut rule and the cuts that give way
        // under a block above), less the end bites, against the Steps, Bites and Grooves its shape asks for.
        var cliffs = Presets.SelectMany(id => Enumerable.Range(1, 20).Select(seed => CliffMeshes.Preset(id) with { Seed = (uint)seed }))
            .Concat(Enumerable.Range(0, 50).Select(CliffMeshes.Random));
        long asked = 0;
        long made = 0;
        foreach (var s in cliffs)
        {
            foreach (var b in CliffLayout.Make(s, s.Seed))
            {
                asked += b.Shape.Steps + b.Shape.Bites + b.Shape.Grooves;
                made += b.Block.Cuts.Count(cut => !cut.Any(EndBiteFloor));
            }
        }

        double share = (double)made / asked;
        Assert.True(share >= 0.85, $"{made} of {asked} cuts made ({share:P1})");

        // An end bite's floor slopes 15° down toward its end, straight along x.
        static bool EndBiteFloor(Plane p) =>
            Math.Abs(Math.Abs(p.Normal.X) - Math.Sin(15 * Math.PI / 180)) < 1e-9 && Math.Abs(p.Normal.Y) < 1e-12;
    }

    [Fact]
    public void NoCutGoesPastFortyPercentOfItsFinishedBlock()
    {
        // Random 129 (review 8-8c Important 3): an upper plate's cut was drawn 1.813 m deep against its shell's 40% limit
        // of 2.248 m, then the plate was footed (a foot plane receding at 45° under its ground line), which narrowed the
        // shell along the cut to a limit of 1.733 m. Every cut is held to 40% of its block's finished shell.
        foreach (var s in new[] { CliffMeshes.Random(129) })
        {
            foreach (var b in CliffGenerator.Layout(s))
            {
                foreach (var cut in b.Block.Cuts)
                {
                    var (depth, limit) = CliffMeshes.CutDepth(b.Block, cut);
                    Assert.True(depth <= limit + 1e-6, $"block c{b.Column} t{b.Tier} l{b.Layer} has a cut {depth:F3} m deep, past {limit:F3} m");
                }
            }
        }
    }

    [Fact]
    public void ManyBlocksOnANarrowCliffStayThick()
    {
        foreach (uint seed in new uint[] { 1, 2, 3, 7 })
        {
            var s = CliffMeshes.Preset("long-cliff", "width", 2) with { Blocks = 12, Seed = seed };
            var layout = CliffLayout.Make(s, s.Seed);
            int columns = layout.Select(b => b.Column).Distinct().Count();
            Assert.InRange(columns, 1, 11);
            Assert.All(layout, b => Assert.True(b.Block.MinWidth() >= MinThickness(s), $"{b.Block.MinWidth()} m"));
            Assert.All(layout, b => Assert.True(b.Shape.Width >= MinThickness(s)));
        }

        var build = CliffGenerator.Build(CliffMeshes.Preset("long-cliff", "width", 2) with { Blocks = 12 }, 1);
        Assert.Null(CliffMeshes.Open(build));
    }

    [Fact]
    public void ManyLayersOnALowCliffStayThick()
    {
        foreach (uint seed in new uint[] { 1, 2, 3, 7 })
        {
            var s = CliffMeshes.Preset("long-cliff", "height", 1) with { Layers = 12, Tiers = 4, Seed = seed };
            var layout = CliffLayout.Make(s, s.Seed);
            Assert.All(layout, b => Assert.True(b.Shape.Height >= MinThickness(s), $"{b.Shape.Height} m tall"));
            Assert.All(layout, b => Assert.True(b.Shape.Depth >= MinThickness(s), $"{b.Shape.Depth} m deep"));
            Assert.All(layout, b => Assert.True(b.Block.MinWidth() >= MinThickness(s), $"{b.Block.MinWidth()} m"));
        }

        var build = CliffGenerator.Build(CliffMeshes.Preset("long-cliff", "height", 1) with { Layers = 12, Tiers = 4 }, 1);
        Assert.Null(CliffMeshes.Open(build));
    }

    [Fact]
    public void ManyTiersAndLayersOnATallCliffStayThick()
    {
        var s = CliffMeshes.Preset("long-cliff") with { Height = 30, Layers = 12, Tiers = 4 };
        var layout = CliffLayout.Make(s, s.Seed);
        Assert.Equal(4, layout.Select(b => b.Tier).Distinct().Count());
        Assert.All(layout, b => Assert.True(b.Shape.Height >= MinThickness(s)));
        Assert.All(layout, b => Assert.True(b.Block.MinWidth() >= MinThickness(s), $"{b.Block.MinWidth()} m"));
    }

    [Fact]
    public void ASmallWidthChangeMovesTheCliffALittle()
    {
        var narrow = CliffMeshes.Preset("long-cliff", "width", 28);
        var wide = CliffMeshes.Preset("long-cliff", "width", 28.1);
        Assert.Equal(
            CliffLayout.Make(narrow, narrow.Seed).Select(b => b.Column).Distinct().Count(),
            CliffLayout.Make(wide, wide.Seed).Select(b => b.Column).Distinct().Count());
        double iou = CliffMeshes.Iou(
            CliffMeshes.FrontOutline(CliffGenerator.Build(narrow, 1)),
            CliffMeshes.FrontOutline(CliffGenerator.Build(wide, 1)));
        Assert.True(iou >= 0.97, $"IoU {iou:F3}");
    }

    [Fact]
    public void TheCliffIsTheUnionOfItsBlocks()
    {
        var s = CliffMeshes.Preset("long-cliff");

        // The blocks the generator lays out: from the sub-seed it picks.
        var layout = CliffGenerator.Layout(s with { Chisel = 0 });
        var (field, _) = CliffGenerator.Pieces(s with { Chisel = 0 }, 1).Single();
        foreach (var b in layout)
        {
            var inside = new V3(b.Shape.Base.X, b.Shape.Base.Y, b.Shape.Base.Z + (b.Shape.Height / 2));
            Assert.True(field.Distance(inside) < 0);
        }

        var points = CliffMeshes.Points(CliffGenerator.Build(s, 1));
        Assert.InRange(points.Max(p => p.X) - points.Min(p => p.X), s.Width * 0.9, s.Width * 1.05);
    }

    [Theory]
    [InlineData(327, false)]
    [InlineData(216, false)]
    [InlineData(300, false)]
    [InlineData(153, false)]
    [InlineData(129, false)]
    [InlineData(76, false)]
    [InlineData(151, false)]
    [InlineData(127, true)]
    [InlineData(80, true)]
    [InlineData(76, true)]
    public void NoBlockIsSunkBelowTheFlatBottom(int random, bool reviewed)
    {
        // Review-final I2: settling doubled a block's sink a try at a time and could take it metres through the blocks
        // under it and out of the cliff's underside. These are the worst of a scan of 400 wide random settings (by their System.Random seeds) and 200
        // with the review's Foot 0, Underside tilt 0, Top tilt 20, Tiers 3 and Depth 20 (reviewed), up to 4.8 m under,
        // two of them at Sink 0.018 and 0.056 (where the cliff stands nearly on the ground line). No block's flat bottom
        // may go below the cliff's, Sink under the ground line.
        var drawn = CliffMeshes.WideDraw(random);
        var s = reviewed ? drawn with { Foot = 0, UndersideTilt = 0, TopTilt = 20, Tiers = 3, Depth = 20 } : drawn;
        var lowest = CliffGenerator.Layout(s).MinBy(b => b.Block.Bounds().Min.Z)!;
        Assert.True(
            lowest.Block.Bounds().Min.Z >= -s.Sink - 1e-9,
            $"block c{lowest.Column} t{lowest.Tier} l{lowest.Layer} reaches {lowest.Block.Bounds().Min.Z:F3} m, under the flat bottom at {-s.Sink:F3} m");
    }

    [Fact]
    public void ABlockSunkThroughTheOnesUnderItStaysInsideTheCliff()
    {
        // The worst of the scan (wide random 327, a tier sunk 4.8 m under the flat bottom): built, its lowest point is
        // within a cell of the flat bottom, Sink under the ground line.
        var s = CliffMeshes.WideDraw(327);
        var layout = CliffGenerator.Layout(s);
        double cell = CliffGenerator.StartCell(s, 0.12, layout);
        double lowest = CliffMeshes.Points(CliffGenerator.Build(s, layout, 0.12)).Min(p => p.Z);
        Assert.True(lowest >= -s.Sink - cell, $"the lowest point is {lowest:F3} m, the flat bottom {-s.Sink:F3} m (cell {cell:F3} m)");
    }
}
