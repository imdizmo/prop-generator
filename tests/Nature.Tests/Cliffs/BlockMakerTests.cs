using PropGenerator.Nature.Cliffs;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

public class BlockMakerTests
{
    private const int Count = 200;
    private const double Deg = Math.PI / 180;

    private static double Draw(uint seed, int salt, double lo, double hi) =>
        lo + ((hi - lo) * CliffNoise.Hash01(seed, 9000 + salt));

    private static BlockShape Random(uint seed)
    {
        double depth = Draw(seed, 0, 1.5, 9);
        double width = Draw(seed, 1, depth, 24);
        return new BlockShape(
            new V3(Draw(seed, 2, -5, 5), Draw(seed, 3, -5, 5), Draw(seed, 4, -2, 2)),
            width,
            depth,
            Draw(seed, 5, 1.5, 12),
            Draw(seed, 6, 0, 1.5),
            (int)Draw(seed, 7, 4, 17),
            Draw(seed, 8, 0, 0.5),
            Draw(seed, 9, -15, 30),
            Draw(seed, 10, 0, 15),
            Draw(seed, 11, 0, 0.6),
            (int)Draw(seed, 12, 0, 3),
            (int)Draw(seed, 13, 0, 3),
            (int)Draw(seed, 14, 0, 3),
            Draw(seed, 15, 0, 1),
            CliffNoise.Hash01(seed, 9016) < 0.5 ? 0 : Draw(seed, 17, 20, 50),
            CliffNoise.Hash01(seed, 9018) < 0.5 ? 0 : Draw(seed, 19, 20, 50),
            Math.Min(0.6, depth / 3));
    }

    private static BlockShape Plain(double lean = 0, double turnLeft = 0, double turnRight = 0) =>
        new(new V3(1, 2, 0.5), 12, 4, 6, 0.5, 8, 0, lean, 0, 0, 0, 0, 0, 0, turnLeft, turnRight, 0.6);

    private static double Area(ConvexBlock block, double z, double step = 0.1)
    {
        var (min, max) = block.Bounds();
        int n = 0;
        for (double x = min.X; x <= max.X; x += step)
        {
            for (double y = min.Y; y <= max.Y; y += step)
            {
                if (block.Distance(new V3(x, y, z)) < 0)
                {
                    n++;
                }
            }
        }

        return n * step * step;
    }

    private static Plane NearestTo(ConvexBlock block, V3 direction) =>
        block.Shell.OrderByDescending(p => V3.Dot(p.Normal, direction)).First();

    [Fact]
    public void TheSameShapeAndSeedGiveTheSameBlock()
    {
        for (uint seed = 1; seed <= Count; seed++)
        {
            var a = BlockMaker.Make(Random(seed), seed);
            var b = BlockMaker.Make(Random(seed), seed);
            Assert.Equal(a.Shell, b.Shell);
            Assert.Equal(a.Cuts.Count, b.Cuts.Count);
            for (int i = 0; i < a.Cuts.Count; i++)
            {
                Assert.Equal(a.Cuts[i], b.Cuts[i]);
            }
        }
    }

    [Fact]
    public void EveryBlockHoldsItsBaseCentreAndStaysThick()
    {
        for (uint seed = 1; seed <= Count; seed++)
        {
            var shape = Random(seed);
            var block = BlockMaker.Make(shape, seed);
            Assert.True(block.Distance(shape.Base + new V3(0, 0, shape.Height / 2)) < 0, $"seed {seed}");
            Assert.True(block.MinWidth() >= shape.MinThickness - 1e-9, $"seed {seed}: {block.MinWidth()}");
        }
    }

    [Fact]
    public void TheBottomIsFlatAtSinkBelowTheBase()
    {
        for (uint seed = 1; seed <= Count; seed++)
        {
            var shape = Random(seed);
            var block = BlockMaker.Make(shape, seed);
            Assert.Contains(
                block.Shell,
                p => Math.Abs(p.Normal.X) < 1e-12 && Math.Abs(p.Normal.Y) < 1e-12 && Math.Abs(p.Normal.Z + 1) < 1e-12
                    && Math.Abs(p.Offset - (shape.Sink - shape.Base.Z)) < 1e-9);
        }
    }

