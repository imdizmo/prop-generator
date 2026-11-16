using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Tests.Support;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.BridgeKit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

public class MainWindowTests
{
    private static string? ContentText(Control c) => c switch
    {
        ContentControl cc => cc.Content?.ToString(),
        TextBox t => t.Text,
        _ => null,
    };

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheFileMenuHasSettingsWithATooltip()
    {
        var (w, _, _, _, _) = Make();
        Commands.Command settings = CommandListTests.Find(w, "settings");
        Assert.Equal(("Settings", "Ctrl+,"), (settings.Label, settings.Keys));
        Assert.True(settings.Tooltip!.Length >= 40);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EveryControlInTheWindowHasAWrittenTooltip()
    {
        MainWindow w = Ui.Open(new FakePainter());

        // The controls the app made (a template's own parts, such as a slider's track buttons, are not).
        var controls = w.GetVisualDescendants().OfType<Control>()
            .Where(c => c.TemplatedParent is null
                && c is Slider or ToggleSwitch or Button or NumericUpDown or ColorPicker or TextBox)
            .ToList();

        // The top bar's Save, Export and brand say what easy-props' say, short as they are; Move, Turn and Resize say
        // why they wait, and Slope why it is greyed over the floor (plan E9's wording).
        string[] shortOnPurpose =
        [
            "Save (Ctrl+S)", "Export to a Groundwork map (Ctrl+E)", "Show every starting point (Ctrl+G)", "Type, paste or copy the seed",
            "This kind of piece has no parts to pick", View.SlopePopover.FloorLevel,
        ];
        Assert.NotEmpty(controls);
        Assert.All(controls, c =>
        {
            string? tip = ToolTip.GetTip(c) as string;
            Assert.False(string.IsNullOrWhiteSpace(tip), $"{c.GetType().Name} '{AutomationProperties.GetName(c)}' has no tooltip");
            Assert.True(tip!.Length >= 40 || shortOnPurpose.Contains(tip), $"tooltip too short: '{tip}'");
            Assert.NotEqual(AutomationProperties.GetName(c), tip);
            Assert.NotEqual(ContentText(c), tip);
        });
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheWindowIsTitledAndHasNoApplyButton()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.EndsWith(" — Prop Generator", w.Title);
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<Button>(), b => b.Content is string s && s.Contains("Apply", StringComparison.OrdinalIgnoreCase));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void BackToThePieceAndNewAndOpenBringTheFloorBack()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("oak.propgen") };
        MainWindow w = Open(s, dialogs);
        FileActionsTests.Press(w, PhysicalKey.S);

        DropMesh(s, "Crate", 0);
        Ui.Wait(1600);
        Assert.True(w.View.ShowingPiece);
        RunCommand(w, "Back to the piece");
        Assert.False(w.View.ShowingPiece);

        DropMesh(s, "Crate", 0);
        Ui.Wait(1600);
        Assert.True(w.View.ShowingPiece);
        FileActionsTests.Press(w, PhysicalKey.N);
        Assert.False(w.View.ShowingPiece);

        DropMesh(s, "Crate", 0);
        Ui.Wait(1600);
        Assert.True(w.View.ShowingPiece);
        _ = w.Files.OpenPathAsync(s.P("oak.propgen"));
        Ui.Wait(200);
        Assert.False(w.View.ShowingPiece);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheBridgeCommandsHaveLongTooltips()
    {
        using var s = new Scratch();
        MainWindow w = Open(s);
        foreach (string id in new[] { "back-to-piece", "send-blender" })
        {
            string? tip = CommandListTests.Find(w, id).Tooltip;
            Assert.True(tip is { Length: >= 40 }, id);
        }

        w.Close();
    }
}
