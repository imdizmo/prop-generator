using System.Globalization;

namespace PropGenerator.Desktop.Crash;

/// <summary>
/// The log of one run of the app, in ~/.config/prop-generator/logs/: every status and trace line, every unhandled
/// error with its stack, and a memory line each minute. A "running" marker names the log while the app runs and is
/// removed by a clean exit, so a marker found at start means the last run ended badly (an error, a crash in native
/// code, a kill by the memory cap): <see cref="Crashed"/> is then that run's log. Each log stops at 2 MB; the newest 5
/// are kept (and always the crashed one).
/// </summary>
internal sealed class RunLog : IDisposable
{
    /// <summary>The most bytes one run's log holds.</summary>
    internal const long MaxBytes = 2L * 1024 * 1024;

    /// <summary>How many logs are kept.</summary>
    internal const int Keep = 5;

    private const string MarkerName = "running";
    private readonly Lock _gate = new();
    private readonly string _marker;
    private StreamWriter? _out;
    private readonly Timer? _memory;

    /// <summary>Starts a run's log, and finds out whether the last run ended badly.</summary>
    /// <param name="dir">The logs folder.</param>
    /// <param name="memoryEvery">How often the memory line is written; null for never (the tests).</param>
    internal RunLog(string dir, TimeSpan? memoryEvery = null)
    {
        Directory.CreateDirectory(dir);
        _marker = System.IO.Path.Combine(dir, MarkerName);
        if (File.Exists(_marker))
        {
            string? last = File.ReadAllText(_marker).Trim();
            Crashed = last.Length > 0 && File.Exists(last) ? last : null;
        }

        Path = System.IO.Path.Combine(dir, string.Create(CultureInfo.InvariantCulture, $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Random.Shared.Next(0x10000):x4}.log"));
        _out = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        File.WriteAllText(_marker, Path);
        foreach (string old in Directory.GetFiles(dir, "run-*.log").OrderByDescending(File.GetLastWriteTimeUtc).Skip(Keep))
        {
            if (old != Crashed && old != Path)
            {
                File.Delete(old);
            }
        }

        Write($"Prop Generator {Version} on {Os} ({System.Runtime.InteropServices.RuntimeInformation.OSDescription}), memory cap {Cap()}");
        if (memoryEvery is { } every)
        {
            _memory = new Timer(_ => Memory(), null, every, every);
        }
    }

    /// <summary>Gets this run's log.</summary>
    internal string Path { get; }

    /// <summary>Gets the log of the last run when it ended badly, else null.</summary>
    internal string? Crashed { get; private set; }

    /// <summary>Gets the app's version, as x.y.z.</summary>
    internal static string Version { get; } = (typeof(RunLog).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0").Split('+')[0];

    /// <summary>Gets the OS as the team server names it.</summary>
    internal static string Os => OperatingSystem.IsWindows() ? "windows" : "linux";

    /// <summary>The memory cap the Launcher ran the app under (JATIFY_MEMORY_MAX, in bytes), as text.</summary>
    /// <returns>Such as "6144 MB", or "none".</returns>
    internal static string Cap() =>
        long.TryParse(Environment.GetEnvironmentVariable("JATIFY_MEMORY_MAX"), NumberStyles.None, CultureInfo.InvariantCulture, out long cap)
            ? string.Create(CultureInfo.InvariantCulture, $"{cap >> 20} MB") : "none";

    /// <summary>Writes a line, with the time (UTC); nothing once the log is full or closed.</summary>
    /// <param name="line">The line.</param>
    internal void Write(string line)
    {
        lock (_gate)
        {
            if (_out is null)
            {
                return;
            }

            try
            {
                if (_out.BaseStream.Length > MaxBytes)
                {
                    _out.WriteLine("(the log is full: nothing more is written for this run)");
                    _out.Dispose();
                    _out = null;
                    return;
                }

                _out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:HH:mm:ss.fff} {line}"));
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                _out = null;  // A log that cannot be written is no reason to stop the app.
            }
        }
    }

    /// <summary>Writes an error with its type, message and stack (and those of what it wraps).</summary>
    /// <param name="where">Where it was caught, such as "unhandled".</param>
    /// <param name="e">The error.</param>
    internal void Error(string where, Exception e) => Write($"error ({where}): {e}");

    /// <summary>Writes the memory line: the process's working set and its peak, the .NET heap, and the cap.</summary>
    internal void Memory()
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        Write(string.Create(CultureInfo.InvariantCulture, $"memory: working set {self.WorkingSet64 >> 20} MB, peak {self.PeakWorkingSet64 >> 20} MB, .NET heap {GC.GetTotalMemory(false) >> 20} MB, cap {Cap()}"));
    }

    /// <summary>The last run ended badly and has been dealt with (sent or not): it is not offered again.</summary>
    internal void CrashDealtWith() => Crashed = null;

    /// <summary>A clean exit: the marker goes, so the next start does not take this run for a crash.</summary>
    internal void Clean()
    {
        Write("exit: clean");
        try
        {
            File.Delete(_marker);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The next start then offers this run as a crash: the person can say Don't send.
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _memory?.Dispose();
        lock (_gate)
        {
            _out?.Dispose();
            _out = null;
        }
    }
}
