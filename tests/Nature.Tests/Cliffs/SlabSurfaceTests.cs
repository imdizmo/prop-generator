using PropGenerator.Nature.Cliffs;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

/// <summary>The surface detail on the plane shells: creases, ledges and lines, the hold at the base, and the colour.</summary>
public class SlabSurfaceTests
{
    /// <summary>The Long cliff without its surface detail, the cracks drawn straight.</summary>
    private static readonly CliffSettings Bare = CliffMeshes.Plain(CliffMeshes.Preset("long-cliff")) with { CreaseWobble = 0, LineWobble = 0 };

    [Theory]
    [InlineData("creases")]
    [InlineData("ledges")]
    [InlineData("lines")]
    public void CreasesLedgesAndLinesCutIn(string feature)
    {
        // Each feature on its own cuts the Long cliff's front in somewhere, by no more than its depth, and its groove is
        // at least 1.5 cells wide (where it cuts in by a hundredth of its depth or more): across x at mid height for a
        // crease, up z for a ledge or a line.
        var (s, depth) = feature switch
        {
            "creases" => (Bare with { Creases = 1, CreaseDepth = 0.2 }, 0.2),
            "ledges" => (Bare with { Ledges = 1, LedgeDepth = 0.3 }, 0.3),
            _ => (Bare with { Lines = 10, LineDepth = 0.05 }, 0.05),
        };
        double cell = CliffGenerator.CellSize(s, 1);
        var blocks = CliffLayout.Make(s, s.Seed);
        var plain = new SlabCliffField(blocks, Bare, 1.5 * cell);
        var cut = new SlabCliffField(blocks, s, 1.5 * cell);
        double deepest = 0;
        double widest = 0;
        IEnumerable<(double X, double Z)[]> lines = feature == "creases"
            ? [[.. Steps(-12, 12, 0.01).Select(x => (x, 4.0))]]
            : Steps(-10, 10, 2.5).Select(x => Steps(0.5, 8, 0.01).Select(z => (x, z)).ToArray());
        foreach (var line in lines)
        {
            double run = 0;
            foreach (var (x, z) in line)
            {
                if (Front(plain.Distance, x, z) is not { } p)
                {
                    run = 0;
                    continue;
                }

                // On the plain surface the field without the feature is 0, so the field with it is how far it cuts in.
                double inward = cut.Distance(p);
                Assert.InRange(inward, -1e-12, depth + 1e-12);
                deepest = Math.Max(deepest, inward);
                run = inward >= 0.01 * depth ? run + 0.01 : 0;
                widest = Math.Max(widest, run);
            }
        }

        Assert.True(deepest > 0.5 * depth, $"the {feature} cut in {deepest:F3} m at most");
        Assert.True(widest >= 1.5 * cell, $"the widest {feature} groove is {widest:F3} m, under 1.5 cells of {cell:F3} m");
    }

