using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Desktop.View;
using Xunit;
using static PropGenerator.Desktop.Tests.FileActionsTests;

namespace PropGenerator.Desktop.Tests;

public class HistoryStripTests
{
    private static Point On(MainWindow w, HistoryStrip strip, int index) =>
        strip.Boxes.TranslatePoint(strip.BoxCentre(index), w) ?? throw new InvalidOperationException("The strip is not in the window.");

    private static bool Waited(Func<bool> done, int ms = 5000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    // Waits until the painter has had no new request for 300 ms (the paints of the changes before have all been asked).
    private static int Settled(FakePainter painter)
    {
        int seen = -1;
        while (seen != painter.Requests.Count)
        {
            seen = painter.Requests.Count;
            Ui.Wait(300);
        }

        return seen;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheStripShowsOnceThereIsAChangeAndTheToolsMoveBelowIt()
    {
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs { Choice = SaveChoice.Discard }, Ui.NewScratch());
        HistoryStrip strip = w.Toolbar.Strip;
        double top = w.Toolbar.Left.Bounds.Top;
        Assert.False(strip.IsVisible);

        SetSeed(w, 4);

        Assert.True(strip.IsVisible);
        Assert.Equal(2, strip.Layout.Count);
        Assert.Equal(1, strip.At);
        Assert.Equal("2 / 2 · Seed 4", strip.Label.Text);
        Assert.True(w.Toolbar.Left.Bounds.Top >= strip.Bounds.Bottom, "the tools on the left sit under the strip");
        Assert.True(w.Toolbar.Right.Bounds.Top >= strip.Bounds.Bottom, "the tools on the right sit under the strip");
        Assert.True(w.Toolbar.StatsCard.Margin.Top > w.Toolbar.Right.Bounds.Bottom - 1);

        // A new piece starts its history again: one step, nothing to step through, and the tools go back up.
        Assert.True(ClaudeKit.Run(w.Files.NewAsync(), 30000));
        Ui.Pump();
        Assert.False(strip.IsVisible);
        Assert.Equal(top, w.Toolbar.Left.Bounds.Top);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ClickingABoxGoesToThatStepAndUndoAndRedoMoveTheMarker()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        HistoryStrip strip = w.Toolbar.Strip;
        SetSeed(w, 4);
        SetSeed(w, 8);
        SetSeed(w, 12);

        Point box = On(w, strip, 1);
        w.MouseMove(box);
        w.MouseDown(box, MouseButton.Left);
        w.MouseUp(box, MouseButton.Left);
        Ui.Pump();

        Assert.Equal(4, w.Panel.Current.GetInt("seed"));
        Assert.Equal(1, w.Files.History.Index);
        Assert.Equal(4, w.Files.History.Steps.Count);
        Assert.Equal(1, strip.At);
        Assert.False(strip.IsUndone(1));
        Assert.True(strip.IsUndone(2));
        Assert.True(strip.IsUndone(3));
        Assert.Equal("2 / 4 · Seed 4", strip.Label.Text);

        Press(w, PhysicalKey.Z, shift: true);
        Assert.Equal(2, strip.At);
        Assert.Equal(8, w.Panel.Current.GetInt("seed"));
        Press(w, PhysicalKey.Y);
        Assert.Equal(3, strip.At);
        Press(w, PhysicalKey.Z);
        Press(w, PhysicalKey.Z);
        Assert.Equal(1, strip.At);
        Assert.Equal(4, w.Panel.Current.GetInt("seed"));

        // A change after stepping back drops the steps that were undone, as Undo then a change does.
        SetSeed(w, 30);
        Assert.Equal(3, strip.Layout.Count);
        Assert.Equal(2, strip.At);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DraggingAcrossTheBoxesPaintsOnceAtTheEnd()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        HistoryStrip strip = w.Toolbar.Strip;
        for (int seed = 1; seed <= 6; seed++)
        {
            SetSeed(w, seed * 10);
        }

        int before = Settled(painter);

        w.MouseMove(On(w, strip, 6));
        w.MouseDown(On(w, strip, 6), MouseButton.Left);
        for (int i = 5; i >= 2; i--)
        {
            w.MouseMove(On(w, strip, i));
            Assert.Equal(i, strip.At);
        }

        w.MouseUp(On(w, strip, 2), MouseButton.Left);
        Ui.Pump();
        Assert.True(Waited(() => w.Panel.Current.GetInt("seed") == 20));
        Settled(painter);

        Assert.Equal(2, w.Files.History.Index);
        Assert.Equal(7, w.Files.History.Steps.Count);
        Assert.True(before + 1 == painter.Requests.Count, string.Join(", ", painter.Requests.Skip(before - 2).Select(r => $"{r.Size}:{r.Settings.GetInt("seed")}")));
        Assert.Equal(20, painter.Requests[^1].Settings.GetInt("seed"));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ABoxsTooltipNamesItsChange()
    {
        MainWindow w = Ui.Open(new FakePainter());
        HistoryStrip strip = w.Toolbar.Strip;
        SetSeed(w, 4);
        SetSeed(w, 8);

        w.MouseMove(On(w, strip, 1));
        Ui.Pump();

        Assert.Equal("2. Seed 4", ToolTip.GetTip(strip.Boxes));
        Assert.Equal("1. Opened", strip.TipFor(0));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheArrowKeysStepAndAWholeHistoryStaysInsideTheStrip()
    {
        MainWindow w = Ui.Open(new FakePainter());
        HistoryStrip strip = w.Toolbar.Strip;
        for (int seed = 1; seed <= 210; seed++)
        {
            Assert.Null(w.Panel.SetFromOutside("seed", System.Text.Json.JsonSerializer.SerializeToElement(seed)));
        }

        Ui.Pump();
        Assert.Equal(200, strip.Layout.Count);
        HistoryLayout l = strip.Layout;
        Assert.InRange(199, l.First, l.First + l.Visible - 1);
        Assert.True(l.X(l.First + l.Visible - 1) + l.Box <= strip.Boxes.Bounds.Width + 0.001);

        strip.Boxes.Focus();
        w.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        w.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Ui.Pump();
        Assert.Equal(197, strip.At);
        Assert.True(Waited(() => w.Files.History.Index == 197));
        Assert.Equal(208, w.Panel.Current.GetInt("seed"));
    }
}
