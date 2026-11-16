using System.Diagnostics;

namespace PropGenerator;

/// <summary>
/// Starts the worker in its own systemd scope, capped at 2 GB of memory and no swap:
/// <c>systemd-run --user --scope -q -p MemoryMax=2G -p MemorySwapMax=0 &lt;python&gt; -I &lt;workerPy&gt;</c>.
/// </summary>
/// <param name="python">The Python to run (<see cref="PainterPython.Find(string)"/>); python3 on PATH if null.</param>
/// <remarks>
/// The PID this returns is python's own. <c>systemd-run --scope</c> asks systemd to make the scope, moves itself
/// into it and then execs the command in place, so the process .NET started becomes python (same PID) and its pipes
/// go straight to python. Checked on 2026-11-02 (systemd user manager, Arch-based Linux): a shell's <c>$!</c> for
/// <c>systemd-run --user --scope -q ... python3 -I -c 'print(os.getpid())'</c> equals the PID python prints, and
/// /proc/self/cgroup shows <c>.../app.slice/run-p&lt;that pid&gt;-i&lt;n&gt;.scope</c>. The test
/// TheSystemdLauncherPaintsOnceCappedAtTwoGigabytes checks it on every run: /proc/&lt;pid&gt;/cmdline is
/// <c>python3 -I .../worker.py</c>, the scope's memory.max is 2147483648 and memory.swap.max is 0, and the scope's
/// cgroup is gone once the worker is. So killing that PID (with its process tree) stops everything; there is no
/// separate scope to stop: systemd removes a scope by itself once its last process has ended.
/// </remarks>
public sealed class SystemdWorkerLauncher(string? python = null) : IWorkerLauncher
{
    /// <summary>The Python this launcher runs.</summary>
    public string Python { get; } = python ?? "python3";

    /// <inheritdoc/>
    public Process Start(string workerPy)
    {
        var psi = new ProcessStartInfo("systemd-run")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in (string[])["--user", "--scope", "-q", "-p", "MemoryMax=2G", "-p", "MemorySwapMax=0", Python, "-I", workerPy])
        {
            psi.ArgumentList.Add(a);
        }

        return Process.Start(psi) ?? throw new InvalidOperationException("systemd-run did not start.");
    }
}
