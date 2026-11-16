using System.Numerics;
using PropGenerator.Desktop.Cards;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Nature.Random;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>Show me more (nine versions), Export many (several at once) and Make it fit (under the face budget).</summary>
public class VersionsTests
{
    private static IMeshRecipe Bush => MeshRecipes.Find("bush")!;

    private static StartingPoint RoundBush => Bush.StartingPoints.Single(p => p.Id == "round-bush");

    private static bool Waited(Func<bool> done, int ms = 60000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    private static MainWindow Open(StartingPoint point, Scratch? scratch = null)
    {
        MainWindow w = scratch is null ? Ui.Open(new FakePainter()) : Ui.Open(new FakePainter(), new FakeDialogs(), scratch);
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(point)));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        return w;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheFloorHasNoVersionsToShowOrExport()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.Equal("The floor tile has one version: change its seed instead", CommandListTests.Find(w, "show-more").Unavailable());
        Assert.Equal("The floor tile has one version: change its seed instead", CommandListTests.Find(w, "export-many").Unavailable());
        Assert.Equal("This kind of prop has to be lowered by hand", CommandListTests.Find(w, "make-it-fit").Unavailable());
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ShowMeMoreShowsNineVersionsAroundThisOneAndAClickTakesOne()
    {
        MainWindow w = Open(RoundBush);
        uint seed = w.Panel.Current.GetSeed("seed");
        int steps = w.Files.History.Steps.Count;

        CommandListTests.Run(w, "show-more");
        ShowMoreCard card = Assert.IsType<ShowMoreCard>(w.Cards.Shown);
        Assert.Equal(Variations.Grid(seed, 0), card.Seeds);
        Assert.Equal(seed, card.Seeds[4]);
        Assert.Equal(9, card.Tiles.Count);
        Assert.True(Waited(() => card.Drawing.IsCompleted));
        Assert.Equal(9, card.Drawn);

        card.ShowPage(1);
        Assert.Equal(Variations.Grid(seed, 1), card.Seeds);

        uint other = card.Seeds[0];
        card.Tiles[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Ui.Pump();
        Assert.Null(w.Cards.Shown);
        Assert.Equal(other, w.Panel.Current.GetSeed("seed"));
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal($"Use seed {other}", w.Files.History.Steps[^1].Label);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MoreClickedAgainAndAgainDrawsOnePictureAtATimeAndOnlyTheNewestPage()
    {
        // The engine's bush, so the pictures go on the engine's Show me more turn with the card's token.
        var slow = new SlowRecipe(Bush, TimeSpan.FromMilliseconds(150));
        using var card = new ShowMoreCard(slow, RoundBush.Settings, "bush", _ => { }, _ => { }, () => { });
        Ui.Pump();
        card.ShowPage(1);
        card.ShowPage(2);
        card.ShowPage(3);
        Assert.True(Waited(() => card.Drawing.IsCompleted));

        Assert.Equal(1, slow.MostAtOnce);
        Assert.Equal(9, card.Drawn);
        HashSet<uint> pageThree = [.. card.Seeds];
        Assert.All(slow.Built.Skip(1), seed => Assert.Contains(seed, pageThree));
        Assert.All(slow.Calls, c => Assert.Equal(BuildPurpose.ShowMore, c.Purpose));
        Assert.All(slow.Calls, c => Assert.True(c.Cancel.CanBeCanceled, "a picture was built with no token to stop it"));
        Assert.Contains(slow.Calls, c => c.Cancel.IsCancellationRequested);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TickedVersionsGoToExportMany()
    {
        MainWindow w = Open(RoundBush);
        CommandListTests.Run(w, "show-more");
        var card = (ShowMoreCard)w.Cards.Shown!;
        card.Toggle(card.Versions[1]);
        card.Toggle(card.Versions[4]);
        card.ExportTicked();

        ExportManyCard export = Assert.IsType<ExportManyCard>(w.Cards.Shown);
        Assert.Equal([card.Seeds[1], card.Seeds[4]], export.Seeds);
        Assert.False(export.Versions.IsVisible);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportManyNamesTheVersionsAndTheFirstIsThePropAsItIs()
    {
        using var s = new Scratch();
        MainWindow w = Open(RoundBush, s);
        w.Editing.Select("cmp-bush-leaves");
        w.Editing.MoveBy(new Vector3(0.5f, 0, 0));
        uint seed = w.Panel.Current.GetSeed("seed");
        string map = s.P("map");
        Directory.CreateDirectory(map);
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        w.Files.Prefs.MapFolder = map;

        IReadOnlyList<Batch.BatchVersion> items = ClaudeKit.Run(w.Versions.ItemsAsync(null, 3, TestContext.Current.CancellationToken), 60000);
        Assert.Equal(["prop_01", "prop_02", "prop_03"], items.Select(i => i.Name));
        Assert.Equal([seed, (uint)Variations.VariationSeed(seed, 1), (uint)Variations.VariationSeed(seed, 2)], items.Select(i => i.Version.Seed));

        // The first keeps the hand edits; the others are the settings on their seeds.
        var kept = new List<bool>();
        FileActions.WriteManyForTests.Value = (name, settings) =>
        {
            kept.Add(settings.Values.ContainsKey(SettingsPanel.PartEditsKey));
            return "pg_" + name;
        };
        try
        {
            Assert.Equal(3, ClaudeKit.Run(w.Files.ExportManyAsync(items), 60000));
        }
        finally
        {
            FileActions.WriteManyForTests.Value = null;
        }

        Assert.Equal([true, false, false], kept);

        int exported = ClaudeKit.Run(w.Versions.ExportAsync(null, 3), 120000);
        Assert.True(exported == 3, string.Join(" | ", w.RecentMessages));
        string[] glbs = [.. Directory.GetFiles(map, "*.glb", SearchOption.AllDirectories).Select(Path.GetFileName)!];
        Assert.Equal(3, glbs.Length);
        Assert.Equal(["pg_prop_01_bush.glb", "pg_prop_02_bush.glb", "pg_prop_03_bush.glb"], glbs.Order(StringComparer.Ordinal));

        Assert.Equal(["prop_01", "prop_02"], ClaudeKit.Run(w.Versions.ItemsAsync([new(5), new(6)], 9, TestContext.Current.CancellationToken), 60000).Select(i => i.Name));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TickedDamagedVersionsExportWithTheirDamage()
    {
        using var s = new Scratch();
        MainWindow w = Open(YourPropsTests.Point("oak"), s);
        w.Editing.Select(w.Editing.Scene!.Components.First(c => c.GeneratorId == "painted-clump").Id);
        CommandListTests.Run(w, "remove-part");
        Assert.True(w.Panel.Current.Values.ContainsKey(SettingsPanel.TreeEditsKey));
        Settings basis = w.Panel.Current;
        uint seed = basis.GetSeed("seed");
        CommandListTests.Run(w, "show-more");
        var card = (ShowMoreCard)w.Cards.Shown!;
        card.ShowMode(ShowMoreMode.Damaged);
        Batch.PropVersion first = card.Versions[0];
        Batch.PropVersion third = card.Versions[2];
        card.Toggle(first);
        card.Toggle(third);
        card.ExportTicked();

        ExportManyCard export = Assert.IsType<ExportManyCard>(w.Cards.Shown);
        Assert.Equal([first, third], export.Chosen);
        Assert.False(export.Versions.IsVisible);

        IReadOnlyList<Batch.BatchVersion> items = ClaudeKit.Run(w.Versions.ItemsAsync(export.Chosen, 9, TestContext.Current.CancellationToken), 60000);
        Assert.Equal(["prop_01", "prop_02"], items.Select(i => i.Name));
        Assert.Equal([first, third], items.Select(i => i.Version));

        // Each is the prop as it is (its seed, its hand edits) with that version's damage.
        var written = new List<Settings>();
        FileActions.WriteManyForTests.Value = (name, settings) =>
        {
            written.Add(settings);
            return "pg_" + name;
        };
        try
        {
            w.Files.Prefs.MapFolder = MapIn(s);
            Assert.Equal(2, ClaudeKit.Run(w.Files.ExportManyAsync(items), 60000));
        }
        finally
        {
            FileActions.WriteManyForTests.Value = null;
        }

        Assert.Equal(2, written.Count);
        foreach ((Settings settings, Batch.PropVersion version) in written.Zip([first, third]))
        {
            Assert.Equal(seed, settings.GetSeed("seed"));
            Assert.Equal(basis.Values[SettingsPanel.TreeEditsKey].GetRawText(), settings.Values[SettingsPanel.TreeEditsKey].GetRawText());
            Assert.All(version.Damage!, d => Assert.Equal(d.Value, settings.Values[d.Key].GetDouble()));
        }

        Assert.NotEqual(written[0], written[1]);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void VersionsOnOtherSeedsKeepAPaintedTreesOwnEdits()
    {
        // easy-props' withSeed swaps the seed and keeps the parameters, the tree's own edits among them; only the part
        // edits (component diffs made on the prop's own seed) go.
        using var s = new Scratch();
        MainWindow w = Open(YourPropsTests.Point("oak"), s);
        w.Editing.Select(w.Editing.Scene!.Components.First(c => c.GeneratorId == "painted-clump").Id);
        CommandListTests.Run(w, "remove-part");
        Settings basis = w.Panel.Current.With(SettingsPanel.PartEditsKey, System.Text.Json.JsonElement.Parse("{}"));
        string edits = basis.Values[SettingsPanel.TreeEditsKey].GetString()!;
        Assert.NotEqual(string.Empty, edits);
        uint seed = basis.GetSeed("seed");

        IReadOnlyList<Batch.BatchVersion> items = ClaudeKit.Run(w.Versions.ItemsAsync(null, 3, TestContext.Current.CancellationToken), 60000);
        var written = new List<Settings>();
        FileActions.WriteManyForTests.Value = (name, settings) =>
        {
            written.Add(settings);
            return "pg_" + name;
        };
        try
        {
            w.Files.Prefs.MapFolder = MapIn(s);
            Assert.Equal(3, ClaudeKit.Run(w.Files.ExportManyAsync(items), 60000));
        }
        finally
        {
            FileActions.WriteManyForTests.Value = null;
        }

        Assert.Equal(3, written.Count);
        Assert.All(written, settings => Assert.Equal(edits, settings.Values[SettingsPanel.TreeEditsKey].GetString()));
        Assert.All(written.Skip(1), settings => Assert.NotEqual(seed, settings.GetSeed("seed")));

        // The same rule for every version on another seed: Export many (and its set) and Show me more's pictures.
        var other = new Batch.PropVersion(seed + 1);
        foreach (Settings made in new[] { other.Of(basis), ShowMoreCard.SettingsOf(basis, other) })
        {
            Assert.Equal(seed + 1, made.GetSeed("seed"));
            Assert.Equal(edits, made.Values[SettingsPanel.TreeEditsKey].GetString());
            Assert.False(made.Values.ContainsKey(SettingsPanel.PartEditsKey));
        }

        w.Close();
    }

    private static string MapIn(Scratch s)
    {
        string map = s.P("map");
        Directory.CreateDirectory(map);
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        return map;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MakeItFitLowersTheControlsUntilThePropFitsAsOneStep()
    {
        // The bush with the most faces, against the Mobile budget.
        StartingPoint heavy = EngineKit.OnThePool(() => Bush.StartingPoints.MaxBy(p => Faces(p.Settings))!);
        MainWindow w = Open(heavy);
        w.ViewState.Budget = "mobile";
        Assert.True(Waited(() => w.Rocks.FullDetailFaces > 2000));
        Assert.Null(CommandListTests.Find(w, "make-it-fit").Unavailable());
        Assert.True(w.StatusBar.MakeItFit.IsEnabled);
        Settings before = w.Panel.Current;
        int steps = w.Files.History.Steps.Count;

        Task fit = w.Versions.FitAsync();
        Assert.True(Waited(() => fit.IsCompleted, 120000));
        fit.GetAwaiter().GetResult();
        Ui.Pump();

        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal("Make it fit", w.Files.History.Steps[^1].Label);
        Assert.NotEqual(before, w.Panel.Current);
        Settings after = w.Panel.Current;
        Assert.True(EngineKit.OnThePool(() => Faces(after) < Faces(before)));
        w.Files.Undo();
        Assert.Equal(before, w.Panel.Current);
        w.Close();
    }

    // The full-detail faces as the engine counts them, in the same job as a light build (proxies left out).
    private static int Faces(Settings s) =>
        Bush.BuildCounted(s, 0.12, countFull: true, BuildPurpose.Count, TestContext.Current.CancellationToken).FullFaces!.Value;
}
