using System.Text.Json;
using PropGenerator.Nature.Cliffs;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

public class ConvexBlockTests
{
    private static Plane[] Cube() =>
    [
        new(new V3(1, 0, 0), 1), new(new V3(-1, 0, 0), 1),
        new(new V3(0, 1, 0), 1), new(new V3(0, -1, 0), 1),
        new(new V3(0, 0, 1), 1), new(new V3(0, 0, -1), 1),
    ];

    [Fact]
    public void ACubeOfSixPlanesIsInsideAtItsCentreAndOutsideBeyond()
    {
        var block = new ConvexBlock(Cube(), []);
        Assert.Equal(-1, block.Distance(new V3(0, 0, 0)), 12);
        Assert.Equal(1, block.Distance(new V3(2, 0, 0)), 12);
        Assert.Equal(0, block.Estimate(new V3(2, 0, 0), out double error) - 1, 12);
        Assert.Equal(0, error);
    }

    [Fact]
    public void AWedgeCutRemovesOnlyItsCorner()
    {
        Plane[] cut = [new(new V3(1, 0, 0), 0.5), new(new V3(0, 0, 1), 0.5)];
        var block = new ConvexBlock(Cube(), [cut]);
        Assert.True(block.Distance(new V3(0.9, 0, 0.9)) > 0);
        Assert.True(block.Distance(new V3(0.9, 0, 0)) < 0);
    }

    [Fact]
    public void TheCubesCornersAndWidthsAreExact()
    {
        var block = new ConvexBlock(Cube(), []);
        Assert.Equal(8, block.Corners().Count);
        Assert.Equal(2, block.MinWidth(), 12);
        Assert.Equal(2 * Math.Sqrt(2), block.WidthAlong(new V3(1, 1, 0) / Math.Sqrt(2)), 12);
        var (min, max) = block.Bounds();
        Assert.Equal(new V3(-1, -1, -1), min);
        Assert.Equal(new V3(1, 1, 1), max);
    }

    [Fact]
    public void ItMatchesTheFittedCliff01()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cliffs", "Data", "cliff01_formula.json")));
        var root = doc.RootElement;
        Plane[] planes =
        [
            .. root.GetProperty("planes").EnumerateArray().Select(e =>
            {
                double[] n = [.. e.GetProperty("n").EnumerateArray().Select(v => v.GetDouble())];
                return new Plane(new V3(n[0], n[1], n[2]), e.GetProperty("d").GetDouble());
            }),
        ];
        int[] hull = [.. root.GetProperty("hull").EnumerateArray().Select(v => v.GetInt32())];
        int[][] cuts = [.. root.GetProperty("cuts").EnumerateArray().Select(c => (int[])[.. c.EnumerateArray().Select(v => v.GetInt32())])];
        var block = new ConvexBlock(
            [.. hull.Select(i => planes[i])],
            [.. cuts.Select(c => (IReadOnlyList<Plane>)[.. c.Select(i => planes[i])])]);

        var rng = new System.Random(7);
        for (int k = 0; k < 1000; k++)
        {
            var p = new V3((rng.NextDouble() * 12) - 6, (rng.NextDouble() * 12) - 6, (rng.NextDouble() * 12) - 6);
            double f = double.NegativeInfinity;
            foreach (int i in hull)
            {
                f = Math.Max(f, (planes[i].Normal.X * p.X) + (planes[i].Normal.Y * p.Y) + (planes[i].Normal.Z * p.Z) - planes[i].Offset);
            }

            foreach (int[] cut in cuts)
            {
                double m = double.PositiveInfinity;
                foreach (int i in cut)
                {
                    m = Math.Min(m, (planes[i].Normal.X * p.X) + (planes[i].Normal.Y * p.Y) + (planes[i].Normal.Z * p.Z) - planes[i].Offset);
                }

                f = Math.Max(f, m);
            }

            Assert.Equal(f, block.Distance(p), 12);
        }

        Assert.True(block.Corners().Count >= 8);
    }

    [Fact]
    public void TheNearestEdgeIsTheTwoLargestPlanes()
    {
        var block = new ConvexBlock(Cube(), []);
        var (a, b, gapA, gapB) = block.NearestEdge(new V3(0.9, 0, 0.8));
        Assert.Equal([0, 4], new[] { a, b }.Order());
        Assert.Equal(0.1, a == 0 ? gapA : gapB, 12);
        Assert.Equal(0.2, a == 0 ? gapB : gapA, 12);
    }
}
