using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class WindowDialogsTests
{
    private static (Window Window, Grid Overlay, WindowDialogs Dialogs) Make()
    {
        var overlay = new Grid();
        var w = new Window { Width = 800, Height = 600, Content = overlay };
        w.Show();
        Ui.Pump();
        return (w, overlay, new WindowDialogs(w, overlay));
    }

    private static List<Button> CardButtons(Window w) =>
        [.. w.GetVisualDescendants().OfType<Button>().Where(b => b.TemplatedParent is null)];

    private static void CheckTooltips(IReadOnlyList<Button> buttons)
    {
        Assert.NotEmpty(buttons);
        Assert.All(buttons, b =>
        {
            string? tip = ToolTip.GetTip(b) as string;
            Assert.False(string.IsNullOrWhiteSpace(tip), $"button '{b.Content}' has no tooltip");
            Assert.True(tip!.Length >= 40, $"tooltip too short: '{tip}'");
            Assert.NotEqual(b.Content?.ToString(), tip);
            Assert.Equal(b.Content?.ToString(), AutomationProperties.GetName(b));
        });
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EveryButtonOnEveryCardHasANameAndAWrittenTooltip()
    {
        var (w, overlay, dialogs) = Make();

        Task<bool> confirm = dialogs.ConfirmAsync("Replace a.propgen?", "Replace", "Keep it");
        Ui.Pump();
        CheckTooltips(CardButtons(w));
        Assert.Equal(2, CardButtons(w).Count);
        CardButtons(w)[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
        Assert.True(confirm.IsCompleted, "the card did not answer");
        Assert.False(confirm.Result);
        Assert.Empty(overlay.Children);

        Task<SaveChoice> save = dialogs.AskSaveChangesAsync("a.propgen");
        Ui.Pump();
        CheckTooltips(CardButtons(w));
        Assert.Equal(3, CardButtons(w).Count);
        CardButtons(w)[2].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
        Assert.True(save.IsCompleted, "the card did not answer");
        Assert.Equal(SaveChoice.Cancel, save.Result);

        Task tell = dialogs.TellAsync("Oops", "It broke.");
        Ui.Pump();
        CheckTooltips(CardButtons(w));
        CardButtons(w)[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
        Assert.True(tell.IsCompleted);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EscapeIsTheCancelChoiceAndEnterTheDefaultOne()
    {
        var (w, overlay, dialogs) = Make();

        Task<SaveChoice> save = dialogs.AskSaveChangesAsync("a.propgen");
        Ui.Pump();
        Avalonia.Headless.HeadlessWindowExtensions.KeyPressQwerty(w, PhysicalKey.Escape, RawInputModifiers.None);
        Ui.Pump();
        Assert.True(save.IsCompleted, "the card did not answer");
        Assert.Equal(SaveChoice.Cancel, save.Result);
        Assert.Empty(overlay.Children);

        Task<SaveChoice> again = dialogs.AskSaveChangesAsync("a.propgen");
        Ui.Pump();
        Avalonia.Headless.HeadlessWindowExtensions.KeyPressQwerty(w, PhysicalKey.Enter, RawInputModifiers.None);
        Ui.Pump();
        Assert.True(again.IsCompleted, "the card did not answer");
        Assert.Equal(SaveChoice.Save, again.Result);

        Task<bool> confirm = dialogs.ConfirmAsync("Replace a.propgen?", "Replace", "Keep it");
        Ui.Pump();
        Avalonia.Headless.HeadlessWindowExtensions.KeyPressQwerty(w, PhysicalKey.Escape, RawInputModifiers.None);
        Ui.Pump();
        Assert.True(confirm.IsCompleted, "the card did not answer");
        Assert.False(confirm.Result);
    }
}
