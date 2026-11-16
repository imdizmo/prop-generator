using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Status;
using PropGenerator.Desktop.Tests.Support;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

/// <summary>The status bar as easy-props': the hint and messages, the seed panel, the faces against the budget, the save state.</summary>
public class StatusBarTests
{
    private static StartingPoint Boulder => RockRecipe.StartingPoints.Single(p => p.Id == "boulder");

    private static StartingPoint Cliff => RockRecipe.StartingPoints.Single(p => p.Name == "Cliff face");

    private static bool Waited(Func<bool> done, int ms)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    private static MainWindow Rock(StartingPoint? point = null, Scratch? scratch = null)
    {
        MainWindow w = scratch is null ? Ui.Open(new FakePainter()) : Ui.Open(new FakePainter(), new FakeDialogs(), scratch);
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(point ?? Boulder)));
        Ui.Pump();
        return w;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheBarIsTheDesignsAtTheNarrowestWindow()
    {
        MainWindow w = Rock();
        w.Width = MainWindow.MinimumWidth;
        w.Height = MainWindow.MinimumHeight;
        Assert.Equal((1024d, 640d), (w.MinWidth, w.MinHeight));
        string message = string.Concat(Enumerable.Repeat("A long message that cannot fit on the line ", 8));
        w.Say(message);
        Ui.Pump();

        // 24 high on the panel surface, with a hairline above.
        StatusBar bar = w.StatusBar;
        Assert.Equal(24, bar.Bounds.Height, 1);
        Assert.Same(Look.Theme.SurfacePanel, bar.Background);

        // The save state first on the left, then the message, cut before the right side, its full text in the tooltip.
        Point state = bar.SaveLabel.TranslatePoint(default, bar)!.Value;
        Point line = w.StatusLine.TranslatePoint(default, bar)!.Value;
        Point right = bar.Right.TranslatePoint(default, bar)!.Value;
        Assert.True(state.X < line.X);
        Assert.True(line.X + w.StatusLine.Bounds.Width <= right.X + 0.5, $"{line.X + w.StatusLine.Bounds.Width} > {right.X}");
        Assert.Equal(message, ToolTip.GetTip(w.StatusLine));

        // The right side is whole: nothing dropped at 1024.
        Assert.Equal(0, bar.Right.DroppedCount);
        Assert.Equal(bar.Right.DesiredSize.Width - bar.Right.Margin.Left, bar.Right.Bounds.Width, 1);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SaveStateReadsUnsavedNotSavedYetSaved()
    {
        using var s = new Scratch();
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        Assert.Equal(SaveState.NotSavedYet, w.Files.SaveState);
        Assert.Equal("Not saved yet", w.StatusBar.SaveText);

        // A change makes it Unsaved, saving makes it Saved, another change Unsaved again.
        Assert.Null(w.Files.SaveWithoutAsking(s.P("a.propgen"), overwrite: true));
        Ui.Pump();
        Assert.Equal(SaveState.Saved, w.Files.SaveState);
        Assert.Equal("Saved", w.StatusBar.SaveText);

        w.Files.ChangeAll(w.Panel.Current.With("seed", 7L), "Seed 7");
        Ui.Pump();
        Assert.Equal(SaveState.Unsaved, w.Files.SaveState);
        Assert.Equal("Unsaved", w.StatusBar.SaveText);

        // Changed before it was ever saved: Unsaved too.
        MainWindow fresh = Ui.Open(new FakePainter());
        fresh.Files.ChangeAll(fresh.Panel.Current.With("seed", 8L), "Seed 8");
        Assert.Equal("Unsaved", fresh.StatusBar.SaveText);
        fresh.Close();
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AMessageShowsForSixSecondsThenTheHint()
    {
        MainWindow w = Ui.Open(new FakePainter());
        w.Say("Saved a.propgen.");
        Assert.Equal("Saved a.propgen.", w.StatusLine.Text);
        Assert.False(w.StatusBar.IsError);
        Assert.True(w.StatusBar.MessageTimer.IsEnabled);
        Assert.Equal(TimeSpan.FromSeconds(6), w.StatusBar.MessageTimer.Interval);

        // The timer's tick brings the hint back; what the mouse does in the view, from the real bindings.
        w.StatusBar.MessageTimer.Stop();
        w.StatusBar.ShowHint();
        Assert.Equal(StatusBar.Hint, w.StatusLine.Text);
        Assert.Contains("orbit", w.StatusLine.Text, StringComparison.Ordinal);
        Assert.Contains("zoom", w.StatusLine.Text, StringComparison.Ordinal);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnErrorStaysUntilTheNextAction()
    {
        MainWindow w = Ui.Open(new FakePainter());
        w.Say("Export failed: no room.", error: true);
        Assert.True(w.StatusBar.IsError);
        Assert.False(w.StatusBar.MessageTimer.IsEnabled);
        Assert.Equal(Look.Theme.Danger, w.StatusLine.Foreground);

        // Nothing brings the hint back by itself; an action does (a setting changed, a command run).
        Ui.Wait(50);
        Assert.Equal("Export failed: no room.", w.StatusLine.Text);
        CommandListTests.Run(w, "stats");
        Assert.False(w.StatusBar.IsError);
        Assert.NotEqual("Export failed: no room.", w.StatusLine.Text);
        Assert.NotEqual(Look.Theme.Danger, w.StatusLine.Foreground);

        // A changed setting is an action too.
        w.Say("again", error: true);
        w.Panel.SetFromOutside("seed", System.Text.Json.JsonSerializer.SerializeToElement(5));
        Assert.False(w.StatusBar.IsError);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SeedPopoverSetsCopiesAndRefusesBadText()
    {
        MainWindow w = Rock();
        SeedPopover seed = w.StatusBar.Seed;
        Assert.Equal("Seed " + w.Panel.SeedValue, ((TextBlock)seed.Trigger.Content!).Text);

        // Use a seed… opens it, with the seed in the field.
        CommandListTests.Run(w, "use-seed");
        Assert.True(seed.IsOpen);
        Assert.Equal(w.Panel.SeedValue.ToString(System.Globalization.CultureInfo.InvariantCulture), seed.Field.Text);

        // A seed typed sets it, as one undo step; text around the number is ignored, as easy-props does.
        int steps = w.Files.History.Steps.Count;
        seed.Field.Text = "Seed 1,234";
        seed.Apply();
        Assert.Equal(1234, w.Panel.SeedValue);
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal("Seed 1234", ((TextBlock)seed.Trigger.Content!).Text);

        // Bad text keeps the old seed and says so, in red.
        foreach (string bad in new[] { "abc", "12.5", "-3", "1 x 2 y", "4294967296", string.Empty })
        {
            seed.Field.Text = bad;
            seed.Apply();
            Assert.Equal(1234, w.Panel.SeedValue);
            Assert.Equal("That isn't a seed: use a whole number from 0 to 4294967295.", seed.Message.Text);
            Assert.Equal(Look.Theme.Danger, seed.Field.BorderBrush);
        }

        seed.Field.Text = "4294967295";
        seed.Apply();
        Assert.Equal(4294967295, w.Panel.SeedValue);
        Assert.Equal("Same settings and seed give the same prop.", seed.Message.Text);

        // Surprise me from the panel: the new seed shows in the field.
        long before = w.Panel.SeedValue;
        seed.Surprise.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
        Assert.NotEqual(before, w.Panel.SeedValue);
        Assert.Equal(w.Panel.SeedValue.ToString(System.Globalization.CultureInfo.InvariantCulture), seed.Field.Text);

        // Copy says Copied for a moment when there is a clipboard, and says why not when there is none.
        seed.CopyButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Ui.Wait(100);
        Assert.True(seed.CopyLabel == "Copied" || seed.Message.Text!.StartsWith("Couldn't copy it.", StringComparison.Ordinal), seed.CopyLabel + " / " + seed.Message.Text);

        // Esc puts the old seed back and closes it.
        long kept = w.Panel.SeedValue;
        seed.Field.Text = "99";
        seed.Field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(seed.IsOpen);
        Assert.Equal(kept, w.Panel.SeedValue);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheFloorSeedKeepsItsRangeAndSaysSo()
    {
        MainWindow w = Ui.Open(new FakePainter());
        SeedPopover seed = w.StatusBar.Seed;
        seed.Field.Text = "10000";
        seed.Apply();
        Assert.Equal("That isn't a seed: use a whole number from 0 to 9999.", seed.Message.Text);
        seed.Field.Text = "9999";
        seed.Apply();
        Assert.Equal(9999, w.Panel.SeedValue);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ThePalettesSeedEntryOpensThePopover()
    {
        MainWindow w = Rock();
        Assert.True(w.RevealSetting("seed"));
        Assert.True(w.StatusBar.Seed.IsOpen);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FacesAgainstTheBudgetTurnWarningWhenOver()
    {
        MainWindow w = Rock(Cliff);
        int faces = w.Rocks.FullDetailFaces;
        Assert.True(faces > 0);
        w.ViewState.Budget = "hero";
        string hero = $"{faces:N0} / 30,000 faces";
        Assert.Equal(hero, w.StatusBar.FacesReadout.Text);

        // Mobile allows 2,000: a Cliff face is over it, and the readout and the warning show it.
        w.ViewState.Budget = "mobile";
        Assert.True(faces > 2000);
        Assert.Equal($"{faces:N0} / 2,000 faces", w.StatusBar.FacesReadout.Text);
        Assert.Equal(Look.Theme.Warning, w.StatusBar.FacesReadout.Foreground);
        Assert.True(w.StatusBar.Warning.IsVisible);
        Assert.Contains("Over budget", ToolTip.GetTip(w.StatusBar.Warning)!.ToString(), StringComparison.Ordinal);

        // Make it fit is the warning chip's one action: a small pill that lowers the rock's Faces and Chunks.
        Button fit = w.StatusBar.MakeItFit;
        Assert.Contains(w.StatusBar.Warning, fit.GetLogicalAncestors());
        Assert.True(fit.IsEffectivelyVisible);
        Assert.True(fit.IsEnabled);
        Assert.Equal("Make it fit", ((TextBlock)fit.Content!).Text);
        Assert.Contains("ep-chip-action", fit.Classes);
        Assert.Same(Look.Theme.WarningSoft, w.StatusBar.Warning.Background);
        Assert.Equal("Make it fit: lowers the controls that cost the most until this prop fits its budget.", ToolTip.GetTip(fit));
        Assert.True(ToolTip.GetShowOnDisabled(fit));

        // Under the budget: no warning, the usual colour.
        w.ViewState.Budget = "hero";
        Assert.Equal(faces > 30000, w.StatusBar.Warning.IsVisible);

        // The floor counts its tile's triangles.
        Run(w.Files.NewAsync());
        Ui.Pump();
        Assert.Equal($"{TileMesh.Square().Indices.Length / 3:N0} / 30,000 faces", w.StatusBar.FacesReadout.Text);
        Assert.False(w.StatusBar.Warning.IsVisible);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void BudgetIsRemembered()
    {
        using var s = new Scratch();
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        Assert.Equal("Standard", (string)w.StatusBar.Picker.SelectedItem!);
        Assert.Equal(["Hero", "Standard", "Background", "Mobile"], w.StatusBar.Picker.Items.Cast<string>());
        w.StatusBar.Picker.SelectedIndex = 3;
        Assert.Equal("mobile", w.ViewState.Budget);
        w.Close();

        MainWindow again = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        Assert.Equal("mobile", again.ViewState.Budget);
        Assert.Equal("Mobile", (string)again.StatusBar.Picker.SelectedItem!);
        Assert.Contains("/ 2,000 faces", again.StatusBar.FacesReadout.Text, StringComparison.Ordinal);
        again.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void LodReadoutOnlyAboveZero()
    {
        MainWindow w = Rock();
        Assert.Equal(string.Empty, w.StatusBar.LodText);
        Assert.DoesNotContain("Full detail", w.StatusBar.FacesReadout.Text, StringComparison.Ordinal);

        w.ViewState.Lod = 1;
        Assert.StartsWith("LOD1: ", w.StatusBar.LodText, StringComparison.Ordinal);
        Assert.EndsWith(" faces", w.StatusBar.LodText, StringComparison.Ordinal);
        Assert.StartsWith("Full detail: ", w.StatusBar.FacesReadout.Text, StringComparison.Ordinal);

        w.ViewState.Lod = 0;
        Assert.Equal(string.Empty, w.StatusBar.LodText);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AboveLevelZeroTheRockIsBuiltAtItsShareAndBothCountsShow()
    {
        MainWindow w = Rock();
        // The engine's rock builds on the pool, never on the UI thread.
        int full = EngineKit.OnThePool(() => RockRecipe.Build(Boulder.Settings).Indices.Length / 3);
        int level2 = EngineKit.OnThePool(() => RockRecipe.Build(Boulder.Settings, RockRecipe.DetailShares[2]).Indices.Length / 3);
        Assert.Equal(full, w.Rocks.ShownFaces);

        w.ViewState.Lod = 2;
        Assert.True(Waited(() => w.Rocks.ShownFaces == level2, 20000), $"{w.Rocks.ShownFaces} faces shown, {level2} wanted");
        Assert.Equal(full, w.Rocks.FullDetailFaces);
        Assert.Equal(level2, w.Rocks.Mesh!.Indices.Length / 3);
        Ui.Pump();
        Assert.Equal(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"LOD2: {level2:N0} faces"), w.StatusBar.LodText);
        Assert.StartsWith(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Full detail: {full:N0} / "), w.StatusBar.FacesReadout.Text, StringComparison.Ordinal);

        w.ViewState.Lod = 0;
        Assert.True(Waited(() => w.Rocks.ShownFaces == full, 20000));
        Assert.Equal(full, w.Rocks.FullDetailFaces);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DuringADragAtALowerLevelTheFullCountWaitsForTheRelease()
    {
        MainWindow w = Rock();
        int fullBuilds = 0;
        w.Rocks.Builder = s =>
        {
            Interlocked.Increment(ref fullBuilds);
            return RockRecipe.Build(s);
        };
        w.ViewState.Lod = 1;
        Assert.True(Waited(() => w.Rocks.ShownDetail == RockRecipe.DetailShares[1], 20000));
        int before = fullBuilds;

        // A button held (a slider being dragged): the rock changes, but only its level is built.
        w.Rocks.PointerHeld = true;
        w.Files.ChangeAll(w.Panel.Current.With("width", 4.0), "Width 4");
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true, 20000));
        Ui.Wait(100);
        Assert.Equal(before, Volatile.Read(ref fullBuilds));

        // Let go: the full rock is counted once, and the bar shows it.
        Settings now = w.Panel.Current;
        int expected = EngineKit.OnThePool(() => RockRecipe.Build(now).Indices.Length / 3);
        w.Rocks.PointerHeld = false;
        Assert.True(Waited(() => w.Rocks.FullDetailFaces == expected, 20000), $"{w.Rocks.FullDetailFaces} faces, {expected} wanted");
        Assert.Equal(before + 1, Volatile.Read(ref fullBuilds));
        Ui.Pump();
        Assert.StartsWith(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Full detail: {expected:N0} / "), w.StatusBar.FacesReadout.Text, StringComparison.Ordinal);
        w.Close();
    }

    [Fact]
    public void TheHintIsTheViewsRealBindings() =>
        Assert.Equal("Left drag to orbit · Right drag to pan · Scroll to zoom", StatusBar.Hint);

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EveryStatusControlHasATooltip()
    {
        MainWindow w = Ui.Open(new FakePainter());
        var controls = w.StatusBar.GetLogicalDescendants().Concat(w.StatusBar.Seed.Content.GetLogicalDescendants()).OfType<Control>()
            .Where(c => c is Button or ComboBox or TextBox or TextBlock { Text.Length: > 0 })
            .ToList();
        Assert.True(controls.Count >= 10);
        foreach (Control c in controls)
        {
            bool tipped = c.GetLogicalAncestors().OfType<Control>().Prepend(c).Any(x => !string.IsNullOrWhiteSpace(ToolTip.GetTip(x)?.ToString()));
            Assert.True(tipped, c.GetType().Name + " " + (c as TextBlock)?.Text);
        }

        Assert.Equal("Type, paste or copy the seed", ToolTip.GetTip(w.StatusBar.Seed.Trigger));
        Assert.Equal("What this prop is for", ToolTip.GetTip(w.StatusBar.Picker));
        Assert.Equal("Faces at full detail, against this budget", ToolTip.GetTip(w.StatusBar.FacesReadout));
        w.Close();
    }

    // An error said while the view is maximized would never be seen (the status bar is hidden there): it leaves the
    // maximized view. Maximizing again or restoring, and hiding the panels, are no answer to it: it stays.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnErrorLeavesTheMaximizedViewAndLayoutChangesKeepIt()
    {
        MainWindow w = Ui.Open(new FakePainter());
        CommandListTests.Run(w, "maximize");
        Assert.True(w.ViewState.Maximized);
        Assert.False(w.StatusBar.IsVisible);

        w.Say("A message only.");
        Assert.True(w.ViewState.Maximized);

        w.Say("Export failed: no room.", error: true);
        Assert.False(w.ViewState.Maximized);
        Assert.True(w.StatusBar.IsVisible);
        Assert.True(w.StatusBar.IsError);

        foreach (string id in new[] { "maximize", "maximize", "hide-panels", "hide-panels" })
        {
            CommandListTests.Run(w, id);
            Assert.True(w.StatusBar.IsError, id);
            Assert.Equal("Export failed: no room.", w.StatusLine.Text);
        }

        Assert.False(w.ViewState.Maximized);
        Assert.True(w.StatusBar.IsVisible);

        // Another action takes it off.
        CommandListTests.Run(w, "stats");
        Assert.False(w.StatusBar.IsError);
        w.Close();
    }

    // The painter's failures are failures: red, kept until the next action.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void APainterFailureIsAnError()
    {
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter);
        CommandListTests.Run(w, "maximize");
        painter.FailFromThreadPool("The painter stopped: out of memory.");
        Assert.True(Waited(() => w.StatusBar.IsError, 2000));
        Assert.Equal("The painter stopped: out of memory.", w.StatusLine.Text);
        Assert.Equal(Look.Theme.Danger, w.StatusLine.Foreground);
        Assert.False(w.ViewState.Maximized);
        w.Close();
    }

    // The window's clocks stop when it closes: the stats card's, the message's and the seed panel's Copied.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ClosingStopsTheClocks()
    {
        MainWindow w = Ui.Open(new FakePainter());
        CommandListTests.Run(w, "stats");
        w.Say("A message.");
        Assert.True(w.Toolbar.StatsCard.Timer.IsEnabled);
        Assert.True(w.StatusBar.MessageTimer.IsEnabled);
        w.StatusBar.Seed.Open();
        w.StatusBar.Seed.CopyButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Ui.Wait(100);
        bool copied = w.StatusBar.Seed.CopiedShowing;

        w.Close();
        Ui.Pump();

        Assert.False(w.Toolbar.StatsCard.Timer.IsEnabled);
        Assert.False(w.StatusBar.MessageTimer.IsEnabled);
        Assert.False(w.StatusBar.Seed.CopiedShowing, copied ? "Copied was showing" : "no clipboard");
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ANewMessageDoesNotWipeAnErrorButAnActionDoes()
    {
        MainWindow w = Ui.Open(new FakePainter());
        w.Say("Export failed: no room.", error: true);
        w.Say("x");
        Assert.Equal("Export failed: no room.", w.StatusLine.Text);
        Assert.True(w.StatusBar.IsError);

        // A newer error replaces an older one.
        w.Say("Other failure.", error: true);
        Assert.Equal("Other failure.", w.StatusLine.Text);

        w.Acted();
        w.Say("x");
        Assert.Equal("x", w.StatusLine.Text);
        Assert.False(w.StatusBar.IsError);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TypingInTheOpenSeedPanelSurvivesARefresh()
    {
        MainWindow w = Rock();
        SeedPopover seed = w.StatusBar.Seed;
        seed.Open();
        seed.Field.Text = "12x 3 4y";
        seed.Apply();
        string complaint = seed.Message.Text!;
        seed.Field.Text = "12x";

        w.StatusBar.Refresh();
        Run(w.Rocks.ShowNow(frame: false));
        Ui.Pump();

        Assert.Equal("12x", seed.Field.Text);
        Assert.Equal(complaint, seed.Message.Text);
        Assert.Equal("Seed " + w.Panel.SeedValue, ((TextBlock)seed.Trigger.Content!).Text);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FacesFollowAMeshFromBlenderAndBack()
    {
        using var s = new Scratch();
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s, s.P("bridge"));
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(Boulder)));
        Ui.Pump();
        string rock = w.StatusBar.FacesReadout.Text!;

        BridgeKit.DropMesh(s, "Crate", 20);
        Ui.Wait(1600);
        Assert.True(w.View.ShowingPiece);
        Assert.StartsWith($"{TileMesh.Square().Indices.Length / 3:N0} / ", w.StatusBar.FacesReadout.Text, StringComparison.Ordinal);

        Run(w.BackToThePieceAsync());
        Ui.Pump();
        Assert.Equal(rock, w.StatusBar.FacesReadout.Text);
        w.Close();
    }
}
