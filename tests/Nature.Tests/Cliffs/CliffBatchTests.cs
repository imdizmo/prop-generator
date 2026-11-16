using PropGenerator.Nature.Cliffs;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

/// <summary>
/// The acceptance batches: every starting point over fifty seeds and two hundred random settings (every slider across its
/// whole range, see <see cref="CliffMeshes.WideRandom"/>), at every detail level,
/// are sound (closed, one shell per connected group of blocks, few folds, thick, grounded, cut within the limit, inside
/// the collision boxes and not below the flat bottom), and seeds give different cliffs. They are slow, so they carry the trait Category = Batch and run on their own
/// (<c>--filter-trait "Category=Batch"</c>; the full runs leave them out with <c>--filter-not-trait</c>). Each case is
/// one starting point, or twenty random settings, so none runs near the five-minute hang-dump limit.
/// </summary>
[Trait("Category", "Batch")]
public class CliffBatchTests
{
    /// <summary>How many random settings one case of the random batch builds.</summary>
    private const int Chunk = 20;

    /// <summary>
    /// The most folds over 150° a mesh may have per 10,000 triangles: the cliff block's own rate (round 8's Cliff 01 has
    /// 13 in 33,540 triangles, 3.9 per 10,000).
    /// </summary>
    private const double FoldsPer10K = 4;

    /// <summary>A shell with more than this share of the mesh's volume is a piece of the cliff.</summary>
    private const double ShellShare = 0.01;

    /// <summary>The detail levels a cliff is built at, as the app and the export build them.</summary>
    private static readonly double[] Levels = [1, 0.4, 0.12];

    /// <summary>How many cliffs a batch builds at once: each build is parallel already, and this bounds the memory.</summary>
    private static readonly ParallelOptions Builds = new() { MaxDegreeOfParallelism = 4 };

    /// <summary>The starting points, in drawer order.</summary>
    public static TheoryData<string> Presets => ["long-cliff", "tall-cliff", "cliff-top", "layered-cliff"];

    /// <summary>The random batch's cases: the first of each run of <see cref="Chunk"/> settings, 0 to 180.</summary>
    public static TheoryData<int> RandomChunks => [.. Enumerable.Range(0, 200 / Chunk).Select(c => c * Chunk)];

    [Theory]
    [MemberData(nameof(Presets))]
    public void EveryPresetOverFiftySeedsIsSound(string id)
    {
        var settings = CliffMeshes.Preset(id);
        AssertSound([.. Enumerable.Range(1, 50).Select(seed => ($"{id} seed {seed}", settings with { Seed = (uint)seed }))]);
    }

