using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>The loading window shown at launch, and the main window's word that its first picture is ready.</summary>
public sealed class LaunchTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SplashShowsTheStepAndHowFarAlong()
    {
        var splash = new Splash();
        splash.Show("Painting the floor", 0.75);
        Assert.Equal("Painting the floor", splash.Step);
        Assert.Equal("75%", splash.Percent);
        Assert.False(splash.Indeterminate);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SplashWithoutAFractionRunsTheBarWithoutAPercent()
    {
        var splash = new Splash();
        splash.Show("Starting", null);
        Assert.True(splash.Indeterminate);
        Assert.Equal(string.Empty, splash.Percent);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FirstPictureIsReadyOnceWhenTheFloorIsPainted()
    {
        MainWindow w = Hidden(new FakePainter());
        Assert.True(Waited(() => w.HasFirstPicture, 30000));
        int again = 0;
        w.FirstPictureReady += () => again++;
        w.Repaint.RepaintNow();
        Ui.Pump();
        Assert.Equal(0, again);
        Assert.True(Launch.FirstPicture(w, TimeSpan.FromMinutes(1)).IsCompleted);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FirstPictureIsReadyWhenThePainterFails()
    {
        var painter = new FakePainter { Reply = (_, _) => new TaskCompletionSource<Layers?>().Task };
        MainWindow w = Hidden(painter);
        int ready = 0;
        w.FirstPictureReady += () => ready++;
        painter.FailFromThreadPool("no python");
        Assert.True(Waited(() => ready > 0, 30000));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void LaunchGivesUpWaitingAfterItsTimeout()
    {
        var painter = new FakePainter { Reply = (_, _) => new TaskCompletionSource<Layers?>().Task };
        MainWindow w = Hidden(painter);
        Task waited = Launch.FirstPicture(w, TimeSpan.FromMilliseconds(200));
        Assert.True(Waited(() => waited.IsCompleted, 30000));
    }

    [Fact]
    public void LoadReadsEasyPropsAsAStepOfItsOwn()
    {
        Scratch s = Ui.NewScratch();
        var steps = new Steps();
        App.Loaded loaded = App.Load(s.P("prefs.json"), s.P("library.json"), steps, EngineHost.Default);
        Assert.Null(loaded.EngineFailure);
        Assert.Contains(steps.Seen, x => x.Step == "Loading easy-props");
        Assert.Equal(steps.Seen.Select(x => x.Share).Order(), steps.Seen.Select(x => x.Share));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void LaunchWithABrokenEngineOpensWithOurKinds()
    {
        var host = new EngineHost(EngineHostTests.Broken(), EngineHostTests.Broken());
        try
        {
            Scratch s = Ui.NewScratch();
            var steps = new Steps();
            App.Loaded loaded = Task.Run(() => App.Load(s.P("prefs.json"), s.P("library.json"), steps, host)).GetAwaiter().GetResult();
            Assert.IsType<EngineException>(loaded.EngineFailure);
            Assert.Contains(steps.Seen, x => x.Step == "Loading easy-props");

            // Only our own kinds are registered; easy-props' are named, not made.
            IReadOnlyList<IMeshRecipe> registered = MeshRecipes.Registered(loaded.EngineFailure);
            Assert.Equal([CliffRecipe.Instance.Id, StylizedRockRecipe.Instance.Id], registered.Select(r => r.Id));

            MainWindow w = Ui.Open(new FakePainter());
            App.SayEngineFailure(w, loaded.EngineFailure);
            Assert.True(w.IsVisible);
            Assert.Equal(1, w.ErrorsSaid);
            Assert.Contains(loaded.EngineFailure!.Message, w.LastError, StringComparison.Ordinal);
            Assert.Contains("easy-props' engine could not be loaded", w.LastError, StringComparison.Ordinal);

            // Said once: no engine failure, nothing said.
            App.SayEngineFailure(w, null);
            Assert.Equal(1, w.ErrorsSaid);
        }
        finally
        {
            host.Shutdown(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task LaunchWhoseKindParametersFailLeavesOnlyOurKinds()
    {
        // easy-props' info and presets load, but a kind's parameters do not: the launch carries that as the engine's
        // failure (the host's too, so only our own kinds are registered) instead of throwing out of the load.
        var scripts = EngineOptions.EmbeddedScripts().ToList();
        scripts.Add(new EngineScript("no-parameters.js", "globalThis.EasyProps = { ...EasyProps, parameters: () => { throw new Error('no parameters here'); } };"));
        var host = new EngineHost(EngineHostTests.Broken(), EngineOptions.ForRules with { Scripts = scripts });
        try
        {
            Scratch s = Ui.NewScratch();
            App.Loaded loaded = await Task.Run(() => App.Load(s.P("prefs.json"), s.P("library.json"), new Steps(), host), TestContext.Current.CancellationToken);
            Assert.IsType<EngineException>(loaded.EngineFailure, exactMatch: false);
            Assert.Contains("no parameters here", loaded.EngineFailure!.Message, StringComparison.Ordinal);
            Assert.Same(loaded.EngineFailure, host.LoadFailure);
        }
        finally
        {
            host.Shutdown(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void LoadAfterTheEngineShutDownCarriesTheFailure()
    {
        // The splash closed while the launch was loading: the app is exiting, and the load still returns.
        var host = new EngineHost(EngineHostTests.Broken(), EngineHostTests.Broken());
        host.Shutdown(TimeSpan.FromSeconds(5));
        Scratch s = Ui.NewScratch();
        App.Loaded loaded = App.Load(s.P("prefs.json"), s.P("library.json"), new Steps(), host);
        Assert.IsType<ObjectDisposedException>(loaded.EngineFailure);
    }

    private static bool Waited(Func<bool> done, int ms)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    // Built as the launch builds it: not shown until its first picture is ready.
    private static MainWindow Hidden(FakePainter painter)
    {
        Scratch s = Ui.NewScratch();
        var w = new MainWindow(painter, Ui.Specs(), new FakeDialogs(), new Prefs(s.P("prefs.json")), new Library(s.P("library.json")), s.P("bridge"));
        Ui.Track(w);
        return w;
    }

    // Every step the load reported, on the thread it was reported on (no posting, unlike Progress<T>).
    private sealed class Steps : IProgress<(string Step, double Share)>
    {
        public List<(string Step, double Share)> Seen { get; } = [];

        public void Report((string Step, double Share) value)
        {
            lock (Seen)
            {
                Seen.Add(value);
            }
        }
    }
}
