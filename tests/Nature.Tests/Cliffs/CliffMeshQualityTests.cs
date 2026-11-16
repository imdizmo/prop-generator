using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PropGenerator.Nature.Cliffs;
using PropGenerator.Nature.Parameters;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

/// <summary>The surface never tears, folds or turns inside out, whatever the sliders say.</summary>
public class CliffMeshQualityTests
{
#if DEBUG
    /// <summary>
    /// The detail levels the random sweep builds. Unoptimised, a block at the triangle budget takes seconds, so a
    /// Debug build sweeps the two lower levels; Release sweeps all three.
    /// </summary>
    private static readonly double[] SweepLevels = [0.4, 0.12];
#else
    /// <summary>The detail levels the random sweep builds: all three (a Debug build sweeps the two lower ones).</summary>
    private static readonly double[] SweepLevels = [1, 0.4, 0.12];
#endif

    /// <summary>The detail levels a block is built at, as the app and the export build them.</summary>
    private static readonly double[] Levels = [1, 0.4, 0.12];

    /// <summary>How many blocks the sweeps build at once: each build is parallel already, and this bounds the memory.</summary>
    private static readonly ParallelOptions Builds = new() { MaxDegreeOfParallelism = 4 };

    /// <summary>The starting points, in drawer order.</summary>
    private static readonly string[] Presets = ["long-cliff", "tall-cliff", "cliff-top", "layered-cliff"];

    [Theory]
    [InlineData("long-cliff", 1)]
    [InlineData("long-cliff", 0.4)]
    [InlineData("long-cliff", 0.12)]
    [InlineData("tall-cliff", 1)]
    [InlineData("tall-cliff", 0.4)]
    [InlineData("tall-cliff", 0.12)]
    [InlineData("cliff-top", 1)]
    [InlineData("cliff-top", 0.4)]
    [InlineData("cliff-top", 0.12)]
    [InlineData("layered-cliff", 1)]
    [InlineData("layered-cliff", 0.4)]
    [InlineData("layered-cliff", 0.12)]
    public void TheStartingPointsAreClosed(string id, double detail)
    {
        var block = CliffGenerator.Build(CliffPresets.All.Single(p => p.Id == id).Settings, detail);
        Assert.True(block.Indices.Length > 0);
        Assert.Null(CliffMeshes.Open(block));
    }

    [Fact]
    public void TheGroundLineIsTheOrigin()
    {
        // The pivot is the ground line z = 0: the cliff is not lifted, and its flat bottom lies Sink below it.
        var s = CliffMeshes.Preset("long-cliff");
        var points = CliffMeshes.Points(CliffGenerator.Build(s, 1));
        double cell = CliffGenerator.CellSize(s, 1);
        Assert.Equal(0.5, s.Sink);
        Assert.InRange(points.Min(p => p.Z), -s.Sink - cell, -s.Sink + cell);
        Assert.InRange(points.Max(p => p.Z), s.Height * 0.5, s.Height * 1.5);
    }

    [Fact]
    public void EverySliderAtEitherEndOfTheLongCliffIsClosedAtEveryLevel()
    {
        var cases = CliffParameterSet.Instance.Schema.OfType<NumberSpec>()
            .SelectMany(spec => new[] { (spec.Key, spec.Min), (spec.Key, spec.Max) })
            .ToList();
        string?[] failures = new string?[cases.Count];
        Parallel.For(0, cases.Count, Builds, i =>
        {
            var (key, value) = cases[i];
            failures[i] = Check(CliffMeshes.Preset("long-cliff", key, value), Levels) is { } failure ? $"{key} = {value}: {failure}" : null;
        });

        Assert.True(failures.All(f => f is null), string.Join("\n", failures.Where(f => f is not null)));
    }

    [Fact]
    public void FiftyRandomSettingsAreClosedAtEveryLevel()
    {
        // Every slider drawn across its whole range, from a fixed list of seeds.
        string?[] failures = new string?[50];
        Parallel.For(0, 50, Builds, i =>
        {
            failures[i] = Check(CliffMeshes.Random(i + 1), SweepLevels) is { } failure ? $"seed {i + 1}: {failure}" : null;
        });

        Assert.True(failures.All(f => f is null), string.Join("\n", failures.Where(f => f is not null)));
    }

    [Theory]
    [InlineData(2, 1, 1, 2)]
    [InlineData(2, 1, 1, 8)]
    [InlineData(3, 3, 3, 2)]
    [InlineData(2, 25, 1, 2)]
    public void SmallBlocksHaveRockAtEveryLevel(double width, double depth, double height, double detail)
    {
        var settings = CliffSettings.Defaults with { Width = width, Depth = depth, Height = height, Detail = detail, Sink = 0 };
        Assert.Null(Check(settings, Levels));
        Assert.True(CliffGenerator.Build(settings, 0.12).Indices.Length > 0);
    }

