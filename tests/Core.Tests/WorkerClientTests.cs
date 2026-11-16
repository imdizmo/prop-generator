using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using PropGenerator.Tests.Support;
using Xunit;

namespace PropGenerator.Tests;

public class WorkerClientTests
{
    private static readonly Settings Defaults = new(new Dictionary<string, JsonElement>());

    private static readonly TimeSpan Gone = TimeSpan.FromSeconds(3);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void NeedNumpy()
    {
        if (!WorkerProcs.HasNumpy())
        {
            Assert.Skip("python3 with numpy is not available, so the real painter cannot run.");
        }
    }

    private static string ScratchFile(string ext)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "prop-generator", "scratch");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{Guid.NewGuid():N}{ext}");
    }

    private static (WorkerClient Client, ConcurrentQueue<string> Failures) Make(
        IWorkerLauncher launcher, TimeSpan? window = null, TimeSpan? replyTimeout = null)
    {
        var client = new WorkerClient(WorkerProcs.WorkerPy(), launcher, window, replyTimeout);
        var failures = new ConcurrentQueue<string>();
        client.Failed += failures.Enqueue;
        return (client, failures);
    }

    /// <summary>Starts support/fake_worker.py in modes[0] first, then modes[1], ...; the last mode repeats.</summary>
    private static RecordingLauncher Fake(params string[] modes)
    {
        string fake = Path.Combine(WorkerProcs.RepoRoot(), "tests", "Core.Tests", "Support", "fake_worker.py");
        int starts = -1;
        return new RecordingLauncher(_ =>
        {
            string mode = modes[Math.Min(Interlocked.Increment(ref starts), modes.Length - 1)];
            return Process.Start(RecordingLauncher.Redirected("python3", "-I", fake, mode))!;
        });
    }

    /// <summary>
    /// One paint against a worker that goes wrong once (first mode) and then works ("ok"): one Failed naming what went
    /// wrong, a second worker that paints, and the first one gone.
    /// </summary>
    private static async Task OneFailureThenARestartThatPaints(string mode, string expected)
    {
        RecordingLauncher launcher = Fake(mode, "ok");
        try
        {
            (WorkerClient client, ConcurrentQueue<string> failures) = Make(launcher, replyTimeout: TimeSpan.FromSeconds(1));
            await using (client)
            {
                Task<Layers?> paint = client.PaintAsync(256, Defaults, Ct);
                Assert.Same(paint, await Task.WhenAny(paint, Task.Delay(8000, Ct)));
                Layers? layers = await paint;
                Assert.Single(failures);
                Assert.Contains(expected, failures.First(), StringComparison.Ordinal);
                Assert.Equal(2, launcher.Pids.Count);
                Assert.NotNull(layers);
                Assert.Equal(256, layers.Size);
                int first = launcher.Pids[0];
                Assert.True(await WorkerProcs.Eventually(() => !WorkerProcs.Alive(first), Gone), $"/proc/{first} still there");
            }
        }
        finally
        {
            WorkerProcs.KillLeftovers(launcher.Pids);
        }
    }

    [Fact(Timeout = 20000)]
    public Task AWorkerThatReadsTheRequestAndSaysNothingIsKilledAndRestarted() =>
        OneFailureThenARestartThatPaints("stall-read", "it did not finish its reply within 1 s");

    [Fact(Timeout = 20000)]
    public Task AWorkerThatStallsAfterTheHeaderIsKilledAndRestarted() =>
        OneFailureThenARestartThatPaints("stall-header", "it did not finish its reply within 1 s");

    [Fact(Timeout = 20000)]
    public Task AMalformedHeaderKillsAndRestartsTheWorker() =>
        OneFailureThenARestartThatPaints("bad-header", "The painter stopped");

    [Fact(Timeout = 20000)]
    public Task AMemoryErrorReplyRestartsTheWorker() =>
        OneFailureThenARestartThatPaints("memory-error", "MemoryError");

    [Fact(Timeout = 20000)]
    public async Task DisposeKillsAWorkerThatIgnoresItsClosedInputAndItsChildren()
    {
        RecordingLauncher launcher = Fake("ignore-eof");
        var kids = new List<int>();
        try
        {
            (WorkerClient client, ConcurrentQueue<string> failures) = Make(launcher);
            Task<Layers?> paint = client.PaintAsync(256, Defaults, Ct);
            Assert.True(await WorkerProcs.Eventually(() => launcher.Pids.Count == 1, Gone));
            int pid = launcher.Pids[0];
            Assert.True(await WorkerProcs.Eventually(() => WorkerProcs.Descendants(pid).Count > 0, Gone), "no child started");
            kids.AddRange(WorkerProcs.Descendants(pid));

            var sw = Stopwatch.StartNew();
            await client.DisposeAsync();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4.5), $"dispose took {sw.Elapsed}");
            Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(1.9), $"dispose did not wait its 2 s: {sw.Elapsed}");
            Assert.True(await WorkerProcs.Eventually(() => !WorkerProcs.Alive(pid), Gone), $"/proc/{pid} still there");
            Assert.True(
                await WorkerProcs.Eventually(() => !kids.Exists(WorkerProcs.Alive), Gone),
                $"its child is still there: {string.Join(' ', kids.Where(WorkerProcs.Alive))}");
            Assert.Null(await paint);
            Assert.Empty(failures);
        }
        finally
        {
            WorkerProcs.KillLeftovers([.. launcher.Pids, .. kids]);
        }
    }

    [Fact(Timeout = 120000)]
    public async Task TwentyRapidPaintsMakeAtMostTwoPaintsAndTheLastCallerGetsLayers()
    {
        NeedNumpy();
        string count = ScratchFile(".count");
        string counting = Path.Combine(WorkerProcs.RepoRoot(), "tests", "Core.Tests", "Support", "counting_worker.py");
        var launcher = new RecordingLauncher(py => Process.Start(RecordingLauncher.Redirected("python3", "-I", counting, py, count))!);
        var kids = new List<int>();
        try
        {
            (WorkerClient client, ConcurrentQueue<string> failures) = Make(launcher);
            await using (client)
            {
                var calls = new List<Task<Layers?>>();
                for (int i = 0; i < 20; i++)
                {
                    calls.Add(client.PaintAsync(256, Defaults, Ct));
                }

                Layers?[] results = await Task.WhenAll(calls);
                kids.AddRange(WorkerProcs.Descendants(launcher.Pids[0]));
                Assert.Empty(failures);
                Assert.NotNull(results[^1]);
                Assert.Equal(256, results[^1]!.Size);
                // The one in flight when the rest came in, and the last; every replaced caller got null.
                int paints = File.ReadAllLines(count).Length;
                Assert.InRange(paints, 1, 2);
                Assert.Equal(paints, results.Count(r => r is not null));
                Assert.Single(launcher.Pids);
            }
        }
        finally
        {
            WorkerProcs.KillLeftovers([.. launcher.Pids, .. kids]);
            File.Delete(count);
        }
    }

    [Fact(Timeout = 120000)]
    public async Task KillingTheWorkerMidPaintRaisesFailedAndTheNextPaintSucceeds()
    {
        NeedNumpy();
        var launcher = RecordingLauncher.RealWorker();
        var kids = new List<int>();
        try
        {
            (WorkerClient client, ConcurrentQueue<string> failures) = Make(launcher);
            await using (client)
            {
                Assert.NotNull(await client.PaintAsync(256, Defaults, Ct));
                int first = launcher.Pids[0];
                kids.AddRange(WorkerProcs.Descendants(first));
                Task<Layers?> killed = client.PaintAsync(1024, Defaults, Ct);
                await Task.Delay(100, Ct);
                using (var p = Process.GetProcessById(first))
                {
                    p.Kill();
                }

                await killed;
                Assert.Single(failures);
                Assert.True(await WorkerProcs.Eventually(() => !WorkerProcs.Alive(first), Gone));

                Layers? next = await client.PaintAsync(256, Defaults, Ct);
                Assert.NotNull(next);
                Assert.Single(failures);
                Assert.Equal(2, launcher.Pids.Count);
                kids.AddRange(WorkerProcs.Descendants(launcher.Pids[1]));
            }

            Assert.True(await WorkerProcs.Eventually(() => !kids.Exists(WorkerProcs.Alive), Gone));
        }
        finally
        {
            WorkerProcs.KillLeftovers([.. launcher.Pids, .. kids]);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task AWorkerThatExitsAtOnceFailsTwiceThenStopsUntilTheNextPaint()
    {
        var launcher = RecordingLauncher.Command("python3", "-c", "import sys; sys.exit(3)");
        try
        {
            (WorkerClient client, ConcurrentQueue<string> failures) = Make(launcher);
            await using (client)
            {
                Task<Layers?> paint = client.PaintAsync(256, Defaults, Ct);
                Assert.Same(paint, await Task.WhenAny(paint, Task.Delay(5000, Ct)));
                Assert.Null(await paint);
                Assert.Equal(2, failures.Count);
                Assert.Contains("3", failures.First(), StringComparison.Ordinal);
                Assert.Equal(2, launcher.Pids.Count);

                // Stopped: nothing restarts on its own.
                await Task.Delay(300, Ct);
                Assert.Equal(2, launcher.Pids.Count);

                // The next paint is the user's action: one more try, and with the last failure under 10 s ago no
                // restart after it.
                paint = client.PaintAsync(256, Defaults, Ct);
                Assert.Same(paint, await Task.WhenAny(paint, Task.Delay(5000, Ct)));
                Assert.Null(await paint);
                Assert.Equal(3, failures.Count);
                Assert.Equal(3, launcher.Pids.Count);
            }

            foreach (int pid in launcher.Pids)
            {
                Assert.False(WorkerProcs.Alive(pid));
            }
        }
        finally
        {
            WorkerProcs.KillLeftovers(launcher.Pids);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task AWorkerThatCannotStartFailsTwiceAndDoesNotHang()
    {
        var launcher = RecordingLauncher.Command("no-such-python-for-prop-generator");
        (WorkerClient client, ConcurrentQueue<string> failures) = Make(launcher);
        await using (client)
        {
            Task<Layers?> paint = client.PaintAsync(256, Defaults, Ct);
            Assert.Same(paint, await Task.WhenAny(paint, Task.Delay(5000, Ct)));
            Assert.Null(await paint);
            Assert.Equal(2, failures.Count);
            Assert.StartsWith("The painter could not start", failures.First(), StringComparison.Ordinal);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task FailuresFurtherApartThanTheWindowKeepRestarting()
    {
        var launcher = RecordingLauncher.Command("python3", "-c", "import sys; sys.exit(3)");
        try
        {
            (WorkerClient client, ConcurrentQueue<string> failures) = Make(launcher, TimeSpan.Zero);
            await using (client)
            {
                Assert.Null(await client.PaintAsync(256, Defaults, Ct));
                Assert.Null(await client.PaintAsync(256, Defaults, Ct));

                // Each paint: the first try and one restart, never more.
                Assert.Equal(4, failures.Count);
                Assert.Equal(4, launcher.Pids.Count);
            }
        }
        finally
        {
            WorkerProcs.KillLeftovers(launcher.Pids);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task AnErrorReplyRaisesFailedAndTheWorkerKeepsPainting()
    {
        NeedNumpy();
        var launcher = RecordingLauncher.RealWorker();
        try
        {
            (WorkerClient client, ConcurrentQueue<string> failures) = Make(launcher);
            await using (client)
            {
                Settings bad = Defaults.With("no_such_setting", 1);
                Assert.Null(await client.PaintAsync(256, bad, Ct));
                Assert.Single(failures);
                Assert.Contains("no_such_setting", failures.First(), StringComparison.Ordinal);
                Assert.NotNull(await client.PaintAsync(256, Defaults, Ct));
                Assert.Single(launcher.Pids);
            }

            Assert.True(await WorkerProcs.Eventually(() => !WorkerProcs.Alive(launcher.Pids[0]), Gone));
        }
        finally
        {
            WorkerProcs.KillLeftovers(launcher.Pids);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task ACancelledWaitingPaintIsCancelledAndTheOneInFlightStillFinishes()
    {
        NeedNumpy();
        var launcher = RecordingLauncher.RealWorker();
        var kids = new List<int>();
        try
        {
            (WorkerClient client, _) = Make(launcher);
            await using (client)
            {
                Task<Layers?> inFlight = client.PaintAsync(256, Defaults, Ct);
                await Task.Delay(50, Ct);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
                Task<Layers?> waiting = client.PaintAsync(256, Defaults, cts.Token);
                await cts.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
                Assert.NotNull(await inFlight);
                kids.AddRange(WorkerProcs.Descendants(launcher.Pids[0]));
            }
        }
        finally
        {
            WorkerProcs.KillLeftovers([.. launcher.Pids, .. kids]);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task DisposeMidPaintLeavesNoProcessBehind()
    {
        NeedNumpy();
        var launcher = RecordingLauncher.RealWorker();
        var kids = new List<int>();
        try
        {
            (WorkerClient client, ConcurrentQueue<string> failures) = Make(launcher);
            Assert.NotNull(await client.PaintAsync(256, Defaults, Ct));
            int pid = launcher.Pids[0];
            kids.AddRange(WorkerProcs.Descendants(pid));
            Assert.NotEmpty(kids);

            Task<Layers?> paint = client.PaintAsync(1024, Defaults, Ct);
            await Task.Delay(50, Ct);
            var sw = Stopwatch.StartNew();
            await client.DisposeAsync();
            Assert.True(await WorkerProcs.Eventually(() => !WorkerProcs.Alive(pid), Gone), $"/proc/{pid} still there");
            Assert.True(sw.Elapsed < Gone, $"dispose took {sw.Elapsed}");
            Assert.True(
                await WorkerProcs.Eventually(() => !kids.Exists(WorkerProcs.Alive), Gone),
                $"painting processes still there: {string.Join(' ', kids.Where(WorkerProcs.Alive))}");
            Assert.Null(await paint);
            Assert.Empty(failures);

            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.PaintAsync(256, Defaults, Ct));
            await client.DisposeAsync();
            Assert.Single(launcher.Pids);
        }
        finally
        {
            WorkerProcs.KillLeftovers([.. launcher.Pids, .. kids]);
        }
    }
}