    [Fact]
    public void LedgeStepsDoNotJumpAtEdgesOrFusedJoins()
    {
        // Ledges set the rock back by as much as the surface faces sideways. Read from the faces near a point, that
        // share turns smoothly from face to face: across a block's top edge (the front sideways, the top not) and across
        // a fused join between a front leaning back 20° and an upright one, the field has no step: 2 mm apart, it
        // changes by under 5 cm (a step of the whole 0.3 m set-back would jump).
        var s = CliffMeshes.Plain(CliffSettings.Defaults) with { Height = 4, Ledges = 3, LedgeDepth = 0, LedgeStep = 0.3, CreaseWobble = 0 };
        var box = new SlabCliffField([CliffMeshes.Laid(CliffMeshes.Box(-3, 3, 0, 3, -0.5, 4), 0, 0, 0)], s, 0);
        double edge = Jump(box.Distance, new V3(0, -0.15, 3.85), new V3(0, 1, 0), new V3(0, 0, 1), 0.3, 0.3);
        Assert.True(edge < 0.05, $"the field jumps {edge:F3} m across the top edge");

        double lean = 20 * Math.PI / 180;
        var leaning = new Plane(new V3(0, -Math.Cos(lean), Math.Sin(lean)), 0);
        var upright = CliffMeshes.Box(-0.5, 3, 0, 3, -0.5, 4);
        var left = new ConvexBlock([.. CliffMeshes.Box(-3, 0.5, 0, 3, -0.5, 4).Shell.Select(plane => plane.Normal.Y < -0.5 ? leaning : plane)], []);
        var fused = new SlabCliffField([CliffMeshes.Laid(left, 0, 0, 0, depth: 3), CliffMeshes.Laid(upright, 1, 0, 0, depth: 3)], s, 0);
        double join = Jump(fused.Distance, new V3(-0.8, -0.1, 2), new V3(1, 0, 0), new V3(0, 1, 0), 0.6, 1);
        Assert.True(join < 0.05, $"the field jumps {join:F3} m across the fused join");

        // The most the field changes between neighbours 2 mm apart over a patch of a plane near the surface.
        static double Jump(Func<V3, double> f, V3 corner, V3 u, V3 v, double width, double height)
        {
            const double Step = 0.002;
            double most = 0;
            for (double a = 0; a <= width; a += Step)
            {
                for (double b = 0; b <= height; b += Step)
                {
                    var p = corner + (u * a) + (v * b);
                    double here = f(p);
                    if (Math.Abs(here) < 0.4)
                    {
                        most = Math.Max(most, Math.Max(Math.Abs(f(p + (u * Step)) - here), Math.Abs(f(p + (v * Step)) - here)));
                    }
                }
            }

            return most;
        }
    }

    [Theory]
    [InlineData("creases", 40)]
    [InlineData("creases", -55)]
    [InlineData("ledges", 35)]
    public void AGrooveOnAFaceTurnedFromItNeverOverhangs(string feature, double turn)
    {
        // The teeth along the Long cliff's joins (seed 1): its crease crosses a block's end face turned about 40° from
        // it. The groove was pushed in along the face's normal but laid out across its own plane, so the push carried
        // the V sideways along the face, and its steeper wall turned past the face into an overhanging lip thinner than
        // a grid cell, which the mesh broke into teeth. Read at the foot of each point on the surface, a groove of the
        // deepest kind (1.5 times its width) on a face turned from it keeps every line in along the face's normal
        // meeting the surface once, and its walls turn less than 75° from the face.
        double a = turn * Math.PI / 180;
        var (normal, tangent, face, s) = feature == "creases"
            ? (new V3(Math.Sin(a), -Math.Cos(a), 0), new V3(Math.Cos(a), Math.Sin(a), 0), CliffMeshes.Box(-3, 3, 0, 3, -0.5, 4, frontTurn: turn),
                Bare with { Width = 4, Height = 4, Creases = 1, CreaseDepth = 0.3, CreaseWidth = 0.2, CreaseAngle = 0 })
            : (new V3(0, -Math.Cos(a), Math.Sin(a)), new V3(0, Math.Sin(a), Math.Cos(a)), Leant(turn),
                Bare with { Height = 4, Ledges = 1, LedgeDepth = 0.3, LedgeStep = 0, CreaseWidth = 0.2 });
        var field = new SlabCliffField([CliffMeshes.Laid(face, 0, 0, 0)], s, 0);
        var middle = feature == "creases" ? new V3(0, 0, 2) : new V3(0, 0, 2);
        double steepest = 0;
        int cut = 0;
        for (double along = -2; along <= 2; along += 0.005)
        {
            var foot = middle + (tangent * along);
            int crossings = 0;
            bool? outside = null;
            for (double h = 0.6; h >= -0.6; h -= 0.002)
            {
                bool now = field.Distance(foot + (normal * h)) > 0;
                crossings += outside is { } was && was != now ? 1 : 0;
                outside = now;
            }

            Assert.True(crossings == 1, $"the line in at {along:F3} m meets the surface {crossings} times");
            double inside = 0.6;
            double outer = -0.6;
            for (int i = 0; i < 50; i++)
            {
                double mid = (inside + outer) / 2;
                if (field.Distance(foot + (normal * mid)) > 0)
                {
                    inside = mid;
                }
                else
                {
                    outer = mid;
                }
            }

            cut += inside < -0.01 ? 1 : 0;
            var p = foot + (normal * inside);
            var g = new V3(
                field.Distance(p + new V3(1e-4, 0, 0)) - field.Distance(p - new V3(1e-4, 0, 0)),
                field.Distance(p + new V3(0, 1e-4, 0)) - field.Distance(p - new V3(0, 1e-4, 0)),
                field.Distance(p + new V3(0, 0, 1e-4)) - field.Distance(p - new V3(0, 0, 1e-4))).Normalized();
            steepest = Math.Max(steepest, Math.Acos(Math.Clamp(V3.Dot(g, normal), -1, 1)) * 180 / Math.PI);
        }

        Assert.True(cut > 20, $"the groove cut in at only {cut} places");
        Assert.True(steepest < 75, $"a wall turns {steepest:F1}° from the face");

        // A box whose front leans back by the turn from upright, about the x axis through its foot.
        static ConvexBlock Leant(double degrees)
        {
            double b = degrees * Math.PI / 180;
            var front = new V3(0, -Math.Cos(b), Math.Sin(b));
            return new ConvexBlock(
            [
                new Plane(new V3(1, 0, 0), 3),
                new Plane(new V3(0, 1, 0), 3),
                new Plane(new V3(-1, 0, 0), 3),
                new Plane(front, V3.Dot(front, new V3(0, 0, 2))),
                new Plane(new V3(0, 0, 1), 4),
                new Plane(new V3(0, 0, -1), 0.5),
            ],
            []);
        }
    }

