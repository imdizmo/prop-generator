using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Tests.Support;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

public class SettingsWindowTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheSettingsWindowShowsToggleAddressAndTokenEachWithAWrittenTooltip()
    {
        var (w, _, _, _, _) = Make();
        var window = new SettingsWindow(w.Claude);
        window.Show();
        Ui.Pump();

        var controls = window.GetVisualDescendants().OfType<Control>()
            .Where(c => c.TemplatedParent is null && c is ToggleSwitch or Button or TextBox)
            .ToList();
        Assert.NotEmpty(controls);
        Assert.All(controls, c =>
        {
            string? tip = ToolTip.GetTip(c) as string;
            Assert.False(string.IsNullOrWhiteSpace(tip), $"{c.GetType().Name} '{AutomationProperties.GetName(c)}' has no tooltip");
            Assert.True(tip!.Length >= 40, $"tooltip too short: '{tip}'");
            Assert.NotEqual(AutomationProperties.GetName(c), tip);
            Assert.NotEqual((c as ContentControl)?.Content?.ToString(), tip);
        });

        Assert.False(Ui.Named<ToggleSwitch>(window, "Allow Claude to connect").IsChecked);
        Assert.Equal("http://127.0.0.1:47120/mcp", Ui.Named<TextBox>(window, "Address").Text);
        Assert.Equal(w.Claude.Token, Ui.Named<TextBox>(window, "Token").Text);
        Assert.True(Ui.Named<TextBox>(window, "Token").IsReadOnly);
        Assert.NotNull(Ui.Named<Button>(window, "Copy address"));
        Assert.NotNull(Ui.Named<Button>(window, "Copy token"));

        string before = w.Claude.Token;
        Ui.Named<Button>(window, "New token").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
        Assert.NotEqual(before, Ui.Named<TextBox>(window, "Token").Text);
        window.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheExportLookIsChosenInSettingsAndRemembered()
    {
        var (w, _, _, _, s) = Make();
        var prefs = new Prefs(s.P("look-prefs.json"));
        var window = new SettingsWindow(w.Claude, prefs);
        window.Show();
        Ui.Pump();

        var look = window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Export look");
        Assert.Equal(["Lit", "Cartoon", "Comic"], look.Items.Cast<ComboBoxItem>().Select(i => i.Content as string));
        Assert.True(((ToolTip.GetTip(look) as string) ?? "").Length >= 40);
        Assert.Equal(0, look.SelectedIndex);

        look.SelectedIndex = 2;
        Ui.Pump();
        Assert.Equal("cel", prefs.ExportLook);
        Assert.Equal("cel", new Prefs(s.P("look-prefs.json")).ExportLook);
        window.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheWindStrengthIsChosenInSettingsAndRemembered()
    {
        var (w, _, _, _, s) = Make();
        var prefs = new Prefs(s.P("wind-prefs.json"));
        var view = new Commands.ViewState(prefs);
        int changes = 0;
        view.Changed += () => changes++;
        var window = new SettingsWindow(w.Claude, prefs, view);
        window.Show();
        Ui.Pump();

        var strength = window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => AutomationProperties.GetName(c) == "Wind strength");
        Assert.Equal(1m, strength.Value);
        Assert.Equal(0.3m, strength.Minimum);
        Assert.Equal(2m, strength.Maximum);
        strength.Value = 1.6m;
        Ui.Pump();
        Assert.Equal(1.6, prefs.WindStrength);
        Assert.Equal(1.6, new Prefs(s.P("wind-prefs.json")).WindStrength);

        // From review: it goes through the view state, so the toolbar's popover hears of it, and back.
        Assert.True(changes > 0);
        view.WindStrength = 0.7;
        Ui.Pump();
        Assert.Equal(0.7m, strength.Value);
        window.Close();
    }
}