    [Fact]
    public void NoCutGoesDeeperThanFortyPercent()
    {
        int cutsSeen = 0;
        for (uint seed = 1; seed <= Count; seed++)
        {
            var shape = Random(seed) with { Width = 6, Depth = 4, Height = 4, MinThickness = 0.6 };
            var block = BlockMaker.Make(shape, seed);
            var shellOnly = new ConvexBlock(block.Shell, []);
            var (min, max) = shellOnly.Bounds();
            foreach (var cut in block.Cuts)
            {
                cutsSeen++;
                var n = cut[0].Normal;
                double hi = shellOnly.Corners().Max(c => V3.Dot(n, c));
                double limit = 0.4 * shellOnly.WidthAlong(n);
                for (double x = min.X; x <= max.X; x += 0.1)
                {
                    for (double y = min.Y; y <= max.Y; y += 0.1)
                    {
                        for (double z = min.Z; z <= max.Z; z += 0.1)
                        {
                            var p = new V3(x, y, z);
                            if (cut.All(pl => pl.At(p) >= 0) && shellOnly.Distance(p) < 0)
                            {
                                Assert.True(hi - V3.Dot(n, p) <= limit + 1e-9, $"seed {seed}");
                            }
                        }
                    }
                }
            }
        }

        Assert.True(cutsSeen > 100);
    }

    [Fact]
    public void StepsWhoseWallsWouldLeaveAThinFinAreCaught()
    {
        // A box 12 × 4 × 6 m (x 0..12, y 0..4, z 0..6), both steps taking the top 1 m.
        var box = new ConvexBlock(
        [
            new Plane(new V3(1, 0, 0), 12), new Plane(new V3(-1, 0, 0), 0), new Plane(new V3(0, 1, 0), 4),
            new Plane(new V3(0, -1, 0), 0), new Plane(new V3(0, 0, 1), 6), new Plane(new V3(0, 0, -1), 0),
        ],
        []);
        var floor = new Plane(new V3(0, 0, 1), 5);
        var left = new Plane(new V3(-1, 0, 0), -4);

        // Facing walls 0.3 m apart, and 2 m apart.
        Assert.True(BlockMaker.LeavesFin(box, [floor, left], [floor, new Plane(new V3(1, 0, 0), 4.3)], 0.6));
        Assert.False(BlockMaker.LeavesFin(box, [floor, left], [floor, new Plane(new V3(1, 0, 0), 6)], 0.6));

        // Walls turned 20° from each other that cross inside the box at y = 2: the fin between them is 0.73 m thick at
        // y = 0 and thins to nothing. A check through the block's heart (6, 2, 3) alone, past both walls, saw no fin.
        var turned = new V3(Math.Cos(20 * Deg), Math.Sin(20 * Deg), 0);
        Assert.True(BlockMaker.LeavesFin(box, [floor, left], [floor, new Plane(turned, V3.Dot(turned, new V3(4, 2, 0)))], 0.6));

        // Walls that meet at a right angle leave a corner, not a fin.
        Assert.False(BlockMaker.LeavesFin(box, [floor, left], [floor, new Plane(new V3(0, 1, 0), 2)], 0.6));
    }

