using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Look;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Engine;

namespace PropGenerator.Desktop;

/// <summary>The Avalonia application: the Fluent theme in dark, the colour picker's theme, and the main window.</summary>
public sealed class App : Application
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://PropGenerator/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"),
        });
        RequestedThemeVariant = ThemeVariant.Dark;
        LookStyles.Apply(this);
    }

    /// <summary>Opens the main window with the real painter (not in the headless tests, which open their own).</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            TraceLog.Write("launch: framework ready");
            string[] args = desktop.Args ?? [];
            string? prefsPath = Option(args, "--prefs");
            string? libraryPath = Option(args, "--library");
            var splash = new Splash();
            desktop.MainWindow = splash;
            splash.Show();
            splash.Show("Reading settings and presets", 0.1);

            // The engine ends with the app: its queue cancelled, and its thread given 5 s to stop.
            desktop.Exit += (_, _) => EngineHost.Default.Shutdown(TimeSpan.FromSeconds(5));

            // The files and easy-props are read off the UI thread while the loading window draws; the window is built
            // on it. Load carries an engine failure instead of throwing it, so the loading window always moves on.
            var progress = new Progress<(string Step, double Share)>(p => splash.Show(p.Step, p.Share));
            _ = Task.Run(() => Load(prefsPath, libraryPath, progress, EngineHost.Default)).ContinueWith(
                loaded => Dispatcher.UIThread.Post(
                    () => _ = OpenAsync(desktop, splash, loaded.Result),
                    DispatcherPriority.Background),
                TaskScheduler.Default);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Reads everything the main window needs from disk and from easy-props before it is built, off the UI thread.
    /// </summary>
    /// <param name="prefsPath">The prefs file, or null for the user's own.</param>
    /// <param name="libraryPath">The library file, or null for the user's own.</param>
    /// <param name="progress">Gets each step and how far along the launch is (the loading window shows it).</param>
    /// <param name="host">The engine host easy-props is read from.</param>
    /// <returns>What was read; an engine that could not be loaded is carried in <see cref="Loaded.EngineFailure"/>.</returns>
    internal static Loaded Load(string? prefsPath, string? libraryPath, IProgress<(string Step, double Share)> progress, EngineHost host)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(host);
        string painterDir = Path.Combine(AppContext.BaseDirectory, "painter");
        IReadOnlyList<ParamSpec> specs = ParamSchema.Load(Path.Combine(painterDir, "propgen_painter", "floor_params.json"));
        var prefs = new Prefs(prefsPath ?? Prefs.DefaultPath());
        var library = new Library(libraryPath ?? Library.DefaultPath());

        // easy-props' own code: its rules runtime, its info and its presets (the drawer and the panel read them).
        progress.Report(("Loading easy-props", 0.15));
        TraceLog.Write("launch: loading easy-props");
        Exception? failure = null;
        try
        {
            EngineCatalog catalog = host.Catalog;
            _ = catalog.Presets;

            // Each kind's parameters too (the drawer's starting points and the panel read them): a kind that cannot be
            // read leaves only our own kinds, as a failed load does, instead of failing later.
            foreach (EngineKind kind in EngineKinds.All)
            {
                _ = catalog.Parameters(kind.Id);
            }

            TraceLog.Write("launch: easy-props loaded");
        }
        catch (Exception e) when (IsEngineFailure(e))
        {
            failure = e;
            host.LoadFailure = e;
            TraceLog.Write($"launch: easy-props could not be loaded: {e.Message}");
        }

        // The drawer's starting points read the presets; reading them here keeps that off the UI thread. A read that
        // fails is only a missed head start: the drawer asks again.
        progress.Report(("Reading the presets", 0.25));
        try
        {
            _ = MeshRecipes.All.Sum(r => r.StartingPoints.Count);
        }
        catch (Exception e) when (IsEngineFailure(e))
        {
            TraceLog.Write($"launch: the starting points could not be read ahead: {e.Message}");
        }

        TraceLog.Write("launch: loaded");
        return new Loaded(painterDir, specs, prefs, library, failure);
    }

    /// <summary>Whether an exception is one easy-props' engine throws when it cannot load or answer (D10), or after the host shut down.</summary>
    /// <param name="e">The exception.</param>
    /// <returns>True for an engine failure the launch carries instead of throwing.</returns>
    internal static bool IsEngineFailure(Exception e) =>
        e is EngineException or TimeoutException or OperationCanceledException or ObjectDisposedException
            or DllNotFoundException or TypeInitializationException or TypeLoadException or IOException;

    /// <summary>Says once, on the window's status line, that easy-props' own code could not be loaded and only our own kinds are offered.</summary>
    /// <param name="window">The main window.</param>
    /// <param name="failure">Why the engine failed to load; null says nothing.</param>
    internal static void SayEngineFailure(MainWindow window, Exception? failure)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (failure is not null)
        {
            window.Say($"{MeshRecipes.EngineDown(failure)}: its kinds cannot be made or opened until it loads; slab cliffs and stylized rocks still work.", error: true);
        }
    }

    private static async Task OpenAsync(IClassicDesktopStyleApplicationLifetime desktop, Splash splash, Loaded loaded)
    {
        splash.Show("Building the window", 0.35);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        var painter = new WorkerClient(Path.Combine(loaded.PainterDir, "worker.py"), WorkerLaunchers.ForThisMachine(AppContext.BaseDirectory));

        // The worker ends with the app: closed (and killed if it does not stop) once the window has gone.
        desktop.Exit += (_, _) => painter.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        var window = new MainWindow(painter, loaded.Specs, prefs: loaded.Prefs, library: loaded.Library);
        TraceLog.Write("launch: window built");
        SayEngineFailure(window, loaded.EngineFailure);
        splash.Show(MeshRecipes.Find(window.Files.Recipe) is { } recipe ? $"Building the {recipe.Label.ToLowerInvariant()}" : "Painting the floor", 0.7);
        await Launch.FirstPicture(window, Launch.Patience);
        window.Opened += (_, _) => TraceLog.Write("launch: window opened");
        desktop.MainWindow = window;
        window.Show();
        splash.Close();
        OfferCrashReport(window, Program.Log);
    }

    /// <summary>
    /// After a run that ended badly, asks whether to send its report to the team; nothing is offered without a team
    /// server, and the question is asked once.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="log">This run's log, or null.</param>
    internal static void OfferCrashReport(MainWindow window, Crash.RunLog? log)
    {
        if (log?.Crashed is not { } crashed || window.Team.Server is null)
        {
            return;
        }

        log.CrashDealtWith();
        Crash.CrashReport report;
        try
        {
            report = Crash.CrashReport.Of(crashed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (report.Zip.Length > Crash.CrashReport.MaxZip)
        {
            return;  // ponytail: a 2 MB log zips far below 20 MB; this never happens, so nothing is offered.
        }

        Crash.CrashCard? card = null;
        card = new Crash.CrashCard(report, window.Team.SendCrashAsync, (said, error) =>
        {
            if (ReferenceEquals(window.Cards.Shown, card))
            {
                window.Cards.Close();
            }

            window.Say(said, error);
        });
        window.Cards.Show(card);
    }

    /// <summary>What the launch read before the main window is built.</summary>
    /// <param name="PainterDir">The painter's folder beside the app.</param>
    /// <param name="Specs">The floor painter's settings list.</param>
    /// <param name="Prefs">The remembered folders and choices.</param>
    /// <param name="Library">The files opened before.</param>
    /// <param name="EngineFailure">Why easy-props' engine could not be loaded (only our own kinds are then offered); null when it loaded.</param>
    internal sealed record Loaded(string PainterDir, IReadOnlyList<ParamSpec> Specs, Prefs Prefs, Library Library, Exception? EngineFailure);

    // The value after a command-line option such as --prefs (the soak test uses scratch files, not the user's own).
    private static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
