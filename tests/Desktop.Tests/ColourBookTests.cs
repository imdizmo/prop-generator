using System.Text;
using PropGenerator.Desktop.Colours;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class ColourBookTests
{
    [Theory]
    [InlineData("#4E7D3E", "#4e7d3e")]
    [InlineData("4e7d3e", "#4e7d3e")]
    [InlineData("#abc", "#aabbcc")]
    [InlineData("abc", "#aabbcc")]
    [InlineData("rgb(78, 125, 62)", "#4e7d3e")]
    [InlineData("rgba(78 125 62 / 0.5)", "#4e7d3e")]
    [InlineData("rgb(300, 0, 0)", null)]
    [InlineData("#12345", null)]
    [InlineData("green", null)]
    public void OneCodeIsReadInEveryUsualForm(string text, string? hex) => Assert.Equal(hex, ColourCodes.ParseOne(text));

    [Fact]
    public void APastedListKeepsItsOrderDropsRepeatsAndCountsWhatIsNotAColour()
    {
        (IReadOnlyList<string> codes, int bad) = ColourCodes.ParseList("#ff0000, 00ff00;#FF0000\n rgb(0, 0, 255) nope abc");
        Assert.Equal(["#ff0000", "#00ff00", "#0000ff", "#aabbcc"], codes);
        Assert.Equal(1, bad);
    }

    [Fact]
    public void RecentKeepsEightNewestFirstAndMyColoursAndPalettesAreKept()
    {
        using var s = new Scratch();
        var book = new ColourBook(s.P("colours.json"));
        for (int i = 0; i < 10; i++)
        {
            book.NoteRecent($"#00000{i}");
        }

        book.NoteRecent("#000009");
        Assert.Equal(8, book.Recent.Count);
        Assert.Equal("#000009", book.Recent[0]);
        Assert.Equal("#000008", book.Recent[1]);

        Assert.Equal(2, book.AddMine(["#111111", "#222222", "#111111"]));
        book.RemoveMine("#111111");
        Assert.Equal(["#222222"], book.Mine);

        Assert.True(book.SavePalette("stone", "Moor", ["#111111", "#222222", "#333333", "#444444"]));
        Assert.False(book.SavePalette("stone", "moor", ["#999999", "#222222", "#333333", "#444444"]));
        Assert.Single(book.Palettes);
        Assert.Equal("#999999", book.Palettes[0].Colours[0]);

        var again = new ColourBook(s.P("colours.json"));
        Assert.Equal(book.Recent, again.Recent);
        Assert.Equal(["#222222"], again.Mine);
        Assert.Equal("Moor", again.Palettes[0].Name);

        again.RemovePalette(again.Palettes[0].Id);
        Assert.Empty(new ColourBook(s.P("colours.json")).Palettes);
    }

    [Fact]
    public void ABackupsColoursAreMergedNotReplaced()
    {
        using var s = new Scratch();
        var theirs = new ColourBook(s.P("theirs.json"));
        theirs.AddMine(["#123456"]);
        theirs.SavePalette("stone", "Moor", ["#000000"]);
        theirs.SavePalette("grass", "Field", ["#00ff00"]);
        var mine = new ColourBook(s.P("mine.json"));
        mine.AddMine(["#abcdef"]);
        mine.SavePalette("stone", "Moor", ["#ffffff"]);
        mine.SavePalette("grass", "Field", ["#00ff00"]);

        mine.MergeFrom(File.ReadAllBytes(s.P("theirs.json")));

        Assert.Equal(["#abcdef", "#123456"], mine.Mine);
        Assert.Equal(["Moor", "Field", "Moor (from backup)"], mine.Palettes.Select(p => p.Name));
        mine.MergeFrom(Encoding.UTF8.GetBytes("not json"));
        Assert.Equal(3, mine.Palettes.Count);
    }

    [Fact]
    public void AColoursFileLargerThanTheLimitIsReadAsNothingKept()
    {
        using var s = new Scratch();
        WritePadded(s.P("colours.json"), "{\"mine\":[\"#ff0000\"]}", ColourBook.MaxBytes + 1);
        Assert.Empty(new ColourBook(s.P("colours.json")).Mine);

        WritePadded(s.P("colours.json"), "{\"mine\":[\"#ff0000\"]}", 1000);
        Assert.Equal(["#ff0000"], new ColourBook(s.P("colours.json")).Mine);
    }

    // Writes JSON followed by spaces up to a size: still JSON, only larger.
    internal static void WritePadded(string path, string json, long size)
    {
        using FileStream f = File.Create(path);
        f.Write(Encoding.UTF8.GetBytes(json));
        byte[] spaces = new byte[1024 * 1024];
        Array.Fill(spaces, (byte)' ');
        while (f.Length < size)
        {
            f.Write(spaces, 0, (int)Math.Min(spaces.Length, size - f.Length));
        }
    }

    [Fact]
    public void APastedListIsReadOnlyUpToItsLimits()
    {
        // More colours than are ever kept: only the first MaxListed come back.
        string many = string.Join(' ', Enumerable.Range(0, ColourCodes.MaxListed + 500).Select(i => $"#{i:x6}"));
        (IReadOnlyList<string> codes, _) = ColourCodes.ParseList(many);
        Assert.Equal(ColourCodes.MaxListed, codes.Count);
        Assert.Equal("#000000", codes[0]);

        // Text past MaxPasteChars is not read.
        string longText = new string(' ', ColourCodes.MaxPasteChars) + "#ffffff";
        Assert.Empty(ColourCodes.ParseList(longText).Codes);
    }

    [Fact]
    public void APaletteKeepsItsColoursInOrderRepeatsAndAll()
    {
        using var s = new Scratch();
        File.WriteAllText(
            s.P("colours.json"),
            "{\"recent\":[\"#ff0000\",\"#ff0000\"],\"palettes\":[{\"id\":\"p1\",\"name\":\"Mine\",\"set\":\"rock\",\"colours\":[\"#ff0000\",\"#ff0000\",\"#0000ff\"]}]}");
        var book = new ColourBook(s.P("colours.json"));
        Assert.Equal(["#ff0000", "#ff0000", "#0000ff"], Assert.Single(book.Palettes).Colours);
        Assert.Equal(["#ff0000"], book.Recent);
    }
}