    [Fact]
    public void ALowerLevelIsNeverHeavier()
    {
        // A thin pillar faceted finer than the coarse levels' grids.
        var settings = CliffSettings.Defaults with { Width = 2, Depth = 1, Height = 40, Detail = 24, Chisel = 0.05, ChiselSize = 0.2 };
        Assert.Null(Check(settings, Levels));
    }

    [Fact]
    public void NoPartNeedsAHugeGrid()
    {
        // The biggest cliff at the finest detail would need about a billion grid points at its own spacing.
        CliffSettings[] cliffs =
        [
            CliffSettings.Defaults with { Width = 60, Depth = 25, Height = 40, Sink = 2, Detail = 24 },
            CliffSettings.Defaults with { Width = 2, Depth = 1, Height = 40, Detail = 24 },
        ];
        foreach (var settings in cliffs)
        {
            foreach (double level in Levels)
            {
                Assert.True(CliffGenerator.MostGridPoints(settings, CliffGenerator.StartCell(settings, level)) <= 8_000_000);
            }
        }
    }

    [Fact]
    public void AHugeCliffStaysInBudget()
    {
        // 60 m wide, 40 m tall and 25 m deep, at the finest detail.
        var s = CliffSettings.Defaults with { Width = 60, Height = 40, Depth = 25, Detail = 24 };
        foreach (double level in Levels)
        {
            int triangles = CliffGenerator.Build(s, level).Indices.Length / 3;
            Assert.InRange(triangles, 1, CliffGenerator.MaxTriangles * level * level);
            Assert.True(CliffGenerator.MostGridPoints(s, CliffGenerator.StartCell(s, level)) <= 8_000_000);
        }
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(4294967295u)]
    public void ExtremeSeedsBuild(uint seed)
    {
        // Every count slider at its top.
        var s = CliffMeshes.Preset("long-cliff") with { Seed = seed, Blocks = 12, Tiers = 4, Layers = 12, Steps = 8, Bites = 8, Grooves = 4 };
        foreach (double level in Levels)
        {
            var block = CliffGenerator.Build(s, level);
            Assert.True(block.Indices.Length > 0, $"level {level} is empty");
            Assert.Null(CliffMeshes.Open(block));
        }
    }

    [Fact]
    public void TheLongCliffHasNoInsideOutOrEmptyTriangles()
    {
        // Each triangle faces the way the part's field grows (its outside), measured at its middle.
        int triangles = 0;
        int inward = 0;
        foreach (var (part, mesh) in CliffGenerator.Pieces(CliffMeshes.Preset("long-cliff"), 1))
        {
            for (int t = 0; t < mesh.Indices.Count; t += 3)
            {
                var (normal, area) = CliffMeshes.Face(mesh.Points, mesh.Indices, t);
                Assert.True(area >= 1e-10, $"triangle {t / 3} has area {area}");
                var middle = (mesh.Points[mesh.Indices[t]] + mesh.Points[mesh.Indices[t + 1]] + mesh.Points[mesh.Indices[t + 2]]) / 3;
                const double H = 1e-4;
                var gradient = new V3(
                    part.Distance(middle + new V3(H, 0, 0)) - part.Distance(middle - new V3(H, 0, 0)),
                    part.Distance(middle + new V3(0, H, 0)) - part.Distance(middle - new V3(0, H, 0)),
                    part.Distance(middle + new V3(0, 0, H)) - part.Distance(middle - new V3(0, 0, H)));
                triangles++;
                inward += V3.Dot(normal, gradient) > 0 ? 0 : 1;
            }
        }

        Assert.True(inward <= triangles * 0.005, $"{inward} of {triangles} triangles face inward");
    }

    [Fact]
    public void ASteppedCrackKeepsTheFoldBudgetAtFullDetail()
    {
        // Random 56 of the batch (two cracks, each setting one side back 1.29 m over 0.40 m): its full-detail mesh keeps
        // the fold budget as meshed, at most 4 per 10,000 triangles (it had 103 in 190,034).
        var s = CliffMeshes.Random(56);
        foreach (var (_, mesh, _) in CliffGenerator.Unsimplified(s, CliffGenerator.Layout(s), 1))
        {
            double folds = CliffMeshes.Folds(mesh, 150) * mesh.Indices.Count / 2;
            Assert.True(folds <= 4.0 * mesh.TriangleCount / 10_000, $"{folds:F0} folds over 150° in {mesh.TriangleCount} triangles");
        }
    }

