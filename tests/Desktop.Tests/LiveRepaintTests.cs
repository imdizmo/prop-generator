using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class LiveRepaintTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheAppPaintsFullDetailWithTheDefaultsAtStart()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        var first = Assert.Single(painter.Requests);
        Assert.Equal(1024, first.Size);
        Assert.Equal(Settings.Defaults(Ui.Specs()), first.Settings);
        Assert.Equal(1024, w.View.Layers?.Size);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DraggingAPaintSliderPaintsPreviewsDuringTheDragAndFullDetailAfter()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        Ui.Wait(400);
        int before = painter.Requests.Count;
        var slider = Ui.Named<Slider>(w, "Carving strength");
        int duringDrag = 0;

        Ui.Drag(w, slider, 10, () => duringDrag = painter.Requests.Count - before);

        var drag = painter.Requests.Skip(before).Take(duringDrag).ToList();
        Assert.True(drag.Count >= 3, $"only {drag.Count} requests while dragging");
        Assert.All(drag, r => Assert.Equal(256, r.Size));
        Assert.NotEqual(drag[0].Settings.GetFloat("carve_strength"), drag[^1].Settings.GetFloat("carve_strength"));

        Ui.Wait(700);
        var after = painter.Requests.Skip(before + duringDrag).ToList();
        var full = Assert.Single(after);
        Assert.Equal(1024, full.Size);
        Assert.Equal(w.Panel.Current, full.Settings);
        Assert.Equal(1024, w.View.Layers?.Size);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MovingContrastIssuesNoPaintRequest()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        Ui.Wait(400);
        int before = painter.Requests.Count;
        var slider = Ui.Named<Slider>(w, "Contrast");

        Ui.Drag(w, slider, 10);
        Ui.Wait(700);

        Assert.Equal(before, painter.Requests.Count);
        Assert.Equal(w.Panel.Current.GetFloat("contrast"), w.View.Contrast);
        Assert.True(w.View.Contrast > 1.0f);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void PickingAColourIssuesNoPaintRequest()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        Ui.Wait(400);
        int before = painter.Requests.Count;
        ColorView picker = w.Panel.ColourRow!.Picker;

        picker.Color = Avalonia.Media.Color.Parse("#D2B48C");
        Ui.Wait(700);

        Assert.Equal(before, painter.Requests.Count);
        Assert.Equal(Rgb.FromHex("#D2B48C"), w.Panel.Current.GetColour("colour"));
        Assert.Equal("#D2B48C", w.Panel.ValueBoxFor("colour").Text);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AFlippedToggleIsFinalAndPaintsFullDetailAtOnce()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        Ui.Wait(400);
        int before = painter.Requests.Count;

        Ui.Named<ToggleSwitch>(w, "Worn edge light").IsChecked = false;
        Ui.Pump();

        var full = Assert.Single(painter.Requests.Skip(before));
        Assert.Equal((1024, false), (full.Size, full.Settings.GetBool("broken_edge")));
        Ui.Wait(500);
        Assert.Single(painter.Requests.Skip(before));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ASeedStepAndATypedValuePaintFullDetailAtOnce()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        Ui.Wait(400);
        int before = painter.Requests.Count;

        Assert.Null(w.Panel.SetFromOutside("seed", System.Text.Json.JsonSerializer.SerializeToElement(7)));
        Ui.Pump();
        var seed = Assert.Single(painter.Requests.Skip(before));
        Assert.Equal((1024, 7), (seed.Size, seed.Settings.GetInt("seed")));

        w.Panel.ValueBoxFor("meander").Text = "3.5";
        w.Panel.CommitValueBox("meander");
        Ui.Pump();
        var typed = painter.Requests.Skip(before + 1).ToList();
        var full = Assert.Single(typed);
        Assert.Equal((1024, 3.5f), (full.Size, full.Settings.GetFloat("meander")));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ADragHeldStillPaintsFullDetailAfterTheQuietTimeAndNotAgainOnRelease()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        Ui.Wait(400);
        int before = painter.Requests.Count;
        var slider = Ui.Named<Slider>(w, "Carving openness");
        int step = 0;
        List<(int Size, Settings Settings)> whileHeld = [];

        Ui.Drag(w, slider, 5, () =>
        {
            if (++step == 5)
            {
                Ui.Wait((int)LiveRepaint.HeldStill.TotalMilliseconds + 300);
                whileHeld = [.. painter.Requests.Skip(before)];
            }
        });
        Ui.Wait(600);

        Assert.True(whileHeld.Count >= 2, $"only {whileHeld.Count} requests while the drag was held");
        Assert.Equal(1024, whileHeld[^1].Size);
        Assert.All(whileHeld.SkipLast(1), r => Assert.Equal(256, r.Size));
        Assert.Equal(whileHeld.Count, painter.Requests.Count - before);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnyErrorFromThePainterShowsInTheStatusLine()
    {
        var painter = new FakePainter { Reply = (_, _) => Task.FromException<Layers?>(new NotSupportedException("The painter broke in an odd way.")) };
        MainWindow w = Ui.Open(painter);
        Ui.Pump();
        Assert.Contains("odd way", w.StatusLine.Text, StringComparison.Ordinal);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ALatePreviewDoesNotReplaceTheFullPaint()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        var preview = new TaskCompletionSource<Layers?>();
        var full = new TaskCompletionSource<Layers?>();
        painter.Reply = (size, _) => size == 256 ? preview.Task : full.Task;

        w.Repaint.OnPaintChanged(dragging: true);
        w.Repaint.OnCommitted("Carving strength");
        full.SetResult(FakePainter.Blank(1024));
        Ui.Pump();
        preview.SetResult(FakePainter.Blank(256));
        Ui.Pump();

        Assert.Equal(1024, w.View.Layers?.Size);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void APainterFailureShowsInTheStatusLine()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        painter.FailFromThreadPool("The painter stopped: out of memory.");
        Ui.Wait(100);
        Assert.Contains("out of memory", w.StatusLine.Text, StringComparison.Ordinal);
    }

    // The user's report: while a paint slider was dragged slowly (fine detail), the view first showed the value the
    // slider had just left, then jumped. A full-detail paint asked during a pause took about a second and landed after
    // the slider had moved on.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AFullPaintTheSliderHasMovedOnFromIsNeverShown()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        Ui.Wait(100);
        var answers = Answers(painter);
        var slider = Ui.Named<Slider>(w, "Carving strength");

        Press(w, slider, 0.2);
        MoveTo(w, slider, 0.3);
        Layers preview = Answer(answers, 256);
        Ui.Wait((int)LiveRepaint.HeldStill.TotalMilliseconds + 200);
        var full = Assert.Single(answers, a => a.Size == 1024);
        Assert.Equal(w.Panel.Current, full.Settings);
        MoveTo(w, slider, 0.4);
        Answer(answers, 1024);

        Assert.Same(preview, w.View.Layers);
        Layers next = Answer(answers, 256);
        Assert.Same(next, w.View.Layers);
        Release(w, slider, 0.4);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AShortPauseInAHeldDragPaintsNoFullDetail()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        Ui.Wait(100);
        int before = painter.Requests.Count;
        var slider = Ui.Named<Slider>(w, "Carving strength");

        Press(w, slider, 0.2);
        MoveTo(w, slider, 0.3);
        Ui.Wait(450);
        MoveTo(w, slider, 0.4);
        Ui.Wait(450);

        Assert.All(painter.Requests.Skip(before), r => Assert.Equal(256, r.Size));
        Release(w, slider, 0.4);
        Ui.Pump();
        Assert.Equal(1024, painter.Requests[^1].Size);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void OnReleaseThePreviewOfTheFinalValueComesFirstThenFullDetail()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        Ui.Wait(100);
        var answers = Answers(painter);
        var slider = Ui.Named<Slider>(w, "Carving strength");

        Press(w, slider, 0.2);
        MoveTo(w, slider, 0.5);
        Release(w, slider, 0.5);
        Ui.Pump();

        Assert.DoesNotContain(answers, a => a.Size == 1024);
        Layers preview = Answer(answers, 256);
        Assert.Same(preview, w.View.Layers);
        var full = Assert.Single(answers, a => a.Size == 1024);
        Assert.Equal(w.Panel.Current, full.Settings);
        Layers sharp = Answer(answers, 1024);
        Assert.Same(sharp, w.View.Layers);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void APreviewOfSettingsAnUndoLeftIsNotShown()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        Ui.Wait(100);
        Layers start = w.View.Layers!;
        var answers = Answers(painter);

        w.Panel.Apply(w.Panel.Current.With("carve_strength", 1.5));
        w.Repaint.OnPaintChanged(dragging: true);
        w.Panel.Apply(w.Panel.Defaults());
        w.Repaint.RepaintNow();
        Answer(answers, 256);

        Assert.Same(start, w.View.Layers);
        Layers full = Answer(answers, 1024);
        Assert.Same(full, w.View.Layers);
    }

    private static List<(int Size, Settings Settings, TaskCompletionSource<Layers?> Reply)> Answers(FakePainter painter)
    {
        var answers = new List<(int Size, Settings Settings, TaskCompletionSource<Layers?> Reply)>();
        painter.Reply = (size, settings) =>
        {
            var tcs = new TaskCompletionSource<Layers?>();
            answers.Add((size, settings, tcs));
            return tcs.Task;
        };
        return answers;
    }

    // Answers every request of this size still waiting, oldest first, with new blank layers, and lets the window show
    // them. The fake painter keeps them all, where the real one would have dropped the older ones.
    private static Layers Answer(List<(int Size, Settings Settings, TaskCompletionSource<Layers?> Reply)> answers, int size)
    {
        var waiting = answers.Where(x => x.Size == size && !x.Reply.Task.IsCompleted).ToList();
        Assert.NotEmpty(waiting);
        Layers l = FakePainter.Blank(size);
        foreach (var a in waiting)
        {
            l = FakePainter.Blank(size);
            a.Reply.SetResult(l);
            Ui.Pump();
        }

        return l;
    }

    private static Point On(Window w, Slider slider, double fraction)
    {
        double left = 12, right = slider.Bounds.Width - 12;
        return slider.TranslatePoint(new Point(left + ((right - left) * fraction), slider.Bounds.Height / 2), w)!.Value;
    }

    private static void Press(Window w, Slider slider, double fraction)
    {
        w.MouseMove(On(w, slider, fraction));
        w.MouseDown(On(w, slider, fraction), MouseButton.Left);
        Ui.Pump();
    }

    private static void MoveTo(Window w, Slider slider, double fraction)
    {
        w.MouseMove(On(w, slider, fraction));
        Ui.Pump();
    }

    private static void Release(Window w, Slider slider, double fraction)
    {
        w.MouseUp(On(w, slider, fraction), MouseButton.Left);
        Ui.Pump();
    }
}
