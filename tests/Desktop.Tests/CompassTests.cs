using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Desktop.View;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class CompassTests
{
    private static string Tip(Control c) => ToolTip.GetTip(c) as string ?? string.Empty;

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void CompassLookAlongKeepsDistanceAndTarget()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Compass compass = w.Toolbar.Compass;
        OrbitCamera cam = w.View.Orbit;
        Assert.Equal(["Look along +X", "Look along +Y", "Look along +Z", "Look along -X", "Look along -Y", "Look along -Z"], Enumerable.Range(0, 6).Select(Compass.TipFor));

        var target = new Vector3(1, -2, 0.75f);
        void Snap(int axis)
        {
            cam.Place(target, 6.5f);
            compass.Snap(axis);
            Assert.Equal(target, cam.Target);
            Assert.Equal(6.5, cam.Distance, 4);
        }

        Snap(0);
        Assert.Equal(MathF.PI / 2, cam.Theta, 4);
        Assert.Equal(OrbitCamera.MaxPolar, cam.Phi, 4);
        Assert.True(cam.Back.X > 0.99f);
        Snap(1);
        Assert.Equal(MathF.PI, MathF.Abs(cam.Theta), 4);
        Assert.True(cam.Back.Y > 0.99f);
        Snap(2);
        Assert.Equal(OrbitCamera.MinPolar, cam.Phi, 4);
        Snap(4);
        Assert.Equal(0, cam.Theta, 4);
        Assert.True(cam.Back.Y < -0.99f);

        // From straight below the camera stops just above the ground, as the orbit's limits keep it.
        Snap(5);
        Assert.Equal(OrbitCamera.MaxPolar, cam.Phi, 4);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheCompassTurnsWithTheCameraAndAClickLooksAlongTheBall()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Compass compass = w.Toolbar.Compass;
        OrbitCamera cam = w.View.Orbit;
        Assert.Equal(76, compass.Bounds.Width, 1);
        Assert.Equal(76, compass.Bounds.Height, 1);

        // From the front, +X lies to the right, +Z up, +Y away from the viewer (faded) and −Y towards it (bold).
        cam.Place(Vector3.Zero, 5);
        cam.LookFrom(new Vector3(0, -1, 0));
        (double X, double Y, double Opacity) x = compass.Place(0), y = compass.Place(1), z = compass.Place(2), minusY = compass.Place(4);
        Assert.Equal(30, x.X, 1);
        Assert.Equal(0, x.Y, 1);
        Assert.Equal(0, z.X, 1);
        Assert.True(z.Y < -29.5, $"{z.Y}");
        Assert.True(y.Opacity < 0.38, $"{y.Opacity}");
        Assert.True(minusY.Opacity > 0.97, $"{minusY.Opacity}");
        Assert.Equal(0.67, x.Opacity, 2);

        // A click on the +X ball looks along +X; its tooltip says so.
        Point centre = new(compass.Bounds.Width / 2, compass.Bounds.Height / 2);
        Point ball = new(centre.X + x.X, centre.Y + x.Y);
        Assert.Equal(0, compass.AxisAt(ball));
        Assert.Null(compass.AxisAt(new Point(1, 1)));
        Point inWindow = compass.TranslatePoint(ball, w)!.Value;
        w.MouseMove(inWindow);
        Ui.Pump();
        Assert.Equal("Look along +X", Tip(compass));
        w.MouseDown(inWindow, MouseButton.Left);
        w.MouseUp(inWindow, MouseButton.Left);
        Ui.Pump();
        Assert.Equal(MathF.PI / 2, cam.Theta, 4);
        Assert.Equal(5, cam.Distance, 4);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheCompassSitsBottomLeftAboveTheDrawer()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Compass compass = w.Toolbar.Compass;
        Point at = compass.TranslatePoint(default, w.View)!.Value;
        Assert.Equal(18, at.X, 1);

        // The drawer lies over the bottom of the view, so the compass stands 8 above it.
        Point card = w.Drawer.Card.TranslatePoint(default, w.View)!.Value;
        Assert.Equal(card.Y - 8, at.Y + compass.Bounds.Height, 1);

        // Maximized there is no drawer: 8 from the bottom, as in easy-props.
        w.ViewState.Maximized = true;
        Ui.Pump();
        at = compass.TranslatePoint(default, w.View)!.Value;
        Assert.Equal(w.View.Bounds.Height - 8, at.Y + compass.Bounds.Height, 1);
        w.Close();
    }
}
