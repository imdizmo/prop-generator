using PropGenerator.Nature.Cliffs;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

/// <summary>The chisel's facets on the cliff's faces, and the chips on its edges.</summary>
public class CliffChiselTests
{
    /// <summary>The Long cliff without its surface detail: each test turns on what it measures.</summary>
    private static readonly CliffSettings Bare = CliffMeshes.Plain(CliffSettings.Defaults);

    /// <summary>The starting points, in drawer order.</summary>
    private static readonly string[] Presets = ["long-cliff", "tall-cliff", "cliff-top", "layered-cliff"];

    [Fact]
    public void ChiselAndChipsAtZeroIgnoreTheirSizes()
    {
        foreach (var s in Presets.Select(id => CliffMeshes.Preset(id) with { Chisel = 0, EdgeChips = 0 }))
        {
            var before = CliffGenerator.Build(s, 1);
            var after = CliffGenerator.Build(s with { ChiselSize = 3.3, ChipSize = 2.2 }, 1);
            Assert.Equal(before.Positions, after.Positions);
            Assert.Equal(before.Indices, after.Indices);
        }
    }

    [Fact]
    public void FacetNoiseIsFlatInsideAndHasNoSteps()
    {
        // Inside a tetrahedron of the grid the noise is linear, so nearly every short stretch of it is straight (the few
        // that bend cross a crease), and it never jumps: across a thousandth of a cell it changes by no more than the
        // steepest a tetrahedron can be, 2√3 per cell.
        var random = new System.Random(5);
        const int Count = 4000;
        int straight = 0;
        for (int i = 0; i < Count; i++)
        {
            var p = new V3(Spread(random, 40), Spread(random, 40), Spread(random, 40));
            var step = new V3(Spread(random, 1), Spread(random, 1), Spread(random, 1)).Normalized() * 0.001;
            double a = CliffNoise.Facets(p - step, 7);
            double b = CliffNoise.Facets(p, 7);
            double c = CliffNoise.Facets(p + step, 7);
            Assert.InRange(b, 0, 1);
            Assert.True(Math.Abs(c - b) <= (2 * Math.Sqrt(3) * 0.001) + 1e-12, $"a step at {p}");
            straight += Math.Abs(a - (2 * b) + c) < 1e-12 ? 1 : 0;
        }

        Assert.True(straight > Count * 0.97, $"{straight} of {Count} straight");
    }

    [Fact]
    public void TheChiselCutsInNoDeeperThanItIsSet()
    {
        var s = Bare with { Chisel = 0.2, ChiselSize = 1 };
        var plain = CliffGenerator.Pieces(Bare, 1).Select(part => part.Field).ToList();
        double deepest = 0;
        foreach (var (_, mesh) in CliffGenerator.Pieces(s, 1))
        {
            foreach (var p in mesh.Points)
            {
                // The point is on the cut surface: the cliff's surface before the chisel is 0 to 0.2 m beyond it.
                double d = plain.Min(field => field.Distance(p));
                Assert.InRange(d, -0.2 - 0.02, 0.02);
                deepest = Math.Min(deepest, d);
            }
        }

        Assert.True(deepest < -0.12, $"the deepest cut is {-deepest} m");
    }

    [Fact]
    public void ChiselStaysWithinAQuarterOfItsSize()
    {
        // Chisel 0.5 on facets 0.4 m across cuts no deeper than 0.1 m.
        var s = Bare with { Chisel = 0.5, ChiselSize = 0.4 };
        var plain = CliffGenerator.Pieces(Bare, 1).Select(part => part.Field).ToList();
        foreach (var (_, mesh) in CliffGenerator.Pieces(s, 1))
        {
            Assert.All(mesh.Points, p => Assert.InRange(plain.Min(field => field.Distance(p)), -0.1 - 0.02, 0.02));
        }
    }

