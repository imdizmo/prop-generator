using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Details;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

/// <summary>The controls panel as easy-props' Details: its rows, groups, filter, header, footer and edge.</summary>
public class PanelTests
{
    private static StartingPoint Boulder => RockRecipe.StartingPoints.Single(p => p.Id == "boulder");

    private static JsonElement J(object v) => JsonSerializer.SerializeToElement(v);

    private static MainWindow Rock(Scratch? scratch = null)
    {
        MainWindow w = scratch is null ? Ui.Open(new FakePainter()) : Ui.Open(new FakePainter(), new FakeDialogs(), scratch);
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Ui.Pump();
        return w;
    }

    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
    }

    private static void Key(MainWindow w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPressQwerty(key, mods);
        Ui.Pump();
    }

    private static Point Centre(Visual v, Visual root) => v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), root)!.Value;

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RowsFollowREFsGrid()
    {
        MainWindow w = Rock();
        NumberRow width = w.Panel.NumberRowFor("width")!;
        Assert.Equal("Width", width.Label.Text);
        Assert.Equal(RockTooltips.For("width"), ToolTip.GetTip(width.Label));
        Assert.Equal(PlacementMode.Left, ToolTip.GetPlacement(width.Label));

        // Label, slider, number field (56) and reset slot (18), 6 apart, the label flexing with the panel: the design's
        // slider row, minmax(88px, 36%).
        Assert.Equal(4, width.ColumnDefinitions.Count);
        Assert.Equal(56, width.ColumnDefinitions[2].Width.Value);
        Assert.Equal(18, width.ColumnDefinitions[3].Width.Value);
        Assert.Equal(6, width.ColumnSpacing);
        Assert.True(width.Bounds.Height >= 28);
        double label = width.ColumnDefinitions[0].Width.Value;
        Assert.True(width.Label.Bounds.Width <= label);
        Assert.Equal(Math.Max(88, Math.Round(width.Bounds.Width * 0.36)), label, 0);
        Assert.Equal(PanelRow.LabelWidth(width.Bounds.Width), label, 0);
        Assert.Equal(0, Grid.GetColumn(width.Label));
        Assert.Equal(1, Grid.GetColumn(width.Slider));
        Assert.Equal(2, Grid.GetColumn(width.Field));
        Assert.Equal(3, Grid.GetColumn(width.Reset));

        // Decimals by step: 0.1 shows one, 0.05 two, a whole number none; metres say so, a count does not.
        Assert.Equal(w.Panel.Current.GetFloat("width").ToString("F1", CultureInfo.InvariantCulture), width.Box.Text);
        Assert.Equal("m", width.Unit!.Text);
        NumberRow faces = w.Panel.NumberRowFor("faces")!;
        Assert.Equal(w.Panel.Current.GetFloat("faces").ToString("F2", CultureInfo.InvariantCulture), faces.Box.Text);
        Assert.Null(faces.Unit);
        NumberRow chunks = w.Panel.NumberRowFor("chunks")!;
        Assert.Equal(w.Panel.Current.GetInt("chunks").ToString(CultureInfo.InvariantCulture), chunks.Box.Text);
        Assert.Null(chunks.Unit);
        Assert.Equal(1, chunks.Slider.SmallChange);

        // The number field: 24 high, a well, right-aligned.
        Assert.Equal(24, width.Field.Height);
        Assert.Equal(Look.Theme.SurfaceSunken, width.Field.Background);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, width.Box.HorizontalContentAlignment);
        w.Close();

        // The floor's light along the plank (step 0.005) shows three decimals, so every step reads exactly; its grain
        // lines (whole) none, both as sliders.
        MainWindow floor = Ui.Open(new FakePainter());
        Assert.Equal("0.080", floor.Panel.NumberRowFor("lit")!.Box.Text);
        Assert.Equal("4", floor.Panel.NumberRowFor("creases")!.Box.Text);
        Assert.IsType<Slider>(floor.Panel.NumberRowFor("creases")!.Slider);
        floor.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ResetShowsOnlyWhenChangedAndRestoresThePresetValue()
    {
        MainWindow w = Rock();
        NumberRow width = w.Panel.NumberRowFor("width")!;
        ChoiceRow stone = w.Panel.ChoiceRowFor("palette")!;
        double preset = Boulder.Settings.GetFloat("width");
        Assert.All(w.Panel.Rows, r => Assert.DoesNotContain(r.Children.OfType<Button>(), b => b.IsVisible && AutomationProperties.GetName(b)!.StartsWith("Reset", StringComparison.Ordinal)));
        Assert.Equal("Reset Width", AutomationProperties.GetName(width.Reset));
        Assert.StartsWith("Reset", ToolTip.GetTip(width.Reset) as string, StringComparison.Ordinal);

        Assert.Null(w.Panel.SetFromOutside("width", J(preset + 1)));
        Assert.True(width.Reset.IsVisible);
        Assert.False(stone.Reset.IsVisible);
        int steps = w.Files.History.Steps.Count;

        Click(width.Reset);
        Assert.Equal(preset, w.Panel.Current.GetFloat("width"), 4);
        Assert.False(width.Reset.IsVisible);
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);

        // A choice resets the same way.
        string other = RockRecipe.Specs.Single(s => s.Key == "palette").Options!.First(o => o.Value != Boulder.Settings.GetString("palette")).Value;
        Assert.Null(w.Panel.SetFromOutside("palette", J(other)));
        Assert.True(stone.Reset.IsVisible);
        Click(stone.Reset);
        Assert.Equal(Boulder.Settings.GetString("palette"), w.Panel.Current.GetString("palette"));
        Assert.False(stone.Reset.IsVisible);

        // Undo brings the change back, and its reset with it.
        w.Files.Undo();
        Ui.Pump();
        Assert.True(stone.Reset.IsVisible);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TypedOutOfRangeClampsAndSaysKeptTo()
    {
        MainWindow w = Rock();
        NumberRow width = w.Panel.NumberRowFor("width")!;
        int steps = w.Files.History.Steps.Count;
        width.Box.Text = "99";
        w.Panel.CommitValueBox("width");
        Assert.Equal(40, w.Panel.Current.GetFloat("width"), 4);
        Assert.Equal("40.0", width.Box.Text);
        Assert.True(width.Note.IsVisible);
        Assert.Equal("Kept to 0.2–40.0 m", width.Note.Text);
        Assert.Equal(Look.Theme.Warning, width.Note.Foreground);
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);

        // A unit or a comma is forgiven; text with no digit is no number.
        width.Box.Text = "5,5 m";
        w.Panel.CommitValueBox("width");
        Assert.Equal(5.5, w.Panel.Current.GetFloat("width"), 4);
        width.Box.Text = "big";
        w.Panel.CommitValueBox("width");
        Assert.Equal(5.5, w.Panel.Current.GetFloat("width"), 4);
        Assert.Equal("5.5", width.Box.Text);

        // The note goes after 2.6 s.
        Ui.Wait(2900);
        Assert.False(width.Note.IsVisible);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ShiftDragIsFiveTimesFiner()
    {
        MainWindow w = Rock();
        w.Width = 1600;
        Ui.Pump();
        Slider slider = w.Panel.NumberRowFor("width")!.Slider;
        double y = slider.Bounds.Height / 2;
        Point At(double x) => slider.TranslatePoint(new Point(x, y), w)!.Value;
        double from = 20, to = slider.Bounds.Width - 20;

        double Drag(RawInputModifiers mods)
        {
            w.MouseMove(At(from));
            w.MouseDown(At(from), MouseButton.Left);
            Ui.Pump();
            double start = w.Panel.Current.GetFloat("width");
            w.MouseMove(At(from), mods);
            Ui.Pump();
            for (int i = 1; i <= 10; i++)
            {
                w.MouseMove(At(from + ((to - from) * i / 10)), mods);
                Ui.Pump();
            }

            double moved = w.Panel.Current.GetFloat("width") - start;
            w.MouseUp(At(to), MouseButton.Left, mods);
            Ui.Pump();
            return moved;
        }

        double plain = Drag(RawInputModifiers.None);
        double fine = Drag(RawInputModifiers.Shift);
        Assert.True(plain > 5, $"a plain drag moved {plain}");
        // A fifth of the travel from where the value first moved with Shift held (one plain step in), on the step.
        Assert.InRange(fine / plain, 0.15, 0.21);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ArrowsNudgeByStepAndShiftByTen()
    {
        MainWindow w = Rock();
        NumberRow width = w.Panel.NumberRowFor("width")!;
        double start = w.Panel.Current.GetFloat("width");
        int steps = w.Files.History.Steps.Count;
        width.Box.Focus();
        Ui.Pump();
        Assert.True(width.Box.IsFocused);

        Key(w, PhysicalKey.ArrowUp);
        Assert.Equal(start + 0.1, w.Panel.Current.GetFloat("width"), 4);
        Key(w, PhysicalKey.ArrowUp, RawInputModifiers.Shift);
        Assert.Equal(start + 1.1, w.Panel.Current.GetFloat("width"), 4);
        Key(w, PhysicalKey.ArrowDown);
        Assert.Equal(start + 1.0, w.Panel.Current.GetFloat("width"), 4);
        Assert.Equal((start + 1.0).ToString("F1", CultureInfo.InvariantCulture), width.Box.Text);
        Assert.Equal(steps + 3, w.Files.History.Steps.Count);

        // Esc leaves what was typed and keeps the value.
        width.Box.Text = "12";
        Key(w, PhysicalKey.Escape);
        Assert.Equal(start + 1.0, w.Panel.Current.GetFloat("width"), 4);
        Assert.Equal((start + 1.0).ToString("F1", CultureInfo.InvariantCulture), width.Box.Text);
        Assert.False(width.Box.IsFocused);
        Assert.Equal(steps + 3, w.Files.History.Steps.Count);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void LeavingTheNumberFieldAfterAStepKeepsTheExactValue()
    {
        MainWindow w = Ui.Open(new FakePainter());
        w.Panel.GroupFor("lit")!.IsOpen = true;
        Ui.Pump();
        NumberRow lit = w.Panel.NumberRowFor("lit")!;
        int steps = w.Files.History.Steps.Count;

        // Only visiting the field changes nothing.
        lit.Box.Focus();
        Ui.Pump();
        lit.Slider.Focus();
        Ui.Pump();
        Assert.Equal(steps, w.Files.History.Steps.Count);

        // ↑ to 0.085, then leaving: still 0.085, one step.
        lit.Box.Focus();
        Ui.Pump();
        Key(w, PhysicalKey.ArrowUp);
        Assert.Equal("0.085", lit.Box.Text);
        lit.Slider.Focus();
        Ui.Pump();
        Assert.False(lit.Box.IsFocused);
        Assert.Equal(0.085, w.Panel.Current.Values["lit"].GetDouble(), 6);
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal("Light along plank 0.085", w.Files.History.Steps[^1].Label);

        // A reveal focuses the field; leaving it again takes no step either.
        Assert.True(w.RevealSetting("lit"));
        Ui.Pump();
        Assert.True(lit.Box.IsFocused);
        lit.Slider.Focus();
        Ui.Pump();
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheChoiceMenuChecksTheCurrentOption()
    {
        MainWindow w = Rock();
        ChoiceRow stone = w.Panel.ChoiceRowFor("palette")!;
        w.Panel.GroupFor("palette")!.IsOpen = true;
        Ui.Pump();
        Assert.Contains(stone.Combo.GetVisualDescendants().OfType<Look.Icon>(), i => i.IsEffectivelyVisible);
        Assert.Empty(stone.Combo.GetVisualDescendants().OfType<Avalonia.Controls.PathIcon>());

        stone.Combo.IsDropDownOpen = true;
        Ui.Pump();
        string current = RockRecipe.Specs.Single(s => s.Key == "palette").Options!.Single(o => o.Value == w.Panel.Current.GetString("palette")).Label;
        Assert.Equal(current, stone.Checked);
        stone.Combo.IsDropDownOpen = false;
        Ui.Pump();

        // Another choice moves the check, the next time the menu is shown too.
        Assert.Null(w.Panel.SetFromOutside("palette", J("granite")));
        stone.Combo.IsDropDownOpen = true;
        Ui.Pump();
        Assert.Equal("Granite", stone.Checked);
        stone.Combo.IsDropDownOpen = false;
        Ui.Pump();
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AFilterIsClearedWhenTheKindChanges()
    {
        MainWindow w = Ui.Open(new FakePainter());
        w.Panel.Filter.Text = "grain";
        Ui.Pump();
        Assert.True(w.Panel.NoMatch.IsVisible);

        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Ui.Pump();
        Assert.Equal(string.Empty, w.Panel.Filter.Text);
        Assert.False(w.Panel.NoMatch.IsVisible);
        Assert.All(w.Panel.Groups, g => Assert.True(g.IsVisible));
        Assert.True(w.Panel.Groups.Single(g => g.Title == "Shape").IsOpen);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ShowMeMoreFollowsItsCommand()
    {
        string? reason = "Comes soon: nine versions side by side";
        string? breaks = "Select a part first";
        static Task Nothing() => Task.CompletedTask;
        Commands.Command[] commands =
        [
            new("surprise", "Surprise me", "Prop", "R", "dices", () => null, () => null, null, Nothing),
            new("show-more", "Show me more", "Prop", null, "layout-grid", () => reason, () => null, null, Nothing),
            new("break-part", "Break branch", "Edit", null, "zap", () => breaks, () => null, null, Nothing),
            new("make-pieces", "Make pieces from this tree…", "Prop", null, "axe", () => "Only a painted tree makes pieces", () => null, null, Nothing),
        ];
        var head = new PanelHeader(new FilterBox(), commands);
        Assert.False(head.Break.IsVisible);
        breaks = null;
        head.RefreshBreak();
        Assert.True(head.Break.IsVisible);
        Assert.False(head.ShowMore.IsEnabled);
        Assert.Equal("Show me more. Comes soon: nine versions side by side.", ToolTip.GetTip(head.ShowMore));

        reason = null;
        head.Refresh();
        Assert.True(head.ShowMore.IsEnabled);
        Assert.Equal("Show me more: nine versions side by side", ToolTip.GetTip(head.ShowMore));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FilterOpensMatchingGroupsAndHidesTheRest()
    {
        MainWindow w = Rock();
        SettingsGroup shape = w.Panel.Groups.Single(g => g.Title == "Shape");
        SettingsGroup surface = w.Panel.Groups.Single(g => g.Title == "Surface");
        Assert.True(shape.IsOpen);
        Assert.False(surface.IsOpen);

        // Moss is weather since easy-props 0.4.5, beside where it grows; Rubble's tooltip names the Mossy rubble.
        SettingsGroup weather = w.Panel.Groups.Single(g => g.Title == "Weather");
        Assert.False(weather.IsOpen);
        w.Panel.Filter.Box.Focus();
        w.Panel.Filter.Text = "MOSS";
        Ui.Pump();
        Assert.Equal(["rubble"], shape.Body.Children.Where(c => c.IsVisible).Select(c => ((PanelRow)c).Spec.Key));
        Assert.False(surface.IsVisible);
        Assert.True(weather.IsVisible);
        Assert.True(weather.IsOpen);
        Assert.Equal(["moss", "mossFrom"], weather.Body.Children.Where(c => c.IsVisible).Select(c => ((PanelRow)c).Spec.Key));
        Assert.True(w.Panel.Filter.Clear.IsVisible);

        // A group's name matches all of it, but for the rows the rock has no use for (its own stone colours until
        // the stone is My own colours).
        w.Panel.Filter.Text = "surf";
        Ui.Pump();
        Assert.Equal(
            surface.Body.Children.Count(c => !((PanelRow)c).Spec.Key.StartsWith("stone", StringComparison.Ordinal)),
            surface.Body.Children.Count(c => c.IsVisible));

        // Esc clears it, and the groups are as they were.
        Key(w, PhysicalKey.Escape);
        Assert.Equal(string.Empty, w.Panel.Filter.Text);
        Assert.True(shape.IsVisible && shape.IsOpen);
        Assert.True(surface.IsVisible);
        Assert.False(surface.IsOpen);
        Assert.True(weather.IsVisible);
        Assert.False(weather.IsOpen);
        Assert.False(w.Panel.NoMatch.IsVisible);
        Assert.False(w.Panel.Filter.Clear.IsVisible);
        Assert.Equal("Filter controls", w.Panel.Filter.Box.PlaceholderText);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FilterWithNoMatchSaysSo()
    {
        MainWindow w = Rock();
        w.Panel.Filter.Text = "zzz";
        Ui.Pump();
        Assert.All(w.Panel.Groups, g => Assert.False(g.IsVisible));
        Assert.True(w.Panel.NoMatch.IsVisible);
        Assert.Equal("Nothing else matches. Try a shorter word, or select the whole rock.", w.Panel.NoMatch.Text);
        Click(w.Panel.Filter.Clear);
        Assert.False(w.Panel.NoMatch.IsVisible);
        Assert.All(w.Panel.Groups, g => Assert.True(g.IsVisible));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void GroupsAreRememberedPerKind()
    {
        Scratch s = Ui.NewScratch();
        MainWindow w = Rock(s);
        SettingsGroup Group(string title) => w.Panel.Groups.Single(g => g.Title == title);
        Click(Group("Surface").Head);
        Click(Group("Shape").Head);
        Assert.True(Group("Surface").IsOpen);
        Assert.False(Group("Shape").IsOpen);

        // The floor keeps its own.
        CommandListTests.Run(w, "new-floor");
        Ui.Pump();
        Assert.True(Group("Wood").IsOpen);
        Assert.True(Group("Carving").IsOpen);
        Click(Group("Grain").Head);

        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Ui.Pump();
        Assert.True(Group("Surface").IsOpen);
        Assert.False(Group("Shape").IsOpen);
        w.Close();

        var again = new Prefs(s.P("prefs.json"));
        Assert.Equal(["Surface"], again.OpenGroups[RockRecipe.Id]);
        Assert.Equal(["Wood", "Carving", "Grain"], again.OpenGroups[FloorRecipe.Id]);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FloorHasFiveGroupsWoodAndCarvingOpen()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.Equal(["Wood", "Carving", "Planks", "Grain", "Edges"], w.Panel.Groups.Select(g => g.Title));
        Assert.Equal(["Wood", "Carving"], w.Panel.Groups.Where(g => g.IsOpen).Select(g => g.Title));
        Assert.Equal("Floor tile", w.PanelHead.Title.Text);
        Assert.Equal(32, w.Panel.Groups[0].Head.Height);
        Assert.Equal(new Thickness(0, 1, 0, 0), w.Panel.Groups[0].BorderThickness);
        Assert.Equal(["colour", "contrast", "drift"], w.Panel.Groups[0].Body.Children.Select(Key));
        Assert.Equal(17, w.Panel.Rows.Count);

        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Assert.Equal("Rock", w.PanelHead.Title.Text);
        Assert.Equal(["Shape", "Surface", "Weather", "Damage"], w.Panel.Groups.Select(g => g.Title));
        Assert.Equal(["Shape"], w.Panel.Groups.Where(g => g.IsOpen).Select(g => g.Title));
        w.Close();

        static string Key(Control row) => row switch
        {
            NumberRow n => n.Spec.Key,
            ColourRow c => c.Spec.Key,
            _ => "?",
        };
    }

    // The header and the resets follow a new entry at once, not only once its first rock is built: while a slow build
    // runs, the header already names the rock and no reset shows on the preset's own values.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheHeaderAndResetsFollowTheEntryBeforeItsFirstBuild()
    {
        MainWindow w = Ui.Open(new FakePainter());
        using var gate = new ManualResetEventSlim(false);
        w.Rocks.Builder = settings =>
        {
            gate.Wait(10000);
            return RockRecipe.Build(settings);
        };
        StartingPoint cliff = RockRecipe.StartingPoints.Single(p => p.Name == "Cliff face");
        Task<bool> starting = w.Files.StartFromAsync(cliff);
        Ui.Pump();

        Assert.False(starting.IsCompleted);
        Assert.Null(w.Rocks.Mesh);
        Assert.Equal("Rock", w.PanelHead.Title.Text);
        Assert.Contains(w.Panel.Specs, s => w.Panel.NumberRowFor(s.Key) is not null
            && Math.Abs(cliff.Settings.Values[s.Key].GetDouble() - s.Default.GetDouble()) > (s.Step ?? 0.01));
        Assert.All(w.Panel.Specs.Where(s => w.Panel.NumberRowFor(s.Key) is not null), s => Assert.False(w.Panel.NumberRowFor(s.Key)!.Reset.IsVisible, s.Key));

        gate.Set();
        Assert.True(Run(starting));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void WoodColourIsASwatchRow()
    {
        MainWindow w = Ui.Open(new FakePainter());
        ColourRow row = w.Panel.ColourRow!;
        Assert.Equal(["Birch", "Ash", "Oak", "Cherry", "Walnut", "Ebony"], row.Swatches.Select(AutomationProperties.GetName));
        Assert.All(row.Swatches, b => Assert.StartsWith(AutomationProperties.GetName(b)!, ToolTip.GetTip(b) as string, StringComparison.Ordinal));
        Assert.Contains(WoodSwatches.All, x => x.Hex == "#45302B");
        Assert.Equal("Walnut", row.Current);
        Assert.StartsWith("Pick any colour", ToolTip.GetTip(row.Custom) as string, StringComparison.Ordinal);

        int steps = w.Files.History.Steps.Count;
        Click(row.Swatches[2]);
        Assert.Equal(Rgb.FromHex("#A07A4E"), w.Panel.Current.GetColour("colour"));
        Assert.Equal("Oak", row.Current);
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal("Wood colour #A07A4E", w.Files.History.Steps[^1].Label);
        Assert.Equal("#A07A4E", w.Panel.ValueBoxFor("colour").Text);

        // The custom circle opens the colour square with the #RRGGBB box; a colour typed there is none of the woods.
        row.OpenPicker();
        Ui.Pump();
        Assert.True(row.Picker.IsEffectivelyVisible);
        Assert.True(row.HexBox.IsEffectivelyVisible);
        row.HexBox.Text = "#336699";
        w.Panel.CommitValueBox("colour");
        Assert.Equal(Rgb.FromHex("#336699"), w.Panel.Current.GetColour("colour"));
        Assert.Null(row.Current);
        Assert.Equal(Avalonia.Media.Color.Parse("#336699"), row.Picker.Color);
        row.Custom.Flyout!.Hide();
        Ui.Pump();
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheSeedIsNotARowAnymore()
    {
        MainWindow floor = Ui.Open(new FakePainter());
        Assert.Null(floor.Panel.RowFor("seed"));
        Assert.Contains(floor.Panel.Specs, s => s.Key == "seed");
        Assert.Null(floor.Panel.SetFromOutside("seed", J(5)));
        Assert.Equal(5, floor.Panel.Current.GetInt("seed"));
        Assert.Equal("Seed 5", floor.Files.History.Steps[^1].Label);
        floor.Close();

        MainWindow w = Rock();
        Assert.Null(w.Panel.RowFor("seed"));
        Assert.Equal(RockRecipe.Specs.Count - 1, w.Panel.Rows.Count);
        Assert.True(w.Panel.Current.Values.ContainsKey("seed"));
        Assert.Null(w.Panel.SetFromOutside("seed", J(99)));
        Assert.Equal(99u, w.Panel.Current.GetSeed("seed"));
        Assert.False(w.Panel.Reveal("seed"));
        Assert.DoesNotContain(
            w.SidePanel.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "Seed" && t.IsEffectivelyVisible);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RestoreToPresetButtonIsOneStep()
    {
        MainWindow w = Rock();
        Assert.Null(w.Panel.SetFromOutside("moss", J(0.1)));
        Assert.Null(w.Panel.SetFromOutside("seed", J(7)));
        Settings changed = w.Panel.Current;
        int steps = w.Files.History.Steps.Count;
        Assert.Equal("Restore to preset", ((TextBlock)w.PanelFoot.Restore.Content!).Text);
        Assert.Equal(32, w.PanelFoot.Height);

        Click(w.PanelFoot.Restore);

        Assert.Equal(Boulder.Settings, w.Panel.Current);
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal("Restore to preset", w.Files.History.Steps[^1].Label);
        Assert.False(w.Panel.NumberRowFor("moss")!.Reset.IsVisible);
        w.Files.Undo();
        Assert.Equal(changed, w.Panel.Current);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SurpriseMeInTheHeaderIsTheCommand()
    {
        MainWindow w = Rock();
        uint seed = w.Panel.Current.GetSeed("seed");
        Assert.Equal("Try a different one with the same shape (R)", ToolTip.GetTip(w.PanelHead.Surprise));
        Click(w.PanelHead.Surprise);
        Assert.NotEqual(seed, w.Panel.Current.GetSeed("seed"));
        Assert.Equal("Surprise me", w.Files.History.Steps[^1].Label);
        Assert.True(w.PanelHead.ShowMore.IsEnabled);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void PanelEdgeResizesWithinLimitsAndHides()
    {
        Scratch s = Ui.NewScratch();
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        Assert.Equal(320, w.SidePanel.Bounds.Width);
        Assert.Equal(1, w.Edge.Bounds.Width);
        // Above the tab, which sits over the handle's middle.
        Point Grab() => w.Edge.Handle.TranslatePoint(new Point(w.Edge.Handle.Bounds.Width / 2, 40), w)!.Value;
        Point grab = Grab();

        void DragBy(double dx)
        {
            Point p = Grab();
            w.MouseMove(p);
            w.MouseDown(p, MouseButton.Left);
            w.MouseMove(new Point(p.X + dx, p.Y));
            w.MouseUp(new Point(p.X + dx, p.Y), MouseButton.Left);
            Ui.Pump();
        }

        DragBy(-100);
        Assert.Equal(420, w.SidePanel.Bounds.Width);
        DragBy(-500);
        Assert.Equal(520, w.SidePanel.Bounds.Width);
        DragBy(1000);
        Assert.Equal(240, w.SidePanel.Bounds.Width);
        Assert.Equal(240, new Prefs(s.P("prefs.json")).PanelWidth);

        // ← and → move it 16 px.
        w.Edge.Handle.Focus();
        Key(w, PhysicalKey.ArrowLeft);
        Assert.Equal(256, w.SidePanel.Bounds.Width);

        // The tab hides the panel and shows it again; remembered.
        Assert.StartsWith("Hide details", ToolTip.GetTip(w.Edge.Tab) as string, StringComparison.Ordinal);
        Assert.Equal(13, w.Edge.Tab.Bounds.Width);
        Assert.Equal(44, w.Edge.Tab.Bounds.Height);
        w.MouseDown(Centre(w.Edge.Tab, w), MouseButton.Left);
        w.MouseUp(Centre(w.Edge.Tab, w), MouseButton.Left);
        Ui.Pump();
        Assert.False(w.SidePanel.IsVisible);
        Assert.True(w.Edge.IsVisible);
        Assert.False(w.Edge.Handle.IsVisible);
        Assert.False(new Prefs(s.P("prefs.json")).PanelOpen);
        Assert.StartsWith("Show details", ToolTip.GetTip(w.Edge.Tab) as string, StringComparison.Ordinal);
        Click(w.Edge.Tab);
        Assert.True(w.SidePanel.IsVisible);
        Assert.True(new Prefs(s.P("prefs.json")).PanelOpen);

        // A control shown from the palette brings a hidden panel back.
        Click(w.Edge.Tab);
        Assert.False(w.SidePanel.IsVisible);
        Assert.True(w.RevealSetting("contrast"));
        Assert.True(w.SidePanel.IsVisible);
        Assert.True(w.Panel.NumberRowFor("contrast")!.Box.IsFocused);

        // Hide panels takes the edge away too; Reset layout brings back 320.
        CommandListTests.Run(w, "hide-panels");
        Assert.False(w.Edge.IsVisible);
        CommandListTests.Run(w, "reset-layout");
        Ui.Pump();
        Assert.True(w.Edge.IsVisible);
        Assert.Equal(320, w.SidePanel.Bounds.Width);
        Assert.NotEqual(default, grab);
        w.Close();

        // A new window opens the panel at the width it was left at.
        var prefs = new Prefs(s.P("prefs.json")) { PanelWidth = 400 };
        prefs.Save();
        MainWindow again = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        Assert.Equal(400, again.SidePanel.Bounds.Width);
        again.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheNewRowStillDragsLive()
    {
        MainWindow w = Rock();
        var shown = new List<Settings>();
        w.Rocks.Builder = s =>
        {
            Thread.Sleep(20);
            float k = s.GetFloat("width");
            return new ColouredMesh([0, 0, 0, k, 0, 0, 0, k, 1], [0, 0, 1, 0, 0, 1, 0, 0, 1], [1, 1, 1, 1, 1, 1, 1, 1, 1], [0, 1, 2]);
        };
        w.Rocks.Showed += (_, s) => shown.Add(s);
        Settings preset = w.Panel.Current;
        int steps = w.Files.History.Steps.Count;
        int beforeRelease = 0;
        int stepsDuring = 0;

        Ui.Drag(w, w.Panel.NumberRowFor("width")!.Slider, 10, () =>
        {
            Thread.Sleep(30);
            Ui.Pump();
            beforeRelease = shown.Count(s => !s.Equals(preset));
            stepsDuring = w.Files.History.Steps.Count;
        });

        Assert.True(beforeRelease >= 1, $"{beforeRelease} rocks shown during the drag");
        Assert.Equal(steps, stepsDuring);
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EveryPanelControlHasATooltip()
    {
        foreach (bool rock in new[] { false, true })
        {
            MainWindow w = rock ? Rock() : Ui.Open(new FakePainter());
            foreach (SettingsGroup g in w.Panel.Groups)
            {
                g.IsOpen = true;
            }

            Ui.Pump();
            IEnumerable<Visual> all = w.SidePanel.GetVisualDescendants().Concat(w.Edge.GetVisualDescendants());
            var touchable = all.OfType<Control>()
                .Where(c => c.TemplatedParent is null && c is Button or TextBox or Slider or ComboBox or ToggleSwitch)
                .ToList();
            Assert.True(touchable.Count > 20);
            Assert.All(touchable, c => Assert.True(
                ToolTip.GetTip(c) is string { Length: >= 20 },
                $"{c.GetType().Name} '{AutomationProperties.GetName(c)}' has no explaining tooltip"));

            // The rows' own inputs keep our fuller tooltips; each label carries the setting's.
            foreach (ParamSpec s in w.Panel.Specs.Where(x => w.Panel.RowFor(x.Key) is not null))
            {
                Grid row = w.Panel.RowFor(s.Key)!;
                Assert.Equal(s.Tooltip, ToolTip.GetTip(row.Children.OfType<TextBlock>().First()));
                Assert.All(
                    row.GetVisualDescendants().OfType<Control>().Where(c => c.TemplatedParent is null && c is Slider or TextBox or ComboBox or ToggleSwitch),
                    c => Assert.True(ToolTip.GetTip(c) is string { Length: >= 40 }, $"{s.Key} {c.GetType().Name}"));
            }

            w.Close();
        }
    }
}

/// <summary>Rows shown with a label and group that depend on the values (easy-props' <c>shown()</c>).</summary>
[Collection(MeshRecipesAlone.Name)]
public class PanelShownRowsTests
{
    // A rock whose Width shows as Breadth under Outline while it is taller than 3 m.
    internal static SlowRecipe Relabelled() => new(RockRecipe.Engine, TimeSpan.Zero)
    {
        Shown = s => s.GetFloat("height") > 3
            ? [.. RockRecipe.Specs.Where(p => !SettingsPanel.IsHidden(p)).Select(p => p.Key == "width" ? new ShownRow("width", "Breadth", "Outline") : new ShownRow(p.Key, p.Label, p.Group))]
            : null,
    };

    internal static MainWindow Rock()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(RockRecipe.StartingPoints.Single(p => p.Id == "boulder")), 30000));
        Ui.Pump();
        return w;
    }

    internal static void Set(MainWindow w, string key, double value)
    {
        Assert.Null(w.Panel.SetFromOutside(key, System.Text.Json.JsonSerializer.SerializeToElement(value)));
        Ui.Pump();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ShownRowsRelabelARow()
    {
        using IDisposable _ = MeshRecipes.Override(Relabelled());
        MainWindow w = Rock();
        Set(w, "height", 2);
        NumberRow width = w.Panel.NumberRowFor("width")!;
        Assert.Equal(("Width", "Shape"), (width.Label.Text, w.Panel.GroupFor("width")!.Title));

        // Taller: the row moves to a new group with its new label.
        Set(w, "height", 4);
        SettingsGroup outline = w.Panel.GroupFor("width")!;
        Assert.Equal(("Breadth", "Outline"), (width.Label.Text, outline.Title));
        Assert.Contains(outline, w.Panel.Groups);
        Assert.Same(outline.Body, width.Parent);
        Assert.True(outline.IsVisible);

        // The filter finds it by its new label.
        w.Panel.Filter.Text = "breadth";
        Ui.Pump();
        Assert.True(width.IsVisible && outline.IsVisible);
        Assert.False(w.Panel.NumberRowFor("depth")!.IsVisible);
        w.Panel.Filter.Text = string.Empty;
        Ui.Pump();

        // The history step names the new label.
        Set(w, "width", 3.5);
        Assert.StartsWith("Breadth ", w.Files.History.Steps[^1].Label, StringComparison.Ordinal);

        // Lower again: the hidden rows stay the same, but the shown rows change back.
        Set(w, "height", 2);
        Assert.Equal(("Width", "Shape"), (width.Label.Text, w.Panel.GroupFor("width")!.Title));
        Assert.Same(w.Panel.GroupFor("width")!.Body, width.Parent);
        Assert.False(w.Panel.Groups.Single(g => g.Title == "Outline").IsVisible);
        w.Close();
    }
}