    [Fact]
    public void NoGrooveIsDeeperThanOneAndAHalfWidths()
    {
        // A 2 m deep crack 0.1 m wide, on a grid whose narrowest groove is 0.15 m: it cuts in at most 1.5 × 0.15 m.
        // Deeper, its walls would stand within 30° of each other at the bottom, a slit the mesh folds to show (random
        // settings with Crease depth up to 2 m). Forty lines 0.5 m deep on 9 m are held the same way to their width
        // (0.05 m on a floor of 0.05 m, so at most 0.075 m deep).
        var cracked = CliffSettings.Defaults with { Creases = 1, CreaseDepth = 2, CreaseWidth = 0.1, CreaseStep = 0, CreaseWobble = 0 };
        var surface = new SlabSurface(cracked, 0.15);
        double deepest = 0;
        for (double x = -14; x <= 14; x += 0.001)
        {
            surface.Push(new V3(x, 0, 4), 1, out double inward);
            deepest = Math.Max(deepest, inward);
        }

        Assert.InRange(deepest, 0.2, (1.5 * 0.15) + 1e-9);

        var lined = CliffSettings.Defaults with { Creases = 0, Lines = 40, LineDepth = 0.5, LineWobble = 0 };
        var lines = new SlabSurface(lined, 0.05);
        double line = 0;
        for (double z = 0.5; z <= 8.5; z += 0.001)
        {
            lines.Push(new V3(0, 0, z), 1, out double inward);
            line = Math.Max(line, inward);
        }

        Assert.InRange(line, 0.05, 0.08);
    }

