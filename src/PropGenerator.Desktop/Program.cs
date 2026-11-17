using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace PropGenerator.Desktop;

/// <summary>Starts the app.</summary>
public static class Program
{
    /// <summary>Starts the window. SIGTERM and SIGHUP close it as the window's close button would.</summary>
    /// <param name="args">The command line (--prefs FILE and --library FILE use other files than the user's own, for tests).</param>
    [STAThread]
    public static void Main(string[] args)
    {
        using Crash.RunLog? log = StartLog(args);
        TraceLog.Write("launch: main");
        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Quit);
        using var hup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, Quit);
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace(Avalonia.Logging.LogEventLevel.Warning)

            // Linux: Avalonia turns OpenGL off for software renderers (Mesa's llvmpipe); the 3D view needs it.
            .With(new X11PlatformOptions { GlxRendererBlacklist = [] })
            .StartWithClassicDesktopLifetime(args);
        log?.Clean();
    }

    /// <summary>Gets this run's log (null in the tests, which do not start through Main).</summary>
    internal static Crash.RunLog? Log { get; private set; }

    // The run's log, fed every status and trace line, every unhandled error and a memory line a minute. A log that
    // cannot be started (a read-only config folder) is no reason not to run.
    // A run with --prefs (the soak test, a scratch run) keeps its logs beside those prefs, not in the user's.
    private static Crash.RunLog? StartLog(string[] args)
    {
        try
        {
            int i = Array.IndexOf(args, "--prefs");
            string prefs = i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : Prefs.DefaultPath();
            var log = new Crash.RunLog(Path.Combine(Path.GetDirectoryName(prefs)!, "logs"), TimeSpan.FromMinutes(1));
            TraceLog.Written += log.Write;
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception x)
                {
                    log.Error("unhandled", x);
                }

                log.Memory();
            };
            TaskScheduler.UnobservedTaskException += (_, e) => log.Error("unobserved task", e.Exception);
            Dispatcher.UIThread.UnhandledException += (_, e) => log.Error("window thread", e.Exception);
            Log = log;
            return log;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Quit(PosixSignalContext c)
    {
        c.Cancel = true;
        Dispatcher.UIThread.Post(() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
    }
}