    [Fact]
    public void ChipsRunAlongConvexEdgesOnly()
    {
        // On the Long cliff, Edge chips 0.5 cuts the surface in near its blocks' convex edges (where the point is within
        // 0.1 m of two of its winning block's shell planes), and a block's chips cut nowhere within 0.1 m of the inner
        // crease of one of its cuts.
        var s = Bare with { EdgeChips = 0.5 };
        double cell = CliffGenerator.CellSize(s, 1);
        var blocks = CliffLayout.Make(s, s.Seed);
        var plain = new SlabCliffField(blocks, Bare, 1.5 * cell);
        var chipped = new SlabCliffField(blocks, s, 1.5 * cell);
        var (min, max) = plain.Bounds()!.Value;
        int onEdges = 0;
        foreach (var p in DualContour.Mesh(plain, min, max, cell).Points)
        {
            var (_, _, gapA, gapB) = blocks[plain.Nearest(p, out _)].Block.NearestEdge(p);
            onEdges += Math.Max(gapA, gapB) <= 0.1 && chipped.Distance(p) - plain.Distance(p) > 0.05 ? 1 : 0;
        }

        Assert.True(onEdges > 50, $"{onEdges} points of the convex edges are chipped more than 5 cm");

        int creases = 0;
        var random = new System.Random(8);
        foreach (var (q, owner) in InnerCreases(blocks, plain))
        {
            creases++;
            for (int k = 0; k < 20; k++)
            {
                var p = q + (new V3(Spread(random, 2), Spread(random, 2), Spread(random, 2)).Normalized() * (0.1 * random.NextDouble()));
                // Only the crease's own block keeps its chips off it: where a fused neighbour wins, or is nearly as near
                // (so the chips turn from one block's to the other's), the chips there are partly the neighbour's, along
                // its own edges, faded where it fuses (NoChipsAlongAFusedJoin covers them).
                if (chipped.Nearest(p, out double other) != owner || other < blocks[owner].Block.Distance(p) + (0.5 * chipped.Blend))
                {
                    continue;
                }

                double chip = chipped.Distance(p) - plain.Distance(p);
                Assert.True(chip <= 1e-9, $"a chip of {chip:F4} m at {p}, {(p - q).Length:F3} m from the crease at {q}");
            }
        }

        Assert.True(creases > 100, $"only {creases} points on the cuts' inner creases show");
    }

    [Fact]
    public void ChipsBreakInFlatFacetsAtSeededAngles()
    {
        // Ruling 35: a chip is a flat cut. Along the front top edge of a box, each chip cell takes the corner off with
        // one plane: the chipped cross-sections 2 cm apart along the edge mostly fit the same line (within 1 mm), so the
        // break is one flat facet across its cell, and the facets lean at seeded angles (more than 10° apart over the
        // edge), not one 45° chamfer whose depth rises and falls along the edge as smooth dents.
        var s = Bare with { EdgeChips = 0.3, ChipSize = 0.6 };
        var box = new[] { CliffMeshes.Laid(CliffMeshes.Box(-5, 5, 0, 3, -0.5, 4), 0, 0, 0) };
        var plain = new SlabCliffField(box, Bare, 0);
        var chipped = new SlabCliffField(box, s, 0);
        var across = new V3(0, 1, 1).Normalized();
        var inward = new V3(0, 1, -1).Normalized();
        var sections = new List<(double Angle, double Offset, bool Flat)?>();
        for (double x = -4; x <= 4; x += 0.02)
        {
            var cut = new List<(double U, double V)>();
            for (double t = -0.6; t <= 0.6; t += 0.005)
            {
                var from = new V3(x, 0, 4) + (across * t) - (inward * 0.5);
                double depth = V3.Dot(Surface(chipped.Distance, from, inward, 1.5) - from, inward);
                if (depth - V3.Dot(Surface(plain.Distance, from, inward, 1.5) - from, inward) > 1e-3)
                {
                    cut.Add((t, depth));
                }
            }

            sections.Add(Line(cut));
        }

        int chippedPairs = 0;
        int flatPairs = 0;
        var angles = new List<double>();
        for (int k = 1; k < sections.Count; k++)
        {
            if (sections[k - 1] is { } a && sections[k] is { } b)
            {
                chippedPairs++;
                if (a.Flat && b.Flat && Math.Abs(a.Angle - b.Angle) < 0.2 && Math.Abs(a.Offset - b.Offset) < 1e-3)
                {
                    flatPairs++;
                    angles.Add(b.Angle);
                }
            }
        }

        Assert.True(chippedPairs > 50, $"only {chippedPairs} chipped sections");
        Assert.True(flatPairs > 0.6 * chippedPairs, $"{flatPairs} of {chippedPairs} neighbouring chipped sections lie on one facet");
        Assert.True(angles.Count > 0);
        Assert.True(angles.Max() - angles.Min() > 10, $"the facets lean from {angles.Min():F1}° to {angles.Max():F1}°");

        // The line through a section's cut points (u, depth along the inward direction), in degrees from across the edge,
        // its depth at u = 0, and whether every point lies within 1 mm of it; none for fewer than five points.
        static (double Angle, double Offset, bool Flat)? Line(List<(double U, double V)> points)
        {
            if (points.Count < 5)
            {
                return null;
            }

            double mu = points.Average(q => q.U);
            double mv = points.Average(q => q.V);
            double slope = points.Sum(q => (q.U - mu) * (q.V - mv)) / points.Sum(q => (q.U - mu) * (q.U - mu));
            double offset = mv - (slope * mu);
            double worst = points.Max(q => Math.Abs(q.V - (offset + (slope * q.U))) / Math.Sqrt(1 + (slope * slope)));
            return (Math.Atan(slope) * 180 / Math.PI, offset, worst < 1e-3);
        }
    }