    [Fact]
    public void LinesTooCloseForTheGridFadeOut()
    {
        // Forty lines on 9 m are 0.225 m apart: on a grid of 0.2 m cells (a floor of 0.3 m) that is about one cell, a
        // ripple the mesh can only fold, so they cut nothing. Ten lines on the same height, 0.9 m apart on 0.1 m cells,
        // are cut in full.
        static double Deepest(int count, double floor)
        {
            var surface = new SlabSurface(CliffSettings.Defaults with { Creases = 0, Lines = count, LineDepth = 0.1, LineWobble = 0 }, floor);
            double most = 0;
            for (double z = 0.5; z <= 8.5; z += 0.002)
            {
                surface.Push(new V3(0, 0, z), 1, out double inward);
                most = Math.Max(most, inward);
            }

            return most;
        }

        Assert.Equal(0, Deepest(40, 0.3));
        Assert.InRange(Deepest(10, 0.15), 0.09, 0.1 + 1e-9);
    }

    [Fact]
    public void TheBaseIsNotCut()
    {
        // With every Surface and Cracks slider strong, the push is nothing below the ground line and at most half the
        // height above it there, so the cliff's footprint at the ground line keeps (nearly) all its area.
        var s = CliffMeshes.Preset("long-cliff") with
        {
            Chisel = 0.3,
            ChiselSize = 1.2,
            EdgeChips = 0.5,
            EdgeWear = 0.6,
            Smudge = 0.5,
            Creases = 3,
            CreaseDepth = 0.6,
            CreaseStep = 0.3,
            Ledges = 2,
            LedgeDepth = 0.5,
            LedgeStep = 0.3,
            Lines = 20,
            LineDepth = 0.1,
        };
        var plainSettings = CliffMeshes.Plain(s);
        double cell = CliffGenerator.CellSize(s, 1);
        var blocks = CliffLayout.Make(s, s.Seed);
        var plain = new SlabCliffField(blocks, plainSettings, 1.5 * cell);
        var pushed = new SlabCliffField(blocks, s, 1.5 * cell);
        var (min, max) = plain.Bounds()!.Value;
        var random = new System.Random(4);
        int near = 0;
        for (int i = 0; i < 20000; i++)
        {
            var p = new V3(
                min.X + ((max.X - min.X) * random.NextDouble()),
                min.Y + ((max.Y - min.Y) * random.NextDouble()),
                -s.Sink + ((0.3 + s.Sink) * random.NextDouble()));
            double push = pushed.Distance(p) - plain.Distance(p);
            Assert.InRange(push, (-0.5 * Math.Max(p.Z, 0)) - 1e-12, (0.5 * Math.Max(p.Z, 0)) + 1e-12);
            near += Math.Abs(plain.Distance(p)) < 0.3 && Math.Abs(push) > 0.01 ? 1 : 0;
        }

        Assert.True(near > 100, $"only {near} points near the base are pushed at all");
        double before = Section(CliffGenerator.Build(plainSettings, 1), 0);
        double after = Section(CliffGenerator.Build(s, 1), 0);
        Assert.True(before > 0.5 * s.Width * s.Depth, $"the plain footprint is {before:F1} m²");
        Assert.True(after >= 0.95 * before, $"the footprint is {after:F1} m², {before:F1} m² without the detail");
    }

