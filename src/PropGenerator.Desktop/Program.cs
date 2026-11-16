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
        TraceLog.Write("launch: main");
        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Quit);
        using var hup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, Quit);
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace(Avalonia.Logging.LogEventLevel.Warning)

            // Linux: Avalonia turns OpenGL off for software renderers (Mesa's llvmpipe); the 3D view needs it.
            .With(new X11PlatformOptions { GlxRendererBlacklist = [] })
            .StartWithClassicDesktopLifetime(args);
    }

    private static void Quit(PosixSignalContext c)
    {
        c.Cancel = true;
        Dispatcher.UIThread.Post(() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
    }
}