    [Theory]
    [InlineData("cliff-top", 5u)]
    [InlineData("cliff-top", 16u)]
    [InlineData("layered-cliff", 9u)]
    [InlineData("layered-cliff", 24u)]
    public void ChippedSeedsKeepTheFoldBudgetAtFullDetail(string id, uint seed)
    {
        // Ruling 37 (review 8-8c Important 1): starting-point seeds whose faceted chips folded the full-detail mesh
        // (23 to 27 folds over 150° in about 50,000 triangles) keep the fold budget as meshed: at most 4 per 10,000
        // triangles, the cliff block's own rate (Ruling 29). Their folds are mended where they are (see MeshFold).
        var s = CliffMeshes.Preset(id) with { Seed = seed };
        foreach (var (_, mesh, _) in CliffGenerator.Unsimplified(s, CliffGenerator.Layout(s), 1))
        {
            double folds = CliffMeshes.Folds(mesh, 150) * mesh.Indices.Count / 2;
            Assert.True(folds <= 4.0 * mesh.TriangleCount / 10_000, $"{folds:F0} folds over 150° in {mesh.TriangleCount} triangles");
            Assert.Null(CliffMeshes.Open(mesh));
        }
    }

    [Theory]
    [InlineData("long-cliff")]
    [InlineData("tall-cliff")]
    [InlineData("cliff-top")]
    [InlineData("layered-cliff")]
    public void TheStartingPointsHaveNoFolds(string id)
    {
        // Edges whose two triangles fold back over each other (bend more than 150°), across the whole cliff.
        double folds = 0;
        double edges = 0;
        foreach (var (_, mesh) in CliffGenerator.Pieces(CliffMeshes.Preset(id), 1))
        {
            double count = mesh.Indices.Count / 2.0;
            folds += CliffMeshes.Folds(mesh, 150) * count;
            edges += count;
        }

        Assert.True(folds < edges * 0.001, $"{folds:F0} of {edges:F0} edges fold over by more than 150°");
    }

