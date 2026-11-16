using System.Collections.Concurrent;
using System.Text.Json;
using PropGenerator.Tests.Support;
using Xunit;

namespace PropGenerator.Tests;

public sealed class WorkerLaunchersTests : IDisposable
{
    private static readonly Settings Defaults = new(new Dictionary<string, JsonElement>());

    private static readonly TimeSpan Gone = TimeSpan.FromSeconds(3);

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("propgen-launchers-");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _dir.Delete(recursive: true);

    private string Touch(params string[] parts)
    {
        string path = Path.Combine([_dir.FullName, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void TheBundledPythonBesideTheAppComesBeforeThePathsOneOnLinux()
    {
        string bundled = Touch("python", "bin", "python3");
        Assert.Equal(bundled, PainterPython.Find(_dir.FullName, windows: false));
    }

    [Fact]
    public void TheBundledPythonBesideTheAppComesBeforeThePathsOneOnWindows()
    {
        string bundled = Touch("python", "python.exe");
        Assert.Equal(bundled, PainterPython.Find(_dir.FullName, windows: true));
    }

    [Fact]
    public void WithoutABundledPythonThePathsOneIsUsed()
    {
        Assert.Equal("python3", PainterPython.Find(_dir.FullName, windows: false));
        Assert.Equal("python", PainterPython.Find(_dir.FullName, windows: true));

        // The other platform's layout does not count.
        Touch("python", "python.exe");
        Assert.Equal("python3", PainterPython.Find(_dir.FullName, windows: false));
    }

    [Fact]
    public void LinuxWithASystemdUserSessionCapsTheWorkerInASystemdScope()
    {
        string bundled = Touch("python", "bin", "python3");
        IWorkerLauncher launcher = WorkerLaunchers.ForThisMachine(_dir.FullName, windows: false, systemd: true);
        SystemdWorkerLauncher systemd = Assert.IsType<SystemdWorkerLauncher>(launcher);
        Assert.Equal(bundled, systemd.Python);
    }

    [Fact]
    public void LinuxWithoutASystemdUserSessionStartsThePlainProcess()
    {
        IWorkerLauncher launcher = WorkerLaunchers.ForThisMachine(_dir.FullName, windows: false, systemd: false);
        ProcessWorkerLauncher plain = Assert.IsType<ProcessWorkerLauncher>(launcher);
        Assert.Equal("python3", plain.Python);
    }

    [Fact]
    public void WindowsStartsThePlainProcessWithTheBundledPython()
    {
        string bundled = Touch("python", "python.exe");
        IWorkerLauncher launcher = WorkerLaunchers.ForThisMachine(_dir.FullName, windows: true, systemd: true);
        ProcessWorkerLauncher plain = Assert.IsType<ProcessWorkerLauncher>(launcher);
        Assert.Equal(bundled, plain.Python);
        Assert.Equal(2L << 30, plain.MemoryCap);
    }

    [Fact]
    public void ASystemdUserSessionNeedsItsRuntimeDirectorysManagerSocket()
    {
        Assert.False(WorkerLaunchers.HasSystemdUserSession(runtimeDir: null));
        Assert.False(WorkerLaunchers.HasSystemdUserSession(_dir.FullName));
        Touch("systemd", "private");
        Assert.Equal(WorkerProcs.Runs("systemd-run", "--version"), WorkerLaunchers.HasSystemdUserSession(_dir.FullName));
    }

    [Fact(Timeout = 60000)]
    public async Task ThePlainProcessLauncherPaintsAndEndsWithItsClient()
    {
        if (!WorkerProcs.HasNumpy())
        {
            Assert.Skip("python3 with numpy is not available, so the real painter cannot run.");
        }

        var launcher = RecordingLauncher.Wrapping(new ProcessWorkerLauncher("python3"));
        var kids = new List<int>();
        try
        {
            int pid;
            var client = new WorkerClient(WorkerProcs.WorkerPy(), launcher);
            var failures = new ConcurrentQueue<string>();
            client.Failed += failures.Enqueue;
            await using (client)
            {
                Layers? layers = await client.PaintAsync(256, Defaults, Ct);
                Assert.Empty(failures);
                Assert.NotNull(layers);
                pid = launcher.Pids[0];
                kids.AddRange(WorkerProcs.Descendants(pid));
                string cmdline = File.ReadAllText($"/proc/{pid}/cmdline").Replace('\0', ' ');
                Assert.Matches(@"^(/usr/bin/)?python3 -I .*worker\.py", cmdline);
            }

            Assert.True(await WorkerProcs.Eventually(() => !WorkerProcs.Alive(pid), Gone));
            Assert.True(await WorkerProcs.Eventually(() => !kids.Exists(WorkerProcs.Alive), Gone));
        }
        finally
        {
            WorkerProcs.KillLeftovers([.. launcher.Pids, .. kids]);
        }
    }
}
