using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using PropGenerator.Desktop.Batch;
using PropGenerator.Desktop.Cards;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Props;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// Export as a set (decisions E7 and E8): props matched to the same colours (upstream's colour groups and
/// <c>inSetColours</c>), written flat into the set's own folder with upstream's shared material names and the set's
/// list last, from Export many and from Your props; the saved props stay as they are.
/// </summary>
public partial class SetExportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Upstream's setMaterialName: the set, what the part is (its picture's name, or its bucket), and how wet.
    [GeneratedRegex("^my_set_[^\\s]+$")]
    private static partial Regex SetMaterial();

    private static StartingPoint Bush => YourPropsTests.Point("round-bush");

    private static EngineRecipe RecipeOf(StartingPoint point) => Assert.IsType<EngineRecipe>(MeshRecipes.Find(point.Recipe));

    private static string[] Materials(byte[] glb)
    {
        using var file = GlbFile.Parse(glb);
        return file.Json.RootElement.TryGetProperty("materials", out JsonElement list)
            ? [.. list.EnumerateArray().Select(m => m.GetProperty("name").GetString()!)]
            : [];
    }

    // Two seeds of the round bush, exported as the set "My set" in its usual colours; returns the set's folder.
    private static (MainWindow W, string Folder, int Done) ExportTwoBushes(Scratch s, Action<string>? before = null)
    {
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        EngineRecipe bush = RecipeOf(Bush);
        Settings first = Bush.Settings;
        SetProp[] props = [new("bush", bush, first), new("bush", bush, first.With("seed", first.GetSeed("seed") + 7))];
        IReadOnlyList<SetProp> named = ClaudeKit.Run(ExportSet.NamedAsync(props, Ct), 60000);
        Assert.Equal(["bush_01", "bush_02"], named.Select(p => p.Name));
        IReadOnlyList<EngineColourGroup> groups = ExportSet.Groups(named);
        string folder = s.P("sets");
        before?.Invoke(Path.Combine(folder, "My set"));
        int done = ClaudeKit.Run(w.Files.ExportSetAsync(named, new SetChoice("My set", groups, ExportSet.Usual(groups)), folder), 240000);
        return (w, Path.Combine(folder, "My set"), done);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MaterialNamesFollowUpstreamsSetRule()
    {
        Scratch s = Ui.NewScratch();
        (MainWindow w, string folder, int done) = ExportTwoBushes(s);
        Assert.Equal(2, done);
        string[] one = Materials(File.ReadAllBytes(Path.Combine(folder, "bush_01.glb")));
        string[] two = Materials(File.ReadAllBytes(Path.Combine(folder, "bush_02.glb")));
        Assert.NotEmpty(one);
        Assert.All(one.Concat(two), m => Assert.Matches(SetMaterial(), m));

        // The same surfaces have the same names in both files: one material in the engine.
        Assert.Equal(one.Order(StringComparer.Ordinal), two.Order(StringComparer.Ordinal));

        // Outside a set the names are the prop's own, so the set's name did reach the export.
        IReadOnlyList<EngineFile> alone = ClaudeKit.Run(RecipeOf(Bush).ExportFilesAsync(Bush.Settings, "bush_01", ExportOptions.Default, Ct), 120000);
        Assert.DoesNotContain(Materials(alone.Single(f => f.Name.EndsWith(".glb", StringComparison.Ordinal)).Bytes), m => m.StartsWith("my_set_", StringComparison.Ordinal));

        // A plain folder, not a map: no catalog.
        Assert.False(File.Exists(Path.Combine(folder, "catalog.json")));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheSetListNamesEveryPropAndMaterial()
    {
        Scratch s = Ui.NewScratch();
        (MainWindow w, string folder, int done) = ExportTwoBushes(s);
        Assert.Equal(2, done);
        string listPath = Path.Combine(folder, "my_set.set.json");
        Assert.True(File.Exists(listPath));
        JsonObject list = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(listPath)));
        Assert.Equal(1, (int)list["format"]!);
        Assert.Equal("my_set", (string)list["set"]!);

        JsonArray props = list["props"]!.AsArray();
        Assert.Equal(["bush_01", "bush_02"], props.Select(p => (string)p!["name"]!));
        foreach (JsonNode? prop in props)
        {
            string name = (string)prop!["name"]!;
            string[] written = [.. Directory.GetFiles(folder, name + ".*").Concat(Directory.GetFiles(folder, name + "_*")).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
            Assert.Equal(written, prop["files"]!.AsArray().Select(f => (string)f!).Order(StringComparer.Ordinal));
        }

        string[] materials = [.. props
            .SelectMany(p => Materials(File.ReadAllBytes(Path.Combine(folder, (string)p!["name"]! + ".glb"))))
            .Distinct().Order(StringComparer.Ordinal)];
        Assert.Equal(materials, list["materials"]!.AsArray().Select(m => (string)m!));

        // The colours the set was matched to, in the setting's and the colour's words.
        IReadOnlyList<EngineColourGroup> groups = ExportSet.Groups([new SetProp("bush", RecipeOf(Bush), Bush.Settings)]);
        JsonArray colours = list["colours"]!.AsArray();
        Assert.Equal(groups.Select(g => g.Label), colours.Select(c => (string)c!["setting"]!));
        Assert.Equal(groups.Select(g => g.Options.Single(o => o.Value == g.Usual).Label), colours.Select(c => (string)c!["colour"]!));

        // The list is written last.
        DateTime listWritten = File.GetLastWriteTimeUtc(listPath);
        Assert.All(Directory.GetFiles(folder).Where(f => f != listPath), f => Assert.True(File.GetLastWriteTimeUtc(f) <= listWritten, f));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AFailedListStillLeavesTheProps()
    {
        Scratch s = Ui.NewScratch();

        // A folder where the list would go: it cannot be written.
        (MainWindow w, string folder, int done) = ExportTwoBushes(s, set => Directory.CreateDirectory(Path.Combine(set, "my_set.set.json")));
        Assert.Equal(2, done);
        Assert.True(File.Exists(Path.Combine(folder, "bush_01.glb")));
        Assert.True(File.Exists(Path.Combine(folder, "bush_02.glb")));
        Assert.Contains(w.RecentMessages, m => m.Contains("The set's list couldn't be written. Its props were.", StringComparison.Ordinal));
        w.Close();
    }

    // Saves an oak, a pear and a boulder (and a cliff, which cannot be in a set) and opens Export as a set on them.
    private static (MainWindow W, PropsPage Page, ExportSetCard Card, Scratch S, string[] Paths) SetOfFour()
    {
        Scratch s = Ui.NewScratch();
        var dialogs = new FakeDialogs();
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        StartingPoint apple = YourPropsTests.Point("apple");
        Settings pearSettings = RecipeOf(apple).Specs.Any(p => p.Key == "fruitKind") ? apple.Settings.With("fruitKind", "pear") : apple.Settings;
        string[] paths =
        [
            YourPropsTests.SaveProp(s, "Oak", YourPropsTests.Point("oak")),
            YourPropsTests.SaveProp(s, "Pear", apple with { Settings = pearSettings }),
            YourPropsTests.SaveProp(s, "Boulder", YourPropsTests.Point("boulder")),
            YourPropsTests.SaveProp(s, "Cliff", YourPropsTests.Point("long-cliff")),
        ];
        foreach (string path in paths)
        {
            w.Library.Add(path);
        }

        w.Files.Prefs.SetFolder = s.P("sets");
        Directory.CreateDirectory(s.P("sets"));
        CommandListTests.Run(w, "my-props");
        PropsPage page = Assert.IsType<PropsPage>(w.Cards.Shown);
        foreach (string name in new[] { "Oak", "Pear", "Boulder", "Cliff" })
        {
            page.Tick(name);
        }

        ClaudeKit.Run(page.RunAsync("Export as a set…"));
        Ui.Pump();
        ExportSetCard card = Assert.IsType<ExportSetCard>(w.Cards.Shown);
        return (w, page, card, s, paths);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ASetFromYourPropsGroupsColoursAcrossProps()
    {
        (MainWindow w, _, ExportSetCard card, _, string[] paths) = SetOfFour();
        SetColoursSection set = card.Set;
        Assert.Equal(["Boulder", "Oak", "Pear"], set.Props.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Contains(set.LeftOut, l => l.StartsWith("Cliff (", StringComparison.Ordinal));

        // The groups are the engine's colourGroups over the three, as they were saved.
        var props = new JsonArray();
        foreach (string path in set.Props.Select(p => paths.Single(x => Path.GetFileNameWithoutExtension(x) == p.Name)))
        {
            string recipe = WorkFile.Load(path).Piece.Recipe;
            EngineRecipe engine = Assert.IsType<EngineRecipe>(MeshRecipes.Find(recipe));
            Settings saved = WorkFile.Load(path, engine.Specs, x => MeshRecipes.Loaded(recipe, x)).Settings;
            props.Add(new JsonObject { ["generatorId"] = engine.Id, ["parameters"] = JsonNode.Parse(engine.ValuesJson(saved)) });
        }

        IReadOnlyList<EngineColourGroup> upstream = EngineHost.Default.Rules.ColourGroups(props.ToJsonString(), Ct);
        Assert.Equal(upstream.Select(g => (g.Id, g.Label, g.Usual, g.Props)), set.Groups.Select(g => (g.Id, g.Label, g.Usual, g.Props)));

        // The two trees share their palette; the boulder has its own colours.
        EngineColourGroup palette = set.Groups.Single(g => g.Key == "palette" && g.Label.StartsWith("Tree", StringComparison.Ordinal));
        Assert.Equal(2, palette.Props);
        Assert.Contains(set.Groups, g => g.Label.StartsWith("Rock", StringComparison.Ordinal) && g.Props == 1);

        // Each choice starts on the usual colour and says how many props have it.
        for (int i = 0; i < set.Groups.Count; i++)
        {
            ComboBox box = set.ColourBoxes[i];
            Assert.Equal(set.Groups[i].Usual, Assert.IsType<ComboBoxItem>(box.SelectedItem).Tag);
            Assert.StartsWith($"{set.Groups[i].Props} prop", ToolTip.GetTip(box) as string, StringComparison.Ordinal);
        }

        Assert.Equal($"The set goes in {Path.Combine(set.Folder!, "My set")}", set.FolderLine.Text);
        card.Dispose();
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheSetColoursRecolourEveryPropButNotTheSave()
    {
        (MainWindow w, PropsPage page, ExportSetCard card, Scratch s, string[] paths) = SetOfFour();
        byte[][] before = [.. paths.Select(File.ReadAllBytes)];
        SetColoursSection set = card.Set;
        EngineColourGroup palette = set.Groups.Single(g => g.Key == "palette" && g.Label.StartsWith("Tree", StringComparison.Ordinal));
        string pick = palette.Options.First(o => o.Value != palette.Usual).Value;
        set.Pick(palette.Id, pick);
        Ui.Pump();
        Assert.Equal(pick, set.Colours[palette.Id]);

        card.Export();
        Assert.NotNull(page.SetExport);
        Assert.Equal(3, ClaudeKit.Run(page.SetExport!, 300000));
        string folder = Path.Combine(s.P("sets"), "My set");

        // Both trees were made in the set's palette; the boulder kept what it had.
        foreach (string tree in new[] { "oak_01", "pear_01" })
        {
            JsonNode asset = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, tree + ".asset.json")))!;
            Assert.Equal(pick, (string?)asset["parameters"]!["palette"]);
        }

        Assert.True(File.Exists(Path.Combine(folder, "boulder_01.glb")));
        Assert.False(File.Exists(Path.Combine(folder, "cliff_01.glb")));

        // The saved props are as they were.
        for (int i = 0; i < paths.Length; i++)
        {
            Assert.Equal(before[i], File.ReadAllBytes(paths[i]));
        }

        Assert.Equal(s.P("sets"), w.Files.Prefs.SetFolder);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SetPreviewsAreCancelledWhenAColourChanges()
    {
        EngineRecipe bush = RecipeOf(Bush);
        SetProp[] props = [.. Enumerable.Range(0, 10).Select(i => new SetProp("bush", bush, Bush.Settings.With("seed", Bush.Settings.GetSeed("seed") + (uint)i)))];
        IReadOnlyList<EngineColourGroup> groups = ExportSet.Groups(props);
        EngineColourGroup palette = groups.First(g => g.Options.Count > 2);
        var calls = new List<(SetProp Prop, string Colours, CancellationToken Stop, TaskCompletionSource<byte[]?> Done)>();
        Task<byte[]?> Draw(SetProp prop, string colours, CancellationToken stop)
        {
            var done = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            calls.Add((prop, colours, stop, done));
            return done.Task;
        }

        using var set = new SetColoursSection(props, groups, null, _ => Task.FromResult<string?>(null), null, Draw);
        Ui.Pump();

        // One at a time, eight of the ten at most.
        Assert.Single(calls);
        Assert.Equal(8, set.PreviewsQueued);

        // A colour changes while the first is drawing: what was queued is dropped, and the new colours wait for it.
        string[] others = [.. palette.Options.Select(o => o.Value).Where(v => v != palette.Usual)];
        set.Pick(palette.Id, others[0]);
        set.Pick(palette.Id, others[1]);
        Ui.Pump();
        Assert.True(calls[0].Stop.IsCancellationRequested);
        Assert.Single(calls);
        calls[0].Done.SetResult(null);
        Ui.Wait(50);
        Ui.Pump();

        // Only the newest colours are drawn, and never more than eight wait.
        for (int i = 1; i < 20 && calls.Count < 9; i++)
        {
            calls[^1].Done.TrySetResult(null);
            Ui.Wait(20);
            Ui.Pump();
        }

        calls[^1].Done.TrySetResult(null);
        Ui.Wait(50);
        Ui.Pump();
        Assert.Equal(9, calls.Count);
        Assert.All(calls.Skip(1), c => Assert.Contains(others[1], c.Colours, StringComparison.Ordinal));
        Assert.Equal(props.Take(8), calls.Skip(1).Select(c => c.Prop));
        Assert.Equal(8, set.PreviewsDrawn);
        Assert.Equal(0, set.PreviewsQueued);
        Assert.True(set.MostPreviewsQueued <= ExportSet.Previews);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ASetNameIsCutAt60()
    {
        EngineRecipe bush = RecipeOf(Bush);
        SetProp[] props = [new("bush", bush, Bush.Settings)];
        using var set = new SetColoursSection(props, ExportSet.Groups(props), null, _ => Task.FromResult<string?>(null), null, (_, _, _) => Task.FromResult<byte[]?>(null));
        Assert.Equal(60, set.NameBox.MaxLength);
        Assert.Equal("My set", set.SetName);
        set.NameBox.Text = new string('a', 75);
        Ui.Pump();
        Assert.Equal(60, set.SetName.Length);
        Assert.Equal(60, set.Choice().Name.Length);
        Assert.Equal(new string('b', 60), ExportSet.Cut(new string('b', 61)));

        // Nothing typed is "My set", as upstream's.
        Assert.Equal("My set", ExportSet.NameOf("   "));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportManyOffersTheSetForEasyPropsKindsOnly()
    {
        Scratch s = Ui.NewScratch();
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(Bush)));
        CommandListTests.Run(w, "export-many");
        ExportManyCard card = Assert.IsType<ExportManyCard>(w.Cards.Shown);
        Assert.NotNull(card.AsSet);
        Assert.Null(card.Set);
        card.AsSet!.IsChecked = true;
        Ui.Pump();
        SetColoursSection set = Assert.IsType<SetColoursSection>(card.Set);
        Assert.Equal(4, set.Props.Count);
        Assert.All(set.Groups, g => Assert.Equal(4, g.Props));

        // More versions, more props in the set; the name typed stays.
        set.NameBox.Text = "Hedge row";
        card.Versions.Value = 6;
        Ui.Pump();
        Assert.Same(set, card.Set);
        Assert.Equal(6, set.Props.Count);
        Assert.All(set.Groups, g => Assert.Equal(6, g.Props));
        Assert.Equal("Hedge row", set.SetName);
        card.AsSet.IsChecked = false;
        Ui.Pump();
        Assert.Null(card.Set);
        w.Cards.Close();
        card.Dispose();

        // Our own kinds have no set colours.
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(YourPropsTests.Point("long-cliff"))));
        CommandListTests.Run(w, "export-many");
        Assert.Null(Assert.IsType<ExportManyCard>(w.Cards.Shown).AsSet);
        w.Close();
    }
}
