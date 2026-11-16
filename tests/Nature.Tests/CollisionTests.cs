using PropGenerator.Nature.Export;
using PropGenerator.Nature.Kernel;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class CollisionTests
{
    [Fact]
    public void NodeNamesAreTheEnginesOwn()
    {
        Assert.Equal("UCX_oak_LOD0_03", Collision.NodeName("unreal", "oak", 3));
        Assert.Equal("UCX_oak_12", Collision.NodeName("unreal", "oak", 12, "oak"));
        Assert.Equal("oak_3-convcolonly", Collision.NodeName("godot", "oak", 3));
        Assert.Equal("oak_collision_3", Collision.NodeName("plain", "oak", 3));
        Assert.Equal("oak_collision_0", Collision.NodeName("anything", "oak", 0));
    }

    [Fact]
    public void AtMostEightShapesAreKeptHeaviestFirstAndTiesKeepTheirOrder()
    {
        var shapes = Enumerable.Range(0, 10)
            .Select(i => new CollisionShape(new Vec3d(0, 0, 0), new Vec3d(0, 1, 0), i % 3, 0))
            .ToList();
        var chosen = Collision.Choose(shapes, []);
        Assert.Equal(Collision.MaxShapes, chosen.Count);
        Assert.Equal([shapes[2], shapes[5], shapes[8], shapes[1], shapes[4], shapes[7], shapes[0], shapes[3]], chosen);
    }

    [Fact]
    public void ANanWeightSortsAsArrayPrototypeSortSortsIt()
    {
        // Weights 1, 4, NaN, 9. V8's sort finds a run of two the wrong way round (lighter first) and turns it round, then
        // inserts the rest by binary search, where NaN counts as equal: 9, 4, 1, NaN. An insertion sort that stops at the
        // NaN would leave 4, 1, NaN, 9.
        static CollisionShape Weighing(double radius) => new(new Vec3d(0, 0, 0), new Vec3d(0, 1, 0), radius, 0);
        var (a, b, nan, c) = (Weighing(1), Weighing(2), Weighing(double.NaN), Weighing(3));
        Assert.Equal([c, b, a, nan], Collision.Choose([a, b, nan, c], []));
    }

    [Fact]
    public void ARunLightestFirstIsTurnedRoundAndEqualWeightsKeepTheirOrder()
    {
        static CollisionShape Weighing(double radius) => new(new Vec3d(0, 0, 0), new Vec3d(0, 1, 0), radius, 0);
        var shapes = new[] { 1.0, 1, 2, 2, 3 }.Select(Weighing).ToList();
        Assert.Equal([shapes[4], shapes[2], shapes[3], shapes[0], shapes[1]], Collision.Choose(shapes, []));
        var rising = new[] { 1.0, 2, 3 }.Select(Weighing).ToList();
        Assert.Equal([rising[2], rising[1], rising[0]], Collision.Choose(rising, []));
    }
}