    [Fact]
    public void ChipDepthsSpreadFromTwoCellsToTheMost()
    {
        // Ruling 40: on a grid, a chip's drawn depth is spread over two cells to the most Edge chips allows, not raised
        // to two cells: along a long box edge with chips 0.3 m deep on 0.125 m cells, few chips sit exactly two cells
        // deep (raised, about three quarters did, by length), they reach past the middle of the range, and none passes
        // the most.
        const double Cell = 0.125;
        var depths = ChipDepths(0.3, Cell);
        Assert.True(depths.Count > 1000, $"only {depths.Count} chipped samples");
        double atTwo = depths.Count(d => Math.Abs(d - (2 * Cell)) < 1e-6) / (double)depths.Count;
        Assert.True(atTwo < 0.3, $"{atTwo:P0} of the chips are exactly two cells deep");
        Assert.True(depths.Max() > 0.275, $"the deepest chip is {depths.Max():F3} m");
        Assert.True(depths.All(d => d is >= (2 * Cell) - 1e-9 and <= 0.3 + 1e-9), $"a chip of {depths.Min():F3} to {depths.Max():F3} m");
    }

    [Fact]
    public void ChipDepthsNearTwoCells()
    {
        // Ruling 40 where the most Edge chips allows is near two grid cells (0.25 m on 0.125 m cells): just under it no
        // chip is cut; at it every chip is exactly two cells deep; just over it the chips spread over the narrow range
        // from two cells to the most, and none passes it. So the one jump is from none to chips two cells deep, at two
        // cells.
        const double Cell = 0.125;
        Assert.Empty(ChipDepths(0.245, Cell));

        var at = ChipDepths(0.25, Cell);
        Assert.True(at.Count > 1000, $"only {at.Count} chipped samples at two cells");
        Assert.All(at, d => Assert.Equal(0.25, d, 9));

        var over = ChipDepths(0.2625, Cell);
        Assert.True(over.Count > 1000, $"only {over.Count} chipped samples just over two cells");
        Assert.True(over.All(d => d is >= 0.25 - 1e-9 and <= 0.2625 + 1e-9), $"a chip of {over.Min():F4} to {over.Max():F4} m");
        Assert.True(over.Max() > 0.26, $"the deepest chip is {over.Max():F4} m");
        Assert.True(over.Count(d => d > 0.25 + 1e-6) > over.Count / 2, "most chips just over two cells are cut deeper than two cells");
    }

