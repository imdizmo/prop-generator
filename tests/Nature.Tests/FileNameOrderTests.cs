using PropGenerator.Nature.Export;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class FileNameOrderTests
{
    [Fact]
    public void AnExportListsItsFilesAsEasyPropsDoes()
    {
        string[] files = ["windy.asset.json", "windy_LOD2.glb", "windy_collision.glb", "windy_LOD0.glb", "windy_LOD1.glb"];
        Assert.Equal(
            ["windy_collision.glb", "windy_LOD0.glb", "windy_LOD1.glb", "windy_LOD2.glb", "windy.asset.json"],
            files.Order(FileNameOrder.Instance));
        string[] one = ["broken_collision.glb", "broken.glb", "broken.asset.json"];
        Assert.Equal(["broken_collision.glb", "broken.asset.json", "broken.glb"], one.Order(FileNameOrder.Instance));
    }

    [Fact]
    public void CaseCountsOnlyWhenTheLettersAreTheSameAndLowerComesFirst()
    {
        Assert.True(FileNameOrder.Instance.Compare("a_lod0", "a_LOD0") < 0);
        Assert.True(FileNameOrder.Instance.Compare("A_b", "a_C") < 0);
        Assert.True(FileNameOrder.Instance.Compare("ab", "Ab") < 0);
        Assert.Equal(0, FileNameOrder.Instance.Compare("Oak", "Oak"));
    }

    [Fact]
    public void SpaceAndPunctuationComeBeforeDigitsAndDigitsBeforeLetters()
    {
        string[] chars = ["a", "0", ".", "-", "_", " ", "9", "Z"];
        Assert.Equal([" ", "_", "-", ".", "0", "9", "a", "Z"], chars.Order(FileNameOrder.Instance));
        Assert.True(FileNameOrder.Instance.Compare("oak", "oak.glb") < 0);
    }

    [Fact]
    public void ACharacterNoExportNameHasIsRefused()
    {
        Assert.Throws<ArgumentException>(() => FileNameOrder.Instance.Compare("é", "e"));
        Assert.Throws<ArgumentException>(() => FileNameOrder.Instance.Compare("a#", "a"));
    }
}