    [Fact]
    public void DetailIsPresent()
    {
        // The detail reads in the mesh: on the Long cliff with its default sliders, at least 20% of the surface area
        // turns more than 5° away from the same cliff with every Surface and Cracks slider at 0, at the same grid (each
        // triangle against the plain field's normal at its middle). A triangle count cannot show it: dual contouring at
        // one grid gives about two triangles per cell of surface whatever the detail (129,260 with it, 130,588 without,
        // measured at cell 0.125). Measured here: 25.2% turned more than 5°, 11.5% more than 10°. Read with the Surface
        // sliders the floor was set with (Ruling 13), not the calmer ones the starting points use since Task 8c (the
        // Long cliff's own turn 17.5%): the test is that the detail reads, not how much of it a starting point asks for.
        var s = CliffMeshes.Preset("long-cliff") with
        {
            Chisel = 0.07,
            ChiselSize = 1.6,
            EdgeChips = 0.3,
            ChipSize = 0.6,
            EdgeWear = 0.25,
            WearWidth = 1.0,
            Smudge = 0.04,
        };
        double cell = CliffGenerator.CellSize(s, 1);
        var blocks = CliffLayout.Make(s, s.Seed);
        var plain = new SlabCliffField(blocks, CliffMeshes.Plain(s), 1.5 * cell);
        double detailed = Turned(Mesh(new SlabCliffField(blocks, s, 1.5 * cell), cell), plain);
        Assert.True(detailed >= 0.2, $"{detailed:P1} of the surface turns more than 5° from the plain cliff");

        // The shipped look (review 8-8c Minor 6): the Long cliff's own, calmer sliders still turn at least 12% of the
        // surface (measured 15.4%), well above the plain mesh's own.
        var shipped = CliffMeshes.Preset("long-cliff");
        double calm = Turned(Mesh(new SlabCliffField(blocks, shipped, 1.5 * cell), cell), plain);
        Assert.True(calm >= 0.12, $"{calm:P1} of the shipped Long cliff's surface turns more than 5° from the plain cliff");

        // The baseline: the plain cliff's own mesh turns from its field far less, so the share above is the detail's.
        double baseline = Turned(Mesh(plain, cell), plain);
        Assert.True(baseline < 0.05, $"{baseline:P1} of the plain mesh turns more than 5° from its own field");

        // The share of a mesh's area whose triangles turn more than 5° from the field's normal at their middle.
        static double Turned(SolidMesh mesh, SlabCliffField field)
        {
            double cos = Math.Cos(5 * Math.PI / 180);
            double area = 0;
            double turned = 0;
            for (int t = 0; t < mesh.Indices.Count; t += 3)
            {
                var (normal, size) = CliffMeshes.Face(mesh.Points, mesh.Indices, t);
                var middle = (mesh.Points[mesh.Indices[t]] + mesh.Points[mesh.Indices[t + 1]] + mesh.Points[mesh.Indices[t + 2]]) / 3;
                area += size;
                turned += V3.Dot(normal, Gradient(field, middle).Normalized()) < cos ? size : 0;
            }

            return turned / area;
        }
    }

