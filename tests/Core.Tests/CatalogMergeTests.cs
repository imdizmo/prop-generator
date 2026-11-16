using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace PropGenerator.Tests;

public sealed class CatalogMergeTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "prop-generator", "scratch",
        "cat-" + Guid.NewGuid().ToString("N"));

    public CatalogMergeTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string MakeMap(string? catalog, string mapJson = "{\"name\":\"M\",\"seaLevel\":3,\"catalog\":\"catalog.json\",\"models\":\"models\"}")
    {
        string map = Path.Combine(_dir, "map-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(map);
        File.WriteAllText(Path.Combine(map, "map.json"), mapJson);
        if (catalog is not null)
        {
            File.WriteAllText(Path.Combine(map, "catalog.json"), catalog);
        }

        return map;
    }

    private static CatalogKind Ours(string model = "pg_a_floor_tile") =>
        new(model, "A floor tile", "Construction", [1.0f, 1.0f]);

    private const string Foreign = """
        {"biomes":[{"n":1}],"kinds":[
        {"model":"x1","label":"X1","category":"Trees","scale":[0.8,1.2],"extra":"keep"},
        {"model":"x2","label":"X2","category":"Rocks","scale":[1,1]},
        {"model":"x3","label":"X3","category":"Props","scale":[1,2]}]}
        """;

    [Fact]
    public void ReadKindsListsModelsAndLabelsWithoutChangingAnything()
    {
        string map = MakeMap(Foreign);
        string before = File.ReadAllText(Path.Combine(map, "catalog.json"));

        var kinds = CatalogMerge.ReadKinds(map);

        Assert.Equal([("x1", "X1"), ("x2", "X2"), ("x3", "X3")], kinds);
        Assert.Equal(before, File.ReadAllText(Path.Combine(map, "catalog.json")));
        Assert.Empty(CatalogMerge.ReadKinds(MakeMap(null)));
        Assert.Throws<CatalogException>(() => CatalogMerge.ReadKinds(MakeMap("{ nope")));
    }

    [Fact]
    public void MergeKeepsForeignKindsAndWritesBackup()
    {
        string map = MakeMap(Foreign);
        var glb = new Dictionary<string, byte[]> { ["pg_a_floor_tile"] = [1, 2, 3] };
        CatalogMerge.AddKinds(map, [Ours()], glb);
        CatalogMerge.AddKinds(map, [Ours() with { Label = "Again" }], glb);
        var cat = JsonNode.Parse(File.ReadAllText(Path.Combine(map, "catalog.json")))!;
        var kinds = cat["kinds"]!.AsArray();
        Assert.Equal(4, kinds.Count);
        Assert.Equal("keep", kinds[0]!["extra"]!.GetValue<string>());
        Assert.Equal(["x1", "x2", "x3", "pg_a_floor_tile"], [.. kinds.Select(k => k!["model"]!.GetValue<string>())]);
        Assert.Equal("Again", kinds[3]!["label"]!.GetValue<string>());
        Assert.Single(cat["biomes"]!.AsArray());
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(map, "models", "pg_a_floor_tile.glb")));
        Assert.Equal(4, JsonNode.Parse(File.ReadAllText(Path.Combine(map, "catalog.json.bak")))!["kinds"]!.AsArray().Count);
        Assert.Contains("\"seaLevel\":3", File.ReadAllText(Path.Combine(map, "map.json")));
    }

    [Fact]
    public void BackupIsTheOldCatalog()
    {
        string map = MakeMap(Foreign);
        string before = File.ReadAllText(Path.Combine(map, "catalog.json"));
        CatalogMerge.AddKinds(map, [Ours()]);
        Assert.Equal(before, File.ReadAllText(Path.Combine(map, "catalog.json.bak")));
    }

    [Fact]
    public void MissingCatalogIsCreated()
    {
        string map = MakeMap(null);
        CatalogMerge.AddKinds(map, [Ours()]);
        var kinds = JsonNode.Parse(File.ReadAllText(Path.Combine(map, "catalog.json")))!["kinds"]!.AsArray();
        Assert.Single(kinds);
        Assert.Equal("Construction", kinds[0]!["category"]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(map, "catalog.json.bak")));
    }

    [Fact]
    public void BrokenCatalogThrowsAndStaysByteIdentical()
    {
        string map = MakeMap("{\"kinds\": [ {oops");
        string file = Path.Combine(map, "catalog.json");
        byte[] before = File.ReadAllBytes(file);
        Assert.Throws<CatalogException>(() =>
            CatalogMerge.AddKinds(map, [Ours()], new Dictionary<string, byte[]> { ["pg_a_floor_tile"] = [1] }));
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".bak"));
        Assert.False(Directory.Exists(Path.Combine(map, "models")));
    }

    [Fact]
    public void SymlinkedCatalogIsRefused()
    {
        string map = MakeMap(null);
        string real = Path.Combine(_dir, "real.json");
        File.WriteAllText(real, Foreign);
        File.CreateSymbolicLink(Path.Combine(map, "catalog.json"), real);
        Assert.Throws<CatalogException>(() => CatalogMerge.AddKinds(map, [Ours()]));
        Assert.Equal(Foreign, File.ReadAllText(real));
    }

    [Fact]
    public void CatalogOutsideTheMapIsRefused()
    {
        string map = MakeMap(null, "{\"catalog\":\"../elsewhere.json\"}");
        Assert.Throws<CatalogException>(() => CatalogMerge.AddKinds(map, [Ours()]));
        Assert.False(File.Exists(Path.Combine(_dir, "elsewhere.json")));
    }

    private static readonly Dictionary<string, byte[]> OneGlb = new() { ["pg_a_floor_tile"] = [9] };

    [Fact]
    public void SharedModelsFolderOutsideTheMapIsUsedWhenItAlreadyHoldsModels()
    {
        string models = Path.Combine(_dir, "shared-models");
        Directory.CreateDirectory(models);
        File.WriteAllBytes(Path.Combine(models, "old.glb"), [1]);
        string map = MakeMap(null, "{\"models\":" + JsonSerializer.Serialize(models) + "}");
        CatalogMerge.AddKinds(map, [Ours()], OneGlb);
        Assert.True(File.Exists(Path.Combine(models, "pg_a_floor_tile.glb")));
    }

    [Fact]
    public void SharedModelsFolderThatDoesNotExistIsNotCreated()
    {
        string models = Path.Combine(_dir, "nowhere", "models");
        string map = MakeMap(null, "{\"models\":" + JsonSerializer.Serialize(models) + "}");
        Assert.Throws<CatalogException>(() => CatalogMerge.AddKinds(map, [Ours()], OneGlb));
        Assert.False(Directory.Exists(Path.Combine(_dir, "nowhere")));
        Assert.False(File.Exists(Path.Combine(map, "catalog.json")));
    }

    [Fact]
    public void SharedModelsFolderWithNoModelsIsRefused()
    {
        string models = Path.Combine(_dir, "empty-models");
        Directory.CreateDirectory(models);
        string map = MakeMap(null, "{\"models\":" + JsonSerializer.Serialize(models) + "}");
        Assert.Throws<CatalogException>(() => CatalogMerge.AddKinds(map, [Ours()], OneGlb));
        Assert.Empty(Directory.GetFiles(models));
    }

    [Fact]
    public void SharedModelsFolderWhoseOnlyModelIsInASubfolderIsRefused()
    {
        // "models": "/home/someone" must not pass because some .glb lies somewhere below it.
        string models = Path.Combine(_dir, "home-like");
        Directory.CreateDirectory(Path.Combine(models, "deep", "down"));
        File.WriteAllBytes(Path.Combine(models, "deep", "down", "old.glb"), [1]);
        string map = MakeMap(null, "{\"models\":" + JsonSerializer.Serialize(models) + "}");
        Assert.Throws<CatalogException>(() => CatalogMerge.AddKinds(map, [Ours()], OneGlb));
        Assert.Empty(Directory.GetFiles(models));
        Assert.False(File.Exists(Path.Combine(map, "catalog.json")));
    }

    [Fact]
    public void SharedModelsFolderThatIsALinkIsRefused()
    {
        string real = Path.Combine(_dir, "real-models");
        Directory.CreateDirectory(real);
        File.WriteAllBytes(Path.Combine(real, "old.glb"), [1]);
        string link = Path.Combine(_dir, "link-models");
        Directory.CreateSymbolicLink(link, real);
        string map = MakeMap(null, "{\"models\":" + JsonSerializer.Serialize(link) + "}");
        Assert.Throws<CatalogException>(() => CatalogMerge.AddKinds(map, [Ours()], OneGlb));
        Assert.Single(Directory.GetFiles(real));
    }

    [Fact]
    public void BackupThatIsALinkIsRefusedAndTheTargetStaysAsItWas()
    {
        string map = MakeMap(Foreign);
        string outside = Path.Combine(_dir, "outside.txt");
        File.WriteAllText(outside, "mine");
        File.CreateSymbolicLink(Path.Combine(map, "catalog.json.bak"), outside);
        string before = File.ReadAllText(Path.Combine(map, "catalog.json"));
        Assert.Throws<CatalogException>(() => CatalogMerge.AddKinds(map, [Ours()]));
        Assert.Equal("mine", File.ReadAllText(outside));
        Assert.Equal(before, File.ReadAllText(Path.Combine(map, "catalog.json")));
    }

    [Fact]
    public void TempNamesThatAreLinksNeverReachOutsideTheMap()
    {
        string map = MakeMap(Foreign);
        string models = Path.Combine(map, "models");
        Directory.CreateDirectory(models);
        string o1 = Path.Combine(_dir, "o1.txt");
        string o2 = Path.Combine(_dir, "o2.txt");
        File.WriteAllText(o1, "one");
        File.WriteAllText(o2, "two");
        File.CreateSymbolicLink(Path.Combine(map, "catalog.json.tmp"), o1);
        File.CreateSymbolicLink(Path.Combine(models, "pg_a_floor_tile.glb.tmp"), o2);
        try
        {
            CatalogMerge.AddKinds(map, [Ours()], OneGlb);
        }
        catch (CatalogException)
        {
        }
        Assert.Equal("one", File.ReadAllText(o1));
        Assert.Equal("two", File.ReadAllText(o2));
    }

    [Fact]
    public void DuplicateKeysInTheCatalogGiveACatalogException()
    {
        string map = MakeMap("{\"kinds\":[],\"kinds\":[]}");
        string file = Path.Combine(map, "catalog.json");
        byte[] before = File.ReadAllBytes(file);
        Assert.Throws<CatalogException>(() => CatalogMerge.AddKinds(map, [Ours()]));
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".bak"));
    }

    [Fact]
    public void DuplicateKeysInMapJsonGiveACatalogException()
    {
        string map = MakeMap(null, "{\"catalog\":\"a.json\",\"catalog\":\"b.json\"}");
        Assert.Throws<CatalogException>(() => CatalogMerge.AddKinds(map, [Ours()]));
    }

    [Fact]
    public void SymlinkedModelsFolderInsideMapIsRefused()
    {
        string map = MakeMap(null);
        string real = Path.Combine(_dir, "realmodels");
        Directory.CreateDirectory(real);
        File.CreateSymbolicLink(Path.Combine(map, "models"), real);
        Assert.Throws<CatalogException>(() =>
            CatalogMerge.AddKinds(map, [Ours()], new Dictionary<string, byte[]> { ["pg_a_floor_tile"] = [9] }));
        Assert.Empty(Directory.GetFiles(real));
    }
}
