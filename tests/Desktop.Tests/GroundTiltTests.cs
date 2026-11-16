using System.Numerics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Desktop.View;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// Ground tilt in the view (plan E9): upstream's three-line formula in our Z-up axes, the Slope tool and its popover,
/// the prefs, the floor tile staying level and the sun's shadow reaching the tilted ground.
/// </summary>
public class GroundTiltTests
{
    private static StartingPoint Boulder => RockRecipe.StartingPoints.First(p => p.Id == "boulder");

    private static void Click(Button b)
    {
        b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
    }

    private static bool Waited(Func<bool> done, int ms = 15000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    private static MainWindow WithBoulder(Scratch? s = null, FakeDialogs? dialogs = null)
    {
        MainWindow w = s is null ? Ui.Open(new FakePainter()) : Ui.Open(new FakePainter(), dialogs ?? new FakeDialogs(), s);
        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(Boulder), 30000));
        Assert.True(Waited(() => w.View.ShowingRock && w.Rocks.Mesh is not null && w.Rocks.ShownSettings == w.Panel.Current));
        Ui.Pump();
        return w;
    }

    // upstream's create-scene.ts applySlope, three.js Y-up: (sin f · sin d, cos f, sin f · cos d).
    private static Vector3 UpstreamUp(double slope, double downhill)
    {
        double f = slope * Math.PI / 180, d = downhill * Math.PI / 180;
        return new Vector3((float)(Math.Sin(f) * Math.Sin(d)), (float)Math.Cos(f), (float)(Math.Sin(f) * Math.Cos(d)));
    }

    // Our Y-up to Z-up, as EngineMeshes turns every built part: (x, y, z) becomes (x, -z, y).
    private static Vector3 ToZUp(Vector3 v) => new(v.X, -v.Z, v.Y);

    // The way the tilted ground falls: the horizontal direction of the disc rim's lowest point.
    private static Vector2 Downhill(Matrix4x4 m)
    {
        Vector3 low = Vector3.Zero;
        for (int i = 0; i < 720; i++)
        {
            float a = i * MathF.PI / 360;
            var p = Vector3.Transform(new Vector3(MathF.Cos(a), MathF.Sin(a), 0), m);
            if (p.Z < low.Z)
            {
                low = p;
            }
        }

        return Vector2.Normalize(new Vector2(low.X, low.Y));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 0)]
    [InlineData(20, 90)]
    [InlineData(35, 180)]
    [InlineData(35, 355)]
    [InlineData(17, 45)]
    [InlineData(1, 270)]
    [InlineData(30, 125)]
    public void UpEqualsUpstreamsFormulaInOurAxes(double slope, double downhill)
    {
        // E9's one fixed front offset: upstream's three.js front (+Z, where its Front camera stands) is our -Y, where our
        // Front camera stands, so the downhill angle carries over unchanged.
        Assert.Equal(0, GroundTilt.FrontOffsetDegrees);
        Vector3 expected = ToZUp(UpstreamUp(slope, downhill + GroundTilt.FrontOffsetDegrees));
        Vector3 up = GroundTilt.Up(slope, downhill);
        Assert.Equal(expected.X, up.X, 5);
        Assert.Equal(expected.Y, up.Y, 5);
        Assert.Equal(expected.Z, up.Z, 5);

        // The matrix turns the ground's up onto it (three.js's setFromUnitVectors), keeps lengths, and leaves the origin
        // (the prop's base) where it is.
        Matrix4x4 m = GroundTilt.Matrix(slope, downhill);
        var turned = Vector3.TransformNormal(Vector3.UnitZ, m);
        Assert.Equal(expected.X, turned.X, 5);
        Assert.Equal(expected.Y, turned.Y, 5);
        Assert.Equal(expected.Z, turned.Z, 5);
        Assert.Equal(1, Vector3.TransformNormal(Vector3.UnitX, m).Length(), 5);
        Assert.Equal(Vector3.Zero, Vector3.Transform(Vector3.Zero, m));
        if (slope == 0)
        {
            Assert.Equal(Matrix4x4.Identity, m);
        }
    }

    [Fact]
    public void DownhillZeroFallsTowardsTheFront()
    {
        // Upstream's help: "0 falls towards the front". The ground falls towards our Front camera (from -Y) and onto the
        // side the camera first looks from (easy-props' three-quarter view, front left).
        Vector2 down = Downhill(GroundTilt.Matrix(20, 0));
        Assert.True(Vector2.Dot(down, new Vector2(0, -1)) > 0.999f, $"{down}");
        var first = Vector2.Normalize(new Vector2(OrbitCamera.Perspective.X, OrbitCamera.Perspective.Y));
        Assert.True(Vector2.Dot(down, first) > 0.7f, $"{down} {first}");

        // Turning round the prop: 90 falls to the right as seen from the front (+X), 180 away from the camera.
        Assert.True(Vector2.Dot(Downhill(GroundTilt.Matrix(20, 90)), Vector2.UnitX) > 0.999f);
        Assert.True(Vector2.Dot(Downhill(GroundTilt.Matrix(20, 180)), Vector2.UnitY) > 0.999f);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheToolIsPressedWhenSloped()
    {
        MainWindow w = WithBoulder();
        SlopePopover slope = w.Toolbar.Slope;
        Assert.Equal("Slope", AutomationProperties.GetName(slope.Trigger));
        Assert.Equal("triangle-right", slope.Trigger.IconName);
        Assert.Null(slope.Trigger.Unavailable);
        Assert.False(slope.Trigger.Pressed);
        Assert.Equal(Matrix4x4.Identity, w.View.GroundModel);

        Click(slope.Trigger);
        Assert.True(slope.IsOpen);
        Assert.Equal((0, 35, 1), (slope.Slope.Slider.Minimum, slope.Slope.Slider.Maximum, slope.Slope.Slider.SmallChange));
        Assert.Equal((0, 355, 5), (slope.Downhill.Slider.Minimum, slope.Downhill.Slider.Maximum, slope.Downhill.Slider.SmallChange));
        Assert.Equal("°", slope.Slope.Unit?.Text);
        Assert.Equal("Tilts the ground, to see how the prop sits on a hillside. The prop keeps its shape.", slope.Caption.Text);

        slope.Slope.Slider.Value = 20;
        Ui.Pump();
        Assert.Equal(20, w.ViewState.GroundSlope);
        Assert.True(slope.Trigger.Pressed);
        Assert.Equal(GroundTilt.Matrix(20, 0), w.View.GroundModel);

        slope.Downhill.Slider.Value = 90;
        Ui.Pump();
        Assert.Equal(90, w.ViewState.GroundDownhill);
        Assert.Equal(GroundTilt.Matrix(20, 90), w.View.GroundModel);

        // A typed value outside the range is kept to it, with the note; steps are whole degrees and fives.
        slope.Slope.Box.Text = "50";
        slope.Slope.Box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
        Ui.Pump();
        Assert.Equal(35, w.ViewState.GroundSlope);
        Assert.Equal("Kept to 0–35 °", slope.Slope.Note.Text);
        w.ViewState.GroundDownhill = 92;
        Assert.Equal(90, w.ViewState.GroundDownhill);

        // A change made elsewhere (Settings) shows in the popover; level ground un-presses the tool.
        w.ViewState.GroundSlope = 0;
        Ui.Pump();
        Assert.Equal(0, slope.Slope.Slider.Value);
        Assert.False(slope.Trigger.Pressed);
        Assert.Equal(Matrix4x4.Identity, w.View.GroundModel);

        // The prop stays level: the rock's mesh does not change with the slope.
        ColouredMesh before = w.Rocks.Mesh!;
        w.ViewState.GroundSlope = 25;
        Ui.Pump();
        Assert.Same(before, w.Rocks.Mesh);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ResetsGoToZero()
    {
        MainWindow w = WithBoulder();
        SlopePopover slope = w.Toolbar.Slope;
        Click(slope.Trigger);
        Assert.False(slope.Slope.Reset.IsVisible);
        Assert.False(slope.Downhill.Reset.IsVisible);
        w.ViewState.GroundSlope = 12;
        w.ViewState.GroundDownhill = 135;
        Ui.Pump();
        Assert.True(slope.Slope.Reset.IsVisible);
        Assert.True(slope.Downhill.Reset.IsVisible);
        Assert.Equal("Reset Slope", AutomationProperties.GetName(slope.Slope.Reset));

        Click(slope.Slope.Reset);
        Assert.Equal(0, w.ViewState.GroundSlope);
        Assert.Equal(135, w.ViewState.GroundDownhill);
        Assert.False(slope.Slope.Reset.IsVisible);
        Click(slope.Downhill.Reset);
        Assert.Equal(0, w.ViewState.GroundDownhill);
        Assert.False(slope.Downhill.Reset.IsVisible);
        Assert.False(slope.Trigger.Pressed);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheSlopeIsSavedInPrefsNotInTheFile()
    {
        Scratch s = Ui.NewScratch();
        var dialogs = new FakeDialogs();
        MainWindow w = WithBoulder(s, dialogs);
        w.ViewState.GroundSlope = 14;
        w.ViewState.GroundDownhill = 200;
        Ui.Pump();

        // Saved once it stays still (a slider dragged writes once), or at once when the window closes.
        Assert.True(Waited(() => new Prefs(s.P("prefs.json")).GroundSlope == 14, 5000));
        Assert.Equal(200, new Prefs(s.P("prefs.json")).GroundDownhill);

        string file = s.P("Sloped boulder.propgen");
        dialogs.SavePath = file;
        Assert.True(CommandListTests.RunAsync(w.Files.SaveAsAsync()));
        string text = File.ReadAllText(file);
        Assert.DoesNotContain("groundSlope", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("groundDownhill", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("slope", text, StringComparison.OrdinalIgnoreCase);
        w.ViewState.GroundSlope = 3;
        w.Close();
        Ui.Pump();
        Assert.Equal(3, new Prefs(s.P("prefs.json")).GroundSlope);

        // The next window starts on the same slope.
        MainWindow again = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        Assert.Equal((3.0, 200.0), (again.ViewState.GroundSlope, again.ViewState.GroundDownhill));
        again.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void OldPrefsOpenLevel()
    {
        using var s = new Scratch();
        File.WriteAllText(s.P("old.json"), """{"scene": "grey", "wind": true}""");
        var old = new Prefs(s.P("old.json"));
        Assert.Equal((0.0, 0.0), (old.GroundSlope, old.GroundDownhill));
        Assert.Equal((0.0, 0.0), (new ViewState(old).GroundSlope, new ViewState(old).GroundDownhill));

        // Odd values are kept to the ranges and steps; anything not a number reads as level.
        File.WriteAllText(s.P("odd.json"), """{"groundSlope": 80, "groundDownhill": 362}""");
        var odd = new Prefs(s.P("odd.json"));
        Assert.Equal((35.0, 355.0), (odd.GroundSlope, odd.GroundDownhill));
        File.WriteAllText(s.P("odd.json"), """{"groundSlope": -4, "groundDownhill": 7.4}""");
        odd = new Prefs(s.P("odd.json"));
        Assert.Equal((0.0, 5.0), (odd.GroundSlope, odd.GroundDownhill));
        File.WriteAllText(s.P("odd.json"), """{"groundSlope": "steep", "groundDownhill": null}""");
        odd = new Prefs(s.P("odd.json"));
        Assert.Equal((0.0, 0.0), (odd.GroundSlope, odd.GroundDownhill));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheFloorTileDisablesTheSlope()
    {
        MainWindow w = Ui.Open(new FakePainter());
        SlopePopover slope = w.Toolbar.Slope;
        Assert.Equal("The floor tile stays level", slope.Trigger.Unavailable);
        Assert.Equal("The floor tile stays level", ToolTip.GetTip(slope.Trigger));

        // A slope chosen earlier stays chosen, but the floor is drawn level and the tool only says why.
        w.ViewState.GroundSlope = 20;
        Ui.Pump();
        Assert.False(slope.Trigger.Pressed);
        Assert.Equal(Matrix4x4.Identity, w.View.GroundModel);
        Click(slope.Trigger);
        Assert.False(slope.IsOpen);
        Assert.Equal("The floor tile stays level", w.StatusLine.Text);

        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(Boulder), 30000));
        Assert.True(Waited(() => w.View.ShowingRock));
        Ui.Pump();
        Assert.Null(slope.Trigger.Unavailable);
        Assert.True(slope.Trigger.Pressed);
        Assert.Equal(GroundTilt.Matrix(20, 0), w.View.GroundModel);
        w.Close();
    }

    [Fact]
    public void TheShadowBoxCoversTheDiscAt35()
    {
        // Every point of a sphere's shadow on ground tilted 35° must land inside the sun's box (width and depth), however
        // the ground falls and wherever the sun stands, while the sun reaches the ground at all.
        var centre = new Vector3(0.1f, -0.2f, 1.1f);
        const float radius = 1.3f;
        int checkedPoints = 0;
        foreach (double downhill in new[] { 0.0, 45, 90, 180, 270, 315 })
        {
            Vector3 up = GroundTilt.Up(35, downhill);
            foreach (float azimuth in new[] { 0f, 60, 135, 200, 290 })
            {
                foreach (float elevation in new[] { 15f, 30, 45, 70 })
                {
                    float a = azimuth * MathF.PI / 180, e = elevation * MathF.PI / 180;
                    var toSun = new Vector3(MathF.Cos(e) * MathF.Sin(a), -MathF.Cos(e) * MathF.Cos(a), MathF.Sin(e));
                    float facing = Vector3.Dot(up, toSun);
                    if (facing < 0.1f)
                    {
                        // The sun is behind the slope or grazes it: the ground there is in its own shade.
                        continue;
                    }

                    var box = ShadowBox.For(centre, radius, toSun, up);
                    Matrix4x4 lookup = box.Lookup;
                    for (int i = 0; i < 64; i++)
                    {
                        // Points spread over the sphere (a Fibonacci sphere).
                        float z = 1 - (2 * (i + 0.5f) / 64), r = MathF.Sqrt(1 - (z * z)), phi = i * 2.39996323f;
                        Vector3 p = centre + (radius * new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), z));
                        float t = Vector3.Dot(up, p) / facing;
                        if (t < 0)
                        {
                            continue;
                        }

                        Vector3 land = p - (t * toSun);
                        var q = Vector4.Transform(new Vector4(land, 1), lookup);
                        Assert.InRange(q.X / q.W, -0.001f, 1.001f);
                        Assert.InRange(q.Y / q.W, -0.001f, 1.001f);
                        Assert.True(q.Z / q.W is >= 0 and <= 1, $"downhill {downhill}, sun {azimuth}/{elevation}: depth {q.Z / q.W}");
                        checkedPoints++;
                    }
                }
            }
        }

        Assert.True(checkedPoints > 1000, $"{checkedPoints}");

        // Level ground: the box is the one fitted before (the slope changes nothing until it is used).
        var sun = Vector3.Normalize(new Vector3(0.3f, -0.5f, 0.6f));
        Assert.Equal(ShadowBox.For(centre, radius, sun), ShadowBox.For(centre, radius, sun, Vector3.UnitZ));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SettingsHasSlopeAndDownhillInTheViewport()
    {
        using var s = new Scratch();
        var prefs = new Prefs(s.P("prefs.json"));
        var view = new ViewState(prefs);
        MainWindow w = Ui.Open(new FakePainter());
        var window = new SettingsWindow(w.Claude, prefs, view);
        window.Show();
        Ui.Pump();
        NumericUpDown slope = window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => AutomationProperties.GetName(c) == "Slope");
        NumericUpDown downhill = window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => AutomationProperties.GetName(c) == "Downhill");
        Assert.Equal((0m, 35m, 1m), (slope.Minimum, slope.Maximum, slope.Increment));
        Assert.Equal((0m, 355m, 5m), (downhill.Minimum, downhill.Maximum, downhill.Increment));
        Assert.Equal(
            "Tilts the ground under the prop, to see how it sits on a hillside. Only the view changes: the prop keeps its shape.",
            ToolTip.GetTip(slope));
        Assert.Equal("Which way the sloping ground falls, turning around the prop. 0 falls towards the front.", ToolTip.GetTip(downhill));
        Assert.Equal("hill hillside slope tilt ground", AutomationProperties.GetHelpText(slope));
        Assert.Equal("hill hillside slope direction ground", AutomationProperties.GetHelpText(downhill));

        slope.Value = 9;
        downhill.Value = 40;
        Ui.Pump();
        Assert.Equal((9.0, 40.0), (view.GroundSlope, view.GroundDownhill));
        view.GroundSlope = 30;
        Ui.Pump();
        Assert.Equal(30m, slope.Value);
        window.Close();
        w.Close();
    }
}