    [Fact]
    public void ChipsTooSmallForTheGridAreNotCut()
    {
        // Ruling 30's two-cell rule: where an edge allows no chip two grid cells deep, or the chips are under two cells
        // long, none is cut (not a shallower one: the grid cannot show its flat cut and sharp sides, only fold them, as
        // on the Long cliff's lower detail levels). On a box's front top edge with chips 0.3 m deep and 1.5 m long: cut
        // on a grid of 0.125 m cells, none on one of 0.2 m (two cells are 0.4 m) nor of 1 m.
        var s = Bare with { EdgeChips = 0.3, ChipSize = 1.5 };
        var box = new[] { CliffMeshes.Laid(CliffMeshes.Box(-5, 5, 0, 3, -0.5, 4), 0, 0, 0) };
        double Deepest(double cell)
        {
            var plain = new SlabCliffField(box, Bare, 1.5 * cell);
            var chipped = new SlabCliffField(box, s, 1.5 * cell);
            double deepest = 0;
            for (double x = -4; x <= 4; x += 0.01)
            {
                var corner = new V3(x, 0, 4);
                deepest = Math.Max(deepest, chipped.Distance(corner) - plain.Distance(corner));
            }

            return deepest;
        }

        Assert.True(Deepest(0.125) > 0.2, $"the chips cut {Deepest(0.125):F3} m on a fine grid");
        Assert.Equal(0, Deepest(0.2), 9);
        Assert.Equal(0, Deepest(1), 9);
    }

    [Fact]
    public void EveryChipAndItsSidesSpanTwoCells()
    {
        // Ruling 30's two-cell rule for chips (Ruling 37, review 8-8c Important 1): along a box's front top edge on a grid
        // of 0.125 m cells, with chips 0.3 m deep and 1.5 m long, every chip that is cut is at least two cells deep (the
        // cut at the corner is flat across a chip's cell at its depth: no flat stretch under two cells deep but none), and
        // its sides, where it meets a shallower neighbour or a whole stretch, fall away over at least two cells (no
        // steeper than the deepest chip over two cells, the chips' warp stretching them by up to a third).
        const double Cell = 0.125;
        var s = Bare with { EdgeChips = 0.3, ChipSize = 1.5 };
        var box = new[] { CliffMeshes.Laid(CliffMeshes.Box(-5, 5, 0, 3, -0.5, 4), 0, 0, 0) };
        var plain = new SlabCliffField(box, Bare, 1.5 * Cell);
        var chipped = new SlabCliffField(box, s, 1.5 * Cell);
        const double Step = 0.005;
        var cuts = new List<double>();
        for (double x = -4; x <= 4; x += Step)
        {
            var corner = new V3(x, 0, 4);
            cuts.Add(chipped.Distance(corner) - plain.Distance(corner));
        }

        int flats = 0;
        double steepest = 0;
        for (int k = 1; k < cuts.Count; k++)
        {
            double change = Math.Abs(cuts[k] - cuts[k - 1]);
            steepest = Math.Max(steepest, change / Step);
            if (change < 1e-9 && cuts[k] > 1e-6)
            {
                flats++;
                Assert.True(cuts[k] >= (2 * Cell) - 1e-9, $"a chip {cuts[k]:F3} m deep at x {-4 + (k * Step):F3}, under two cells");
            }
        }

        Assert.True(flats > 200, $"only {flats} samples of chips");
        Assert.True(steepest <= 1.35 * 0.3 / (2 * Cell), $"a chip's side falls {steepest:F2} m per metre along the edge");
    }

