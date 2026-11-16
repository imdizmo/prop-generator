using System.Runtime.InteropServices;
using System.Text.Json;
using PropGenerator.Nature.Cliffs;
using PropGenerator.Nature.Parameters;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

/// <summary>How the laid blocks join into one cliff field: side by side they fuse, plates and tiers show a bedding groove.</summary>
public class SlabCliffFieldTests
{
    /// <summary>The settings the hand-laid blocks are joined with: the Long cliff without its surface detail, so only the joints shape the surface.</summary>
    private static readonly CliffSettings Plain = CliffMeshes.Plain(CliffSettings.Defaults);

    /// <summary>How many golden samples a small case has anywhere in the bounds (drawn first), and again within 0.3 m of the surface.</summary>
    private const int GoldenFar = 1000;

    /// <summary>How many golden samples each large case has anywhere in the bounds, and again within 0.3 m of the surface (the field is slow with this many blocks).</summary>
    private const int GoldenLarge = 400;

    /// <summary>Nearest and Tag are pinned at every this-many-th golden sample of a small case (all of a large case's), to keep the data file small.</summary>
    private const int GoldenPinEvery = 2;

    /// <summary>The golden <c>Nearest</c> pair's other distance is stored held to this, in metres.</summary>
    private const double GoldenOtherCap = 100;

    /// <summary>The golden values are stored in steps of 1 / this, in metres (10⁻⁷ m, well inside the 10⁻⁶ m tolerance).</summary>
    private const double GoldenScale = 1e7;

    /// <summary>
    /// The golden samples' settings: the four starting points at seeds 1 and 2, three random settings with every Surface
    /// and Cracks slider high, then two large layouts at full detail (well over the 40 blocks and joints where the cull
    /// grid cuts its lists): the random sweep's seed 38 and the Long cliff with the most blocks, tiers and layers.
    /// </summary>
    private static readonly (string Name, CliffSettings Settings, int Points)[] GoldenCases =
    [
        .. new[] { "long-cliff", "tall-cliff", "cliff-top", "layered-cliff" }
            .SelectMany(id => new uint[] { 1, 2 }.Select(seed => ($"{id} seed {seed}", CliffMeshes.Preset(id) with { Seed = seed }, GoldenFar))),
        .. new[] { 1, 2, 3 }.Select(seed => ($"detailed random {seed}", Detailed(seed), GoldenFar)),
        ("random sweep 38", CliffMeshes.Random(38), GoldenLarge),
        ("long cliff 12 blocks, 4 tiers, 12 layers", CliffMeshes.Preset("long-cliff") with { Blocks = 12, Tiers = 4, Layers = 12 }, GoldenLarge),
    ];

    [Fact]
    public void SideBySideBlocksMeetInANarrowCrack()
    {
        // Two blocks side by side whose fronts meet in a valley at x = 0, each turned 35° the other way. They join hard
        // (the hand-made pieces' joins, Ruling 34), and a V-crack opens along the line where their fronts meet: at
        // most Joint crack wide across the surface, about three quarters of that deep, and nothing beyond it. With Joint
        // crack 0, or on a grid too coarse for it (under two cells wide), the join is the bare hard seam.
        var left = CliffMeshes.Laid(CliffMeshes.Box(-3, 0.5, 0, 3, -0.5, 4, frontTurn: 35), 0, 0, 0, depth: 3);
        var right = CliffMeshes.Laid(CliffMeshes.Box(-0.5, 3, 0, 3, -0.5, 4, frontTurn: -35), 1, 0, 0, depth: 3);
        double Hard(V3 p) => Math.Min(left.Block.Distance(p), right.Block.Distance(p));
        const double Crack = 0.2;
        var cracked = new SlabCliffField([left, right], Plain with { JointCrack = Crack }, 0);
        double widest = 0;
        double deepest = 0;
        double run = 0;
        for (double x = -1; x <= 1; x += 0.002)
        {
            double extra = Inside(cracked.Distance, x, 2) - Inside(Hard, x, 2);
            Assert.True(extra >= -1e-9, $"the crack adds rock at x {x:F3}");
            run = extra > 1e-3 ? run + 0.002 : 0;
            widest = Math.Max(widest, run);
            deepest = Math.Max(deepest, extra);
            if (Math.Abs(x) > Crack)
            {
                Assert.True(extra <= 1e-9, $"the crack reaches x {x:F3}");
            }
        }

        // Across x, the crack's opening on the 35° faces is its width times cos 35°; in along y its depth over cos 35°.
        Assert.InRange(widest, 0.7 * Crack * Math.Cos(35 * Math.PI / 180), (Crack * Math.Cos(35 * Math.PI / 180)) + 0.01);
        Assert.InRange(deepest, 0.5 * 0.75 * Crack, (0.75 * Crack / Math.Cos(35 * Math.PI / 180)) + 0.01);

        // On a grid of 1.33 cells across the crack (between the one cell where it is none and the two where it is
        // whole) it is cut about half as deep.
        var ramp = new SlabCliffField([left, right], Plain with { JointCrack = Crack }, 1.5 * (Crack / 1.5));
        double half = 0;
        for (double x = -0.3; x <= 0.3; x += 0.002)
        {
            half = Math.Max(half, Inside(ramp.Distance, x, 2) - Inside(Hard, x, 2));
        }

        Assert.InRange(half, 0.3 * deepest, 0.7 * deepest);

        foreach (var bare in new[]
        {
            new SlabCliffField([left, right], Plain with { JointCrack = 0 }, 0),
            new SlabCliffField([left, right], Plain with { JointCrack = Crack }, 1.5 * Crack),
        })
        {
            for (double x = -1; x <= 1; x += 0.01)
            {
                Assert.Equal(Inside(Hard, x, 2), Inside(bare.Distance, x, 2), 9);
            }
        }
    }