    [Theory]
    [MemberData(nameof(RandomChunks))]
    public void TwoHundredRandomSettingsAreSound(int first)
    {
        AssertSound([.. Enumerable.Range(first, Chunk).Select(i => ($"random {i}", CliffMeshes.WideRandom(i)))]);
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void SeedsGiveDifferentCliffs(string id)
    {
        // Twenty seeds at the starting point's settings: no two front outlines (each scaled to its own bounds) overlap
        // by more than 0.95.
        var settings = CliffMeshes.Preset(id);
        bool[][,] outlines = new bool[20][,];
        Parallel.For(0, outlines.Length, Builds, i => outlines[i] = CliffMeshes.FrontOutline(CliffGenerator.Build(settings with { Seed = (uint)(i + 1) }, 1)));
        var close = new List<string>();
        for (int a = 0; a < outlines.Length; a++)
        {
            for (int b = a + 1; b < outlines.Length; b++)
            {
                double iou = CliffMeshes.Iou(outlines[a], outlines[b]);
                if (iou > 0.95)
                {
                    close.Add($"seeds {a + 1} and {b + 1}: {iou:F3}");
                }
            }
        }

        Assert.True(close.Count == 0, string.Join("\n", close));
    }

    /// <summary>Builds the cliffs a few at a time and fails with every fault found (see <see cref="Fault"/>).</summary>
    private static void AssertSound(List<(string Name, CliffSettings Settings)> cliffs)
    {
        string?[] faults = new string?[cliffs.Count];
        Parallel.For(0, cliffs.Count, Builds, i => faults[i] = Fault(cliffs[i].Settings) is { } fault ? $"{cliffs[i].Name}: {fault}" : null);
        Assert.True(faults.All(f => f is null), string.Join("\n", faults.Where(f => f is not null)));
    }

    /// <summary>
    /// What is wrong with a cliff, or null. Its picked layout: every block at least Min thickness, and no cut deeper than
    /// 40% of its block's thickness along the cut. Each level, built from that layout (each mesh let go before the next
    /// is built): rock; closed and 2-manifold, with no open edge and no empty triangle; no inside-out shell; no more
    /// shells above <see cref="ShellShare"/> of the volume than the layout has connected groups of blocks; every shell
    /// reaching down to the ground line (nothing floating); and at most <see cref="FoldsPer10K"/> folds over 150° per
    /// 10,000 triangles as meshed, before the thinning (see <see cref="MeshSimplify"/>), which adds none (at the lower
    /// levels not counting the folds Ruling 39 excuses, see <see cref="Excused"/>): every rule but the fold rate is
    /// checked on the thinned mesh, and its folds are no more than before. Every point of the thinned meshes is inside
    /// one of the cliff's collision boxes, and none is below the flat bottom (Sink under the ground line), within a cell
    /// (review-final I3).
    /// </summary>
    private static string? Fault(CliffSettings s)
    {
        var layout = CliffGenerator.Layout(s);
        double thin = CliffLayout.MinThickness(s);
        foreach (var b in layout)
        {
            string where = $"block c{b.Column} t{b.Tier} l{b.Layer}";
            if (b.Block.MinWidth() < thin - 1e-9)
            {
                return $"{where} is {b.Block.MinWidth():F3} m thick, under {thin:F3} m";
            }

            foreach (var cut in b.Block.Cuts)
            {
                var (depth, limit) = CliffMeshes.CutDepth(b.Block, cut);
                if (depth > limit + 1e-6)
                {
                    return $"{where} has a cut {depth:F3} m deep, past its limit {limit:F3} m";
                }
            }
        }

        int groups = CliffRules.Groups(layout);
        var chunks = CliffGenerator.Chunks(s, layout);
        string? overBudget = null;
        foreach (double level in Levels)
        {
            var meshed = CliffGenerator.Unsimplified(s, layout, level);
            if (meshed.Count == 0)
            {
                return $"level {level} has no rock";
            }

            // A closed mesh has 1.5 edges per triangle. The fold rate is measured as meshed, and reported after every
            // level's thinned mesh is checked, so every setting's thinning is.
            var folded = new List<(double Folds, int Triangles, int Excused, double Widest, double Cell)>();
            foreach (var (field, mesh, meshCell) in meshed)
            {
                double folds = CliffMeshes.Folds(mesh, 150) * mesh.Indices.Count / 2;
                var (excused, widest) = level < 1 && folds > 0 ? Excused(mesh, field, meshCell, s) : (0, 0.0);
                folded.Add((folds, mesh.TriangleCount, excused, widest, meshCell));
            }

            var parts = CliffGenerator.Finish(s, meshed);
            double cell = CliffGenerator.StartCell(s, level, layout);
            for (int part = 0; part < parts.Count; part++)
            {
                var mesh = parts[part].Mesh;
                if (CliffMeshes.Open(mesh) is { } open)
                {
                    return $"level {level}: {open}";
                }

                for (int t = 0; t < mesh.Indices.Count; t += 3)
                {
                    if (CliffMeshes.Face(mesh.Points, mesh.Indices, t).Area < 1e-10)
                    {
                        return $"level {level}: triangle {t / 3} is empty";
                    }
                }

                var shells = Shells(mesh);
                double total = shells.Sum(shell => shell.Volume);
                if (shells.FirstOrDefault(shell => shell.Volume <= 0) is { Count: > 0 } inside)
                {
                    return $"level {level}: a shell of {inside.Count} triangles is inside out ({inside.Volume:F4} m³)";
                }

                int pieces = shells.Count(shell => shell.Volume > ShellShare * total);
                if (pieces > groups)
                {
                    return $"level {level}: {pieces} shells above {ShellShare:P0} of the volume, for {groups} groups of blocks";
                }

                if (shells.FirstOrDefault(shell => shell.Lowest > cell) is { Count: > 0 } floating)
                {
                    return $"level {level}: a shell of {floating.Volume:F2} m³ floats, its lowest point {floating.Lowest:F2} m up";
                }

                var low = mesh.Points.MinBy(p => p.Z);
                if (low.Z < -s.Sink - cell)
                {
                    return $"level {level}: a point at {low} is {-s.Sink - low.Z:F3} m below the flat bottom (cell {cell:F3} m)";
                }

                var outside = mesh.Points.MaxBy(p => CliffMeshes.Outside(chunks, p));
                if (CliffMeshes.Outside(chunks, outside) > cell)
                {
                    return $"level {level}: a point at {outside} is {CliffMeshes.Outside(chunks, outside):F3} m outside every collision box (cell {cell:F3} m)";
                }

                double thinned = CliffMeshes.Folds(mesh, 150) * mesh.Indices.Count / 2;
                if (thinned > folded[part].Folds + 0.5)
                {
                    return $"level {level}: thinning {folded[part].Triangles} triangles to {mesh.TriangleCount} took the folds over 150° from {folded[part].Folds:F0} to {thinned:F0}";
                }

                var fold = folded[part];
                if (fold.Folds - fold.Excused > FoldsPer10K * fold.Triangles / 10_000)
                {
                    overBudget ??= $"level {level}: {fold.Folds:F0} folds over 150° in {fold.Triangles} triangles as meshed ({fold.Excused} excused; the widest {fold.Widest:F3} m, the cell {fold.Cell:F3} m)";
                }
            }
        }

        return overBudget;
    }

    /// <summary>
    /// Ruling 39: at the lower detail levels (distant views), the folds of a mesh as meshed that are excused: each fold
    /// under one grid cell wide (its two triangles' far corners closer than a cell, a sub-cell fin under a pixel there),
    /// so long as no collapse can take any of the mesh's folds away (MeshFold, run again on a copy, makes none). Also the
    /// widest fold, in metres.
    /// </summary>
    private static (int Excused, double Widest) Excused(SolidMesh mesh, ICliffPart field, double cell, CliffSettings s)
    {
        var widths = CliffMeshes.FoldWidths(mesh, 150);
        var copy = new SolidMesh();
        copy.Points.AddRange(mesh.Points);
        copy.Indices.AddRange(mesh.Indices);
        bool fixable = MeshFold.Collapse(copy, field, cell, CliffGenerator.SimplifyCells * cell, [0, -s.Sink]) > 0;
        return (fixable ? 0 : widths.Count(w => w < cell), widths.DefaultIfEmpty(0).Max());
    }

    /// <summary>Each separate shell of a mesh: its triangle count, its signed volume and its lowest point's height.</summary>
    private static List<(int Count, double Volume, double Lowest)> Shells(SolidMesh mesh)
    {
        int[] parent = [.. Enumerable.Range(0, mesh.Points.Count)];
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            parent[Root(mesh.Indices[t + 1])] = Root(mesh.Indices[t]);
            parent[Root(mesh.Indices[t + 2])] = Root(mesh.Indices[t]);
        }

        var shells = new Dictionary<int, (int Count, double Volume, double Lowest)>();
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            var a = mesh.Points[mesh.Indices[t]];
            var b = mesh.Points[mesh.Indices[t + 1]];
            var c = mesh.Points[mesh.Indices[t + 2]];
            int root = Root(mesh.Indices[t]);
            var (count, volume, lowest) = shells.GetValueOrDefault(root, (0, 0, double.PositiveInfinity));
            shells[root] = (count + 1, volume + (V3.Dot(a, V3.Cross(b, c)) / 6), Math.Min(lowest, Math.Min(a.Z, Math.Min(b.Z, c.Z))));
        }

        return [.. shells.Values];

        int Root(int i)
        {
            while (parent[i] != i)
            {
                i = parent[i] = parent[parent[i]];
            }

            return i;
        }
    }
}
