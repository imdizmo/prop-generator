using Xunit;
using static PropGenerator.Tests.Support.Fixtures;

namespace PropGenerator.Tests;

public class LibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "prop-generator", "scratch", "lib-" + Guid.NewGuid().ToString("N"));

    public LibraryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Directory.Delete(_dir, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ARenamedFileKeepsItsPlaceAndColour()
    {
        string a = Path.Combine(_dir, "a.propgen");
        string b = Path.Combine(_dir, "b.propgen");
        string c = Path.Combine(_dir, "c.propgen");
        WorkFile.Save(a, APiece(), Defaults().With("colour", new Rgb(1, 0, 0)), new History());
        WorkFile.Save(b, APiece(), Defaults(), new History());
        var lib = new Library(Path.Combine(_dir, "cfg", "library.json"));
        lib.Add(a);
        lib.Add(b);
        DateTimeOffset opened = lib.List()[1].LastOpened;

        File.Move(a, c);
        lib.Rename(a, c);

        Assert.Equal(["b", "c"], lib.List().Select(e => e.Name));
        Assert.Equal(c, lib.List()[1].Path);
        Assert.Equal(opened, lib.List()[1].LastOpened);
        Assert.Equal("#FF0000", lib.List()[1].ColourHex);

        // A file the library did not have is added at the top.
        string d = Path.Combine(_dir, "d.propgen");
        File.Move(b, d);
        lib.Rename(Path.Combine(_dir, "never.propgen"), d);
        Assert.Equal(["d", "c"], lib.List().Select(e => e.Name));
    }

    [Fact]
    public void LibraryDropsDeletedFilesAndCapsAt500()
    {
        string a = Path.Combine(_dir, "a.propgen");
        string b = Path.Combine(_dir, "b.propgen");
        Settings s = Defaults().With("colour", new Rgb(1, 0, 0));
        WorkFile.Save(a, APiece(), s, new History());
        WorkFile.Save(b, APiece(), s, new History());
        var lib = new Library(Path.Combine(_dir, "cfg", "library.json"));
        lib.Add(a);
        lib.Add(b);
        lib.Add(a);
        Assert.Equal(["a", "b"], lib.List().Select(e => e.Name));
        Assert.Equal("#FF0000", lib.List()[0].ColourHex);
        File.Delete(b);
        Assert.Equal(["a"], lib.List().Select(e => e.Name));
        Assert.Single(new Library(Path.Combine(_dir, "cfg", "library.json")).List());

        var big = new Library(Path.Combine(_dir, "big.json"));
        for (int i = 0; i < 505; i++)
        {
            string p = Path.Combine(_dir, $"p{i}.propgen");
            File.WriteAllText(p, "{}");
            big.Add(p);
        }

        IReadOnlyList<LibraryEntry> list = big.List();
        Assert.Equal(500, list.Count);
        Assert.Equal("p504", list[0].Name);
        Assert.Null(list[0].ColourHex);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"entries\": [{\"path\": \"/a\", \"na")]
    [InlineData("{\"entries\": 5}")]
    [InlineData("{\"entries\": [{\"path\": 7}]}")]
    [InlineData("{\"entries\": [{\"path\": \"/a\", \"name\": \"a\", \"lastOpened\": \"yesterday\"}]}")]
    [InlineData("[]")]
    [InlineData("")]
    public void CorruptLibraryFileReadsAsEmptyAndAddStillWorks(string text)
    {
        string lib = Path.Combine(_dir, "bad.json");
        File.WriteAllText(lib, text);
        var l = new Library(lib);
        Assert.Empty(l.List());
        string a = Path.Combine(_dir, "a.propgen");
        File.WriteAllText(a, "{}");
        l.Add(a);
        Assert.Single(l.List());
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void UnreadableLibraryFileReadsAsEmpty()
    {
        string lib = Path.Combine(_dir, "locked.json");
        File.WriteAllText(lib, "{\"entries\": []}");
        File.SetUnixFileMode(lib, UnixFileMode.None);
        try
        {
            Assert.Empty(new Library(lib).List());
        }
        finally
        {
            File.SetUnixFileMode(lib, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void ListSurvivesAReadOnlyConfigFolder()
    {
        string cfg = Path.Combine(_dir, "ro");
        Directory.CreateDirectory(cfg);
        string a = Path.Combine(_dir, "a.propgen");
        string b = Path.Combine(_dir, "b.propgen");
        File.WriteAllText(a, "{}");
        File.WriteAllText(b, "{}");
        var l = new Library(Path.Combine(cfg, "library.json"));
        l.Add(a);
        l.Add(b);
        File.Delete(b);
        File.SetUnixFileMode(cfg, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.Equal(["a"], l.List().Select(e => e.Name));
        }
        finally
        {
            File.SetUnixFileMode(cfg, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
