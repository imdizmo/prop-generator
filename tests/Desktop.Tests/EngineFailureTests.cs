using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PropGenerator.Desktop.Props;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// When easy-props' engine cannot be loaded (ruling 4 of the phase 4 plan): the app starts with only our own kinds
/// (the floor tile, slab cliffs, stylized rocks), and easy-props' kinds keep their names and are refused with the
/// engine's own reason, never "Unknown kind" or "the kind is gone". These tests put the failure in place for the
/// recipes (<see cref="MeshRecipes.EngineFailed"/>), so they run alone.
/// </summary>
[Collection(MeshRecipesAlone.Name)]
public sealed partial class EngineFailureTests
{
    private const string Why = "the bundle is unreadable";

    // The commands that start one of easy-props' kinds.
    private static readonly string[] TheirNewCommands = ["new-rock", "new-grass", "new-cactus", "new-tree", "new-palm"];

    private static IDisposable Down() => MeshRecipes.EngineFailed(new EngineException(Why));

    private static string Reason => $"easy-props' engine could not be loaded ({Why})";

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheAppStartsWithOnlyOurKinds()
    {
        using IDisposable _ = Down();
        Assert.Equal(["slab-cliff", "stylized-rock"], MeshRecipes.All.Select(r => r.Id));
        MainWindow w = Ui.Open(new FakePainter());
        Assert.True(w.IsVisible);
        Assert.Equal(
            [FloorRecipe.Id, CliffRecipe.Instance.Id, StylizedRockRecipe.Instance.Id],
            w.StartingPoints.Select(p => p.Recipe).Distinct());
        Assert.Contains(w.Drawer.TabNames, t => t.StartsWith("All", StringComparison.Ordinal));

        // Starting a cliff from the drawer works as ever.
        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(CliffRecipe.Instance.StartingPoints[0])));
        Assert.Equal(CliffRecipe.Instance.Id, w.Files.Recipe);
        Assert.Null(w.LastError);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void NewCommandsForTheirKindsSayWhy()
    {
        using IDisposable _ = Down();
        MainWindow w = Ui.Open(new FakePainter());
        foreach (string id in TheirNewCommands)
        {
            Assert.Equal(Reason, CommandListTests.Find(w, id).Unavailable());
            CommandListTests.Run(w, id);
            Assert.Contains(Reason, w.RecentMessages);
            Assert.Equal(FloorRecipe.Id, w.Files.Recipe);
        }

        // Our own kinds' and the floor's are not refused.
        Assert.Null(CommandListTests.Find(w, "new-floor").Unavailable());
        w.Close();
    }

    [Fact]
    public void TheirNewCommandsAreAvailableWithTheEngine()
    {
        Assert.All(TheirNewCommands, id => Assert.Null(MeshRecipes.Unavailable(id switch
        {
            "new-rock" => "cut-rock",
            "new-tree" => "painted-tree",
            "new-palm" => "palm-tree",
            _ => id[4..],
        })));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void OpeningABushSaysTheEngineIsDown()
    {
        using IDisposable _ = Down();
        using var s = new Scratch();
        var dialogs = new FakeDialogs();
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        string path = PortFixtures.Copy("bush.propgen", s.P("Round bush.propgen"));

        Assert.False(ClaudeKit.Run(w.Files.OpenPathAsync(path)));
        string said = string.Join("\n", dialogs.Told) + "\n" + w.LastError;
        Assert.Contains("Round bush.propgen was made with easy-props' engine, which could not be loaded (" + Why + "); slab cliffs and stylized rocks still work", said, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown kind", said, StringComparison.Ordinal);
        Assert.DoesNotContain("the kind is gone", said, StringComparison.Ordinal);
        Assert.Equal(FloorRecipe.Id, w.Files.Recipe);
        w.Close();
    }

    [Fact]
    public void YourPropsNamesTheirKinds()
    {
        using IDisposable _ = Down();
        Assert.Equal("Bush", YourProps.KindOf("bush"));
        Assert.Equal("Old-style palm", YourProps.KindOf("palm-tree"));
        Assert.Equal("Fallen log", YourProps.KindOf("tree-log"));
        Assert.Equal("Cliff", YourProps.KindOf(CliffRecipe.Instance.Id));
        Assert.Equal("Unknown kind (marble)", YourProps.KindOf("marble"));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportManySkipsTheirKindsWithTheReason()
    {
        using IDisposable _ = Down();
        var (w, _, _, _, s) = ClaudeKit.Make();
        string bush = PortFixtures.Copy("bush.propgen", s.P("props", "Round bush.propgen"));
        string map = s.P("map");
        Directory.CreateDirectory(map);
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        w.Files.Prefs.MapFolder = map;

        Assert.Equal(0, ClaudeKit.Run(w.Files.ExportFilesAsync([bush]), 120000));
        Assert.Contains($"Round bush ({Reason})", w.LastError, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(map, "*.glb", SearchOption.AllDirectories));

        // A set leaves it out with the same reason.
        (IReadOnlyList<Batch.SetProp> props, IReadOnlyList<string> left) = FileActions.SetPropsOf([bush]);
        Assert.Empty(props);
        Assert.Equal([$"Round bush ({Reason})"], left);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MyPresetsSkipsTheirKindsWithTheReason()
    {
        using var s = new Scratch();
        string file = s.P("presets.json");
        var cliff = CliffRecipe.Instance.StartingPoints[0];
        var bush = YourPropsTests.Point("round-bush");
        var mine = new UserPresets(file);
        mine.Add("My bush", bush.Recipe, bush.Settings);
        mine.Add("My cliff", cliff.Recipe, cliff.Settings);

        using IDisposable _ = Down();
        var down = new UserPresets(file);
        Assert.Equal(["My cliff"], down.All.Select(p => p.Name));
        Assert.Equal($"1 of My presets is not shown: {Reason}.", down.Hidden);

        // The bush is kept in the file through a change, and comes back with the engine.
        down.Add("Another cliff", cliff.Recipe, cliff.Settings);
        JsonArray saved = JsonNode.Parse(File.ReadAllText(file))!["presets"]!.AsArray();
        Assert.Equal(["My bush", "My cliff", "Another cliff"], saved.Select(p => p!["name"]!.GetValue<string>()));

        // The drawer's My presets tab says why.
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        w.Drawer.ChooseTab("My presets");
        Assert.Contains($"1 of My presets is not shown: {Reason}.", w.RecentMessages);
        w.Close();
    }

    [Fact]
    public void HeldPresetsCountAgainstTheCapAndKeepTheirPlace()
    {
        // A full file, 150 of easy-props' kinds (held while the engine is down) and 50 cliffs, a bush first.
        using var s = new Scratch();
        string file = s.P("presets.json");
        var cliff = CliffRecipe.Instance.StartingPoints[0];
        var bush = YourPropsTests.Point("round-bush");
        File.WriteAllText(file, PresetsJson(Enumerable.Range(0, UserPresets.Max).Select(i => i % 4 == 3 ? ("cliff " + i, cliff) : ("bush " + i, bush))));

        using (Down())
        {
            var down = new UserPresets(file);
            Assert.Equal(50, down.All.Count);
            down.Add("One more cliff", cliff.Recipe, cliff.Settings);
        }

        // The oldest preset went (the first bush), not a later one; the rest kept their order.
        string[] names = [.. JsonNode.Parse(File.ReadAllText(file))!["presets"]!.AsArray().Select(p => p!["name"]!.GetValue<string>())];
        Assert.Equal(UserPresets.Max, names.Length);
        Assert.Equal([.. Enumerable.Range(1, UserPresets.Max - 1).Select(i => (i % 4 == 3 ? "cliff " : "bush ") + i), "One more cliff"], names);

        // With the engine back every one of them is there.
        Assert.Equal(149, new UserPresets(file).All.Count(p => p.Recipe == bush.Recipe));
    }

    [Fact]
    public void ABackupMergedWhileTheEngineIsDownMakesNoRoomByDroppingHeldPresets()
    {
        using var s = new Scratch();
        string file = s.P("presets.json");
        var cliff = CliffRecipe.Instance.StartingPoints[0];
        var bush = YourPropsTests.Point("round-bush");
        File.WriteAllText(file, PresetsJson(Enumerable.Range(0, 200).Select(i => i < 50 ? ("cliff " + i, cliff) : ("bush " + i, bush))));
        File.WriteAllText(s.P("backup.json"), PresetsJson(Enumerable.Range(0, 150).Select(i => ("backup cliff " + i, cliff)), "backup-"));

        using (Down())
        {
            var down = new UserPresets(file);
            down.MergeFrom(File.ReadAllBytes(s.P("backup.json")));
            Assert.Equal(50, down.All.Count);
        }

        Assert.Equal(150, new UserPresets(file).All.Count(p => p.Recipe == bush.Recipe));
    }

    // A presets file of these named presets, in order.
    private static string PresetsJson(IEnumerable<(string Name, StartingPoint Point)> presets, string ids = "mine-") =>
        new JsonObject
        {
            ["presets"] = new JsonArray([.. presets.Select((p, i) => (JsonNode?)new JsonObject
            {
                ["id"] = ids + i,
                ["name"] = p.Name,
                ["recipe"] = p.Point.Recipe,
                ["settings"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(p.Point.Settings.Values)),
            })]),
        }.ToJsonString();

    [Fact]
    public void MyPresetsKeepTheirKindsWithTheEngine()
    {
        using var s = new Scratch();
        string file = s.P("presets.json");
        var bush = YourPropsTests.Point("round-bush");
        new UserPresets(file).Add("My bush", bush.Recipe, bush.Settings);
        var again = new UserPresets(file);
        Assert.Equal(["My bush"], again.All.Select(p => p.Name));
        Assert.Null(again.Hidden);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheStatusLineAndReportNameTheEngine()
    {
        var failure = new EngineException(Why);
        using IDisposable _ = MeshRecipes.EngineFailed(failure);
        MainWindow w = Ui.Open(new FakePainter());
        App.SayEngineFailure(w, failure);
        Assert.Contains(Reason, w.LastError, StringComparison.Ordinal);
        Assert.Contains("slab cliffs and stylized rocks still work", w.LastError, StringComparison.Ordinal);
        Assert.DoesNotMatch(Port(), w.LastError!);

        string[] lines = [.. ProblemReport.EngineLines(failure)];
        Assert.Contains(lines, l => l.Contains(Why, StringComparison.Ordinal) && l.Contains("only slab cliffs and stylized rocks", StringComparison.Ordinal));
        Assert.All(lines, l => Assert.DoesNotMatch(Port(), l));
        w.Close();
    }

    [GeneratedRegex(@"\bport\b", RegexOptions.IgnoreCase)]
    private static partial Regex Port();
}
