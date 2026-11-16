using System.Text.Json;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// The view's builds as an engine's queue sees them: each with its purpose, its window's own replace key and the
/// token that stops it once the prop is no longer shown; a build dropped by a newer one is built again, silently.
/// </summary>
[Collection(MeshRecipesAlone.Name)]
public class RockLiveTests
{
    private static StartingPoint Boulder => RockRecipe.StartingPoints.Single(p => p.Id == "boulder");

    private static bool Waited(Func<bool> done, int ms = 20000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    // A window on the boulder, built by the slow double, with every failure it reports noted.
    private static MainWindow Rock(List<string> failures)
    {
        MainWindow w = Ui.Open(new FakePainter());
        w.Rocks.Failed += failures.Add;
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Ui.Pump();
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        return w;
    }

    private static void Set(MainWindow w, string key, double value)
    {
        Assert.Null(w.Panel.SetFromOutside(key, JsonSerializer.SerializeToElement(value)));
        Ui.Pump();
    }

    private static bool OnTheView(SlowRecipe.Call c) => c.Purpose is BuildPurpose.View or BuildPurpose.Count;

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ARecountOverlappingANewBuildShowsNoError()
    {
        var slow = new SlowRecipe(RockRecipe.Engine, TimeSpan.FromMilliseconds(30)) { Drop = c => c.Purpose == BuildPurpose.Count };
        using IDisposable _ = MeshRecipes.Override(slow);
        var failures = new List<string>();
        MainWindow w = Rock(failures);
        w.ViewState.Lod = 1;
        Assert.True(Waited(() => w.Rocks.ShownDetail == RockRecipe.DetailShares[1]));

        // A drag at LOD1 keeps the count; let go, the recount is dropped by the queue while a new build starts.
        w.Rocks.PointerHeld = true;
        Set(w, "width", 3.5);
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        w.Rocks.PointerHeld = false;
        Set(w, "width", 3.7);
        Assert.True(Waited(() => slow.Calls.Any(c => c.Purpose == BuildPurpose.Count)));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        Ui.Wait(200);

        Assert.Empty(failures);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ADroppedBuildIsBuiltAgain()
    {
        int viewDrops = 0;
        int shapeDrops = 0;
        var slow = new SlowRecipe(RockRecipe.Engine, TimeSpan.FromMilliseconds(40));
        using IDisposable _ = MeshRecipes.Override(slow);
        var failures = new List<string>();
        MainWindow w = Rock(failures);

        // The shapes asked for after the rock was shown are dropped once, and so is the view's build of the last value.
        slow.Drop = c =>
            (c.Method == "collision" && c.Replaces is not null && Interlocked.Increment(ref shapeDrops) == 1)
            || (c.Purpose == BuildPurpose.View && c.Method != "collision" && Math.Abs(c.Settings.GetFloat("width") - 4.0) < 1e-6 && Interlocked.Increment(ref viewDrops) == 1);
        w.ViewState.ShowCollision = true;
        Ui.Pump();
        Set(w, "width", 3.4);
        Set(w, "width", 3.8);
        Set(w, "width", 4.0);

        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true), "the view never reached the last value");
        Assert.True(Waited(() => w.Rocks.ShapesOf?.Equals(w.Panel.Current) == true), "the shapes never reached the last value");
        Assert.True(viewDrops >= 1 && shapeDrops >= 1, $"{viewDrops} view drops, {shapeDrops} shape drops");
        Assert.Empty(failures);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DragTicksAtLod1AskForNoFullCount()
    {
        // The engine's rock, as the count it is checked against (the port's rock is easy-props 0.4.2's).
        var slow = new SlowRecipe(RockRecipe.Engine, TimeSpan.Zero);
        using IDisposable _ = MeshRecipes.Override(slow);
        var failures = new List<string>();
        MainWindow w = Rock(failures);
        w.ViewState.Lod = 1;
        Assert.True(Waited(() => w.Rocks.ShownDetail == RockRecipe.DetailShares[1] && w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        Ui.Wait(100);
        int before = slow.Calls.Count;

        w.Rocks.PointerHeld = true;
        foreach (double width in new[] { 3.2, 3.4, 3.6 })
        {
            Set(w, "width", width);
            Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        }

        List<SlowRecipe.Call> ticks = [.. slow.Calls.Skip(before).Where(OnTheView)];
        Assert.NotEmpty(ticks);
        Assert.All(ticks, c => Assert.Equal((BuildPurpose.View, false, RockRecipe.DetailShares[1]), (c.Purpose, c.CountFull ?? true, c.Detail)));

        // Let go: one count.
        int held = slow.Calls.Count;
        // Off the UI thread: the cut rock's statics build on easy-props' engine, which the UI thread never waits on.
        Settings current = w.Panel.Current;
        int expected = ClaudeKit.Run(Task.Run(() => RockRecipe.Build(current).Indices.Length / 3, TestContext.Current.CancellationToken), 120000);
        w.Rocks.PointerHeld = false;
        Assert.True(Waited(() => w.Rocks.FullDetailFaces == expected), $"{w.Rocks.FullDetailFaces} faces, {expected} wanted");
        Ui.Wait(100);
        SlowRecipe.Call count = Assert.Single(slow.Calls.Skip(held), OnTheView);
        Assert.Equal((BuildPurpose.Count, true), (count.Purpose, count.CountFull));
        Assert.Equal(w.Rocks.CountKey, count.Replaces);
        Assert.Empty(failures);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SwitchingKindCancelsTheRunningBuild()
    {
        var slow = new SlowRecipe(RockRecipe.Engine, TimeSpan.Zero);
        using IDisposable _ = MeshRecipes.Override(slow);
        var failures = new List<string>();
        MainWindow w = Rock(failures);

        // A slow build running when the kind changes: its token is cancelled.
        slow.Delay = TimeSpan.FromSeconds(20);
        int before = slow.Calls.Count;
        Set(w, "width", 3.3);
        Assert.True(Waited(() => slow.Calls.Skip(before).Any(OnTheView)));
        SlowRecipe.Call running = slow.Calls.Skip(before).First(OnTheView);
        Assert.False(running.Cancel.IsCancellationRequested);
        w.Rocks.Recipe = EngineKit.Recipe("grass");
        Assert.True(running.Cancel.IsCancellationRequested);

        // And when the rock is no longer the entry.
        w.Rocks.Recipe = slow;
        w.Rocks.Active = true;
        before = slow.Calls.Count;
        Set(w, "width", 3.6);
        Assert.True(Waited(() => slow.Calls.Skip(before).Any(OnTheView)));
        running = slow.Calls.Skip(before).First(OnTheView);
        Assert.False(running.Cancel.IsCancellationRequested);
        w.Rocks.Active = false;
        Assert.True(running.Cancel.IsCancellationRequested);

        slow.Delay = TimeSpan.Zero;
        Ui.Wait(100);
        Assert.Empty(failures);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TwoRockLivesUseTheirOwnKeys()
    {
        var slow = new SlowRecipe(RockRecipe.Engine, TimeSpan.Zero);
        using IDisposable _ = MeshRecipes.Override(slow);
        var failures = new List<string>();
        MainWindow one = Rock(failures);
        MainWindow two = Rock(failures);

        string[] keys = [one.Rocks.BuildKey, one.Rocks.CountKey, one.Rocks.ShapesKey, two.Rocks.BuildKey, two.Rocks.CountKey, two.Rocks.ShapesKey];
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.StartsWith("view-build:", one.Rocks.BuildKey, StringComparison.Ordinal);
        Assert.StartsWith("view-count:", one.Rocks.CountKey, StringComparison.Ordinal);
        Assert.StartsWith("view-shapes:", one.Rocks.ShapesKey, StringComparison.Ordinal);

        // Each window's view builds carry its own key.
        HashSet<string?> used = [.. slow.Calls.Where(c => c.Purpose == BuildPurpose.View && c.Method != "collision").Select(c => c.Replaces)];
        Assert.Contains(one.Rocks.BuildKey, used);
        Assert.Contains(two.Rocks.BuildKey, used);
        Assert.Empty(failures);
        one.Close();
        two.Close();
    }
}
