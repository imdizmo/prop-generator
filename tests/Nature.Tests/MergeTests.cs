using PropGenerator.Nature.Export;
using PropGenerator.Nature.Kernel;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class MergeTests
{
    [Fact]
    public void ComicSkipsPartsWithoutColoursAndLeavesWithoutAMap()
    {
        // A part without colours and leaves without a map: nothing to flatten or to round, and nothing throws.
        var plain = Triangle("bark");
        var leaves = Triangle("leaves") with { Surface = new PaintedSurface(false, AlphaTest: 0.5) };
        var lit = Merge.Parts([plain, leaves], "p", new Vec3d(0, 0, 0), null, true);
        var comic = Merge.Parts([plain, leaves], "p", new Vec3d(0, 0, 0), null, true, comic: true);
        Assert.Equal(lit.Count, comic.Count);
        for (int i = 0; i < lit.Count; i++)
        {
            Assert.Equal(lit[i].Mesh.Color, comic[i].Mesh.Color);
            Assert.Equal(lit[i].Mesh.Normal, comic[i].Mesh.Normal);
        }
    }

    [Fact]
    public void APartWithANanPositionIsDropped()
    {
        var good = Triangle("leaves");
        var broken = Triangle("leaves");
        broken.Mesh.Position[4] = float.NaN;
        var merged = Merge.Parts([broken, good], "p", new Vec3d(0, 0, 0), null, true);
        var mesh = Assert.Single(merged);
        Assert.Equal(3, mesh.Mesh.VertexCount);
        Assert.Equal(1, mesh.Faces);
    }

    [Fact]
    public void PartsWithAnInfinityNoVerticesOrAProxyAreDropped()
    {
        var infinite = Triangle("bark");
        infinite.Mesh.Position[0] = float.PositiveInfinity;
        var empty = Triangle("bark") with { Mesh = new PropMesh { Position = [] } };
        var proxy = Triangle("bark") with { Surface = new PaintedSurface(false, Proxy: true) };
        Assert.Empty(Merge.Parts([infinite, empty, proxy], "p", new Vec3d(0, 0, 0), null, true));
    }

    [Fact]
    public void BucketsAreNamedInTheOrderFirstMetWithTwoForARepeat()
    {
        var plainWood = Triangle("bark");
        var leaves = Triangle("leaves") with { Surface = new PaintedSurface(false, MapKey: "leaf"), AttributeOrder = ["position", "uv"] };
        leaves.Mesh.Uv = [0, 0, 1, 0, 0, 1];
        var texturedWood = Triangle("bark") with { Surface = new PaintedSurface(true, MapKey: "bark:none:0"), AttributeOrder = ["position", "uv"] };
        texturedWood.Mesh.Uv = [0, 0, 1, 0, 0, 1];
        var merged = Merge.Parts([plainWood, leaves, texturedWood, Triangle("coconut")], "oak", new Vec3d(0, 0, 0), null, true);
        Assert.Equal(["oak_wood", "oak_leaves", "oak_wood_2", "oak_leaves_2"], merged.Select(m => m.Name));
        Assert.Equal(["white", "leaf", "bark:none:0", "white"], merged.Select(m => m.Material.MapKey));
        Assert.Equal([0.85, 1, 1, 0.85], merged.Select(m => m.Material.Roughness));
    }

    [Fact]
    public void AMergedMeshCarriesWindOnlyWhenAsked()
    {
        var still = Merge.Parts([Triangle("leaves")], "p", new Vec3d(0, 0, 0), null, true)[0];
        Assert.Equal(["position", "color", "normal", "uv"], still.AttributeOrder);
        var windy = Merge.Parts([Triangle("leaves")], "p", new Vec3d(0, 0, 0), new WindExtent(1, 1), true)[0];
        Assert.Equal(["position", "color", "normal", "uv", "uv1"], windy.AttributeOrder);
    }

    [Fact]
    public void JoiningNothingGivesNothing() => Assert.Empty(Merge.Join([], "p"));

    [Fact]
    public void JoiningKeepsEachMeshsMaterialAsAGroup()
    {
        var merged = Merge.Parts([Triangle("bark"), Triangle("leaves")], "p", new Vec3d(0, 0, 0), null, true);
        var joined = Assert.Single(Merge.Join(merged, "p"));
        Assert.Equal("p", joined.Name);
        Assert.Equal(2, joined.Faces);
        Assert.Equal([(0, 3, 0), (3, 3, 1)], joined.Mesh.Groups);
        Assert.Equal(merged.Select(m => m.Material), joined.Materials);
    }

    private static GeneratedPart Triangle(string material) =>
        new(new PropMesh { Position = [0, 0, 0, 1, 0, 0, 0, 1, 0] }, material, null, null, ["position"], new Mat4d(), 1);
}