    [Fact]
    public void TheFieldHasNoStepWhereTheNearestBlockChanges()
    {
        // The dark teeth down the joins of the Long cliff (seed 1, with its chips and wear): each level's chips and wear
        // were those of the block nearest the point alone, so where the nearest block changed from one neighbour to the
        // other the cut jumped from one block's edges to the other's, by as much as the fade let through (the joined
        // surface lies outside both blocks there, so some always was): a step in the field the mesh shows as notches.
        // Across every join between neighbouring columns, near the surface, the field never steps: where it changes by
        // more than three times the step between two samples 2 mm apart (a distance field changes by at most the step,
        // a steep chip's side by about three times it), at least half of that change is not made within one tenth of
        // the step (a slope spreads it evenly; a step keeps it all in one place however fine the samples).
        var s = CliffMeshes.Preset("long-cliff") with { EdgeChips = 0.3, ChipSize = 0.6, EdgeWear = 0.25 };
        var layout = CliffRules.Choose(s).Layout;
        var field = new SlabCliffField(layout, s, 1.5 * CliffGenerator.StartCell(s, 1, layout));
        const double Step = 0.002;
        int read = 0;
        var steps = new List<string>();
        foreach (var (left, right) in layout.Zip(layout.Skip(1)).Where(pair => pair.First.Tier == pair.Second.Tier && pair.First.Layer == pair.Second.Layer))
        {
            var (leftMin, leftMax) = left.Block.Bounds();
            var (rightMin, _) = right.Block.Bounds();
            for (double z = 1; z <= 8; z += 0.5)
            {
                for (double y = Math.Min(leftMin.Y, rightMin.Y) - 0.2; y <= Math.Min(leftMin.Y, rightMin.Y) + 2.5; y += 0.1)
                {
                    double last = double.NaN;
                    for (double x = rightMin.X - 0.3; x <= leftMax.X + 0.3; x += Step)
                    {
                        double f = field.Distance(new V3(x, y, z));
                        if (Math.Abs(f) < 0.1)
                        {
                            read++;
                            if (Math.Abs(f - last) > 3 * Step && Steepest(x - Step, y, z) > 0.5 * Math.Abs(f - last))
                            {
                                steps.Add($"{f - last:F4} m at ({x:F3}, {y:F2}, {z:F1})");
                            }
                        }

                        last = f;
                    }
                }
            }
        }

        Assert.True(read > 10_000, $"only {read} samples near the surface");
        Assert.True(steps.Count == 0, $"{steps.Count} steps in the field across the joins, the first: {string.Join("; ", steps.Take(5))}");

        // The largest change of the field between ten samples evenly over one step from x on.
        double Steepest(double x, double y, double z)
        {
            double most = 0;
            double before = field.Distance(new V3(x, y, z));
            for (int k = 1; k <= 10; k++)
            {
                double now = field.Distance(new V3(x + (k * Step / 10), y, z));
                most = Math.Max(most, Math.Abs(now - before));
                before = now;
            }

            return most;
        }
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public void AShowingCrackKeepsTheCliffWhole(uint seed)
    {
        // At a Detail where Joint crack shows (Ruling 38: from about Detail 16, on a cliff small enough for its grid), a
        // 0.2 m crack along every seam leaves the built cliff closed, with no more shells than connected groups of
        // blocks: the crack never opens a hole through a block or splits one.
        var s = CliffMeshes.Preset("tall-cliff") with { Detail = 16, JointCrack = 0.2, Seed = seed };
        var layout = CliffGenerator.Layout(s);
        Assert.True(CliffGenerator.StartCell(s, 1, layout) < 0.1, "the grid is too coarse for the crack to show");
        int groups = CliffRules.Groups(layout);
        foreach (var (_, mesh) in CliffGenerator.Pieces(s, layout, 1))
        {
            Assert.Null(CliffMeshes.Open(mesh));
            var shells = CliffMeshes.ShellVolumes(mesh);
            Assert.True(shells.Count(v => v > 0.01 * shells.Sum()) <= groups, $"{shells.Count} shells for {groups} groups");
        }
    }

    [Fact]
    public void CoplanarNeighboursStayFlat()
    {
        // Two blocks side by side sharing their front, top and back planes, overlapping over x -0.5 to 0.5: across the
        // overlap and its hidden ends the front and the top stay on their planes.
        var left = CliffMeshes.Laid(CliffMeshes.Box(-3, 0.5, 0, 3, -0.5, 4), 0, 0, 0);
        var right = CliffMeshes.Laid(CliffMeshes.Box(-0.5, 3, 0, 3, -0.5, 4), 1, 0, 0);
        var field = new SlabCliffField([left, right], Plain, 0);
        for (double x = -1.5; x <= 1.5; x += 0.01)
        {
            Assert.InRange(Inside(field.Distance, x, 2), -0.01, 0.01);
            Assert.InRange(1 - Depth(field.Distance, new V3(x, 1.5, 5), new V3(0, 0, -1)), -0.01, 0.01);
        }
    }

    [Fact]
    public void ALedgeStepSetsTheWholeSideBack()
    {
        // A 4 m block with one ledge whose step sets the rock above it back 0.5 m. Above the ledge every point 0.25 m in
        // from the front is air right up to the top: the block above the ledge is a smaller block. Read as a push by how
        // far the nearest face faces sideways, the step left the top's rim whole (near the top the top plane is the
        // nearest face) over a 45° underside: a lip the mesh folded (random settings with Ledge step up to 1.5 m).
        var block = CliffMeshes.Laid(CliffMeshes.Box(-2, 2, 0, 3, 0, 4), 0, 0, 0);
        var stepped = CliffMeshes.Plain(CliffSettings.Defaults) with { Height = 4, Ledges = 1, LedgeStep = 0.5, CreaseWobble = 0 };
        var s = Enumerable.Range(1, 50).Select(seed => stepped with { Seed = (uint)seed })
            .First(t => new SlabCliffField([block], t, 0) is var f && f.Distance(new V3(0, 0.25, 3)) > 0 && f.Distance(new V3(0, 0.25, 1)) < 0);
        var field = new SlabCliffField([block], s, 0);
        for (double z = 3; z <= 3.98; z += 0.02)
        {
            Assert.True(field.Distance(new V3(0, 0.25, z)) > 0, $"rock 0.25 m in from the front at z {z:F2}");
        }

        // The top stays where it was, over the smaller block.
        Assert.InRange(field.Distance(new V3(0, 1.5, 3.98)), -0.03, -0.01);
    }

    [Fact]
    public void TheBeddingGrooveRunsOnlyWhereThePlatesAreFlush()
    {
        // A plate on a plate, flush at the back (y = 3) and set back 0.5 m at the front (its front at y = 0.5, the lower
        // plate's at 0), the joint at z = 2. At the back the groove's V pulls both faces in at the joint. At the front
        // the upper plate's wall stands on the lower plate's top as it is: half a V there would undercut the wall into a
        // slot along the ledge that the mesh folds over itself to show (Layered cliff, seed 4).
        var lower = CliffMeshes.Laid(CliffMeshes.Box(-2, 2, 0, 3, -0.3, 2), 0, 0, 0);
        var upper = CliffMeshes.Laid(CliffMeshes.Box(-2, 2, 0.5, 3, 1.7, 4), 0, 0, 1);
        var field = new SlabCliffField([lower, upper], Plain, 0);
        for (double z = 2.01; z <= 2.2; z += 0.01)
        {
            // 1 cm in front of the wall: outside it by that much, with no groove.
            Assert.Equal(0.01, field.Distance(new V3(0, 0.49, z)), 6);
        }

        // 1 cm behind the back face, just above the joint, the groove makes it outside.
        Assert.True(field.Distance(new V3(0, 2.99, 2.01)) > 0.05);
    }

    [Fact]
    public void ARimTooSmallForTheGridCutsNothingAbove()
    {
        // The lower plate's front rim chamfered 0.21 m deep (at 45°), the upper plate flush on it. 0.15 m above the joint
        // and 5 cm inside the upper plate's front, the chamfer's mirror cuts the upper plate's base back (the two rims
        // meet in a V) where the grid can show it; on a grid of 0.2 m cells (a floor of 0.3 m), where the chamfer is
        // under two cells, it cuts nothing there: meshed, so small a V only folded (big random cliffs).
        double c = 1 / Math.Sqrt(2);
        var lower = CliffMeshes.Laid(
            new ConvexBlock([.. CliffMeshes.Box(-2, 2, 0, 3, -0.3, 2).Shell, new Plane(new V3(0, -c, c), c * 1.7)], []), 0, 0, 0);
        var upper = CliffMeshes.Laid(CliffMeshes.Box(-2, 2, 0, 3, 1.7, 4), 0, 0, 1);
        var p = new V3(0, 0.05, 2.15);
        Assert.InRange(new SlabCliffField([lower, upper], Plain, 0).Distance(p), 0.06, 0.08);
        Assert.True(new SlabCliffField([lower, upper], Plain, 0.3).Distance(p) < 0);

        // A chamfer the grid does show is still matched on the coarse grid.
        var deep = CliffMeshes.Laid(
            new ConvexBlock([.. CliffMeshes.Box(-2, 2, 0, 3, -0.3, 2).Shell, new Plane(new V3(0, -c, c), c * 1.2)], []), 0, 0, 0);
        Assert.True(new SlabCliffField([deep, upper], Plain, 0.3).Distance(new V3(0, 0.05, 2.3)) > 0);
    }

    [Fact]
    public void ARimCutsNothingPastItsBedsSides()
    {
        // Sunk-part clipping (Task 8, review 8-8c Important 2): an upper plate hanging 1 m past the +x end of the plate
        // it rests on, whose top rim there is chamfered 0.3 m. The bed's rim planes cut the upper plate's base to match
        // only over the bed: past the bed's sides by more than the chamfer's own depth (0.21 m) and the groove's half
        // width, the upper plate is its own plain block, with no wedge trimmed along the chamfer's plane carried on past
        // the bed's end.
        double c = 1 / Math.Sqrt(2);
        var lower = CliffMeshes.Laid(
            new ConvexBlock([.. CliffMeshes.Box(-2, 2, 0, 3, -0.3, 2).Shell, new Plane(new V3(c, 0, c), c * 3.7)], []), 0, 0, 0);
        var upper = CliffMeshes.Laid(CliffMeshes.Box(-2, 3, 0, 3, 1.7, 4), 0, 0, 1);
        var field = new SlabCliffField([lower, upper], Plain, 0);
        int read = 0;
        for (double x = 2.35; x <= 2.95; x += 0.05)
        {
            for (double y = 0.25; y <= 2.75; y += 0.25)
            {
                for (double z = 1.72; z <= 2.5; z += 0.02)
                {
                    var p = new V3(x, y, z);
                    double own = upper.Block.Distance(p);
                    if (Math.Abs(own) < 0.3)
                    {
                        read++;
                        Assert.True(Math.Abs(field.Distance(p) - own) < 1e-9, $"at {p} the field is {field.Distance(p):F4}, the upper plate's own {own:F4}");
                    }
                }
            }
        }

        Assert.True(read > 100);
    }

    [Fact]
    public void BevelledPlatesKeepTheirOwnRims()
    {
        // Plates whose lower one has 0.3 m chamfers on its front and +x end top rims: below the joint the surface is the
        // lower plate's own chamfer (the upper plate's sunk part and the joint's filler cover none of it, and no ledge
        // or strip stands out on it); across the joint it does not jump; above it the upper plate's base closes back
        // to its face without a step (it matches the chamfer, so no flat lip hangs over it).
        double c = 1 / Math.Sqrt(2);
        var lower = CliffMeshes.Laid(
            new ConvexBlock(
            [
                .. CliffMeshes.Box(-2, 2, 0, 3, -0.3, 2).Shell,
                new Plane(new V3(0, -c, c), c * 1.7),
                new Plane(new V3(c, 0, c), c * 3.7),
            ],
            []),
            0,
            0,
            0);
        var upper = CliffMeshes.Laid(CliffMeshes.Box(-2, 2, 0, 3, 1.7, 4), 0, 0, 1);
        var field = new SlabCliffField([lower, upper], Plain, 0);
        foreach (var (origin, inward) in new[] { (new V3(0, -1, 0), new V3(0, 1, 0)), (new V3(3, 1.5, 0), new V3(-1, 0, 0)) })
        {
            // From the outside in, in metres past the face (y = 0 at the front, x = 2 at the end).
            double Inset(Func<V3, double> f, double z) => Depth(f, origin + new V3(0, 0, z), inward) - 1;

            for (double z = 1.5; z < 1.995; z += 0.005)
            {
                double own = Inset(lower.Block.Distance, z);
                Assert.InRange(Inset(field.Distance, z) - own, -0.01, 0.01);
            }

            double last = Inset(field.Distance, 2.005);
            Assert.InRange(last - Inset(field.Distance, 1.995), -0.02, 0.02);
            for (double z = 2.01; z <= 2.4; z += 0.005)
            {
                double inset = Inset(field.Distance, z);
                Assert.True(inset <= last + 1e-3, $"a step out at z {z:F3} ({inset:F3} after {last:F3})");
                last = inset;
            }

            Assert.InRange(last, -0.005, 0.005);
        }
    }

    [Fact]
    public void DetailLeavesTheJointCracksAndGroovesOpen()
    {
        // Two plates in each of two columns, the upper sunk 0.3 m into the lower, the joint at z = 3; the columns' fronts
        // meet in a valley at x = 0, each turned 35° the other way, as in SideBySideBlocksMeetInANarrowCrack, and each
        // front ends in a face along x further out (so its cheap bound is not the face on the seam, as on blocks of more
        // than four sides). With Smudge and the creases' depth high, the joint filler (the rock both plates' sides hold
        // near the joint) must still leave the crack between the columns open off the joint's plane, and the bedding
        // groove open on it (review 8-8b-8c round 2, Important 2: its reach grew with the whole surface detail, to
        // about 1.3 m here, and filled the crack all along that band).
        LaidBlock[] blocks =
        [
            Fronted(-3, 0.5, 0, 3, 35, 0, 0),
            Fronted(-0.5, 3, 0, 3, -35, 1, 0),
            Fronted(-3, 0.5, 2.7, 6, 35, 0, 1),
            Fronted(-0.5, 3, 2.7, 6, -35, 1, 1),
        ];
        var s = Plain with { JointCrack = 0.2, Smudge = 1, Creases = 4, CreaseDepth = 0.6, CreaseWidth = 0.5, CreaseWobble = 0 };
        var cracked = new SlabCliffField(blocks, s, 0);
        var bare = new SlabCliffField(blocks, s with { JointCrack = 0 }, 0);

        // 5 cm in on the seam (0.041 m in along the faces' normals) the crack lifts the field by its pull there: 0.225 m
        // on the seam, less 0.041 / 0.45 of it for the depth, about 0.20 m, so the point is outside the rock. The detail's
        // push is the same with the crack and without, so the difference is the crack's alone. Off the plane by 0.5 m to
        // 2 m, above and below.
        for (double z = 1; z <= 5.001; z += 0.05)
        {
            if (Math.Abs(z - 3) >= 0.5)
            {
                var p = new V3(0, 0.05, z);
                double pull = cracked.Distance(p) - bare.Distance(p);
                Assert.True(pull > 0.18, $"the crack pulls the field in by {pull:F4} m at z {z:F2}");
            }
        }

        // On the flat front at x = -2 the surface at the joint's plane sits the groove's 0.1 m further in than 0.4 m off it.
        double at = Inside(cracked.Distance, -2, 3);
        double off = (Inside(cracked.Distance, -2, 2.6) + Inside(cracked.Distance, -2, 3.4)) / 2;
        Assert.InRange(at - off, 0.08, 0.12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.4)]
    public void PlatesShowABeddingGroove(double floor)
    {
        // Two plates of one column, the upper sunk 0.3 m into the lower, their fronts flush at y = 0: at the joint
        // (z = 2) the surface sits 0.1 m in, and away from it on the faces.
        var lower = CliffMeshes.Laid(CliffMeshes.Box(-2, 2, 0, 3, -0.3, 2), 0, 0, 0);
        var upper = CliffMeshes.Laid(CliffMeshes.Box(-2, 2, 0, 3, 1.7, 4), 0, 0, 1);
        var field = new SlabCliffField([lower, upper], Plain, floor);
        double at = Inside(field.Distance, 0, 2);
        Assert.InRange(at - Inside(field.Distance, 0, 1.5), 0.08, 0.12);
        Assert.InRange(at - Inside(field.Distance, 0, 2.5), 0.08, 0.12);

        // The groove is at least the floor wide, or 0.15 m: where the surface is a centimetre or more in from the face.
        const double Step = 0.002;
        int cut = 0;
        for (double z = 1.5; z <= 2.5; z += Step)
        {
            cut += Inside(field.Distance, 0, z) >= 0.01 ? 1 : 0;
        }

        Assert.True(cut * Step >= Math.Max(0.15, floor), $"the groove is {cut * Step:F3} m wide");
    }

    [Fact]
    public void TheEstimateBoundsTheField()
    {
        // The chisel on, on layered, tiered blocks: the estimate is within its error of the field everywhere.
        var s = CliffMeshes.Preset("cliff-top", "layers", 3) with { Chisel = 0.3, ChiselSize = 1.5 };
        var field = new SlabCliffField(CliffLayout.Make(s, s.Seed), s, 0.15);
        var (min, max) = field.Bounds()!.Value;
        var random = new System.Random(11);
        for (int i = 0; i < 5000; i++)
        {
            var p = new V3(
                min.X + ((max.X - min.X) * random.NextDouble()),
                min.Y + ((max.Y - min.Y) * random.NextDouble()),
                min.Z + ((max.Z - min.Z) * random.NextDouble()));
            double estimate = field.Estimate(p, out double error);
            Assert.True(Math.Abs(field.Distance(p) - estimate) <= error + 1e-12, $"at {p}: {field.Distance(p)} vs {estimate} ± {error}");
        }
    }

    [Fact]
    public void NearestFindsTheWinnerWithoutAllocating()
    {
        // Two fused blocks side by side, near their surface: the winner is the block whose field is smaller (the first on
        // a tie), the other's field comes with it while it is under twice the blend radius, and reading them takes
        // nothing from the heap.
        var left = CliffMeshes.Laid(CliffMeshes.Box(-3, 0.5, 0, 3, -0.5, 4, frontTurn: 35), 0, 0, 0, depth: 3);
        var right = CliffMeshes.Laid(CliffMeshes.Box(-0.5, 3, 0, 3, -0.5, 4, frontTurn: -35), 1, 0, 0, depth: 3);
        var field = new SlabCliffField([left, right], CliffSettings.Defaults, 0);
        var (min, max) = field.Bounds()!.Value;
        var random = new System.Random(3);
        var points = new List<V3>();
        while (points.Count < 2000)
        {
            var p = new V3(
                min.X + ((max.X - min.X) * random.NextDouble()),
                min.Y + ((max.Y - min.Y) * random.NextDouble()),
                min.Z + ((max.Z - min.Z) * random.NextDouble()));
            double a = left.Block.Distance(p);
            double b = right.Block.Distance(p);
            if (Math.Abs(Math.Min(a, b)) > 0.3)
            {
                continue;
            }

            points.Add(p);
            Assert.Equal(a <= b ? 0 : 1, field.Nearest(p, out double other));
            if (Math.Max(a, b) < 2 * field.Blend)
            {
                Assert.Equal(Math.Max(a, b), other, 12);
            }
            else
            {
                Assert.True(other >= 2 * field.Blend, $"at {p}: {other} for {Math.Max(a, b)}");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        foreach (var p in points)
        {
            sum += field.Nearest(p, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.InRange(sum, 1, points.Count - 1);
    }

    [Fact]
    public void TheUnionIsOneClosedPart()
    {
        var parts = CliffGenerator.Pieces(CliffMeshes.Preset("long-cliff"), 1);
        var (field, mesh) = Assert.Single(parts);
        Assert.IsType<SlabCliffField>(field);
        Assert.Single(CliffMeshes.ShellVolumes(mesh));
        Assert.Null(CliffMeshes.Open(mesh));
    }

    [Fact]
    public void SpeedWorkKeepsTheField()
    {
        // The field at golden samples, as the code at d24c753 gave it, regenerated for Ruling 24's detail cap (Task 7,
        // fix rounds 2 and 3), for each change of Task 8c's to the field and for the final review's fixes (I2's settle
        // held to the floor, I1's filler bound, Data/slab_field_golden.json): on the four starting points at seeds 1 and 2 and on three random settings with
        // every Surface and Cracks slider high, 1,000 points anywhere in the bounds and 1,000 within 0.3 m of the surface; on two large layouts (259 blocks and more, past
        // the cull grid's 40-rank cut), 400 and 400. Every value is the same within 1e-6 m, everywhere. So are the
        // Nearest winner (the same block) and its other distance (where that is under twice the blend radius, where it is exact), and the Tag, at the same points. The estimate stays
        // within its error of the field there too.
        // Set SLAB_GOLDEN_WRITE to a path to write the samples there from the code as it is.
        string? write = Environment.GetEnvironmentVariable("SLAB_GOLDEN_WRITE");
        var golden = write is null
            ? JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cliffs", "Data", "slab_field_golden.json"))).RootElement.GetProperty("cases")
            : default;
        var cases = new List<object>();
        for (int c = 0; c < GoldenCases.Length; c++)
        {
            var (name, s, count) = GoldenCases[c];
            int pin = count == GoldenFar ? GoldenPinEvery : 1;
            // On the cliff's own seed, not the sub-seed the generator picks: the samples pin the field, not the pick.
            var layout = CliffLayout.Make(s, s.Seed);
            var field = new SlabCliffField(layout, s, 1.5 * CliffGenerator.StartCell(s, 1, layout));
            var (min, max) = field.Bounds()!.Value;
            var random = new System.Random(1000 + c);
            V3 Draw() => new(
                min.X + ((max.X - min.X) * random.NextDouble()),
                min.Y + ((max.Y - min.Y) * random.NextDouble()),
                min.Z + ((max.Z - min.Z) * random.NextDouble()));

            // The far points are drawn first; each near point is the first draw after it within 0.3 m of the surface,
            // skipping as many draws as the samples say.
            var points = new List<V3>();
            for (int i = 0; i < count; i++)
            {
                points.Add(Draw());
            }

            if (write is not null)
            {
                var skips = new List<ushort>();
                while (skips.Count < count)
                {
                    int skip = 0;
                    var p = Draw();
                    for (; Math.Abs(field.Distance(p)) >= 0.3; skip++)
                    {
                        p = Draw();
                    }

                    skips.Add(checked((ushort)skip));
                    points.Add(p);
                }

                int[] values = [.. points.Select(p => checked((int)Math.Round(field.Distance(p) * GoldenScale)))];
                cases.Add(new
                {
                    name,
                    skips = Convert.ToBase64String(MemoryMarshal.AsBytes(skips.ToArray().AsSpan())),
                    values = Convert.ToBase64String(MemoryMarshal.AsBytes(values.AsSpan())),
                    more = Pack([.. points.Where((_, i) => i % pin == 0).Select(p => (field.Nearest(p, out double other), other, field.Tag(p)))]),
                });
                continue;
            }

            var sample = golden[c];
            Assert.Equal(name, sample.GetProperty("name").GetString());
            foreach (ushort skip in MemoryMarshal.Cast<byte, ushort>(Convert.FromBase64String(sample.GetProperty("skips").GetString()!)))
            {
                for (int k = 0; k < skip; k++)
                {
                    Draw();
                }

                points.Add(Draw());
            }

            var expected = MemoryMarshal.Cast<byte, int>(Convert.FromBase64String(sample.GetProperty("values").GetString()!));
            Assert.Equal(2 * count, expected.Length);
            var more = Unpack(sample.GetProperty("more").GetString()!, (expected.Length + pin - 1) / pin);
            for (int i = 0; i < points.Count; i++)
            {
                double was = expected[i] / GoldenScale;
                double now = field.Distance(points[i]);
                Assert.True(Math.Abs(now - was) <= 1e-6, $"{name}, sample {i} at {points[i]}: {now} was {was}");

                if (i % pin == 0)
                {
                    var (pinnedWinner, pinnedOther, pinnedTag) = more[i / pin];
                    int winner = field.Nearest(points[i], out double other);
                    Assert.True(winner == pinnedWinner, $"{name}, sample {i} at {points[i]}: nearest block {winner} was {pinnedWinner}");

                    // The other distance is exact only while under twice the blend radius; past it, +inf is as good.
                    bool exact = pinnedOther < 2 * field.Blend;
                    Assert.True(exact ? Math.Abs(other - pinnedOther) <= 1e-6 : other >= 2 * field.Blend, $"{name}, sample {i} at {points[i]}: the other distance {other} was {pinnedOther}");
                    double tag = field.Tag(points[i]);
                    Assert.True(Math.Abs(tag - pinnedTag) <= 1e-6, $"{name}, sample {i} at {points[i]}: the tag {tag} was {pinnedTag}");
                }

                double estimate = field.Estimate(points[i], out double error);
                Assert.True(Math.Abs(now - estimate) <= error + 1e-9, $"{name}, sample {i} at {points[i]}: {now} vs {estimate} ± {error}");
            }
        }

        if (write is not null)
        {
            File.WriteAllText(write, JsonSerializer.Serialize(new
            {
                about = $"SlabCliffField.Distance at golden samples (see SlabCliffFieldTests.SpeedWorkKeepsTheField): values in 1/{GoldenScale} m as little-endian int32, the near points' skipped draws as uint16.",
                cases,
            }));
        }
    }

    /// <summary>
    /// Packs the Nearest winners (16 bits), their other distances (held to <see cref="GoldenOtherCap"/>) and the tags
    /// (in steps of 1 / <see cref="GoldenScale"/>) as three runs, deflated, as base 64: mostly repeats, so it stays small.
    /// </summary>
    private static string Pack((int Winner, double Other, double Tag)[] rows)
    {
        var bytes = new List<byte>();
        foreach ((int winner, _, _) in rows)
        {
            bytes.AddRange(BitConverter.GetBytes(checked((ushort)winner)));
        }

        foreach ((_, double other, _) in rows)
        {
            bytes.AddRange(BitConverter.GetBytes(checked((int)Math.Round(Math.Min(other, GoldenOtherCap) * GoldenScale))));
        }

        foreach ((_, _, double tag) in rows)
        {
            bytes.AddRange(BitConverter.GetBytes(checked((int)Math.Round(tag * GoldenScale))));
        }

        using var packed = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(packed, System.IO.Compression.CompressionLevel.SmallestSize, true))
        {
            deflate.Write(bytes.ToArray());
        }

        return Convert.ToBase64String(packed.ToArray());
    }

    /// <summary>Reads what <see cref="Pack"/> wrote: <paramref name="count"/> rows.</summary>
    private static (int Winner, double Other, double Tag)[] Unpack(string text, int count)
    {
        using var packed = new MemoryStream(Convert.FromBase64String(text));
        using var deflate = new System.IO.Compression.DeflateStream(packed, System.IO.Compression.CompressionMode.Decompress);
        using var all = new MemoryStream();
        deflate.CopyTo(all);
        byte[] bytes = all.ToArray();
        Assert.Equal(count * 10, bytes.Length);
        var rows = new (int, double, double)[count];
        for (int i = 0; i < count; i++)
        {
            rows[i] = (
                BitConverter.ToUInt16(bytes, 2 * i),
                BitConverter.ToInt32(bytes, (2 * count) + (4 * i)) / GoldenScale,
                BitConverter.ToInt32(bytes, (6 * count) + (4 * i)) / GoldenScale);
        }

        return rows;
    }

    /// <summary>
    /// A hand-laid box block 3 m deep over x and z whose front is turned about the upright through the origin by degrees
    /// (as <see cref="CliffMeshes.Box"/> turns it) and ends, 0.6 m in front of y = 0, in a fifth side facing −y.
    /// </summary>
    private static LaidBlock Fronted(double x0, double x1, double z0, double z1, double turn, int column, int layer)
    {
        double a = turn * Math.PI / 180;
        var block = new ConvexBlock(
        [
            new Plane(new V3(1, 0, 0), x1),
            new Plane(new V3(0, 1, 0), 3),
            new Plane(new V3(-1, 0, 0), -x0),
            new Plane(new V3(Math.Sin(a), -Math.Cos(a), 0), 0),
            new Plane(new V3(0, -1, 0), 0.6),
            new Plane(new V3(0, 0, 1), z1),
            new Plane(new V3(0, 0, -1), -z0),
        ],
        []);
        var laid = CliffMeshes.Laid(block, column, 0, layer, depth: 3);
        return laid with { Shape = laid.Shape with { Faces = 5 } };
    }

    /// <summary>How far in from y = 0 the surface is at x and z: where the field, read from the front, turns negative.</summary>
    private static double Inside(Func<V3, double> f, double x, double z) => Outside(f, x, z, 0);

    /// <summary>The y in front of the surface at x and z where the field is <paramref name="level"/>, by bisection from well in front to well inside.</summary>
    private static double Outside(Func<V3, double> f, double x, double z, double level)
    {
        double front = -2;
        double back = 1.5;
        for (int i = 0; i < 60; i++)
        {
            double mid = (front + back) / 2;
            if (f(new V3(x, mid, z)) > level)
            {
                front = mid;
            }
            else
            {
                back = mid;
            }
        }

        return (front + back) / 2;
    }

    /// <summary>How far along a direction from a point the field first turns negative, by bisection over 0 to 3 m (the point is outside).</summary>
    private static double Depth(Func<V3, double> f, V3 from, V3 direction)
    {
        double outside = 0;
        double inside = 3;
        for (int i = 0; i < 60; i++)
        {
            double mid = (outside + inside) / 2;
            if (f(from + (direction * mid)) > 0)
            {
                outside = mid;
            }
            else
            {
                inside = mid;
            }
        }

        return (outside + inside) / 2;
    }

    /// <summary>
    /// Random settings (as <see cref="CliffMeshes.Random(int)"/> draws them, from a fixed seed) with every Surface and Cracks
    /// slider drawn from the top quarter of its range: the most detail.
    /// </summary>
    private static CliffSettings Detailed(int seed)
    {
        var random = new System.Random(500 + seed);
        var raw = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var spec in CliffMeshes.FirstDrawn)
        {
            Draw(spec);
        }

        // The sliders added since are drawn after the seed, so these settings keep the rest (see CliffMeshes.Random).
        uint cliffSeed = (uint)random.Next();
        foreach (var spec in CliffMeshes.Added)
        {
            Draw(spec);
        }

        var result = CliffParameterSet.Instance.Validate(raw);
        Assert.True(result.Valid);
        return CliffSettings.From(result.Parameters, cliffSeed);

        void Draw(ParameterSpec spec)
        {
            if (spec is NumberSpec n)
            {
                double share = n.Group is "Surface" or "Cracks" ? 0.75 + (0.25 * random.NextDouble()) : random.NextDouble();
                double value = n.Min + (share * (CliffMeshes.DrawMax(n, true) - n.Min));
                raw[n.Key] = n.Whole ? Math.Floor(value + 0.5) : value;
            }
            else if (spec is ChoiceSpec c)
            {
                raw[c.Key] = c.Options[random.Next(c.Options.Count)].Value;
            }
        }
    }
}