    [Theory]
    [InlineData("long-cliff", 0.5)]
    [InlineData("tall-cliff", 0.5)]
    [InlineData("cliff-top", 0.5)]
    [InlineData("layered-cliff", 0.5)]
    [InlineData("long-cliff", 0)]
    [InlineData("tall-cliff", 0)]
    [InlineData("cliff-top", 0)]
    [InlineData("layered-cliff", 0)]
    public void TheShapeSlidersAtTheirMostKeepThePieceSound(string id, double sink)
    {
        // Each starting point at its full detail with the shape sliders at their most, sunk or standing on the ground
        // line: closed, rock in every shell, one shell per connected group of blocks, still standing on the ground
        // (reaching z = 0, no shell floating), and every point inside the collision boxes.
        var s = CliffMeshes.Preset(id) with { Foot = 0.4, UndersideTilt = 30, Taper = 0.6, TopTilt = 60, Sink = sink };
        var layout = CliffGenerator.Layout(s);
        var block = CliffGenerator.Build(s, layout, 1);
        double cell = CliffGenerator.StartCell(s, 1, layout);
        Assert.Null(CliffMeshes.Open(block));
        Assert.True(CliffMeshes.Shells(block) <= CliffRules.Groups(layout), $"{CliffMeshes.Shells(block)} shells");
        var points = CliffMeshes.Points(block);
        Assert.True(points.Min(p => p.Z) <= 0.1 * cell, $"the lowest point is {points.Min(p => p.Z):F3} m up (cell {cell:F3} m)");
        foreach (var (_, mesh) in CliffGenerator.Pieces(s, 1))
        {
            Assert.True(CliffMeshes.Volume(mesh) > 0);
            Assert.True(mesh.Points.Min(p => p.Z) <= cell, $"a part floats {mesh.Points.Min(p => p.Z):F3} m up");
        }

        foreach (var p in points)
        {
            Assert.True(
                block.Chunks.Any(c => Math.Abs(p.X - c.Base.X) <= c.HalfX + cell && Math.Abs(p.Y - c.Base.Y) <= c.HalfY + cell && p.Z >= c.Base.Z - cell && p.Z <= c.Top.Z + cell),
                $"{p} is outside every collision box, {block.Chunks.Min(c => Math.Max(Math.Max(Math.Abs(p.X - c.Base.X) - c.HalfX, Math.Abs(p.Y - c.Base.Y) - c.HalfY), Math.Max(c.Base.Z - p.Z, p.Z - c.Top.Z))):F3} m out (cell {cell:F3} m)");
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(31)]
    public void AFillerStaysInsideTheCollisionBoxesWithAllDetailOff(int random)
    {
        // Review-final I1: the joint filler was bounded above the joint by the two blocks' sides and the upper block's
        // upward planes alone at Foot 0, so where a leaning bed's rock fell away from those sides, the filler ran on as
        // a bridge or a lip. These wide random
        // settings at Foot 0, Top tilt 20 and every surface detail off (the worst of a scan of 70 such cliffs; the
        // review's own 0.23 m draw was not kept) had mesh points 3.33 and 1.44 m outside every collision box, the
        // filler bridging out from a leaning tier. Now every point is inside one, within a cell.
        var s = CliffMeshes.Plain(CliffMeshes.WideDraw(random)) with { Foot = 0, TopTilt = 20, Taper = 0, UndersideTilt = 0, Tiers = 2 + (random % 3) };
        var layout = CliffGenerator.Layout(s);
        var block = CliffGenerator.Build(s, layout, 1);
        double cell = CliffGenerator.StartCell(s, 1, layout);
        double worst = CliffMeshes.Points(block).Max(p => CliffMeshes.Outside(block.Chunks, p));
        Assert.True(worst <= cell, $"a point is {worst:F3} m outside every collision box (cell {cell:F3} m)");
    }

    [Fact]
    public void TheBoundFillerKeepsTheFoldBudgetAtFullDetail()
    {
        // The batch's random 0 (a footed, leaning, heavily chiselled three-tier cliff): bounding the joint filler by the
        // upper block's own foot too took it from 12 folds over 150 degrees to 107 in 187,000 triangles at full detail;
        // bounded by the bed's downward planes and the upper block's flat bottom alone, it keeps the fold budget, at
        // most 4 per 10,000 triangles as meshed.
        var s = CliffMeshes.WideRandom(0);
        foreach (var (_, mesh, _) in CliffGenerator.Unsimplified(s, CliffGenerator.Layout(s), 1))
        {
            double folds = CliffMeshes.Folds(mesh, 150) * mesh.Indices.Count / 2;
            Assert.True(folds <= 4.0 * mesh.TriangleCount / 10_000, $"{folds:F0} folds over 150° in {mesh.TriangleCount} triangles");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0.12)]
    public void AHeavySmudgeStaysInsideTheCollisionBoxes(double level)
    {
        // Review-final Minor 1: the smudge pushes the rock out past its blocks, by up to Smudge, and the boxes were the
        // blocks' own (the Long cliff at Smudge 1.5 stood 0.64 m out of them at full detail, 0.70 m at 0.12). The boxes
        // grow by that reach, so every point is inside one, within a cell; there are still at most 32.
        var s = CliffMeshes.Preset("long-cliff") with { Smudge = 1.5 };
        var layout = CliffGenerator.Layout(s);
        var block = CliffGenerator.Build(s, layout, level);
        double cell = CliffGenerator.StartCell(s, level, layout);
        double worst = CliffMeshes.Points(block).Max(p => CliffMeshes.Outside(block.Chunks, p));
        Assert.True(worst <= cell, $"a point is {worst:F3} m outside every collision box (cell {cell:F3} m)");
        Assert.InRange(block.Chunks.Count, 1, CliffExport.MaxCollisionShapes);
    }

    [Theory]
    [InlineData("long-cliff", 10)]
    [InlineData("long-cliff", 30)]
    [InlineData("tall-cliff", 10)]
    [InlineData("tall-cliff", 30)]
    [InlineData("cliff-top", 10)]
    [InlineData("cliff-top", 30)]
    [InlineData("layered-cliff", 10)]
    [InlineData("layered-cliff", 30)]
    public void ATiltedUndersideStillStandsOnTheGroundLine(string id, double tilt)
    {
        // Sink 0 with the bottom tilted: the cliff still rests on the ground line, its lowest mesh point within a tenth
        // of a cell of z = 0 (with a flat bottom it is on it), over seeds 1 to 3.
        for (uint seed = 1; seed <= 3; seed++)
        {
            var s = CliffMeshes.Preset(id) with { Sink = 0, UndersideTilt = tilt, Seed = seed };
            var layout = CliffGenerator.Layout(s);
            double cell = CliffGenerator.StartCell(s, 1, layout);
            double lowest = CliffMeshes.Points(CliffGenerator.Build(s, layout, 1)).Min(p => p.Z);
            Assert.True(Math.Abs(lowest) <= 0.1 * cell, $"seed {seed}: the lowest point is {lowest:F3} m (cell {cell:F3} m)");
        }
    }

    [Theory]
    [InlineData(1u, 0.05, "379D82E5E198AE895F224AB233822347A35FA02276F2B44335F4D3D237A9B5B0")]
    [InlineData(1u, 0.5, "BB69A61E802AEEC23CEA6E60E0AD9CB6DB4C3DF6C69A239DEE87BA96B2C47929")]
    [InlineData(2u, 0.05, "2950F2C3F932BCDE10B34588DF9EAE96E6DDAF63264D776BF56335BEB3D4C225")]
    [InlineData(2u, 0.5, "CAC29FE899E6C5E4B699D8E82572BEEB5C378C161DDE3C4FF4EDA7958898CC7A")]
    [InlineData(3u, 0.05, "5E514BCE269B175F1F9C30DE92F92DC063C51DD18264B3418ECC68C697469172")]
    [InlineData(3u, 0.5, "2CAC073AA7BB2FF4E9E0E7C3DA225669DE4F25E49471072CAB0BC6301D95AFAF")]
    [InlineData(4u, 0.05, "D2EA5BB02F656CBE69808261101C6C0171BADEF6D2C4EE4F2220CF6C9CD3FD33")]
    [InlineData(4u, 0.5, "E64B5D60A02FF3F30AA77525F401C7CD19DF41CB4C5091BEA6F16B5E43EA312E")]
    public void TheShapeSlidersAtTheirDefaultsKeepTheMeshesTheyHadBeforeThem(uint seed, double sink, string expected)
    {
        // Ruling 44: with every shape slider of Task 9b at its default, a cliff the sliders could already make is the
        // same mesh as before them, to the bit. These are Cliff top's full detail at Top tilt 25 (the old range's top),
        // whose tier joints the golden samples don't reach; the hashes were taken from 88f27fe's code, and again for
        // the final review's fixes (Ruling 48): I2's settle, held to the floor of the blocks under it, and I1's filler,
        // bounded by the upper block's flat bottom and the bed's downward planes at every Foot (it was so only at Foot > 0).
        var s = CliffMeshes.Preset("cliff-top", "top-tilt", 25) with { Seed = seed, Sink = sink };
        var block = CliffGenerator.Build(s, 1);
        byte[] bytes = [.. MemoryMarshal.AsBytes(block.Positions.AsSpan()), .. MemoryMarshal.AsBytes(block.Indices.AsSpan())];
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public void EachPartIsClosedOnItsOwn()
    {
        foreach (var settings in Presets.Select(id => CliffMeshes.Preset(id)).Append(CliffMeshes.Random(3)).Append(CliffMeshes.Random(17)))
        {
            foreach (var (_, mesh) in CliffGenerator.Pieces(settings, 1))
            {
                Assert.Null(CliffMeshes.Open(mesh));
                Assert.True(CliffMeshes.Volume(mesh) > 0);
            }
        }
    }

    /// <summary>
    /// What is wrong with a block's levels, or null: each level non-empty if the first is, empty if the first is, never
    /// heavier than the level before, within the triangle budget, finite, and closed. The levels share one pick of the
    /// sub-seed, as the export's do (each level picks the same one on its own; see CliffRulesTests).
    /// </summary>
    private static string? Check(CliffSettings settings, double[] levels)
    {
        int above = int.MaxValue;
        bool rock = false;
        var layout = CliffGenerator.Layout(settings);
        for (int l = 0; l < levels.Length; l++)
        {
            var block = CliffGenerator.Build(settings, layout, levels[l]);
            int triangles = block.Indices.Length / 3;
            if (l == 0)
            {
                rock = triangles > 0;
            }
            else if ((triangles > 0) != rock)
            {
                return $"level {levels[l]} has {triangles} triangles, level {levels[0]} {(rock ? "has rock" : "none")}";
            }

            if (triangles > above)
            {
                return $"level {levels[l]} has {triangles} triangles, more than the {above} of the level before";
            }

            if (triangles > CliffGenerator.MaxTriangles)
            {
                return $"level {levels[l]} has {triangles} triangles";
            }

            if (triangles > 0 && (!block.Positions.All(float.IsFinite) || !block.Normals.All(float.IsFinite) || !block.Colours.All(float.IsFinite)))
            {
                return $"level {levels[l]} is not finite";
            }

            if (triangles > 0 && CliffMeshes.Open(block) is { } open)
            {
                return $"level {levels[l]}: {open}";
            }

            above = triangles;
        }

        return null;
    }
}
