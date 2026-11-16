using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Desktop.View;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class SunDialTests
{
    [Fact]
    public void SunDialMapsPositionToAzimuthAndElevation()
    {
        Lighting now = Lighting.Default;

        // The centre is straight up (88° at most); the rim is the horizon (4° at least).
        Assert.Equal(88, SunDial.FromDial(0, 0, now).Elevation, 6);
        Lighting top = SunDial.FromDial(0, -1, now);
        Assert.Equal(0, top.Azimuth, 6);
        Assert.Equal(4, top.Elevation, 6);
        Lighting right = SunDial.FromDial(2, 0, now);
        Assert.Equal(90, right.Azimuth, 6);
        Assert.Equal(4, right.Elevation, 6);
        Assert.Equal(270, SunDial.FromDial(-0.5, 0, now).Azimuth, 6);
        Assert.Equal(0.5 * 90 / 0.72, SunDial.FromDial(0, 0.5, now).Elevation, 6);
        Assert.Equal(180, SunDial.FromDial(0, 0.5, now).Azimuth, 6);

        // Only the sun moves: warmth and shadow are kept.
        Assert.Equal(now.Warmth, right.Warmth);
        Assert.Equal(now.Shadow, right.Shadow);

        // Drawn where it was picked: radius 1 − elevation / 90 × 0.72, round from the top.
        (double x, double y) = SunDial.Position(new Lighting(90, 45, 0.5, 0.5));
        Assert.Equal(1 - (0.5 * 0.72), x, 6);
        Assert.Equal(0, y, 6);
        (x, y) = SunDial.Position(now);
        Lighting back = SunDial.FromDial(x, y, now);
        Assert.Equal(now.Azimuth, back.Azimuth, 6);
        Assert.Equal(now.Elevation, back.Elevation, 6);

        // The keys: right and up turn and raise it 5°, left and down the other way; Shift makes it 15°.
        var l = new Lighting(358, 86, 0.5, 0.5);
        Assert.Equal(new Lighting(3, 88, 0.5, 0.5), SunDial.Nudge(l, Key.Right, shift: false));
        Assert.Equal(new Lighting(3, 88, 0.5, 0.5), SunDial.Nudge(l, Key.Up, shift: false));
        Assert.Equal(new Lighting(343, 71, 0.5, 0.5), SunDial.Nudge(l, Key.Left, shift: true));
        Assert.Equal(new Lighting(343, 71, 0.5, 0.5), SunDial.Nudge(l, Key.Down, shift: true));
        Assert.Equal(new Lighting(0, 4, 0.5, 0.5), SunDial.Nudge(new Lighting(5, 6, 0.5, 0.5), Key.Left, shift: false));
        Assert.Null(SunDial.Nudge(l, Key.A, shift: false));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DraggingOnTheDialMovesTheSun()
    {
        MainWindow w = Ui.Open(new FakePainter());
        SunPopover sun = w.Toolbar.Sun;
        sun.Open();
        Ui.Pump();
        SunDial dial = sun.Dial;
        Assert.Equal(84, dial.Bounds.Width, 1);
        Assert.Equal("Sun position", Avalonia.Automation.AutomationProperties.GetName(dial));

        // A press on the dial's right edge puts the sun there: 90° round, on the horizon.
        TopLevel root = TopLevel.GetTopLevel(dial)!;
        Point at = dial.TranslatePoint(new Point(42 + 36, 42), root)!.Value;
        root.MouseDown(at, MouseButton.Left);
        root.MouseUp(at, MouseButton.Left);
        Ui.Pump();
        Assert.Equal(90, w.ViewState.Lighting.Azimuth, 3);
        Assert.Equal(4, w.ViewState.Lighting.Elevation, 3);

        // The keys move it too.
        dial.Focus();
        root.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Ui.Pump();
        Assert.Equal(105, w.ViewState.Lighting.Azimuth, 3);
        Assert.Equal(19, w.ViewState.Lighting.Elevation, 3);
        w.Close();
    }
}
