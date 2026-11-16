using System.Collections.Concurrent;
using System.Diagnostics;

namespace PropGenerator.Tests.Support;

/// <summary>A launcher that remembers the PID of every process it starts.</summary>
internal sealed class RecordingLauncher(Func<string, Process> start) : IWorkerLauncher
{
    private readonly ConcurrentQueue<int> _pids = new();

    public IReadOnlyList<int> Pids => [.. _pids];

    /// <summary>Starts this command line (all three streams redirected) whatever worker.py it is given.</summary>
    public static RecordingLauncher Command(string file, params string[] args) => new(_ => Process.Start(Redirected(file, args))!);

    /// <summary>Starts the real worker uncapped, as the tests' own python3.</summary>
    public static RecordingLauncher RealWorker() => new(py => Process.Start(Redirected("python3", "-I", py))!);

    /// <summary>Records what another launcher (the real systemd one) starts.</summary>
    public static RecordingLauncher Wrapping(IWorkerLauncher inner) => new(inner.Start);

    public static ProcessStartInfo Redirected(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        return psi;
    }

    public Process Start(string workerPy)
    {
        Process p = start(workerPy);
        _pids.Enqueue(p.Id);
        return p;
    }
}