    [Fact]
    public void CutsWhoseFacesCrossAtAKnifeEdgeAreCaught()
    {
        // The same box. A step off the top 1 m left of x = 4, and a bite out of the front 1 m whose cross plane, turned
        // 20° from facing the step's wall, crosses it at y = 2: the rock between them in front of y = 1 is 0.36 m thick
        // at most and thins to a knife edge (the Long cliff's seed 9 had one, 17° sharp, which the mesh showed as a row of
        // teeth).
        var box = new ConvexBlock(
        [
            new Plane(new V3(1, 0, 0), 12), new Plane(new V3(-1, 0, 0), 0), new Plane(new V3(0, 1, 0), 4),
            new Plane(new V3(0, -1, 0), 0), new Plane(new V3(0, 0, 1), 6), new Plane(new V3(0, 0, -1), 0),
        ],
        []);
        IReadOnlyList<Plane> step = [new Plane(new V3(0, 0, 1), 5), new Plane(new V3(-1, 0, 0), -4)];
        var turned = new V3(Math.Cos(20 * Deg), Math.Sin(20 * Deg), 0);
        IReadOnlyList<Plane> bite = [new Plane(new V3(0, -1, 0), -1), new Plane(turned, V3.Dot(turned, new V3(4, 2, 0)))];
        Assert.True(BlockMaker.LeavesFin(box, step, bite, 0.6));
        Assert.True(BlockMaker.LeavesAnyFin(box, bite, [step], 0.6));

        // The bite's cross plane 3 m further right leaves 3 m of rock: no fin.
        IReadOnlyList<Plane> far = [bite[0], new Plane(turned, V3.Dot(turned, new V3(7, 2, 0)))];
        Assert.False(BlockMaker.LeavesAnyFin(box, far, [step], 0.6));

        // A step whose wall crosses the box's own left end at 20° (at y = 2, 0.73 m in from it at y = 0) leaves a knife
        // edge against the shell.
        IReadOnlyList<Plane> edge = [new Plane(new V3(0, 0, 1), 5), new Plane(turned, V3.Dot(turned, new V3(0, 2, 0)))];
        Assert.True(BlockMaker.LeavesAnyFin(box, edge, [], 0.6));
        Assert.False(BlockMaker.LeavesAnyFin(box, step, [], 0.6));
    }

    [Fact]
    public void NoBlockKeepsAThinFinBetweenAnyTwoCuts()
    {
        // Blocks asking for many cuts of every kind: no pair of cuts a block keeps, and no cut with the shell, leaves a
        // fin thinner than Min thickness.
        int cuts = 0;
        for (uint seed = 1; seed <= Count; seed++)
        {
            var shape = Random(seed) with { Steps = 3, Bites = 4, Grooves = 2 };
            var block = BlockMaker.Make(shape, seed);
            var shell = new ConvexBlock(block.Shell, []);
            for (int i = 0; i < block.Cuts.Count; i++)
            {
                cuts++;
                Assert.False(BlockMaker.LeavesAnyFin(shell, block.Cuts[i], block.Cuts.Take(i), shape.MinThickness), $"seed {seed}, cut {i}");
            }
        }

        Assert.True(cuts > 300, $"{cuts} cuts");
    }

    [Fact]
    public void NoBlockKeepsAThinFinBetweenTwoSteps()
    {
        // Blocks asking for many steps: every pair of steps a block keeps leaves its fin, if any, at least Min thickness.
        int pairs = 0;
        for (uint seed = 1; seed <= Count; seed++)
        {
            var shape = Random(seed) with { Steps = 6 };
            var block = BlockMaker.Make(shape, seed);
            var shell = new ConvexBlock(block.Shell, []);
            var top = block.Shell[Math.Max(shape.Faces, 4)];
            var steps = block.Cuts.Where(cut => cut.Count == 2 && cut[0].Normal == top.Normal).ToList();
            for (int i = 0; i < steps.Count; i++)
            {
                for (int j = i + 1; j < steps.Count; j++)
                {
                    pairs++;
                    Assert.False(BlockMaker.LeavesFin(shell, steps[i], steps[j], shape.MinThickness), $"seed {seed}");
                }
            }
        }

        Assert.True(pairs > 100, $"{pairs} pairs of steps");
    }

    [Fact]
    public void LeanBattersOnlyTheFront()
    {
        for (uint seed = 1; seed <= Count; seed++)
        {
            var shape = Plain(lean: 20);
            var block = BlockMaker.Make(shape, seed);
            var front = NearestTo(block, new V3(0, -1, 0));
            double dy = (front.Normal.Z * shape.Height) / -front.Normal.Y;
            Assert.InRange(dy, Math.Tan(16 * Deg) * shape.Height, Math.Tan(24 * Deg) * shape.Height);
            var back = NearestTo(block, new V3(0, 1, 0));
            Assert.True(Math.Abs(back.Normal.Z) <= Math.Sin(5 * Deg), $"seed {seed}");
        }
    }

