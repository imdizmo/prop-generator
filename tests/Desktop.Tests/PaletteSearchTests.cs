using PropGenerator.Desktop.Searching;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class PaletteSearchTests
{
    [Theory]
    [InlineData("exp", "Export…", "", 8.0)] // the label starts with the word
    [InlineData("EXPORT", "Export…", "", 8.0)] // case does not matter
    [InlineData("part", "Remove part", "", 6.0)] // a label part starts with it (split on spaces and : ( ) … -)
    [InlineData("level", "Detail (level)", "", 6.0)]
    [InlineData("0", "Level 0 (LOD0)", "", 6.0)]
    [InlineData("groundwork", "Export…", "File groundwork map", 3.0)] // a part of label and keywords
    [InlineData("move", "Remove part", "", 2.0)] // only inside a word
    [InlineData("save copy", "Save a copy…", "", 14.0)] // 8 for "save", 6 for "copy"
    [InlineData("save zzz", "Save a copy…", "", -1.0)] // every word must match
    [InlineData("expotr", "Export…", "", 0.5)] // one swapped pair
    [InlineData("exp0rt", "Export…", "", 0.5)] // one wrong letter
    [InlineData("exort", "Export…", "", 0.5)] // one missing letter
    [InlineData("exo", "Export…", "", -1.0)] // under four letters there is no typo tolerance
    [InlineData("exoprts", "Export…", "", -1.0)] // two slips
    [InlineData("lod", "Detail level", "", 6.5)] // a synonym of a label part
    [InlineData("lod", "Show stats", "detail level", -1.0)] // keywords do not count for a synonym
    [InlineData("delete", "Remove part", "", 6.5)]
    [InlineData("reset", "Restore to preset", "", 6.5)] // beats the 2 for "reset" inside "preset"
    [InlineData("colour", "Wood color", "", 6.5)]
    [InlineData("color", "Wood colour", "", 6.5)]
    [InlineData("tris", "Faces", "", 6.5)]
    [InlineData("options", "Settings", "", 6.5)]
    [InlineData("zzz", "Export…", "groundwork", -1.0)]
    public void ScoresFollowEasyProps(string query, string label, string keywords, double expected) =>
        Assert.Equal(expected, PaletteSearch.Score(query, label, keywords), 6);

    [Fact]
    public void KeepOnlyRealMatchesWhenThereAreAny()
    {
        string[] names = ["Export", "Expro"];
        Assert.Equal(["Export"], PaletteSearch.Rank(names, "expor", n => n, _ => string.Empty));

        // With nothing better, the typo answers.
        Assert.Equal(["Export"], PaletteSearch.Rank(["Export", "Save"], "exprot", n => n, _ => string.Empty));
    }

    [Fact]
    public void UseOnlyBreaksNearTies()
    {
        string[] names = ["Copy part", "Copy seed"];
        Assert.Equal(["Copy part", "Copy seed"], PaletteSearch.Rank(names, "copy", n => n, _ => string.Empty));
        Assert.Equal(
            ["Copy seed", "Copy part"],
            PaletteSearch.Rank(names, "copy", n => n, _ => string.Empty, n => n == "Copy seed" ? 5 : 0));

        // Never above a clearly better match, however much it was used, and the count stops at 20.
        Assert.Equal(
            ["Copy part", "Recopy"],
            PaletteSearch.Rank(["Recopy", "Copy part"], "copy", n => n, _ => string.Empty, n => n == "Recopy" ? 999 : 0));
    }

    [Fact]
    public void PrefixesLimitTheSources()
    {
        Assert.Equal((PaletteSection.Actions, "save"), PaletteSearch.ParsePrefix(">save"));
        Assert.Equal((PaletteSection.Controls, "stone"), PaletteSearch.ParsePrefix("#stone"));
        Assert.Equal((PaletteSection.Presets, string.Empty), PaletteSearch.ParsePrefix("@"));
        Assert.Equal((null, "save"), PaletteSearch.ParsePrefix("save"));
        Assert.Equal((null, string.Empty), PaletteSearch.ParsePrefix(string.Empty));
    }
}
