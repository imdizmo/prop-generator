using System.Numerics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Menus;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Desktop.View;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class ViewToolbarTests
{
    private static StartingPoint Boulder => RockRecipe.StartingPoints.First(p => p.Id == "boulder");

    private static void Click(Button b)
    {
        b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
    }

    private static string Tip(Control c) => ToolTip.GetTip(c) as string ?? string.Empty;

    private static string Name(Control c) => AutomationProperties.GetName(c) ?? string.Empty;

    private static void Key(MainWindow w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPressQwerty(key, mods);
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

    private static MainWindow WithBoulder()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(Boulder), 30000));
        Assert.True(Waited(() => w.View.ShowingRock && w.Rocks.Mesh is not null && w.Rocks.ShownSettings == w.Panel.Current));
        Ui.Pump();
        return w;
    }

    private static (Vector3 Lo, Vector3 Hi) Bounds(ColouredMesh m)
    {
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (int i = 0; i < m.Positions.Length; i += 3)
        {
            var p = new Vector3(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]);
            lo = Vector3.Min(lo, p);
            hi = Vector3.Max(hi, p);
        }

        return (lo, hi);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ToolbarOrderAndGreyedReasonsFollowREF()
    {
        MainWindow w = Ui.Open(new FakePainter());
        ViewToolbar t = w.Toolbar;
        Assert.Equal(["Select", "Move", "Turn", "Resize"], t.Left.Tools.Select(Name));
        Assert.Equal(["Shading: Lit", "Detail level 0"], t.GroupA.Tools.Select(Name));
        Assert.Equal(
            ["Scene", "Sun and shadow", "Slope", "Wind", "Camera angle", "Frame", "Stats", "A person for scale", "What you can walk into", "Maximize"],
            t.GroupB.Tools.Select(Name));

        // Select is pressed; Move, Turn and Resize wait for a part to be picked, and a click only says why.
        Assert.True(t.Select.Pressed);
        Assert.Equal("Select (Q): click a part in the view to pick it; drag to look around.", Tip(t.Select));
        foreach (ToolButton b in new[] { t.Move, t.Turn, t.Resize })
        {
            Assert.Equal("This kind of piece has no parts to pick", b.Unavailable);
            Assert.Equal("This kind of piece has no parts to pick", Tip(b));
            Assert.Contains("ep-unavailable", b.Classes);
            Assert.False(b.Pressed);
        }

        Click(t.Move);
        Assert.Equal("This kind of piece has no parts to pick", w.StatusLine.Text);

        // The groups sit 8 from the top and 18 in from the sides, 8 apart, as the design's toolbar cards over a 3D view.
        Point left = t.Left.TranslatePoint(default, w.View)!.Value;
        Assert.Equal(18, left.X, 1);
        Assert.Equal(8, left.Y, 1);
        Point b2 = t.GroupB.TranslatePoint(default, w.View)!.Value;
        Assert.Equal(18, w.View.Bounds.Width - (b2.X + t.GroupB.Bounds.Width), 1);
        Assert.Equal(8, b2.Y, 1);
        Point a = t.GroupA.TranslatePoint(default, w.View)!.Value;
        Assert.Equal(8, b2.X - (a.X + t.GroupA.Bounds.Width), 1);
        Assert.Equal(new Thickness(4), t.GroupA.Padding);
        Assert.Equal(new CornerRadius(12), t.GroupA.CornerRadius);
        Assert.All(t.GroupB.Tools, c => Assert.Equal(28, c.Bounds.Height, 1));
        Assert.All(t.GroupB.Tools, c => Assert.True(c.Bounds.Width >= 28 - 0.01));

        // Too narrow for one line, the view's group goes onto a second line under the look's, both on the right (the
        // window at its narrowest, the panel at its widest).
        w.Width = MainWindow.MinimumWidth;
        Ui.Pump();
        w.Edge.Handle.Focus();
        for (int i = 0; i < 13; i++)
        {
            w.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        }

        Ui.Pump();
        Assert.Equal(520, w.SidePanel.Bounds.Width);
        Point a2 = t.GroupA.TranslatePoint(default, w.View)!.Value, b3 = t.GroupB.TranslatePoint(default, w.View)!.Value;
        Assert.True(b3.Y > a2.Y + 20, $"{a2} {b3}");
        Assert.Equal(18, w.View.Bounds.Width - (a2.X + t.GroupA.Bounds.Width), 1);
        Assert.Equal(18, w.View.Bounds.Width - (b3.X + t.GroupB.Bounds.Width), 1);
        Assert.True(a2.X > t.Left.Bounds.Right, "the right tools stay clear of the left ones");
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ShadingMenuChangesTheViewStateAndZCyclesLiveOnes()
    {
        MainWindow w = WithBoulder();
        ViewToolbar t = w.Toolbar;
        Assert.Equal("Shading: how the prop is drawn — lit, flat or as its triangles.", Tip(t.Shading.Trigger));
        Assert.Equal("Lit", t.Shading.Trigger.Text);
        Assert.Equal("sun", t.Shading.Trigger.IconName);

        t.Shading.Open();
        Assert.True(t.Shading.IsOpen);
        Assert.Equal(["Lit ✓", "Flat", "Wireframe", "Wind ✗"], t.Shading.Lines);
        Assert.Equal(
            ["The finished look, with light and shadow.", "One flat colour per part. Good for checking the shape.", "Shows the triangles the prop is made of.", "How much each part moves in the wind."],
            t.Shading.Rows.Select(r => r.Help));
        Click(t.Shading.Rows.Single(r => r.Label == "Flat"));
        Assert.False(t.Shading.IsOpen);
        Assert.Equal(ViewState.Flat, w.ViewState.Shading);
        Assert.Equal(ViewState.Flat, w.View.Shading);
        Assert.Equal("Flat", t.Shading.Trigger.Text);
        Assert.Equal("box", t.Shading.Trigger.IconName);
        Assert.Equal("Shading: Flat", Name(t.Shading.Trigger));

        // Wind is greyed: its row says why and changes nothing.
        t.Shading.Open();
        MenuRow wind = t.Shading.Rows.Single(r => r.Label == "Wind");
        Assert.False(wind.IsEnabled);
        Assert.Equal("Comes with wind", Tip(wind));

        // Esc closes it, and changes nothing else.
        Key(w, PhysicalKey.Escape);
        Assert.False(t.Shading.IsOpen);
        Assert.Equal(ViewState.Flat, w.ViewState.Shading);

        // Z steps through the live modes only.
        Key(w, PhysicalKey.Z);
        Assert.Equal(ViewState.Wireframe, w.ViewState.Shading);
        Assert.Equal("grid-2x2", t.Shading.Trigger.IconName);
        Key(w, PhysicalKey.Z);
        Assert.Equal(ViewState.Lit, w.ViewState.Shading);
        Key(w, PhysicalKey.Z);
        Assert.Equal(ViewState.Flat, w.ViewState.Shading);
        w.Close();
    }

    // The floor is painted by hand and drawn lit whatever the shading says, and so is a mesh from Blender: Flat and
    // Wireframe are greyed for them with the reason, Lit shows, Z says why, and the rock's choice is kept for the rock.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FlatAndWireframeAreGreyedForAnythingButARock()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        ViewToolbar t = w.Toolbar;
        const string Floor = "The floor is painted by hand; shading is for rocks";

        void Greyed(string reason, string checkedRow)
        {
            t.Shading.Open();
            Assert.Equal([checkedRow, "Flat ✗", "Wireframe ✗", "Wind ✗"], t.Shading.Lines);
            Assert.Equal(reason, Tip(t.Shading.Rows.Single(r => r.Label == "Flat")));
            Assert.Equal(reason, Tip(t.Shading.Rows.Single(r => r.Label == "Wireframe")));
            Assert.False(t.Shading.Rows.Single(r => r.Label == "Wireframe").IsEnabled);
            t.Shading.Close();
            Assert.Equal(reason, CommandList.Find(w.Commands, "shading-flat")!.Unavailable());
            Assert.Equal(reason, CommandList.Find(w.Commands, "next-shading")!.Unavailable());
            Assert.Equal("Lit", t.Shading.Trigger.Text);
            Assert.Equal(ViewState.Lit, w.ShadingShown);
        }

        Greyed(Floor, "Lit ✓");
        Key(w, PhysicalKey.Z);
        Assert.Equal(ViewState.Lit, w.ViewState.Shading);
        Assert.Equal(Floor, w.StatusLine.Text);

        // The palette's Shading list greys them too: choosing one says why and changes nothing.
        w.Palette.Open();
        w.Palette.Type("shading");
        ClaudeKit.Run(w.Palette.ChooseAsync(w.Palette.Rows.ToList().FindIndex(r => r.Label == "Shading")));
        Assert.Equal("Shading", w.Palette.SubList);
        ClaudeKit.Run(w.Palette.ChooseAsync(w.Palette.Rows.ToList().FindIndex(r => r.Label == "Wireframe")));
        Assert.Equal(Floor, w.Palette.Notice);
        Assert.Equal(ViewState.Lit, w.ViewState.Shading);
        w.Palette.Close();

        // On a rock they are live; the rock's choice is kept while the floor is shown, and shown again with the rock.
        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(Boulder), 30000));
        Assert.True(Waited(() => w.View.ShowingRock && w.Rocks.Mesh is not null));
        Ui.Pump();
        Assert.Null(w.ShadingUnavailable);
        Key(w, PhysicalKey.Z);
        Assert.Equal(ViewState.Flat, w.ViewState.Shading);
        Assert.Equal("Flat", t.Shading.Trigger.Text);

        // A mesh from Blender: greyed, with its own reason.
        BridgeKit.DropMesh(s, "Crate", 20);
        Assert.True(Waited(() => w.View.ShowingPiece));
        Ui.Pump();
        Greyed("Shading is for rocks", "Lit ✓");
        Assert.Equal(ViewState.Flat, w.ViewState.Shading);

        // Back to the rock: Flat again.
        ClaudeKit.Run(w.BackToThePieceAsync());
        Assert.True(Waited(() => w.View.ShowingRock && !w.View.ShowingPiece));
        Ui.Pump();
        Assert.Equal("Flat", t.Shading.Trigger.Text);
        Assert.Equal(ViewState.Flat, w.ShadingShown);

        // And the floor: Lit shows, Flat is kept for the next rock.
        Assert.True(ClaudeKit.Run(w.Files.NewAsync(), 30000));
        Ui.Pump();
        Greyed(Floor, "Lit ✓");
        Assert.Equal(ViewState.Flat, w.ViewState.Shading);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheFloorOffersOneDetailLevel()
    {
        MainWindow w = Ui.Open(new FakePainter());
        ViewToolbar t = w.Toolbar;

        // The detail level: one for the floor.
        Assert.Equal("Detail level shown: full detail, or the lighter levels an export writes.", Tip(t.Lod.Trigger));
        Assert.Equal("LOD0", t.Lod.Trigger.Text);
        t.Lod.Open();
        Assert.Equal(["Level 0 (LOD0) ✓"], t.Lod.Lines);
        t.Lod.Close();
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheDetailLevelMenuOffersARocksThreeLevels()
    {
        MainWindow w = WithBoulder();
        ViewToolbar t = w.Toolbar;
        t.Lod.Open();
        Assert.Equal(["Level 0 (LOD0) ✓", "Level 1 (LOD1)", "Level 2 (LOD2)"], t.Lod.Lines);
        Assert.Equal("About 40% of the faces, for further away.", t.Lod.Rows[1].Help);
        Click(t.Lod.Rows[2]);
        Assert.Equal(2, w.ViewState.Lod);
        Assert.Equal("LOD2", t.Lod.Trigger.Text);
        Assert.Equal("Detail level 2", Name(t.Lod.Trigger));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SceneMenuIsRememberedAndDemosAreGreyed()
    {
        Scratch s = Ui.NewScratch();
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        ViewToolbar t = w.Toolbar;
        Assert.Equal("Scene around the prop: a plain studio, the studio under a blue sky, or neutral grey.", Tip(t.Scene.Trigger));
        Assert.Equal("tree-pine", t.Scene.Trigger.IconName);
        t.Scene.Open();
        Assert.Equal(["Studio", "Studio with sky ✓", "Meadow demo ✗", "Grove demo ✗", "Neutral grey"], t.Scene.Lines);
        Assert.Equal(
            ["A plain place to work", "A plain place to work, in bright daylight under a blue sky", "Grass, flowers, bushes, rocks and a cliff around your prop", "The meadow, with trees as well", "Best for judging colour"],
            t.Scene.Rows.Select(r => r.Help));
        foreach (string demo in new[] { "Meadow demo", "Grove demo" })
        {
            MenuRow row = t.Scene.Rows.Single(r => r.Label == demo);
            Assert.False(row.IsEnabled);
            Assert.Equal("Comes with the demo scenery around the prop; the Grove also waits for painted trees", Tip(row));
        }

        Click(t.Scene.Rows.Single(r => r.Label == "Neutral grey"));
        Assert.Equal("grey", w.ViewState.Scene);
        Assert.Equal("grey", w.View.Scene);
        Assert.Equal("grey", new Prefs(s.P("prefs.json")).Scene);
        w.Close();

        MainWindow again = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        Assert.Equal("grey", again.ViewState.Scene);
        again.Toolbar.Scene.Open();
        Assert.Equal("Neutral grey ✓", again.Toolbar.Scene.Lines[^1]);
        again.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SunPresetsSetREFsValues()
    {
        MainWindow w = Ui.Open(new FakePainter());
        SunPopover sun = w.Toolbar.Sun;
        Assert.Equal("Sun and shadow: where the sun stands and how dark its shadow is.", Tip(sun.Trigger));
        Assert.Equal(
            [("Morning", new Lighting(48, 30, 0.72, 0.85)), ("Midday", new Lighting(165, 68, 0.2, 0.5)), ("Golden", new Lighting(232, 18, 1, 0.8)), ("Dusk", new Lighting(268, 11, 0.5, 0.4))],
            SunPopover.Presets);
        Click(sun.Trigger);
        Assert.True(sun.IsOpen);
        Assert.Equal(230, sun.Content.Width);
        Assert.Equal("Drag to move the sun", sun.Caption.Text);
        Assert.Equal(["Morning", "Midday", "Golden", "Dusk"], sun.Times.Select(b => b.Content as string));
        Assert.DoesNotContain(sun.Times, b => b.Classes.Contains("ep-pressed"));
        Assert.Equal(0.8, sun.Shadow.Slider.Value, 3);

        Click(sun.Times[2]);
        Assert.Equal(new Lighting(232, 18, 1, 0.8), w.ViewState.Lighting);
        Assert.Contains("ep-pressed", sun.Times[2].Classes);
        Assert.Equal(0.8, sun.Shadow.Slider.Value, 3);

        Click(sun.Times[3]);
        Assert.Equal(new Lighting(268, 11, 0.5, 0.4), w.ViewState.Lighting);
        Assert.Equal(0.4, sun.Shadow.Slider.Value, 3);
        Assert.DoesNotContain("ep-pressed", sun.Times[2].Classes);

        // The shadow slider: 0 to 1 in steps of 0.05; the time stays picked, as only the sun's place decides it.
        Assert.Equal(0, sun.Shadow.Slider.Minimum);
        Assert.Equal(1, sun.Shadow.Slider.Maximum);
        Assert.Equal(0.05, sun.Shadow.Slider.SmallChange, 6);
        sun.Shadow.Slider.Value = 0.25;
        Ui.Pump();
        Assert.Equal(0.25, w.ViewState.Lighting.Shadow, 6);
        Assert.Contains("ep-pressed", sun.Times[3].Classes);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void WindPopoverSwitchesTheWindAndSaysWhenThePropHoldsStill()
    {
        MainWindow w = Ui.Open(new FakePainter());
        WindPopover wind = w.Toolbar.Wind;
        Assert.Equal("Wind: move the prop as the wind materials will move it in your engine.", Tip(wind.Trigger));
        Click(wind.Trigger);
        Assert.True(wind.IsOpen);
        Assert.True(wind.Switch.IsEnabled);
        Assert.False(wind.Switch.IsChecked);
        Assert.Equal("This prop doesn't move in the wind.", wind.Caption.Text);

        // Switched on with the floor: remembered, but the floor holds still, so the tool is not pressed.
        wind.Switch.IsChecked = true;
        Ui.Pump();
        Assert.True(w.ViewState.Wind);
        Assert.False(wind.Trigger.Pressed);

        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(MeshRecipes.Find("grass")!.StartingPoints[0])));
        Ui.Pump();
        Assert.True(wind.Trigger.Pressed);
        Assert.Equal("Moves the prop as the wind materials will move it in your engine.", wind.Caption.Text);

        Assert.Equal(1, wind.Strength.Slider.Value);
        Assert.Equal((0.3, 2.0), (wind.Strength.Slider.Minimum, wind.Strength.Slider.Maximum));
        wind.Strength.Slider.Value = 1.5;
        Ui.Pump();
        Assert.Equal(1.5, w.ViewState.WindStrength);

        // From review: a typed strength is taken on Enter, clamped to calm..storm.
        wind.Strength.Box.Text = "1.8";
        wind.Strength.Box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
        Ui.Pump();
        Assert.Equal(1.8, w.ViewState.WindStrength, 6);
        wind.Strength.Box.Focus();
        Ui.Pump();
        wind.Strength.Box.Text = "9";
        wind.Switch.Focus();
        Ui.Pump();
        Assert.Equal(2, w.ViewState.WindStrength, 6);

        // From review: a change elsewhere (Settings writes through the view state) shows in the popover.
        w.ViewState.WindStrength = 0.6;
        Ui.Pump();
        Assert.Equal(0.6, wind.Strength.Slider.Value, 6);
        w.ViewState.Wind = false;
        Ui.Pump();
        Assert.False(wind.Switch.IsChecked);
        Assert.False(wind.Trigger.Pressed);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SlopeSitsBetweenSunAndWindAndClosesWithTheOthers()
    {
        MainWindow w = WithBoulder();
        ViewToolbar t = w.Toolbar;
        var tools = t.GroupB.Tools.ToList();
        Assert.Equal(tools.IndexOf(t.Sun.Trigger) + 1, tools.IndexOf(t.Slope.Trigger));
        Assert.Equal(tools.IndexOf(t.Wind.Trigger) - 1, tools.IndexOf(t.Slope.Trigger));
        Assert.Equal("Slope: tilt the ground under the prop, to see how it sits on a hillside. Only the view changes.", Tip(t.Slope.Trigger));
        Assert.Contains(t.Slope.Popup, t.Overlay.Children);
        Click(t.Slope.Trigger);
        Assert.True(t.Slope.IsOpen);
        Assert.True(t.ClosePopups());
        Assert.False(t.Slope.IsOpen);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void StatsCardShowsFacesDrawCallsAndSize()
    {
        MainWindow w = Ui.Open(new FakePainter());
        ViewToolbar t = w.Toolbar;
        Assert.False(t.StatsCard.IsVisible);
        Click(t.Stats);
        Assert.True(w.ViewState.ShowStats);
        Assert.True(t.Stats.Pressed);
        Assert.True(t.StatsCard.IsVisible);
        Point at = t.StatsCard.TranslatePoint(default, w.View)!.Value;
        Assert.Equal(54, at.Y, 1);
        Assert.Equal(18, w.View.Bounds.Width - (at.X + t.StatsCard.Bounds.Width), 1);
        Assert.False(t.StatsCard.IsHitTestVisible);
        Assert.Equal(TimeSpan.FromMilliseconds(500), t.StatsCard.Timer.Interval);
        Assert.True(t.StatsCard.Timer.IsEnabled);

        // The floor: its tile's faces, and the tile's size.
        int tileFaces = TileMesh.Square().Indices.Length / 3;
        Assert.Equal(["Faces", "Draw calls", "Size"], t.StatsCard.Labels);
        Assert.Equal(tileFaces.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), t.StatsCard.Values[0]);
        Assert.Equal("0", t.StatsCard.Values[1]);
        // Width × height × depth, as easy-props reads it: X × Z × Y here, Z up.
        Assert.Equal("2.2 × 0.3 × 2.2 m", t.StatsCard.Values[2]);
        Assert.Equal("1.0 × 0.0 × 12.3 m", StatsCard.Size(new Vector3(1, 12.25f, 0.04f)));

        // A rock: the card follows what is shown (at once when it changes, and every 500 ms for the draw calls).
        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(Boulder), 30000));
        Assert.True(Waited(() => w.View.ShowingRock && w.Rocks.Mesh is not null));
        Ui.Wait(600);
        (Vector3 lo, Vector3 hi) = Bounds(w.Rocks.Mesh!);
        Assert.Equal(w.Rocks.ShownFaces.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), t.StatsCard.Values[0]);
        Assert.Equal(StatsCard.Size(hi - lo), t.StatsCard.Values[2]);
        // easy-props 0.4.5's boulder (0.4.2's was 2.7 m deep).
        Assert.Equal("3.0 × 2.1 × 2.6 m", t.StatsCard.Values[2]);

        Click(t.Stats);
        Assert.False(t.StatsCard.IsVisible);
        Assert.False(t.StatsCard.Timer.IsEnabled);
        Assert.False(t.Stats.Pressed);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void PersonStandsBesideThePropAtOnePointEightMetres()
    {
        ColouredMesh person = PersonMesh.Build();
        (Vector3 lo, Vector3 hi) = Bounds(person);
        Assert.Equal(0, lo.Z, 3);
        Assert.Equal(1.8, hi.Z, 3);
        Assert.Equal(0.42, hi.X - lo.X, 3);
        Assert.Equal(0.42 * 0.72, hi.Y - lo.Y, 3);
        Vector3 grey = SceneLook.ToLinear(SceneLook.Srgb("#c9ccd1"));
        Assert.Equal(grey.X, person.Colours[0], 4);
        Assert.Equal(grey.Z, person.Colours[2], 4);
        Assert.Equal(person.Positions.Length, person.Normals.Length);

        MainWindow w = WithBoulder();
        ViewToolbar t = w.Toolbar;
        Assert.False(w.View.ShowPerson);
        Click(t.Person);
        Assert.True(w.ViewState.ShowPerson);
        Assert.True(t.Person.Pressed);
        Assert.True(w.View.ShowPerson);

        // Beside the rock's front-left corner (easy-props' min x − 0.6 and the front, here −Y), on the ground.
        (Vector3 rlo, Vector3 _) = Bounds(w.Rocks.Mesh!);
        Assert.Equal(new Vector3(rlo.X - 0.6f, rlo.Y - 0.6f, 0), w.View.PersonAt);
        Click(t.Person);
        Assert.False(w.View.ShowPerson);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void CollisionShowsTheRocksShapesOrTheNote()
    {
        MainWindow w = Ui.Open(new FakePainter());
        ViewToolbar t = w.Toolbar;
        Assert.False(t.Note.IsVisible);
        Click(t.Collision);
        Assert.True(w.ViewState.ShowCollision);
        Assert.True(t.Collision.Pressed);
        Assert.True(t.Note.IsVisible);
        Assert.Equal("Nothing to walk into: this prop has no collision shapes.", t.NoteText.Text);
        Point at = t.Note.TranslatePoint(default, w.View)!.Value;
        Assert.Equal(new Point(18, 54), at);
        Assert.Null(w.View.Prisms);

        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(Boulder), 30000));
        Assert.True(Waited(() => w.View.ShowingRock && w.View.Prisms is not null));
        Settings shown = w.Panel.Current;
        IReadOnlyList<PropGenerator.Nature.Export.CollisionShape> shapes = EngineKit.OnThePool(() => RockRecipe.Collision(shown));
        Assert.NotEmpty(shapes);
        CollisionPrisms prisms = w.View.Prisms!;
        Assert.Equal(shapes.Count, prisms.Count);
        Assert.False(t.Note.IsVisible);

        // The port's prism, turned from Y up to Z up: (x, y, z) becomes (x, −z, y).
        PropGenerator.Nature.Kernel.PropMesh first = PropGenerator.Nature.Export.Collision.Prism(shapes[0], new PropGenerator.Nature.Kernel.Vec3d(0, 0, 0));
        Assert.Equal(first.Position[0], prisms.Positions[0], 5);
        Assert.Equal(-first.Position[2], prisms.Positions[1], 5);
        Assert.Equal(first.Position[1], prisms.Positions[2], 5);

        // Each prism outlines its two rings and its sides (the edges three's EdgesGeometry keeps): 3 × 8 lines.
        int sides = PropGenerator.Nature.Export.Collision.Sides;
        Assert.Equal(shapes.Count * sides * 3 * 2 * 3, prisms.Lines.Length);

        Click(t.Collision);
        Assert.Null(w.View.Prisms);
        Assert.False(t.Note.IsVisible);
        w.Close();
    }

    // The shapes are worked out off the window's thread (beside each build while they are shown, or once when they are
    // turned on); one worked out for settings no longer shown is dropped, so the view ends on the rock's own.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void CollisionShapesAreWorkedOutOffTheWindowsThread()
    {
        MainWindow w = WithBoulder();
        var onUi = new System.Collections.Concurrent.ConcurrentQueue<bool>();
        using var gate = new ManualResetEventSlim(true);
        w.Rocks.ShapeBuilder = settings =>
        {
            onUi.Enqueue(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
            gate.Wait(10000);
            return RockRecipe.Collision(settings);
        };

        void Seed(long seed)
        {
            Assert.Null(w.Panel.SetFromOutside("seed", System.Text.Json.JsonSerializer.SerializeToElement(seed)));
            Ui.Pump();
        }

        bool Settled() => w.Rocks.ShownSettings == w.Panel.Current && w.Rocks.ShapesOf == w.Panel.Current && w.View.Prisms is not null;

        // Not shown: none are worked out.
        Seed(11);
        Assert.True(Waited(() => w.Rocks.ShownSettings == w.Panel.Current));
        Assert.Empty(onUi);

        // Turned on: the rock shown gets its shapes, worked out on the thread pool.
        Click(w.Toolbar.Collision);
        Assert.True(Waited(Settled));
        Assert.Single(onUi);

        // Each build while they are shown brings its own, off the window's thread.
        Seed(12);
        Assert.True(Waited(Settled));
        Assert.Equal(2, onUi.Count);

        // Held back while the settings move on twice: the view ends on the newest rock's shapes.
        gate.Reset();
        Seed(13);
        Seed(14);
        Ui.Wait(100);
        gate.Set();
        Assert.True(Waited(Settled));
        Assert.DoesNotContain(true, onUi);
        Assert.Same(w.Rocks.Shapes, w.View.Prisms);
        Settings last = w.Panel.Current;
        Assert.Equal(EngineKit.OnThePool(() => RockRecipe.Collision(last)).Count, w.View.Prisms!.Count);
        Assert.False(w.Toolbar.Note.IsVisible);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void CameraAngleMenuSetsTheFourViews()
    {
        MainWindow w = Ui.Open(new FakePainter());
        ViewToolbar t = w.Toolbar;
        OrbitCamera cam = w.View.Orbit;
        Assert.Equal("Camera angle: look from the front, side, top or the usual three-quarter view.", Tip(t.Camera.Trigger));
        Assert.Equal("camera", t.Camera.Trigger.IconName);
        cam.Place(new Vector3(1, 2, 0.5f), 7);
        t.Camera.Open();
        Assert.Equal(["Perspective", "Front", "Side", "Top"], t.Camera.Lines);
        Assert.All(t.Camera.Rows, r => Assert.Null(r.Help));

        void Pick(string label)
        {
            t.Camera.Open();
            Click(t.Camera.Rows.Single(r => r.Label == label));
            Assert.Equal(new Vector3(1, 2, 0.5f), cam.Target);
            Assert.Equal(7, cam.Distance, 4);
        }

        Pick("Front");
        Assert.Equal(0, cam.Theta, 4);
        Assert.Equal(OrbitCamera.MaxPolar, cam.Phi, 4);
        Pick("Side");
        Assert.Equal(MathF.PI / 2, cam.Theta, 4);
        Assert.Equal(OrbitCamera.MaxPolar, cam.Phi, 4);
        Pick("Top");
        Assert.Equal(OrbitCamera.MinPolar, cam.Phi, 4);
        Pick("Perspective");
        Vector3 back = cam.Back, want = Vector3.Normalize(new Vector3(-0.62f, -0.62f, 0.36f));
        Assert.True(Vector3.Distance(want, back) < 1e-4, $"{back}");
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MaximizeSurvivesARecipeSwitch()
    {
        MainWindow w = Ui.Open(new FakePainter());
        ViewToolbar t = w.Toolbar;
        Assert.Equal("Maximize (Ctrl+Space): let the 3D view fill the window: the panel, the drawer and the status bar go until you press it again.", Tip(t.Maximize));
        Assert.Equal("maximize", t.Maximize.IconName);
        Click(t.Maximize);
        Assert.True(w.ViewState.Maximized);
        Assert.True(t.Maximize.Pressed);
        Assert.Equal("minimize", t.Maximize.IconName);
        Assert.Equal("Restore the panels (Ctrl+Space): bring back the panel, the drawer and the status bar as they were.", Tip(t.Maximize));
        Assert.Equal("Restore the panels", Name(t.Maximize));

        void StillMaximized()
        {
            Assert.True(w.ViewState.Maximized);
            Assert.False(w.SidePanel.IsVisible);
            Assert.False(w.Edge.IsVisible);
            Assert.False(w.StatusBar.IsVisible);
            Assert.False(w.Drawer.Card.IsVisible);
            Assert.True(t.Right.IsVisible);
            Assert.True(t.Compass.IsVisible);
        }

        StillMaximized();
        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(Boulder), 30000));
        Assert.True(Waited(() => w.View.ShowingRock && w.Rocks.Mesh is not null));
        Ui.Wait(100);
        StillMaximized();
        Assert.True(ClaudeKit.Run(w.Files.NewAsync(), 30000));
        Ui.Wait(100);
        StillMaximized();

        // Restored, everything comes back as it was before.
        Click(t.Maximize);
        Assert.False(w.ViewState.Maximized);
        Assert.True(w.SidePanel.IsVisible);
        Assert.True(w.Edge.IsVisible);
        Assert.True(w.StatusBar.IsVisible);
        Assert.True(w.Drawer.Card.IsVisible);
        Assert.Equal("Maximize", Name(t.Maximize));

        // A hidden panel stays hidden through a maximize and back.
        Key(w, PhysicalKey.Backslash, RawInputModifiers.Control);
        Click(t.Maximize);
        Click(t.Maximize);
        Assert.False(w.SidePanel.IsVisible);
        Assert.True(w.StatusBar.IsVisible);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EveryViewControlHasATooltip()
    {
        MainWindow w = Ui.Open(new FakePainter());
        ViewToolbar t = w.Toolbar;
        Assert.Equal("Shading: how the prop is drawn — lit, flat or as its triangles.", Tip(t.Shading.Trigger));
        Assert.Equal("Detail level shown: full detail, or the lighter levels an export writes.", Tip(t.Lod.Trigger));
        Assert.Equal("Scene around the prop: a plain studio, the studio under a blue sky, or neutral grey.", Tip(t.Scene.Trigger));
        Assert.Equal("Camera angle: look from the front, side, top or the usual three-quarter view.", Tip(t.Camera.Trigger));
        Assert.Equal("Frame (F): move the camera so the whole piece fits in the view, looking from where it looks now.", Tip(t.Frame));
        Assert.Equal("Stats: show a card with the piece's faces, draw calls and size.", Tip(t.Stats));
        Assert.Equal("A person for scale: stand a 1.8 m person beside the piece, to judge its size.", Tip(t.Person));
        Assert.Equal("What you can walk into: show the shapes a player bumps into, drawn over the piece.", Tip(t.Collision));

        static void AllTipped(Visual root)
        {
            foreach (Control c in root.GetVisualDescendants().OfType<Control>().Where(c => c.TemplatedParent is null && c is Button or ToggleSwitch or Slider or TextBox or SunDial or Compass))
            {
                Assert.False(string.IsNullOrWhiteSpace(Tip(c)), $"{c.GetType().Name} '{Name(c)}' has no tooltip");
            }
        }

        AllTipped(t.Overlay);
        foreach (ToolButton b in t.Left.Tools.Concat(t.GroupA.Tools).Concat(t.GroupB.Tools).Cast<ToolButton>().Where(b => b.Unavailable is null))
        {
            Assert.True(Tip(b).Length >= 40, Tip(b));
            Assert.NotEqual(Name(b), Tip(b));
        }

        Assert.False(string.IsNullOrWhiteSpace(Tip(t.Compass)));
        Click(t.Sun.Trigger);
        AllTipped(t.Sun.Content);
        Assert.False(string.IsNullOrWhiteSpace(Tip(t.Sun.Dial)));
        Assert.All(t.Sun.Times, b => Assert.True(Tip(b).Length >= 20, b.Content as string));
        t.Sun.Close();
        Click(t.Wind.Trigger);
        AllTipped(t.Wind.Content);
        t.Wind.Close();
        Assert.Equal("The floor tile stays level", Tip(t.Slope.Trigger));
        t.Slope.Open();
        AllTipped(t.Slope.Content);
        t.Slope.Close();
        foreach (ToolMenu menu in new[] { t.Shading, t.Lod, t.Scene, t.Camera })
        {
            menu.Open();
            Assert.All(menu.Rows, r => Assert.True(Tip(r).Length >= 10, r.Label));
            menu.Close();
        }

        w.Close();
    }

    // When the view's tools wrap onto a second line (the window at its narrowest, the drawer docked on the left), the
    // stats card and the collision note sit 8 under the last line, never over the tools (the Stats toggle included).
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void StatsCardAndNoteStayUnderWrappedTools()
    {
        MainWindow w = Ui.Open(new FakePainter());
        ViewToolbar t = w.Toolbar;
        w.Width = MainWindow.MinimumWidth;
        w.ViewState.DrawerLeft = true;
        w.ViewState.DrawerOpen = true;
        Ui.Pump();
        Click(t.Stats);
        Assert.True(t.StatsCard.IsVisible);
        w.ViewState.ShowCollision = true;
        Ui.Pump();
        Point a = t.GroupA.TranslatePoint(default, w.View)!.Value, b = t.GroupB.TranslatePoint(default, w.View)!.Value;
        Assert.True(b.Y > a.Y + 20, $"the tools wrap here: {a} {b}");
        double toolsBottom = t.Right.TranslatePoint(new Point(0, t.Right.Bounds.Height), w.View)!.Value.Y;
        Point card = t.StatsCard.TranslatePoint(default, w.View)!.Value;
        Assert.Equal(toolsBottom + 8, card.Y, 1);
        if (t.Note.IsVisible)
        {
            Point note = t.Note.TranslatePoint(default, w.View)!.Value;
            Assert.True(note.Y >= toolsBottom + 8 - 0.5, $"note {note.Y} under tools {toolsBottom}");
        }

        // Back to one line: the card goes back up under it.
        w.ViewState.DrawerLeft = false;
        w.Width = 1600;
        Ui.Pump();
        Ui.Pump();
        card = t.StatsCard.TranslatePoint(default, w.View)!.Value;
        Assert.Equal(54, card.Y, 1);
        w.Close();
    }

    // With the drawer docked on the left the view narrows; the right-hand tools wrap before they reach the left tools,
    // never drawn over them (seen at 1600 with a 460 drawer: Normal / Low-poly sat on the Resize tool).
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RightToolsNeverCoverTheLeftTools()
    {
        foreach (double wide in new double[] { 220, 380, 460, 560 })
        {
            using var s = new Scratch();
            new Prefs(s.P("prefs.json")) { DrawerLeft = true, DrawerWidth = wide, DrawerOpen = true }.Save();
            MainWindow w = BridgeKit.Open(s);
            ViewToolbar t = w.Toolbar;
            t.Style.IsVisible = true; // as on a tree: Normal / Low-poly shows too
            w.Width = 1600;
            Ui.Pump();
            Ui.Pump();
            Rect left = new(t.Left.TranslatePoint(default, w.View)!.Value, t.Left.Bounds.Size);
            foreach (Control g in t.Right.Children)
            {
                Rect r = new(g.TranslatePoint(default, w.View)!.Value, g.Bounds.Size);
                bool sameBand = r.Top < left.Bottom && r.Bottom > left.Top;
                Assert.False(sameBand && r.Left < left.Right + 8, $"drawer {wide}: a tool group at {r} is over the left tools at {left}");
            }

            w.Close();
        }
    }
}
