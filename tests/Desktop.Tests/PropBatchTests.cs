using System.Diagnostics;
using System.Text.Json;
using PropGenerator.Desktop.Batch;
using PropGenerator.Desktop.Cards;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using PropGenerator.Nature.Random;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// Make it fit and Export many's names on the engine (D1, part 2): the controls Make it fit may lower, the fit itself
/// and the batch's names are upstream's, asked of easy-props' engine, never of the port.
/// </summary>
public class PropBatchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool Waited(Func<bool> done, int ms = 60000)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    private static MainWindow Start(string pointId, Scratch? scratch = null, FakeDialogs? dialogs = null)
    {
        MainWindow w = scratch is null ? Ui.Open(new FakePainter()) : Ui.Open(new FakePainter(), dialogs ?? new FakeDialogs(), scratch);
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(YourPropsTests.Point(pointId))));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        return w;
    }

    private static string Map(Scratch s, MainWindow w)
    {
        string map = s.P("map");
        Directory.CreateDirectory(map);
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        w.Files.Prefs.MapFolder = map;
        return map;
    }

    private static string[] Glbs(string map) =>
        [.. Directory.GetFiles(map, "*.glb", SearchOption.AllDirectories).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FitControlsComeFromTheEngine()
    {
        EngineRecipe oak = EngineKit.Recipe("painted-tree");
        Settings s = YourPropsTests.Point("oak").Settings;
        string[] upstream = [.. EngineHost.Default.Rules.FitControls(oak.SceneJson(s), Ct).Select(c => c.Key)];
        Assert.NotEmpty(upstream);
        Assert.Equal(upstream, oak.FitControls(s).Select(c => c.Key));

        MainWindow w = Start("oak");
        Assert.Equal(upstream, w.Versions.FitControls().Select(c => c.Key));

        // Export many's limit is upstream's MAX_VARIATIONS, read from the engine.
        CommandListTests.Run(w, "export-many");
        ExportManyCard card = Assert.IsType<ExportManyCard>(w.Cards.Shown);
        Assert.Equal(EngineHost.Default.Catalog.Info.MaxVariations, (int)card.Versions.Maximum);
        Assert.Equal(50, (int)card.Versions.Maximum);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MakeItFitBringsAnOverBudgetOakUnder()
    {
        MainWindow w = Start("oak");
        w.ViewState.Budget = "background";
        Assert.True(Waited(() => w.Rocks.FullDetailFaces > 5000));
        Assert.Null(CommandListTests.Find(w, "make-it-fit").Unavailable());
        Settings before = w.Panel.Current;
        int steps = w.Files.History.Steps.Count;
        string[] controls = [.. w.Versions.FitControls().Select(c => c.Key)];

        // No deadline, so the outcome does not depend on how busy the machine is (the app keeps upstream's 5 s).
        w.Versions.FitDeadline = Timeout.InfiniteTimeSpan;
        Task fit = w.Versions.FitAsync();
        Assert.True(Waited(() => fit.IsCompleted, 300000));
        fit.GetAwaiter().GetResult();
        Ui.Pump();

        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal("Make it fit", w.Files.History.Steps[^1].Label);
        Settings after = w.Panel.Current;
        string[] changed = [.. after.Values.Keys.Where(k => !before.Values.TryGetValue(k, out JsonElement was) || was.ToString() != after.Values[k].ToString())];
        Assert.NotEmpty(changed);
        Assert.All(changed, k => Assert.Contains(k, controls));
        Assert.All(changed, k => Assert.True(after.Values[k].GetDouble() < before.Values[k].GetDouble(), $"{k} was not lowered"));
        Assert.Contains(w.RecentMessages, m => m.StartsWith("Fits the Background budget now", StringComparison.Ordinal) && m.Contains('→', StringComparison.Ordinal));

        // The view builds the new values, and they fit.
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(after) == true, 120000));
        Assert.True(Waited(() => w.Rocks.FullDetailFaces <= 5000, 120000), $"{w.Rocks.FullDetailFaces} faces");
        w.Files.Undo();
        Assert.Equal(before, w.Panel.Current);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MakeItFitNeverRunsOnTheUiThread()
    {
        MainWindow w = Start("oak");
        w.ViewState.Budget = "mobile";
        Assert.True(Waited(() => w.Rocks.FullDetailFaces > 2000));
        long queued = EngineHost.Default.Builds.JobsQueued(EngineJobKind.Export);

        var clock = Stopwatch.StartNew();
        Task fit = w.Versions.FitAsync();
        long returned = clock.ElapsedMilliseconds;
        Assert.False(fit.IsCompleted, "Make it fit finished on the UI thread");
        Assert.True(returned < 500, $"FitAsync held the UI thread {returned} ms");
        Assert.Equal("Make it fit is working", CommandListTests.Find(w, "make-it-fit").Unavailable());

        // The fit is one job on the engine's export turn; the window keeps answering meanwhile.
        Assert.True(Waited(() => EngineHost.Default.Builds.JobsQueued(EngineJobKind.Export) == queued + 1, 10000));
        bool answered = false;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => answered = true);
        Assert.True(Waited(() => answered || fit.IsCompleted, 5000));
        Assert.True(answered);
        Assert.True(Waited(() => fit.IsCompleted, 120000));
        fit.GetAwaiter().GetResult();
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportManyNamesEqualUpstreams()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("Old Oak 2.propgen") };
        MainWindow w = Start("round-bush", s, dialogs);
        Assert.True(CommandListTests.RunAsync(w.Files.SaveAsAsync()));
        uint own = w.Panel.Current.GetSeed("seed");
        string name = EngineHost.Default.Rules.ExportNames("Old Oak 2", Ct).Export;
        Assert.Equal("old_oak_2", name);

        // Three versions: upstream's batchItems numbering, two digits from 01; the first is the prop as it is.
        IReadOnlyList<BatchVersion> three = ClaudeKit.Run(w.Versions.ItemsAsync(null, 3, Ct), 60000);
        Assert.Equal([$"{name}_01", $"{name}_02", $"{name}_03"], three.Select(i => i.Name));
        Assert.Equal([own, (uint)Variations.VariationSeed(own, 1), (uint)Variations.VariationSeed(own, 2)], three.Select(i => i.Version.Seed));

        // Seeds ticked in Show me more: one each, whatever the number.
        IReadOnlyList<BatchVersion> ticked = ClaudeKit.Run(w.Versions.ItemsAsync([new(5), new(6), new(7)], 9, Ct), 60000);
        Assert.Equal([$"{name}_01", $"{name}_02", $"{name}_03"], ticked.Select(i => i.Name));
        Assert.Equal([5u, 6u, 7u], ticked.Select(i => i.Version.Seed));

        string map = Map(s, w);
        int exported = ClaudeKit.Run(w.Versions.ExportAsync([new(5), new(6), new(7)], 9), 120000);
        Assert.True(exported == 3, string.Join(" | ", w.RecentMessages));
        Assert.Equal([$"pg_{name}_01_bush.glb", $"pg_{name}_02_bush.glb", $"pg_{name}_03_bush.glb"], Glbs(map));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportManyBuildsOneSceneAtATime()
    {
        using var s = new Scratch();
        EngineRecipe trees = EngineKit.Recipe("painted-tree");

        // The painted tree with the most parts.
        StartingPoint biggest = EngineKit.OnThePool(() => trees.StartingPoints.MaxBy(p => trees.SceneOf(p.Settings).Components.Count)!);
        MainWindow w = Start(biggest.Id, s);
        uint own = w.Panel.Current.GetSeed("seed");

        // Fifty names in one job: names only, no version's scene made.
        long queued = EngineHost.Default.Builds.JobsQueued(EngineJobKind.Export);
        IReadOnlyList<BatchVersion> items = ClaudeKit.Run(w.Versions.ItemsAsync(null, 50, Ct), 60000);
        Assert.Equal(queued + 1, EngineHost.Default.Builds.JobsQueued(EngineJobKind.Export));
        Assert.Equal(50, items.Count);
        Assert.Equal("prop_50", items[^1].Name);
        Assert.Equal(Enumerable.Range(0, 50).Select(v => (uint)Variations.VariationSeed(own, v)), items.Select(i => i.Version.Seed));

        // The export makes each version's scene at its turn, one at a time.
        _ = Map(s, w);
        var written = new List<(string Name, uint Seed)>();
        int alive = 0;
        int most = 0;
        FileActions.WriteManyForTests.Value = (name, settings) =>
        {
            int now = Interlocked.Increment(ref alive);
            InterlockedMax(ref most, now);
            Thread.Sleep(5);
            lock (written)
            {
                written.Add((name, settings.GetSeed("seed")));
            }

            Interlocked.Decrement(ref alive);
            return "pg_" + name;
        };
        try
        {
            int exported = ClaudeKit.Run(w.Files.ExportManyAsync(items), 120000);
            Assert.Equal(50, exported);
        }
        finally
        {
            FileActions.WriteManyForTests.Value = null;
        }

        Assert.Equal(1, most);
        Assert.Equal(1, w.Files.MostVersionsAtOnce);
        Assert.Equal(items.Select(i => (i.Name, i.Version.Seed)), written);
        w.Close();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen = Volatile.Read(ref target);
        while (value > seen)
        {
            int was = Interlocked.CompareExchange(ref target, value, seen);
            if (was == seen)
            {
                return;
            }

            seen = was;
        }
    }
}
