namespace PropGenerator;

/// <summary>Picks how the painter's worker is started on this machine.</summary>
public static class WorkerLaunchers
{
    /// <summary>
    /// On Linux with a systemd user session, a systemd scope capped at 2 GB (<see cref="SystemdWorkerLauncher"/>);
    /// on Windows, a plain process in a Job Object capped at 2 GB; on Linux without systemd (some distributions, a
    /// container), a plain process without a cap. Each runs <see cref="PainterPython.Find(string)"/>'s Python.
    /// </summary>
    /// <param name="baseDir">The app's folder (where a bundled python/ would be).</param>
    /// <returns>The launcher.</returns>
    public static IWorkerLauncher ForThisMachine(string baseDir) =>
        ForThisMachine(baseDir, OperatingSystem.IsWindows(), !OperatingSystem.IsWindows() && HasSystemdUserSession(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")));

    /// <summary>As <see cref="ForThisMachine(string)"/>, for the platform given.</summary>
    /// <param name="baseDir">The app's folder.</param>
    /// <param name="windows">True on Windows.</param>
    /// <param name="systemd">True when a systemd user session can make scopes (ignored on Windows).</param>
    /// <returns>The launcher.</returns>
    internal static IWorkerLauncher ForThisMachine(string baseDir, bool windows, bool systemd)
    {
        string python = PainterPython.Find(baseDir, windows);
        return !windows && systemd ? new SystemdWorkerLauncher(python) : new ProcessWorkerLauncher(python);
    }

    /// <summary>
    /// Whether <c>systemd-run --user</c> can work: the user's service manager listens on
    /// $XDG_RUNTIME_DIR/systemd/private, and systemd-run is on PATH.
    /// </summary>
    /// <param name="runtimeDir">$XDG_RUNTIME_DIR, or null when it is not set.</param>
    /// <returns>True when the worker can be started in a systemd scope.</returns>
    internal static bool HasSystemdUserSession(string? runtimeDir)
    {
        if (string.IsNullOrEmpty(runtimeDir) || !new FileInfo(Path.Combine(runtimeDir, "systemd", "private")).Exists)
        {
            return false;
        }

        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(d => File.Exists(Path.Combine(d, "systemd-run")));
    }
}