    [Fact]
    public void NoChipsAlongAFusedJoin()
    {
        // Two fused blocks: within the blend radius of where they join, on the surface away from the top and the base,
        // the chips cut nothing the mesh could show (under a millimetre: where the fused join stands a hair proud of both
        // blocks, the neighbour's field there lets a sliver of a chip through). Neither block's end, hidden in the
        // other, shows as a row of chips.
        var s = Bare with { EdgeChips = 0.3, ChipSize = 0.6 };
        var valley = new[]
        {
            CliffMeshes.Laid(CliffMeshes.Box(-3, 0.5, 0, 3, -0.5, 4, frontTurn: 35), 0, 0, 0, depth: 3),
            CliffMeshes.Laid(CliffMeshes.Box(-0.5, 3, 0, 3, -0.5, 4, frontTurn: -35), 1, 0, 0, depth: 3),
        };
        var flat = new[]
        {
            CliffMeshes.Laid(CliffMeshes.Box(-3, 0.5, 0, 3, -0.5, 4), 0, 0, 0),
            CliffMeshes.Laid(CliffMeshes.Box(-0.5, 3, 0, 3, -0.5, 4), 1, 0, 0),
        };
        foreach (var (blocks, joins) in new[] { (valley, new[] { 0.0 }), (flat, new[] { -0.5, 0.5 }) })
        {
            var plain = new SlabCliffField(blocks, Bare, 0);
            var chipped = new SlabCliffField(blocks, s, 0);
            double blend = plain.Blend;
            int read = 0;
            double deepest = 0;
            foreach (double join in joins)
            {
                for (double x = join - blend; x <= join + blend; x += 0.02)
                {
                    // Down the front, and across the top away from its front and back edges.
                    for (double z = 0.6; z <= 3.4; z += 0.1)
                    {
                        var p = Surface(plain.Distance, new V3(x, -2, z), new V3(0, 1, 0), 3.5);
                        deepest = Math.Max(deepest, Math.Abs(chipped.Distance(p) - plain.Distance(p)));
                        read++;
                    }

                    for (double y = 0.6; y <= 2.4; y += 0.1)
                    {
                        var p = Surface(plain.Distance, new V3(x, y, 6), new V3(0, 0, -1), 4);
                        deepest = Math.Max(deepest, Math.Abs(chipped.Distance(p) - plain.Distance(p)));
                        read++;
                    }
                }
            }

            Assert.True(read > 500);
            Assert.True(deepest < 1e-3, $"a chip {deepest:E2} m deep within the blend radius of the join");
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(0.5, true)]
    [InlineData(0.5, false)]
    public void PlateRimsKeepTheirChipsAndWear(double setBack, bool chips)
    {
        // Two plates of one column, the upper sunk 0.3 m into the lower, its front flush with the lower's or set back
        // 0.5 m: the lower plate's front rim is chipped (or worn) below the bedding groove, and nothing cuts into the
        // upper plate's own face away from its edges (a chip's flat cut leans as little as 40° from a face, so a chip of
        // its ends 0.3 m deep reaches 0.47 m along the front: 0.6 m in from the ends is away from them).
        var bare = CliffMeshes.Plain(CliffSettings.Defaults) with { WearWidth = 0.5 };
        var s = chips ? bare with { EdgeChips = 0.3, ChipSize = 0.6 } : bare with { EdgeWear = 0.5 };
        var lower = CliffMeshes.Laid(CliffMeshes.Box(-2, 2, 0, 3, -0.3, 2), 0, 0, 0);
        var upper = CliffMeshes.Laid(CliffMeshes.Box(-2, 2, setBack, 3, 1.7, 4), 0, 0, 1);
        var plain = new SlabCliffField([lower, upper], bare, 0);
        var cut = new SlabCliffField([lower, upper], s, 0);
        double rim = 0;
        for (double x = -1.5; x <= 1.5; x += 0.01)
        {
            for (double z = 1.75; z <= 1.88; z += 0.01)
            {
                var p = Surface(plain.Distance, new V3(x, -1, z), new V3(0, 1, 0), 2);
                rim = Math.Max(rim, cut.Distance(p) - plain.Distance(p));
            }

            for (double z = 2.25; z <= 3.45 && Math.Abs(x) <= 1.4; z += 0.05)
            {
                var p = Surface(plain.Distance, new V3(x, -1, z), new V3(0, 1, 0), 2.5);
                double into = cut.Distance(p) - plain.Distance(p);
                Assert.True(Math.Abs(into) < 1e-9, $"the upper plate is cut {into:F4} m at {p}");
            }
        }

        Assert.True(rim > 0.02, $"the lower plate's rim is cut in only {rim:F4} m");
    }

    [Fact]
    public void CutRimsAreChippedOnABlockOfManyPlanes()
    {
        // A small block of 16 sides with bevelled rims and cuts, chipped 1 m deep: dozens of its planes are within the
        // chips' reach of every point, and every one is read. So the rims of its cuts (where a cut's face meets a shell
        // plane) are chipped along much of their length instead of switching off wherever the shell's planes crowd
        // them out, and the chips change smoothly (half a millimetre apart, the cut changes by under 1.5 cm).
        var bare = CliffMeshes.Plain(CliffSettings.Defaults) with
        {
            Width = 2,
            Depth = 1.5,
            Height = 1.5,
            Blocks = 1,
            Tiers = 1,
            Layers = 0,
            Faces = 16,
            Bevel = 0.5,
            Steps = 2,
            Bites = 2,
            Grooves = 1,
        };
        var s = bare with { EdgeChips = 1, ChipSize = 1 };
        double cell = CliffGenerator.CellSize(s, 1);
        var blocks = CliffLayout.Make(s, s.Seed);
        var block = blocks[0].Block;
        Assert.True(block.Shell.Count + block.Cuts.Count > 30, $"{block.Shell.Count} planes and {block.Cuts.Count} cuts");
        var plain = new SlabCliffField(blocks, bare, 1.5 * cell);
        var chipped = new SlabCliffField(blocks, s, 1.5 * cell);
        var (min, max) = plain.Bounds()!.Value;
        var random = new System.Random(2);
        double most = 0;
        int rims = 0;
        int cut = 0;
        foreach (var p in DualContour.Mesh(plain, min, max, cell).Points)
        {
            var q = p + (new V3(Spread(random, 2), Spread(random, 2), Spread(random, 2)).Normalized() * 0.0005);
            double here = chipped.Distance(p) - plain.Distance(p);
            double there = chipped.Distance(q) - plain.Distance(q);
            most = Math.Max(most, Math.Abs(there - here));

            // On a cut's rim: its face is the block's surface there, and a shell plane is within 5 cm of it.
            double face = block.Cuts.Max(planes => planes.Min(plane => plane.At(p)));
            double shell = block.Shell.Max(plane => plane.At(p));
            if (Math.Abs(face - block.Distance(p)) < 1e-9 && face - shell < 0.05)
            {
                rims++;
                cut += here > 0.02 ? 1 : 0;
            }
        }

        Assert.True(rims > 30, $"only {rims} points on the cuts' rims");
        Assert.True(cut > 0.3 * rims, $"{cut} of {rims} points on the cuts' rims are chipped");
        Assert.True(most < 0.015, $"the chips jump {most:F3} m within half a millimetre");
    }

    [Theory]
    [InlineData(1, 0.2, 0.3)]
    [InlineData(0.25, 3, 0.25)]
    public void ChipsStayWithinTheirLimits(double chips, double size, double limit)
    {
        // The deepest chip is no deeper than Edge chips, nor than 1.5 × Chip size: read at every point of the uncut
        // surface (a chip is deepest on its edge, where the surface was). The fields are made for no grid, so the
        // sliders' own limits are read, not the fade of chips too small for the grid (see ChipsTooSmallForTheGridAreNotCut).
        var s = Bare with { EdgeChips = chips, ChipSize = size };
        double cell = CliffGenerator.CellSize(s, 1);
        var blocks = CliffLayout.Make(s, s.Seed);
        var plain = new SlabCliffField(blocks, Bare, 0);
        var chipped = new SlabCliffField(blocks, s, 0);
        var (min, max) = plain.Bounds()!.Value;
        double deepest = 0;
        foreach (var p in DualContour.Mesh(plain, min, max, cell).Points)
        {
            double chip = chipped.Distance(p) - plain.Distance(p);
            Assert.True(chip <= limit + 1e-9, $"a chip {chip:F3} m deep at {p}, past {limit} m");
            deepest = Math.Max(deepest, chip);
        }

        Assert.True(deepest > 0.5 * limit, $"the deepest chip is {deepest:F3} m");
    }

    [Theory]
    [InlineData(0.5, 1, 0.5)]
    [InlineData(0.1, 0.3, 0)]
    [InlineData(0, 1, 1)]
    public void TheCliffStillStandsOnItsWholeBase(double chisel, double size, double chips)
    {
        // However deep the chisel and the chips, they cut nothing below the ground line and no more than half the height
        // above it, so the flat bottom Sink below the ground line is the uncut cliff's, edge to edge.
        var plain = CliffGenerator.Build(Bare, 1);
        var cut = CliffGenerator.Build(Bare with { Chisel = chisel, ChiselSize = size, EdgeChips = chips }, 1);
        double bottom = -Bare.Sink + 1e-3;
        double before = CliffMeshes.FlatArea(plain, p => p.Z < bottom);
        Assert.True(before > 0.5 * Bare.Width * Bare.Depth, $"the plain bottom is {before} m²");
        Assert.InRange(CliffMeshes.FlatArea(cut, p => p.Z < bottom), 0.95 * before, 1.05 * before);
        Assert.Equal(CliffMeshes.Points(plain).Min(p => p.Z), CliffMeshes.Points(cut).Min(p => p.Z), 1e-6);
    }

    [Theory]
    [InlineData(0.05, 1.2, 0, 0.6)]
    [InlineData(0.5, 0.2, 1, 0.1)]
    [InlineData(0.2, 5, 0.6, 3)]
    public void ChiselledCliffsAreClosed(double chisel, double size, double chips, double chipSize)
    {
        foreach (var s in Presets.Select(id => CliffMeshes.Preset(id)))
        {
            var block = CliffGenerator.Build(s with { Chisel = chisel, ChiselSize = size, EdgeChips = chips, ChipSize = chipSize }, 1);
            Assert.Null(CliffMeshes.Open(block));
        }
    }

    [Theory]
    [InlineData(0.5, 0.2, 0, 0.6)]
    [InlineData(0.5, 2, 0, 0.6)]
    [InlineData(0.2, 0.8, 0, 0.6)]
    [InlineData(0.5, 0.2, 1, 0.1)]
    [InlineData(0.2, 0.8, 0.6, 0.3)]
    [InlineData(0.2, 0.8, 0.3, 0.6)]
    public void OnTheStartingPointsTheChiselSealsNothing(double chisel, double size, double chips, double chipSize)
    {
        // Meshed straight from each part's field, before the clean-up: with Chisel held to a quarter of its size and a
        // chip changing only along its edge, the starting points' blocks come out whole at the sliders' far ends. Laid
        // out from the cliffs' own seeds, not the sub-seeds the generator picks: this pins the chisel on these layouts,
        // not the pick (on some picked layouts the chisel seals a pocket of under 0.1 m³, which every built part drops).
        // Since side-by-side blocks join hard (Ruling 34), no smooth join fills the concave corner where three blocks
        // meet any more, and there the chisel's facets can close over a sliver: none is over a litre (the clean-up drops
        // it, and it is inside the rock).
        foreach (var basis in Presets.Select(id => CliffMeshes.Plain(CliffMeshes.Preset(id))))
        {
            var s = basis with { Chisel = chisel, ChiselSize = size, EdgeChips = chips, ChipSize = chipSize };
            double cell = CliffGenerator.CellSize(s, 1);
            foreach (var (field, _) in CliffGenerator.Pieces(s, CliffLayout.Make(s, s.Seed), 1))
            {
                var (min, max) = field.Bounds()!.Value;
                var shells = CliffMeshes.ShellVolumes(DualContour.Mesh(field, min, max, cell));
                string all = string.Join(", ", shells.Select(v => v.ToString("G3", System.Globalization.CultureInfo.InvariantCulture)));
                Assert.True(shells.Count(v => Math.Abs(v) > 0.001) == 1, $"{shells.Count} shells: {all}");

                // And each sliver is sealed inside the rock (inside out, so its volume is negative), not a crumb outside.
                Assert.True(shells.All(v => Math.Abs(v) > 0.001 || v < 0), $"a crumb outside the rock: {all}");
            }
        }
    }

    [Theory]
    [InlineData(0.5, 0.3, 0, 0.6, null)]
    [InlineData(0.5, 1.2, 0, 0.6, null)]
    [InlineData(0, 1.2, 1, 0.1, null)]
    [InlineData(0.2, 1.2, 1, 1, null)]
    [InlineData(0, 1.2, 0.6, 0.6, "cliff-top")]
    [InlineData(0, 1.2, 1, 1, "cliff-top")]
    [InlineData(0, 1.2, 0.6, 0.6, "layered-cliff")]
    [InlineData(0, 1.2, 1, 1, "layered-cliff")]
    public void NoBuiltPartKeepsAPocketOrACrumb(double chisel, double size, double chips, double chipSize, string? preset)
    {
        // On random cliffs (or a starting point, chipped deep), the chisel and the chips can seal a pocket or cut a crumb
        // loose; every part drops them after meshing, so each built part is only shells of rock worth seeing.
        foreach (int seed in preset is null ? Enumerable.Range(0, 6) : [0])
        {
            var basis = preset is null ? CliffMeshes.Random(seed) : CliffMeshes.Preset(preset);
            var s = basis with { Chisel = chisel, ChiselSize = size, EdgeChips = chips, ChipSize = chipSize };
            foreach (var (_, mesh) in CliffGenerator.Pieces(s, 1))
            {
                var shells = CliffMeshes.ShellVolumes(mesh);
                Assert.All(shells, v => Assert.True(v >= 0.01 * shells.Max(), $"seed {seed}: a shell of {v} m³ beside {shells.Max()}"));
            }
        }
    }

    /// <summary>
    /// Points every 2 cm along the inner creases of the blocks' cuts (where two planes of one cut meet) that are on the
    /// joined surface: on the block's own surface with both planes the cut's face there, and the field within 2 cm of 0;
    /// each with the index of its block.
    /// </summary>
    private static IEnumerable<(V3 Point, int Block)> InnerCreases(IReadOnlyList<LaidBlock> blocks, SlabCliffField field)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i].Block;
            var (lo, hi) = block.Bounds();
            var middle = (lo + hi) / 2;
            double span = (hi - lo).Length;
            foreach (var cut in block.Cuts)
            {
                for (int a = 0; a < cut.Count; a++)
                {
                    for (int b = a + 1; b < cut.Count; b++)
                    {
                        var na = cut[a].Normal;
                        var nb = cut[b].Normal;
                        var along = V3.Cross(na, nb);
                        if (along.Length < 1e-6)
                        {
                            continue;
                        }

                        // The point of the line where the two planes meet nearest the block's middle, then along it.
                        double d = V3.Dot(na, nb);
                        double va = cut[a].At(middle);
                        double vb = cut[b].At(middle);
                        var start = middle - (na * ((va - (d * vb)) / (1 - (d * d)))) - (nb * ((vb - (d * va)) / (1 - (d * d))));
                        along = along.Normalized();
                        for (double t = -span; t <= span; t += 0.02)
                        {
                            var q = start + (along * t);
                            bool face = cut.All(plane => plane.At(q) >= -1e-9);
                            if (face && Math.Abs(block.Distance(q)) < 1e-9 && Math.Abs(field.Distance(q)) < 0.02)
                            {
                                yield return (q, i);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Where a line from a point outside, along a direction, meets the surface (by bisection; it is inside <paramref name="length"/> along).</summary>
    private static V3 Surface(Func<V3, double> f, V3 from, V3 direction, double length)
    {
        Assert.True(f(from) > 0 && f(from + (direction * length)) < 0);
        double outside = 0;
        double inside = length;
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

        return from + (direction * inside);
    }

    private static double Spread(System.Random random, double width) => (random.NextDouble() - 0.5) * width;

    /// <summary>
    /// The depth at each flat sample of a chip (1 cm apart along it, so a chip counts by its length) along the front top
    /// edge of a 60 m box, with chips the given most deep and 1.5 m long, on a grid of the given cells.
    /// </summary>
    private static List<double> ChipDepths(double edgeChips, double cell)
    {
        var box = new[] { CliffMeshes.Laid(CliffMeshes.Box(-30, 30, 0, 3, -0.5, 4), 0, 0, 0) };
        var plain = new SlabCliffField(box, Bare, 1.5 * cell);
        var chipped = new SlabCliffField(box, Bare with { EdgeChips = edgeChips, ChipSize = 1.5 }, 1.5 * cell);
        var depths = new List<double>();
        double last = double.NaN;
        for (double x = -28; x <= 28; x += 0.01)
        {
            var corner = new V3(x, 0, 4);
            double cut = chipped.Distance(corner) - plain.Distance(corner);
            if (cut > 1e-6 && Math.Abs(cut - last) < 1e-9)
            {
                depths.Add(cut);
            }

            last = cut;
        }

        return depths;
    }
}