    [Fact]
    public void NoColourLineWhereBlocksFuse()
    {
        // Two blocks side by side whose fronts meet in a valley at x = 0, fused, coloured at the strongest Variation:
        // across the join (within half a metre of it, on the front, away from the top and the base) the vertex colour
        // changes by at most 0.03 per 10 cm, because the colour patches are read where the vertex lies.
        var s = CliffMeshes.Plain(CliffSettings.Defaults) with { Variation = 1 };
        var left = CliffMeshes.Laid(CliffMeshes.Box(-3, 0.5, 0, 3, -0.5, 4, frontTurn: 35), 0, 0, 0, depth: 3);
        var right = CliffMeshes.Laid(CliffMeshes.Box(-0.5, 3, 0, 3, -0.5, 4, frontTurn: -35), 1, 0, 0, depth: 3);
        var field = new SlabCliffField([left, right], s, 0);
        var mesh = Mesh(field, 0.05);
        mesh.Tags.Clear();
        mesh.Tags.AddRange(mesh.Points.Select(field.Tag));
        var block = CliffBlock.From([mesh], s, []);
        var points = CliffMeshes.Points(block);
        var front = Enumerable.Range(0, points.Length)
            .Where(v => Math.Abs(points[v].X) <= 0.5 && points[v].Z is > 0.5 and < 3.5 && points[v].Y < 1)
            .ToList();
        Assert.True(front.Count > 200, $"{front.Count} vertices across the join");
        double steepest = 0;
        foreach (int a in front)
        {
            foreach (int b in front)
            {
                double apart = (points[a] - points[b]).Length;
                if (b > a && apart <= 0.1)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        steepest = Math.Max(steepest, Math.Abs(block.Colours[(3 * a) + c] - block.Colours[(3 * b) + c]));
                    }
                }
            }
        }

        Assert.True(steepest <= 0.03, $"the colour changes by {steepest:F4} within 10 cm across the join");

        // The colour does drift across a cliff, in broad patches: over 60 m the shift spans a good part of its ± 0.12.
        double[] shifts = [.. Steps(-30, 30, 0.1).Select(x => CliffColours.Shift(s.Seed, new V3(x, -3, 4), 1))];
        Assert.True(shifts.Max() - shifts.Min() > 0.1, $"the shift spans only {shifts.Max() - shifts.Min():F3} over 60 m");
        Assert.All(shifts.Zip(shifts.Skip(1)), pair => Assert.True(Math.Abs(pair.First - pair.Second) < 0.01));
    }

    /// <summary>A field's gradient at a point, by central differences.</summary>
    private static V3 Gradient(SlabCliffField field, V3 p)
    {
        const double H = 1e-4;
        return new V3(
            field.Distance(p + new V3(H, 0, 0)) - field.Distance(p - new V3(H, 0, 0)),
            field.Distance(p + new V3(0, H, 0)) - field.Distance(p - new V3(0, H, 0)),
            field.Distance(p + new V3(0, 0, H)) - field.Distance(p - new V3(0, 0, H))) / (2 * H);
    }

    /// <summary>The closed mesh of a field at a grid spacing, over its bounds.</summary>
    private static SolidMesh Mesh(SlabCliffField field, double cell)
    {
        var (min, max) = field.Bounds()!.Value;
        return DualContour.Mesh(field, min, max, cell);
    }

    /// <summary>The point of the front surface at x and z, read from well in front (y = -8) back to the middle (y = 0); null when the middle is not inside.</summary>
    private static V3? Front(Func<V3, double> f, double x, double z)
    {
        double front = -8;
        double back = 0;
        if (f(new V3(x, back, z)) >= 0 || f(new V3(x, front, z)) <= 0)
        {
            return null;
        }

        for (int i = 0; i < 60; i++)
        {
            double mid = (front + back) / 2;
            if (f(new V3(x, mid, z)) > 0)
            {
                front = mid;
            }
            else
            {
                back = mid;
            }
        }

        return new V3(x, back, z);
    }

    /// <summary>
    /// The area of a closed block's cross-section at a height: each triangle the plane crosses gives a segment, and the
    /// segments, run the way the triangles face, bound the section (the shoelace sum of them).
    /// </summary>
    private static double Section(CliffBlock block, double z)
    {
        var points = CliffMeshes.Points(block);
        double twice = 0;
        for (int t = 0; t < block.Indices.Length; t += 3)
        {
            var crossings = new List<V3>(2);
            for (int k = 0; k < 3; k++)
            {
                var a = points[block.Indices[t + k]];
                var b = points[block.Indices[t + ((k + 1) % 3)]];
                if ((a.Z < z) != (b.Z < z))
                {
                    crossings.Add(a + ((b - a) * ((z - a.Z) / (b.Z - a.Z))));
                }
            }

            if (crossings.Count == 2)
            {
                // Each segment runs along up × the triangle's outward normal, so the section's outline runs
                // counter-clockwise around the solid seen from above, and the shoelace sum is its area.
                var a = points[block.Indices[t]];
                var normal = V3.Cross(points[block.Indices[t + 1]] - a, points[block.Indices[t + 2]] - a);
                var along = new V3(-normal.Y, normal.X, 0);
                var (from, to) = V3.Dot(crossings[1] - crossings[0], along) >= 0 ? (crossings[0], crossings[1]) : (crossings[1], crossings[0]);
                twice += (from.X * to.Y) - (to.X * from.Y);
            }
        }

        return twice / 2;
    }

    /// <summary>The values from <paramref name="from"/> to <paramref name="to"/> in steps.</summary>
    private static IEnumerable<double> Steps(double from, double to, double step)
    {
        for (int i = 0; from + (i * step) <= to + 1e-9; i++)
        {
            yield return from + (i * step);
        }
    }
}
