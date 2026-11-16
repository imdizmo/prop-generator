using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>Helpers for driving the window headless.</summary>
internal static class Ui
{
    /// <summary>The settings list the app ships with (copied beside the tests from painter/).</summary>
    /// <returns>Every setting.</returns>
    public static IReadOnlyList<ParamSpec> Specs() =>
        ParamSchema.Load(Path.Combine(AppContext.BaseDirectory, "painter", "propgen_painter", "floor_params.json"));

    /// <summary>Opens the main window with a fake painter and lets it lay out.</summary>
    /// <param name="painter">The fake painter.</param>
    /// <returns>The shown window.</returns>
    public static MainWindow Open(FakePainter painter) => Open(painter, new FakeDialogs(), NewScratch());

    /// <summary>
    /// Opens the main window with a fake painter and fake dialogs; prefs and library live in the scratch folder, the
    /// pictures in a scratch folder shared by the run.
    /// </summary>
    /// <param name="painter">The fake painter.</param>
    /// <param name="dialogs">The fake dialogs.</param>
    /// <param name="scratch">Where the prefs and the library are kept.</param>
    /// <returns>The shown window.</returns>
    /// <param name="bridgeRoot">The folder with inbox and outbox (the scratch folder's own "bridge" when null).</param>
    public static MainWindow Open(FakePainter painter, FakeDialogs dialogs, Scratch scratch, string? bridgeRoot = null)
    {
        var w = new MainWindow(
            painter, Specs(), dialogs, new Prefs(scratch.P("prefs.json")), new Library(scratch.P("library.json")),
            bridgeRoot ?? scratch.P("bridge"), Thumbs())
        {
            Width = 1400,
            Height = 900,
        };
        w.Files.GroundworkData = scratch.P("groundwork");
        Track(w);
        w.Show();
        Pump();
        return w;
    }

    /// <summary>
    /// Notes a window whose drawer is stopped when the run ends, before the scratch folders are removed (its pictures
    /// are drawn on other threads, which would otherwise write into a folder just removed and so leave it behind). The
    /// window is held weakly, so a test's window can be let go once the test is done: a window still drawing stays
    /// alive through its picture task and is stopped at the end.
    /// </summary>
    /// <param name="w">The window.</param>
    public static void Track(MainWindow w) => Windows.Add(new WeakReference<MainWindow>(w));

    /// <summary>The run's shared picture folder, as a cache.</summary>
    /// <returns>The cache.</returns>
    public static Pictures.ThumbCache Thumbs() => new(SharedThumbs.Value.P("thumbs"));

    /// <summary>Makes a scratch folder that is removed when the test run ends.</summary>
    /// <returns>The folder.</returns>
    public static Scratch NewScratch()
    {
        var s = new Scratch();
        Leftovers.Add(s);
        return s;
    }

    private static readonly System.Collections.Concurrent.ConcurrentBag<Scratch> Leftovers = [];

    private static readonly System.Collections.Concurrent.ConcurrentBag<WeakReference<MainWindow>> Windows = [];

    // One picture folder for the windows of the whole run (a scratch folder too): the drawer's rock pictures are drawn
    // by the first windows and read by the others, rather than drawn again by each of the many windows the tests open.
    private static readonly Lazy<Scratch> SharedThumbs = new(NewScratch);

    static Ui() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        try
        {
            List<Task> stops = [];
            foreach (WeakReference<MainWindow> r in Windows)
            {
                if (r.TryGetTarget(out MainWindow? w))
                {
                    stops.Add(w.Drawer.StopDrawing());
                }
            }

            Task.WhenAll(stops).Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // A picture failed; the folders still go.
        }

        foreach (Scratch s in Leftovers)
        {
            try
            {
                s.Dispose();
            }
            catch (IOException)
            {
                // Left behind; the next run's scratch names are new.
            }
        }
    };

    /// <summary>Runs what the dispatcher has queued.</summary>
    public static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>Keeps the dispatcher running (its timers included) for a while.</summary>
    /// <param name="ms">How long, in milliseconds.</param>
    public static void Wait(int ms)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < ms)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The control with this automation name (the setting's label).</summary>
    /// <typeparam name="T">The control type.</typeparam>
    /// <param name="root">Where to look.</param>
    /// <param name="name">The label.</param>
    /// <returns>The control.</returns>
    public static T Named<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.TemplatedParent is null && AutomationProperties.GetName(c) == name);

    /// <summary>Drags across a slider from its left end to its right end in steps, the mouse held down.</summary>
    /// <param name="w">The window.</param>
    /// <param name="slider">The slider.</param>
    /// <param name="steps">How many moves.</param>
    /// <param name="duringDrag">Called after each move, with the button still down.</param>
    public static void Drag(Window w, Slider slider, int steps, Action? duringDrag = null)
    {
        double y = slider.Bounds.Height / 2;
        double left = 12, right = slider.Bounds.Width - 12;
        Point At(double x) => slider.TranslatePoint(new Point(x, y), w)!.Value;
        w.MouseMove(At(left));
        w.MouseDown(At(left), MouseButton.Left);
        Pump();
        for (int i = 1; i <= steps; i++)
        {
            w.MouseMove(At(left + ((right - left) * i / steps)));
            Pump();
            duringDrag?.Invoke();
        }

        w.MouseUp(At(right), MouseButton.Left);
        Pump();
    }
}
