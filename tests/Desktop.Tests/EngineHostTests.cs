using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>The app's one engine host: shared by every window, made lazily, shut down once at exit.</summary>
public sealed class EngineHostTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TwoWindowsShareOneBuildEngine()
    {
        MainWindow first = Ui.Open(new FakePainter());
        EngineThread seen = EngineHost.Default.Builds;
        MainWindow second = Ui.Open(new FakePainter());
        Assert.NotSame(first, second);
        Assert.Same(seen, EngineHost.Default.Builds);
        Assert.Same(EngineHost.Default.Rules, EngineHost.Default.Rules);
        Assert.Same(EngineHost.Default.Textures, EngineHost.Default.Textures);
    }

    [Fact]
    public void ShutdownTwiceDoesNotThrowAndLaterReadsAreRefused()
    {
        var host = new EngineHost(new EngineOptions(), EngineOptions.ForRules);
        EngineThread builds = host.Builds;
        host.Shutdown(TimeSpan.FromSeconds(5));
        host.Shutdown(TimeSpan.FromSeconds(5));
        Assert.Throws<ObjectDisposedException>(() => host.Builds);
        Assert.Throws<ObjectDisposedException>(() => host.Rules);
        Assert.Throws<ObjectDisposedException>(() => host.Catalog);
        Assert.Throws<ObjectDisposedException>(() => host.Textures);
        Assert.Throws<ObjectDisposedException>(() => { _ = builds.Enqueue(EngineJobKind.Live, (_, _) => 1); });
    }

    [Fact]
    public void ABrokenBundleIsCarriedAsTheLoadFailureAndTriedAgain()
    {
        var host = new EngineHost(Broken(), Broken());
        try
        {
            Assert.Null(host.LoadFailure);
            Assert.Throws<EngineException>(() => host.Catalog);
            Assert.IsType<EngineException>(host.LoadFailure);

            // Not cached as a Lazy would: the next read tries again (and fails again).
            Assert.Throws<EngineException>(() => host.Rules);
        }
        finally
        {
            host.Shutdown(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void TheCatalogReadsInfoPresetsAndParametersOnce()
    {
        EngineCatalog catalog = EngineHost.Default.Catalog;
        Assert.Same(catalog, EngineHost.Default.Catalog);
        Assert.Equal(EnginePin.Tag, catalog.Info.Tag);
        // One picture per preset upstream ships (build.mjs counts them), not a release's number.
        Assert.Equal(EnginePin.Thumbnails, catalog.Presets.Presets.Count);
        Assert.Equal("oak", catalog.Preset("oak")?.Id);
        Assert.Null(catalog.Preset("no-such-preset"));
        EngineParameters bush = catalog.Parameters("bush");
        Assert.NotEmpty(bush.Specs);
        Assert.Same(bush, catalog.Parameters("bush"));
        Assert.ThrowsAny<EngineException>(() => catalog.Parameters("no-such-generator"));
    }

    [Fact]
    public async Task GetStatesEngineBlockNamesTheTagGenerationAndBytes()
    {
        _ = await EngineHost.Default.Builds.GenerationAsync().WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        System.Text.Json.Nodes.JsonObject state = await ClaudeTools.EngineState(EngineHost.Default);
        Assert.Equal(true, (bool?)state["started"]);
        Assert.Equal(EnginePin.Tag, (string?)state["tag"]);
        Assert.True((int?)state["generation"] >= 1);
        Assert.True((long?)state["heap_bytes"] > 0);
        Assert.NotNull(state["external_bytes"]);
        Assert.NotNull(state["texture_bytes"]);

        // A host whose build engine nobody asked for answers without loading one.
        var quiet = new EngineHost(new EngineOptions(), EngineOptions.ForRules);
        try
        {
            System.Text.Json.Nodes.JsonObject idle = await ClaudeTools.EngineState(quiet);
            Assert.Equal(false, (bool?)idle["started"]);
            Assert.False(quiet.BuildsStarted);
        }
        finally
        {
            quiet.Shutdown(TimeSpan.FromSeconds(5));
        }

        var host = new EngineHost(Broken(), Broken());
        try
        {
            Assert.Contains("bundle", (string?)(await ClaudeTools.EngineState(host))["error"], StringComparison.Ordinal);
        }
        finally
        {
            host.Shutdown(TimeSpan.FromSeconds(5));
        }
    }

    // Options whose bundle is missing: the engine cannot load.
    internal static EngineOptions Broken() => EngineOptions.ForRules with { Scripts = [] };
}
