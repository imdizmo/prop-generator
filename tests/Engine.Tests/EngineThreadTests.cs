using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using PropGenerator.Engine.Tests.Support;
using Xunit;

namespace PropGenerator.Engine.Tests;

/// <summary>The engine's own thread: its queue, priorities, cancels and recovery.</summary>
[Collection(MeasuredAlone.Name)]
public sealed class EngineThreadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private static readonly string[] LaterKinds = ["log", "branch", "litter"];

    [Fact]
    public async Task ABuildRunsOnTheEngineThreadNotTheCaller()
    {
        using var thread = new EngineThread(Standin.Options());
        int caller = Environment.CurrentManagedThreadId;
        int ran = await thread.Enqueue(EngineJobKind.Live, (_, _) => Environment.CurrentManagedThreadId, Ct).WaitAsync(Wait, Ct);
        Assert.NotEqual(caller, ran);

        EngineMesh mesh = await thread.BuildAsync(Standin.Scene, "low-poly", 1, cancel: Ct).WaitAsync(Wait, Ct);
        Assert.Equal(3, mesh.Parts.Count);
        EngineInfo info = await thread.InfoAsync(Ct).WaitAsync(Wait, Ct);
        Assert.Equal("v0.4.2", info.Tag);
    }

    [Fact]
    public async Task TheQueueDropsAStaleLiveJob()
    {
        using var thread = new EngineThread(Standin.Options());
        using var started = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        Task<int> blocker = thread.Enqueue(EngineJobKind.Export, (_, token) =>
        {
            started.Set();
            gate.Wait(Wait, token);
            return 0;
        }, Ct);
        Assert.True(started.Wait(Wait, Ct));

        Task<EngineMesh> stale = thread.BuildAsync(Standin.Scene, "low-poly", 1, cancel: Ct);
        Task<EngineMesh> fresh = thread.BuildAsync(Standin.Scene, "low-poly", 0.4, cancel: Ct);
        Task<EngineMesh> show = thread.BuildAsync(Standin.Scene, "low-poly", 0.12, EngineJobKind.ShowMore, cancel: Ct);
        gate.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stale.WaitAsync(Wait, Ct));
        Assert.True(stale.IsCanceled);
        Assert.Equal(0.4f, (await fresh.WaitAsync(Wait, Ct)).Parts[0].Positions[8]);
        Assert.Equal(0.12f, (await show.WaitAsync(Wait, Ct)).Parts[0].Positions[8]);
        await blocker.WaitAsync(Wait, Ct);
    }

    [Fact]
    public async Task JobsRunLiveFirstThenExportThenShowMoreThenThumbnails()
    {
        using var thread = new EngineThread(Standin.Options());
        using var started = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        var order = new ConcurrentQueue<EngineJobKind>();
        Task<int> blocker = thread.Enqueue(EngineJobKind.Thumbnail, (_, token) =>
        {
            started.Set();
            gate.Wait(Wait, token);
            return 0;
        }, Ct);
        Assert.True(started.Wait(Wait, Ct));

        EngineJobKind[] queued =
            [EngineJobKind.Thumbnail, EngineJobKind.ShowMore, EngineJobKind.Export, EngineJobKind.Live, EngineJobKind.Thumbnail];
        Task<int>[] jobs = [.. queued.Select(kind => thread.Enqueue(kind, (_, _) =>
        {
            order.Enqueue(kind);
            return 0;
        }, Ct))];
        gate.Set();
        await Task.WhenAll(jobs).WaitAsync(Wait, Ct);
        await blocker.WaitAsync(Wait, Ct);

        Assert.Equal(
            [EngineJobKind.Live, EngineJobKind.Export, EngineJobKind.ShowMore, EngineJobKind.Thumbnail, EngineJobKind.Thumbnail],
            order);
    }

    [Fact]
    public async Task ACancelledJobRejects()
    {
        using var thread = new EngineThread(Standin.Options());
        using var cancel = new CancellationTokenSource();
        Task<object?> slow = thread.Enqueue(EngineJobKind.Export, (e, token) => e.Call("Test.slow", token), cancel.Token);
        await Task.Delay(100, Ct);
        Assert.False(slow.IsCompleted);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow.WaitAsync(Wait, Ct));
        Assert.Equal("v0.4.2", (await thread.InfoAsync(Ct).WaitAsync(Wait, Ct)).Tag);
    }

    [Fact]
    public async Task AQueuedJobCancelledBeforeItRunsNeverRuns()
    {
        using var thread = new EngineThread(Standin.Options());
        using var started = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        using var cancel = new CancellationTokenSource();
        int ran = 0;
        Task<int> blocker = thread.Enqueue(EngineJobKind.Live, (_, token) =>
        {
            started.Set();
            gate.Wait(Wait, token);
            return 0;
        }, Ct);
        Assert.True(started.Wait(Wait, Ct));
        Task<int> job = thread.Enqueue(EngineJobKind.Thumbnail, (_, _) => Interlocked.Increment(ref ran), cancel.Token);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.WaitAsync(Wait, Ct));
        gate.Set();
        await blocker.WaitAsync(Wait, Ct);
        await thread.InfoAsync(Ct).WaitAsync(Wait, Ct);
        Assert.Equal(0, ran);
    }

    [Fact]
    public async Task AHeapBlowUpOnTheThreadIsCaughtAndTheNextJobRuns()
    {
        EngineOptions options = Standin.Options() with { MaxHeapBytes = 48L << 20, MaxOldSpaceMiB = 256 };
        using var thread = new EngineThread(options);

        await Assert.ThrowsAsync<EngineOutOfMemoryException>(
            () => thread.Enqueue(EngineJobKind.Live, (e, token) => e.Call("Test.blowUp", token), Ct).WaitAsync(Wait, Ct));

        Assert.Equal("v0.4.2", (await thread.InfoAsync(Ct).WaitAsync(Wait, Ct)).Tag);
    }

    [Fact]
    public async Task ExportAndTexturesGoThroughTheThread()
    {
        using var thread = new EngineThread(Standin.Options());
        IReadOnlyList<EngineFile> files = await thread.ExportAsync(Standin.Scene, "low-poly", """{"name":"oak"}""", cancel: Ct).WaitAsync(Wait, Ct);
        Assert.Equal(2, files.Count);
        EngineTexture texture = await thread.TextureAsync("leaf", cancel: Ct).WaitAsync(Wait, Ct);
        Assert.Equal(16, texture.Rgba.Length);
        EnginePresets presets = await thread.PresetsAsync(Ct).WaitAsync(Wait, Ct);
        Assert.Equal(3, presets.Presets.Count);
    }

    [Fact]
    public async Task ADroppedJobLetsGoOfItsWorkWhileItsTokenLives()
    {
        // One token for the window's lifetime, and live builds replacing each other: a dropped build must not stay
        // registered on the token, holding its work (and the scene it captured) until the window closes.
        using var thread = new EngineThread(Standin.Options());
        using var lifetime = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        Task<int> blocker = thread.Enqueue(EngineJobKind.Export, (_, token) =>
        {
            started.Set();
            gate.Wait(Wait, token);
            return 0;
        }, Ct);
        Assert.True(started.Wait(Wait, Ct));

        (WeakReference dropped, Task<int> stale) = QueueALiveJobHolding(thread, lifetime.Token);
        Task<int> fresh = thread.Enqueue(EngineJobKind.Live, "live", static (_, _) => 2, lifetime.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stale.WaitAsync(Wait, Ct));
        gate.Set();
        Assert.Equal(2, await fresh.WaitAsync(Wait, Ct));
        await blocker.WaitAsync(Wait, Ct);
        stale = Task.FromResult(0);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(dropped.IsAlive, "the dropped job's work is still held by its token");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (WeakReference Held, Task<int> Job) QueueALiveJobHolding(EngineThread thread, CancellationToken token)
    {
        byte[] scene = new byte[1 << 20];
        Task<int> job = thread.Enqueue(EngineJobKind.Live, "live", (_, _) => scene.Length, token);
        return (new WeakReference(scene), job);
    }

    [Fact]
    public void TheDefaultCapsArePhaseOnes()
    {
        var options = new EngineOptions();
        Assert.Equal(512L << 20, options.MaxHeapBytes);
        Assert.Equal(512L << 20, options.MaxArrayBufferBytes);
        Assert.Equal(256L << 20, options.RecycleBytes);
        Assert.Equal(384L << 20, options.ExportHeadroomBytes);
        Assert.True((long)options.MaxOldSpaceMiB << 20 > options.MaxHeapBytes);
        Assert.Equal(TimeSpan.FromSeconds(1), EngineOptions.ForRules.JobTimeout);
    }

    [Fact]
    public async Task LiveBuildsWithDifferentKeysBothComplete()
    {
        using var thread = new EngineThread(Standin.Options());
        using var blocker = Blocker.Start(thread, Ct);

        Task<EngineMesh> view = thread.BuildAsync(Standin.Scene, "low-poly", 1, cancel: Ct, replaces: "view-build:1");
        Task<EngineMesh> count = thread.BuildAsync(Standin.Scene, "low-poly", 0.4, cancel: Ct, replaces: "view-count:1");
        Task<EngineCollision> shapes = thread.CollisionAsync(Standin.Scene, "low-poly", cancel: Ct, replaces: "view-shapes:1");
        blocker.Release();

        Assert.Equal(1f, (await view.WaitAsync(Wait, Ct)).Parts[0].Positions[8]);
        Assert.Equal(0.4f, (await count.WaitAsync(Wait, Ct)).Parts[0].Positions[8]);
        Assert.Single((await shapes.WaitAsync(Wait, Ct)).Shapes);
        await blocker.Job.WaitAsync(Wait, Ct);
    }

    [Fact]
    public async Task LiveBuildsWithTheSameKeyDropTheFirst()
    {
        using var thread = new EngineThread(Standin.Options());
        using var blocker = Blocker.Start(thread, Ct);

        Task<EngineMesh> stale = thread.BuildAsync(Standin.Scene, "low-poly", 1, cancel: Ct, replaces: "view-build:1");
        Task<EngineMesh> other = thread.BuildAsync(Standin.Scene, "low-poly", 0.12, cancel: Ct, replaces: "view-build:2");
        Task<EngineMesh> fresh = thread.BuildAsync(Standin.Scene, "low-poly", 0.4, cancel: Ct, replaces: "view-build:1");
        Task<EngineCollision> staleShapes = thread.CollisionAsync(Standin.Scene, "low-poly", cancel: Ct, replaces: "view-shapes:1");
        Task<EngineCollision> freshShapes = thread.CollisionAsync(Standin.Scene, "low-poly", cancel: Ct, replaces: "view-shapes:1");
        blocker.Release();

        await Assert.ThrowsAsync<TaskCanceledException>(() => stale.WaitAsync(Wait, Ct));
        await Assert.ThrowsAsync<TaskCanceledException>(() => staleShapes.WaitAsync(Wait, Ct));
        Assert.Equal(0.4f, (await fresh.WaitAsync(Wait, Ct)).Parts[0].Positions[8]);
        Assert.Equal(0.12f, (await other.WaitAsync(Wait, Ct)).Parts[0].Positions[8]);
        Assert.Single((await freshShapes.WaitAsync(Wait, Ct)).Shapes);
        await blocker.Job.WaitAsync(Wait, Ct);
    }

    [Fact]
    public async Task ABuildReadsItsSurfaceAndTextureFields()
    {
        using var thread = new EngineThread(Standin.Options());
        EngineMesh mesh = await thread.BuildAsync(Standin.Scene, "low-poly", 1, cancel: Ct).WaitAsync(Wait, Ct);
        Assert.True(mesh.Parts[0].Surface.ReceiveShadow);
        Assert.False(mesh.Parts[0].Surface.Proxy);
        Assert.False(mesh.Parts[1].Surface.ReceiveShadow);
        Assert.True(mesh.Parts[1].Surface.Proxy);
        Assert.False(mesh.Parts[2].Surface.Proxy);

        EngineTexture bark = await thread.TextureAsync("bark:2:0.5", cancel: Ct).WaitAsync(Wait, Ct);
        Assert.Equal("srgb", bark.ColorSpace);
        Assert.True(bark.Repeat);
        EngineTexture relief = await thread.TextureAsync("relief:2", cancel: Ct).WaitAsync(Wait, Ct);
        Assert.Equal("", relief.ColorSpace);
        Assert.True(relief.Repeat);
        EngineTexture leaf = await thread.TextureAsync("leaf", cancel: Ct).WaitAsync(Wait, Ct);
        Assert.False(leaf.Repeat);
    }

    [Fact]
    public async Task AnExportReportsProgressEndingAtOne()
    {
        using var thread = new EngineThread(Standin.Options());
        var progress = new Recorded();
        IReadOnlyList<EngineFile> files = await thread.ExportAsync(Standin.Scene, "low-poly", """{"name":"oak"}""", progress, Ct)
            .WaitAsync(Wait, Ct);
        Assert.Equal(2, files.Count);
        Assert.Equal([0.5, 1], progress.Values);
    }

    [Fact]
    public async Task JobsQueuedCountsByKind()
    {
        using var thread = new EngineThread(Standin.Options());
        Assert.All(Enum.GetValues<EngineJobKind>(), kind => Assert.Equal(0, thread.JobsQueued(kind)));

        await thread.BuildAsync(Standin.Scene, "low-poly", 1, cancel: Ct).WaitAsync(Wait, Ct);
        await thread.InfoAsync(Ct).WaitAsync(Wait, Ct);
        await thread.BuildAsync(Standin.Scene, "low-poly", 1, EngineJobKind.Thumbnail, cancel: Ct).WaitAsync(Wait, Ct);
        await thread.ExportAsync(Standin.Scene, "low-poly", """{"name":"oak"}""", cancel: Ct).WaitAsync(Wait, Ct);

        Assert.Equal(2, thread.JobsQueued(EngineJobKind.Live));
        Assert.Equal(1, thread.JobsQueued(EngineJobKind.Export));
        Assert.Equal(0, thread.JobsQueued(EngineJobKind.ShowMore));
        Assert.Equal(1, thread.JobsQueued(EngineJobKind.Thumbnail));
    }

    [Fact]
    public async Task IsEngineThreadIsTrueOnlyInsideAJob()
    {
        using var thread = new EngineThread(Standin.Options());
        Assert.False(thread.IsEngineThread);
        Assert.True(await thread.Enqueue(EngineJobKind.Live, (_, _) => thread.IsEngineThread, Ct).WaitAsync(Wait, Ct));
        Assert.False(await Task.Run(() => thread.IsEngineThread, Ct));
        using var other = new EngineThread(Standin.Options());
        Assert.False(await other.Enqueue(EngineJobKind.Live, (_, _) => thread.IsEngineThread, Ct).WaitAsync(Wait, Ct));
    }

    [Fact]
    public async Task TheGenerationIsReadThroughAJob()
    {
        using var thread = new EngineThread(Standin.Options());
        Assert.Equal(1, await thread.GenerationAsync().WaitAsync(Wait, Ct));
        Assert.Equal(1, await thread.GenerationAsync(EngineJobKind.Thumbnail).WaitAsync(Wait, Ct));
    }

    [Fact]
    public async Task DisposeWithABoundStopsAnExportReportingProgress()
    {
        var thread = new EngineThread(Standin.Options());
        var progress = new Recorded();
        Task<IReadOnlyList<EngineFile>> export = thread.ExportAsync(Standin.Scene, "low-poly", """{"name":"oak","slow":true}""", progress, Ct);
        Assert.True(SpinWait.SpinUntil(() => progress.Values.Count > 2, Wait));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(thread.Dispose(TimeSpan.FromSeconds(10)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export.WaitAsync(Wait, Ct));
        thread.Dispose();
    }

    [Fact]
    public async Task DisposeWithABoundReturnsWhileAProgressCallbackIsStuck()
    {
        // A progress report that never returns (a synchronous call to a busy UI thread): the bounded dispose gives up
        // in time and says so, and the thread still ends once the callback lets go.
        var thread = new EngineThread(Standin.Options());
        using var reported = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        var stuck = new Stuck(reported, gate);
        Task<IReadOnlyList<EngineFile>> export = thread.ExportAsync(Standin.Scene, "low-poly", """{"name":"oak","slow":true}""", stuck, Ct);
        Assert.True(reported.Wait(Wait, Ct));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(thread.Dispose(TimeSpan.FromMilliseconds(300)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");

        gate.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export.WaitAsync(Wait, Ct));
        Assert.True(thread.Dispose(Wait));
        thread.Dispose();
    }

    [Fact]
    public async Task DisposingCancelsWhatIsQueued()
    {
        Task<int> queued;
        Task<int> blocker;
        using var started = new ManualResetEventSlim();
        using (var thread = new EngineThread(Standin.Options()))
        {
            blocker = thread.Enqueue(EngineJobKind.Live, (_, token) =>
            {
                started.Set();
                token.WaitHandle.WaitOne(Wait);
                return 0;
            }, Ct);
            Assert.True(started.Wait(Wait, Ct));
            queued = thread.Enqueue(EngineJobKind.Thumbnail, (_, _) => 1, Ct);
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(Wait, Ct));
        await blocker.WaitAsync(Wait, Ct);
    }

    [Fact]
    public async Task ExportsAt2048AcrossManyTexturesStayUnderTheCaps()
    {
        // Upstream's texture caches have no bound (one 2048 picture and its relief are 16 MiB each), so the bound is
        // the engine's recycle (E13): boulders at every fifth grain and lichen stop and oaks at each bark print, each
        // a texture of its own, one export job each. One prop exported ten times would pass with unbounded caches.
        Assert.SkipUnless(EngineOptions.HasEmbeddedBundle, "Resources/engine.js is not built yet (tools/engine).");
        var options = new EngineOptions();
        using var thread = new EngineThread(options);
        EnginePresets presets = await thread.PresetsAsync(Ct).WaitAsync(Wait, Ct);
        EnginePreset boulder = presets.Presets.Single(p => p.Id == "boulder");
        EnginePreset oak = presets.Presets.Single(p => p.Id == "oak");
        double[] stops = [0, 0.25, 0.5, 0.75, 1];
        string[] prints = ["none", "ringed", "plated", "banded", "peeling", "fibrous"];
        var props = new List<(string Name, string Scene)>();
        foreach (double grain in stops)
        {
            foreach (double lichen in stops)
            {
                props.Add(($"boulder_g{grain * 100:0}_l{lichen * 100:0}", With(boulder, ("grain", grain), ("lichen", lichen))));
            }
        }

        props.AddRange(prints.Select(print => ($"oak_{print}", With(oak, ("barkPattern", print)))));

        // Two passes over the same props, RSS read after each export once the engine has recycled. A runtime that
        // is not given back raises the second pass over the first (every recycle would add to it); the oaks' own
        // footprint, larger than a boulder's, is in both passes, so it is not taken for growth, as one sample after
        // the last oak against one after a boulder was. Single samples swing by up to ~90 MiB with where the recycle
        // falls, so the passes' means and peaks are compared, not samples.
        int generation = 1;
        bool recycled = false;
        List<long>[] passes = [[], []];
        var report = new List<string>();
        for (int pass = 0; pass < 2; pass++)
        {
            foreach ((string name, string scene) in props)
            {
                var watch = Stopwatch.StartNew();
                IReadOnlyList<EngineFile> files = await thread.ExportAsync(scene, "low-poly", Settings(name), cancel: Ct).WaitAsync(Wait, Ct);
                Assert.Contains(files, f => f.Name == $"{name}_LOD0.glb");
                (long external, int now) = await thread.Enqueue(EngineJobKind.Export, (e, _) =>
                {
                    long p = e.PeakExternalBytes;
                    e.ResetPeak();
                    return (p, e.Generation);
                }, Ct).WaitAsync(Wait, Ct);
                Assert.True(external < options.MaxArrayBufferBytes, $"{name}: {external >> 20} MiB of ArrayBuffers, cap {options.MaxArrayBufferBytes >> 20} MiB");
                long rss = Rss();
                recycled |= now > generation;
                generation = now;
                if (recycled)
                {
                    passes[pass].Add(rss);
                }

                report.Add($"{pass + 1}:{name} {watch.ElapsedMilliseconds} ms, {external >> 20} MiB, gen {now}, rss {rss >> 20} MiB");
                TestContext.Current.TestOutputHelper?.WriteLine(report[^1]);
            }
        }

        Assert.True(generation > 2, $"the engine never recycled ({generation}): {string.Join("; ", report)}");
        Assert.True(passes[0].Count >= props.Count / 2, $"the first recycle came late: {string.Join("; ", report)}");
        (double firstMean, double secondMean) = (passes[0].Average(), passes[1].Average());
        (long firstPeak, long secondPeak) = (passes[0].Max(), passes[1].Max());
        string got = $"mean {(long)firstMean >> 20} -> {(long)secondMean >> 20} MiB, peak {firstPeak >> 20} -> {secondPeak >> 20} MiB";
        TestContext.Current.TestOutputHelper?.WriteLine(got);
        Assert.True(secondMean <= firstMean * 1.10, $"RSS grew from the first pass to the second ({got}): {string.Join("; ", report)}");
        Assert.True(secondPeak <= firstPeak * 1.10, $"RSS peaked higher in the second pass ({got}): {string.Join("; ", report)}");
    }

    [Fact]
    public async Task TheLargestExportAt2048FitsTheBufferCap()
    {
        // The painted-tree preset with the most parts (ancient on v0.4.5), with a full load of fruit and snow, at
        // 2048: its peak ArrayBuffer memory must leave the cap room, and fit the room an export is given
        // (EngineOptions.ExportHeadroomBytes). Measured on v0.4.5: see Margin.
        Assert.SkipUnless(EngineOptions.HasEmbeddedBundle, "Resources/engine.js is not built yet (tools/engine).");
        var options = new EngineOptions();
        using var thread = new EngineThread(options);
        using var rules = new RulesEngine();
        EnginePreset biggest = rules.Presets(Ct).Presets
            .Where(p => p.GeneratorId == "painted-tree")
            .MaxBy(p => JsonNode.Parse(rules.Components(p.GeneratorId, p.Parameters.GetRawText(), p.Seed))!.AsArray().Count)!;
        string scene = With(biggest, ("fruitCount", 12), ("fruitKind", "apple"), ("snow", 1));

        IReadOnlyList<EngineFile> files = await thread.ExportAsync(scene, "low-poly", Settings("biggest"), cancel: Ct).WaitAsync(Wait, Ct);
        Assert.Contains(files, f => f.Name == "biggest_LOD0.glb");
        long peak = await thread.Enqueue(EngineJobKind.Export, static (e, _) => e.PeakExternalBytes, Ct).WaitAsync(Wait, Ct);
        TestContext.Current.TestOutputHelper?.WriteLine($"{biggest.Id} with fruit and snow at 2048: peak {peak >> 20} MiB of ArrayBuffers");
        Assert.True(peak > 0);
        Assert.True(peak <= options.MaxArrayBufferBytes * Margin, $"{biggest.Id}: peak {peak >> 20} MiB, more than {Margin:P0} of the {options.MaxArrayBufferBytes >> 20} MiB cap");
        Assert.True(peak <= options.ExportHeadroomBytes, $"{biggest.Id}: peak {peak >> 20} MiB, more than the {options.ExportHeadroomBytes >> 20} MiB an export is given");
    }

    [Fact]
    public async Task APieceAt2048FitsTheRoomAnExportIsGiven()
    {
        // A piece prints bark and the cut ends' end grain with their reliefs: at 2048 a stump peaks near 290 MiB of
        // ArrayBuffers on a fresh runtime (more than the largest tree, ancient, at 141 MiB), and a little over 310 MiB
        // after a tree's export left its pictures cached. Each piece, twice over in one runtime, exports without
        // running out of buffer room, and within the room an export is given.
        Assert.SkipUnless(EngineOptions.HasEmbeddedBundle, "Resources/engine.js is not built yet (tools/engine).");
        var options = new EngineOptions();
        using var thread = new EngineThread(options);
        EnginePresets presets = await thread.PresetsAsync(Ct).WaitAsync(Wait, Ct);
        string oak = EngineFixture.SceneOf(presets.Presets.Single(p => p.Id == "oak"));
        var pieces = new List<(string Name, string Scene)> { ("oak", oak) };
        foreach (string kind in (string[])["stump", "log", "branch"])
        {
            EnginePiece piece = await thread.MakePieceAsync(oak, "Oak", kind, "low-poly", Ct).WaitAsync(Wait, Ct);
            pieces.Add(($"oak_{kind}", piece.Scene));
        }

        foreach ((string name, string scene) in pieces.Concat(pieces))
        {
            _ = await thread.Enqueue(EngineJobKind.Export, static (e, _) =>
            {
                e.ResetPeak();
                return 0;
            }, Ct).WaitAsync(Wait, Ct);
            IReadOnlyList<EngineFile> files = await thread.ExportAsync(scene, "low-poly", Settings(name), cancel: Ct).WaitAsync(Wait, Ct);
            Assert.Contains(files, f => f.Name == $"{name}_LOD0.glb");
            long peak = await thread.Enqueue(EngineJobKind.Export, static (e, _) => e.PeakExternalBytes, Ct).WaitAsync(Wait, Ct);
            TestContext.Current.TestOutputHelper?.WriteLine($"{name} at 2048: peak {peak >> 20} MiB of ArrayBuffers");
            Assert.True(peak <= options.MaxArrayBufferBytes * Margin, $"{name}: peak {peak >> 20} MiB, more than {Margin:P0} of the {options.MaxArrayBufferBytes >> 20} MiB cap");
            Assert.True(peak <= options.ExportHeadroomBytes, $"{name}: peak {peak >> 20} MiB, more than the {options.ExportHeadroomBytes >> 20} MiB an export is given");
        }
    }

    [Fact]
    public async Task MakePieceIsCancellableBetweenKinds()
    {
        // One kind per job: a cancel after the first piece stops the kinds still queued, and the engine goes on.
        Assert.SkipUnless(EngineOptions.HasEmbeddedBundle, "Resources/engine.js is not built yet (tools/engine).");
        using var thread = new EngineThread();
        EnginePresets presets = await thread.PresetsAsync(Ct).WaitAsync(Wait, Ct);
        string oak = EngineFixture.SceneOf(presets.Presets.Single(p => p.Id == "oak"));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var gate = new ManualResetEventSlim();

        Task<EnginePiece> stump = thread.MakePieceAsync(oak, "Oak", "stump", "low-poly", cancel.Token);
        Task<int> held = thread.Enqueue(EngineJobKind.Export, (_, token) =>
        {
            gate.Wait(Wait, token);
            return 0;
        }, Ct);
        Task<EnginePiece>[] rest = [.. LaterKinds.Select(kind => thread.MakePieceAsync(oak, "Oak", kind, "low-poly", cancel.Token))];

        Assert.Equal("stump", (await stump.WaitAsync(Wait, Ct)).Kind);
        await cancel.CancelAsync();
        gate.Set();
        await held.WaitAsync(Wait, Ct);
        foreach (Task<EnginePiece> piece in rest)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => piece.WaitAsync(Wait, Ct));
        }

        Assert.Equal("log", (await thread.MakePieceAsync(oak, "Oak", "log", "low-poly", Ct).WaitAsync(Wait, Ct)).Kind);
    }

    /// <summary>
    /// Peak ArrayBuffer bytes of the largest export at 2048, as a share of the cap: measured on v0.4.5 141 MiB for the
    /// largest tree (ancient, 12 apples, snow 1) and about 310 MiB for a stump after the oak (of 512, 61 %), so 75 %
    /// leaves room for upstream to grow a little before this fails.
    /// </summary>
    private const double Margin = 0.75;

    private static string Settings(string name) =>
        $$"""{"name":"{{name}}","engine":"godot","levels":3,"collision":true,"wind":true,"far":false,"look":"lit","materials":false,"pictureSize":2048}""";

    private static string With(EnginePreset preset, params (string Key, object Value)[] values)
    {
        JsonObject scene = JsonNode.Parse(EngineFixture.SceneOf(preset))!.AsObject();
        foreach ((string key, object value) in values)
        {
            scene["parameters"]![key] = value switch
            {
                string s => JsonValue.Create(s),
                int i => JsonValue.Create(i),
                double d => JsonValue.Create(d),
                _ => throw new ArgumentException(value.GetType().Name),
            };
        }

        return scene.ToJsonString();
    }

    private static long Rss()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        foreach (string line in OperatingSystem.IsLinux() ? File.ReadLines("/proc/self/status") : [])
        {
            if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
            {
                return long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture) << 10;
            }
        }

        using var self = Process.GetCurrentProcess();
        return self.WorkingSet64;
    }

    /// <summary>Progress reports, kept in order on the thread that reports them.</summary>
    private sealed class Recorded : IProgress<double>
    {
        private readonly List<double> _values = [];

        public IReadOnlyList<double> Values
        {
            get
            {
                lock (_values)
                {
                    return [.. _values];
                }
            }
        }

        public void Report(double value)
        {
            lock (_values)
            {
                _values.Add(value);
            }
        }
    }

    /// <summary>A progress report that blocks until its gate opens.</summary>
    private sealed class Stuck(ManualResetEventSlim reported, ManualResetEventSlim gate) : IProgress<double>
    {
        public void Report(double value)
        {
            reported.Set();
            gate.Wait(TimeSpan.FromSeconds(60));
        }
    }

    /// <summary>An export job holding the engine thread until released, so the jobs queued after it wait.</summary>
    private sealed class Blocker : IDisposable
    {
        private readonly ManualResetEventSlim _started = new();
        private readonly ManualResetEventSlim _gate = new();

        private Blocker(EngineThread thread, CancellationToken cancel) =>
            Job = thread.Enqueue(EngineJobKind.Export, (_, token) =>
            {
                _started.Set();
                _gate.Wait(Wait, token);
                return 0;
            }, cancel);

        public Task<int> Job { get; }

        public static Blocker Start(EngineThread thread, CancellationToken cancel)
        {
            var blocker = new Blocker(thread, cancel);
            Assert.True(blocker._started.Wait(Wait, cancel));
            return blocker;
        }

        public void Release() => _gate.Set();

        public void Dispose()
        {
            _gate.Set();
            _started.Dispose();
            _gate.Dispose();
        }
    }
}
