using System.Collections.Concurrent;
using System.Text.Json;
using PropGenerator.Tests.Support;
using Xunit;

namespace PropGenerator.Tests;

public class SystemdWorkerLauncherTests
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

    private static (WorkerClient Client, ConcurrentQueue<string> Failures) Make(IWorkerLauncher launcher)
    {
        var client = new WorkerClient(WorkerProcs.WorkerPy(), launcher);
        var failures = new ConcurrentQueue<string>();
        client.Failed += failures.Enqueue;
        return (client, failures);
    }

    [Fact(Timeout = 60000)]
    public async Task TheSystemdLauncherPaintsOnceCappedAtTwoGigabytes()
    {
        NeedNumpy();
        if (!WorkerProcs.Runs("systemd-run", "--user", "--scope", "-q", "true"))
        {
            Assert.Skip("systemd-run --user is not available.");
        }

        var launcher = RecordingLauncher.Wrapping(new SystemdWorkerLauncher());
        var kids = new List<int>();
        try
        {
            string cgroup;
            int pid;
            (WorkerClient client, ConcurrentQueue<string> failures) = Make(launcher);
            await using (client)
            {
                Layers? layers = await client.PaintAsync(256, Defaults, Ct);
                Assert.Empty(failures);
                Assert.NotNull(layers);
                pid = launcher.Pids[0];
                kids.AddRange(WorkerProcs.Descendants(pid));

                // systemd-run --scope runs the command in its own process: the PID .NET got is python's.
                string cmdline = File.ReadAllText($"/proc/{pid}/cmdline").Replace('\0', ' ');
                Assert.DoesNotContain("systemd-run", cmdline, StringComparison.Ordinal);
                Assert.Matches(@"^(\S*/)?python3(\.\d+)? -I ", cmdline);  // PATH's python3: /usr/bin here, a venv on CI
                Assert.Contains("worker.py", cmdline, StringComparison.Ordinal);

                // The scope's cgroup holds the cap.
                string rel = File.ReadAllText($"/proc/{pid}/cgroup").Trim().Split("::", 2)[1];
                Assert.EndsWith(".scope", rel, StringComparison.Ordinal);
                cgroup = "/sys/fs/cgroup" + rel;
                Assert.Equal("2147483648", File.ReadAllText(Path.Combine(cgroup, "memory.max")).Trim());
                Assert.Equal("0", File.ReadAllText(Path.Combine(cgroup, "memory.swap.max")).Trim());
            }

            Assert.True(await WorkerProcs.Eventually(() => !WorkerProcs.Alive(pid), Gone));
            Assert.True(await WorkerProcs.Eventually(() => !kids.Exists(WorkerProcs.Alive), Gone));
            Assert.True(await WorkerProcs.Eventually(() => !Directory.Exists(cgroup), Gone), $"{cgroup} still there");
        }
        finally
        {
            WorkerProcs.KillLeftovers([.. launcher.Pids, .. kids]);
        }
    }
}