    [Fact]
    public void TheTopKeepsItsThicknessAtAnyLean()
    {
        for (uint seed = 1; seed <= 50; seed++)
        {
            var shape = Plain(lean: 70) with { Depth = 3, Height = 10 };
            var block = BlockMaker.Make(shape, seed);
            double z = shape.Base.Z + (shape.Height * 0.9);
            int n = 0;
            for (double y = shape.Base.Y - 3; y <= shape.Base.Y + 3; y += 0.02)
            {
                if (block.Distance(new V3(shape.Base.X, y, z)) < 0)
                {
                    n++;
                }
            }

            Assert.True(n * 0.02 >= shape.MinThickness - 0.05, $"seed {seed}: {n * 0.02}");
        }
    }

    [Fact]
    public void ANegativeLeanOverhangsButStillStands()
    {
        for (uint seed = 1; seed <= Count; seed++)
        {
            var shape = new BlockShape(
                new V3(0, 0, 0), Draw(seed, 1, 6, 10), Draw(seed, 0, 4, 8), 5, 0.5, (int)Draw(seed, 7, 8, 17),
                Draw(seed, 8, 0, 0.05), -10, 0, 0, 0, 0, 0, 0, 0, 0, 0.6);
            var block = BlockMaker.Make(shape, seed);
            Assert.True(Area(block, 0) >= 0.9 * shape.Width * shape.Depth, $"seed {seed}");
            Assert.True(block.Distance(new V3(0, 0, 2.5)) < 0);
        }
    }

    [Fact]
    public void EndTurnsTurnTheEndFacesBack()
    {
        for (uint seed = 1; seed <= Count; seed++)
        {
            var block = BlockMaker.Make(Plain(turnLeft: 35, turnRight: 25), seed);
            var left = NearestTo(block, new V3(-1, 0, 0));
            Assert.Equal(35, Math.Acos(-left.Normal.X) / Deg, 1);
            Assert.True(left.Normal.Y < 0);
            Assert.Equal(0, left.Normal.Z, 12);
            var right = NearestTo(block, new V3(1, 0, 0));
            Assert.Equal(25, Math.Acos(right.Normal.X) / Deg, 1);
            Assert.True(right.Normal.Y < 0);

            // The front end corners go; the back end corners stay.
            var plain = BlockMaker.Make(Plain(), seed);
            foreach (double end in new[] { -1.0, 1.0 })
            {
                var front = new V3(1 + (end * (6 - 1.2)), 2 - 2 + 0.8, 0.5);
                var back = new V3(1 + (end * (6 - 1.2)), 2 + 2 - 0.8, 0.5);
                Assert.True(plain.Distance(front) < 0);
                Assert.True(block.Distance(front) > 0, $"seed {seed} end {end}");
                Assert.True(block.Distance(back) < 0, $"seed {seed} end {end}");
            }
        }
    }

    [Fact]
    public void MoreFacesMakeARounderFootprint()
    {
        double few = 0;
        double many = 0;
        for (uint seed = 1; seed <= Count; seed++)
        {
            var shape = Plain() with { Width = 6, Depth = 4, Height = 3, Sink = 0.2, Base = new V3(0, 0, 0) };
            few += CornerLoss(BlockMaker.Make(shape with { Faces = 4 }, seed), shape);
            many += CornerLoss(BlockMaker.Make(shape with { Faces = 16 }, seed), shape);
        }

        Assert.True(many > few, $"{many} vs {few}");
    }

    private static double CornerLoss(ConvexBlock block, BlockShape shape)
    {
        int n = 0;
        for (double x = -shape.Width / 2; x < shape.Width / 2; x += 0.1)
        {
            for (double y = -shape.Depth / 2; y < shape.Depth / 2; y += 0.1)
            {
                if (block.Distance(new V3(x + 0.05, y + 0.05, 0)) < 0)
                {
                    n++;
                }
            }
        }

        return (shape.Width * shape.Depth) - (n * 0.01);
    }
}
