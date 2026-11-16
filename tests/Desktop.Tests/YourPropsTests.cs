using PropGenerator.Desktop.Props;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class YourPropsTests
{
    internal static string SaveProp(Scratch s, string name, StartingPoint point)
    {
        string path = s.P("props", name + ".propgen");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var h = new History();
        h.Push(point.Name, point.Settings);
        WorkFile.Save(path, new Piece(point.Recipe, []), point.Settings, h);
        return path;
    }

    internal static StartingPoint Point(string id) =>
        MeshRecipes.All.SelectMany(r => r.StartingPoints).Single(p => p.Id == id);

    private static (YourProps Props, Library Library, RemovedBin Bin, Func<DateTimeOffset> Clock, Action<TimeSpan> Pass) Make(Scratch s)
    {
        DateTimeOffset now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
        var library = new Library(s.P("library.json"));
        var bin = new RemovedBin(s.P("removed"), () => now);
        return (new YourProps(library, bin), library, bin, () => now, t => now += t);
    }

    [Fact]
    public void ListsEachPropWithItsDetailsAndFindsThemByNameKindOrStartingPoint()
    {
        using var s = new Scratch();
        (YourProps props, Library library, _, _, _) = Make(s);
        library.Add(SaveProp(s, "Old boulder", Point("boulder")));
        library.Add(SaveProp(s, "Sea cliff", Point("long-cliff")));
        library.Add(SaveProp(s, "Yard", Point("short-lawn")));

        IReadOnlyList<PropSummary> all = props.List();
        Assert.Equal(["Yard", "Sea cliff", "Old boulder"], all.Select(p => p.Name));
        PropSummary boulder = all.Single(p => p.Name == "Old boulder");
        Assert.Equal(("cut-rock", "Rock", "Boulder"), (boulder.Recipe, boulder.KindLabel, boulder.StartedFrom));
        Assert.Equal(Point("boulder").Settings.GetSeed("seed"), boulder.Seed);
        Assert.True(boulder.Size > 0);
        Assert.True(boulder.Readable);

        Assert.Equal(["Old boulder"], props.List("BOULD").Select(p => p.Name));
        Assert.Equal(["Sea cliff"], props.List("cliff").Select(p => p.Name));
        Assert.Equal(["Yard"], props.List("lawn").Select(p => p.Name));
        Assert.Equal(["Old boulder", "Sea cliff", "Yard"], props.List(sort: PropSort.Name).Select(p => p.Name));
        Assert.Contains("Rock", props.Kinds());
        Assert.Equal(["Old boulder"], props.List(kind: "Rock").Select(p => p.Name));
    }

    [Fact]
    public void ASeedThatIsNotANumberIsListedWithoutOne()
    {
        using var s = new Scratch();
        (YourProps props, Library library, _, _, _) = Make(s);
        string path = SaveProp(s, "Odd", Point("boulder"));
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        root["settings"]!["seed"] = "bad";
        File.WriteAllText(path, root.ToJsonString());
        library.Add(path);

        PropSummary p = Assert.Single(props.List());
        Assert.Equal("Odd", p.Name);
        Assert.Null(p.Seed);
    }

    [Fact]
    public void AFileThatCannotBeReadIsStillListedAsUnreadable()
    {
        using var s = new Scratch();
        (YourProps props, Library library, _, _, _) = Make(s);
        Directory.CreateDirectory(s.P("props"));
        File.WriteAllText(s.P("props", "broken.propgen"), "{ not json");
        library.Add(s.P("props", "broken.propgen"));

        PropSummary p = Assert.Single(props.List());
        Assert.False(p.Readable);
        Assert.Equal("Unreadable prop", p.KindLabel);
    }

    [Fact]
    public void DuplicateNamesTheCopyAfterTheOriginalAndCountsOn()
    {
        using var s = new Scratch();
        (YourProps props, Library library, _, _, _) = Make(s);
        string rock = SaveProp(s, "Rock", Point("boulder"));
        library.Add(rock);

        string first = props.Duplicate(rock);
        string second = props.Duplicate(rock);

        Assert.Equal("Rock copy.propgen", Path.GetFileName(first));
        Assert.Equal("Rock copy 2.propgen", Path.GetFileName(second));
        Assert.Equal(File.ReadAllText(rock), File.ReadAllText(first));
        Assert.Equal(3, props.List().Count);
    }

    [Fact]
    public void RenameMovesTheFileAndRefusesABadOrTakenName()
    {
        using var s = new Scratch();
        (YourProps props, Library library, _, _, _) = Make(s);
        string a = SaveProp(s, "A", Point("boulder"));
        string b = SaveProp(s, "B", Point("boulder"));
        library.Add(a);
        library.Add(b);

        (string? path, string? error) = props.Rename(a, "  Big rock  ");
        Assert.Null(error);
        Assert.Equal("Big rock.propgen", Path.GetFileName(path));
        Assert.False(File.Exists(a));

        Assert.NotNull(props.Rename(b, "Big rock").Error);
        Assert.NotNull(props.Rename(b, "a/b").Error);
        Assert.NotNull(props.Rename(b, new string('x', 61)).Error);
        Assert.Equal((b, null), props.Rename(b, "  "));
        Assert.Equal(["B", "Big rock"], props.List(sort: PropSort.Name).Select(p => p.Name));
    }

    [Fact]
    public void RemovedPropsWaitInTheBinAndComeBack()
    {
        using var s = new Scratch();
        (YourProps props, Library library, RemovedBin bin, _, Action<TimeSpan> pass) = Make(s);
        string a = SaveProp(s, "A", Point("boulder"));
        string b = SaveProp(s, "B", Point("short-lawn"));
        library.Add(a);
        library.Add(b);

        IReadOnlyList<RemovedProp> gone = props.Remove([a, b]);
        Assert.Equal(2, gone.Count);
        Assert.Empty(props.List());
        Assert.False(File.Exists(a));
        pass(TimeSpan.FromMinutes(1));
        props.Remove([]);
        Assert.Equal(["A", "B"], bin.List().Select(r => r.Name).Order());

        // Undo puts back what one remove took.
        props.PutBack(gone);
        Assert.True(File.Exists(a));
        Assert.Equal(2, props.List().Count);
        Assert.Empty(bin.List());

        // Removing for good and emptying the bin.
        props.Remove([a]);
        props.Remove([b]);
        RemovedProp ra = bin.List().Single(r => r.Name == "A");
        bin.Forget(ra.Id);
        Assert.Equal(["B"], bin.List().Select(r => r.Name));
        Assert.Equal(1, bin.Empty());
        Assert.Empty(bin.List());
        Assert.Empty(Directory.GetFiles(s.P("removed")));
    }

    [Fact]
    public void PutBackFailsWhileAnotherPropHasTakenItsPlace()
    {
        using var s = new Scratch();
        (YourProps props, Library library, RemovedBin bin, _, _) = Make(s);
        string a = SaveProp(s, "A", Point("boulder"));
        library.Add(a);
        RemovedProp r = Assert.Single(props.Remove([a]));
        SaveProp(s, "A", Point("short-lawn"));

        IOException e = Assert.Throws<IOException>(() => props.PutBack([r]));
        Assert.Contains("Couldn't put", e.Message, StringComparison.Ordinal);
        Assert.Single(bin.List());
    }

    [Fact]
    public void TheBinForgetsWhatWasRemovedMoreThanThirtyDaysAgo()
    {
        using var s = new Scratch();
        (YourProps props, Library library, RemovedBin bin, _, Action<TimeSpan> pass) = Make(s);
        string a = SaveProp(s, "A", Point("boulder"));
        string b = SaveProp(s, "B", Point("boulder"));
        library.Add(a);
        library.Add(b);
        props.Remove([a]);
        pass(TimeSpan.FromDays(10));
        props.Remove([b]);
        pass(TimeSpan.FromDays(20) + TimeSpan.FromMinutes(1));

        Assert.Equal(1, bin.ForgetOld());
        Assert.Equal(["B"], bin.List().Select(r => r.Name));
        Assert.Equal(TimeSpan.FromDays(30), RemovedBin.KeepFor);
    }
}
