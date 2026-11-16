using PropGenerator.Nature.Style;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class MaterialsTests
{
    [Fact]
    public void ColoursAndTintsMatchEasyPropsBitForBit()
    {
        using var json = Fixture.Json("material-tints.json");
        int checkedKinds = 0;
        foreach (var table in json.RootElement.GetProperty("tints").EnumerateArray())
        {
            var materials = table.GetProperty("materials");
            var scene = new SceneMaterials(materials.GetProperty("bark").GetString()!, materials.GetProperty("leaves").GetString()!);
            foreach (var kind in table.GetProperty("colours").EnumerateObject())
            {
                Assert.Equal(kind.Value.GetProperty("hex").GetString(), Materials.ColourOf(kind.Name, scene));
                var tint = Materials.Tint(kind.Name, scene);
                var want = kind.Value.GetProperty("tint");
                double[] ours = [tint.R, tint.G, tint.B];
                for (int c = 0; c < 3; c++)
                {
                    Assert.True(Exact.Same(want[c].GetDouble(), ours[c]), $"{kind.Name} with {scene}: channel {c}, port {ours[c]:R}");
                }

                checkedKinds++;
            }
        }

        Assert.Equal(18, checkedKinds);
    }

    [Fact]
    public void TheDefaultsAreEasyProps()
    {
        Assert.Equal(new SceneMaterials("#6b4e35", "#4e7d3e"), SceneMaterials.Default);
        Assert.Equal("#7a5c3e", Materials.ColourOf("coconut", SceneMaterials.Default));
        Assert.Equal("#ffffff", Materials.ColourOf("painted", new SceneMaterials("#000", "#000")));
    }

    [Theory]
    [InlineData("#000000", 0.5, "#808080")]
    [InlineData("#abc", 0.2, "#bbc9d6")]
    [InlineData("not a colour", 0.2, "not a colour")]
    public void LightenMixesTowardsWhiteRoundingAsJavaScriptRounds(string hex, double amount, string mixed) =>
        Assert.Equal(mixed, Materials.Lighten(hex, amount));

    [Fact]
    public void DarkenMixesTowardsBlack() => Assert.Equal("#000000", Materials.Darken("#ffffff", 1));

    [Fact]
    public void AnUnknownMaterialThrows() =>
        Assert.Throws<ArgumentException>(() => Materials.ColourOf("marble", SceneMaterials.Default));
}
