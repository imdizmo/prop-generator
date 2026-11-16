using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Headless;
using PropGenerator.Desktop.Look;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class LookTests
{
    // The icons the app draws (lucide names).
    private static readonly string[] Needed =
    [
        "trees", "search", "save", "download", "check", "chevron-right", "chevron-down", "chevron-left", "x", "dices",
        "layout-grid", "rotate-ccw", "triangle-alert", "mouse-pointer-2", "move", "rotate-3d", "sun", "box", "grid-2x2",
        "wind", "layers", "tree-pine", "camera", "frame", "info", "person-standing", "brick-wall", "maximize", "minimize",
        "sliders-horizontal", "shapes", "settings-2", "copy", "trash-2", "keyboard",
    ];

    private static Color C(IBrush b) => ((ISolidColorBrush)b).Color;

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EveryIconTheAppUsesExists()
    {
        foreach (string name in Needed)
        {
            Assert.Contains(name, Icons.Names);
            Rect bounds = Icons.Get(name).Bounds;
            Assert.False(bounds.Width <= 0 && bounds.Height <= 0, name);
            Assert.InRange(bounds.Left, -0.01, 24.01);
            Assert.InRange(bounds.Top, -0.01, 24.01);
            Assert.InRange(bounds.Right, -0.01, 24.01);
            Assert.InRange(bounds.Bottom, -0.01, 24.01);
        }

        Assert.Throws<ArgumentException>(() => Icons.Get("no-such-icon"));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ThemeIsTheSharedTokens()
    {
        // The Jatify Tools design v2 tokens (Look/Tokens.cs), under the app's names.
        Assert.Equal(Color.Parse("#0F0F10"), C(Theme.SurfacePage));
        Assert.Equal(Color.Parse("#151518"), C(Theme.SurfacePanel));
        Assert.Equal(Color.Parse("#1A1A1E"), C(Theme.SurfaceRaised));
        Assert.Equal(Color.Parse("#232328"), C(Theme.SurfaceRaised2));
        Assert.Equal(Color.Parse("#0F0F10"), C(Theme.SurfaceSunken));
        Assert.Equal(Color.Parse("#2A2A30"), C(Theme.BorderSubtle));
        Assert.Equal(Color.Parse("#FFFFFF"), C(Theme.TextPrimary));
        Assert.Equal(Color.Parse("#A7A7AD"), C(Theme.TextSecondary));
        Assert.Equal(Color.Parse("#86868D"), C(Theme.TextTertiary));
        Assert.Equal(Color.Parse("#EC8219"), C(Theme.Accent));
        Assert.Equal(Color.Parse("#F0953E"), C(Theme.AccentHover));
        Assert.Equal(Color.Parse("#1A0E03"), C(Theme.AccentInk));
        Assert.Equal(Color.Parse("#EC8219"), C(Theme.Selection));
        Assert.Equal(Color.Parse("#F2786E"), C(Theme.Danger));
        Assert.Equal(Color.Parse("#C9372C"), C(Theme.DangerFill));
        Assert.Equal(Color.FromArgb(224, 20, 20, 22), C(Theme.OverlayBg));
        Assert.Equal(Color.FromArgb(43, 236, 130, 25), C(Theme.ToolPressed));
        Assert.Equal(Color.Parse("#4E4E56"), C(Theme.ScrollbarThumbHover));
        Assert.Equal("Space Grotesk", Theme.Font.FamilyNames[0]);

        // The dense tier's sizes and the shared radii.
        Assert.Equal(40, Theme.MenuBarHeight);
        Assert.Equal(24, Theme.StatusBarHeight);
        Assert.Equal(28, Theme.RowHeight);
        Assert.Equal(28, Theme.ControlHeight);
        Assert.Equal(32, Theme.SectionHeight);
        Assert.Equal((4, 6, 8, 16, 12), (Theme.Radius1, Theme.Radius2, Theme.Radius3, Theme.Radius4, Theme.RadiusCard));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ChromeButtonsCarryTheirTooltip()
    {
        Button[] buttons =
        [
            Chrome.TextButton("File", "Open the file menu"),
            Chrome.IconButton("x", "Close it"),
            Chrome.IconTextButton("save", "Save", "Save (Ctrl+S)"),
            Chrome.PrimaryButton("download", "Export", "Export to a Groundwork map (Ctrl+E)"),
        ];
        foreach (Button b in buttons)
        {
            Assert.NotNull(ToolTip.GetTip(b));
            Assert.Equal(Theme.ControlHeight, b.Height);
        }

        Assert.Equal(Theme.TooltipDelayMs, ToolTip.GetShowDelay(buttons[0]));
    }

    private static object Res(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, Avalonia.Styling.ThemeVariant.Dark, out object? v), key);
        return v!;
    }

    private static Color ResColour(string key) => Res(key) switch
    {
        ISolidColorBrush b => b.Color,
        Color c => c,
        object o => throw new Xunit.Sdk.XunitException($"{key} is a {o.GetType().Name}"),
    };

    // The design's Fluent resource table: plain Fluent controls (a TextBox, a Button, a CheckBox, a ComboBox, a
    // ListBox, a scrollbar, a slider) draw in the tokens without styling each one.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FluentResourcesFollowTheDesignTable()
    {
        Color bg = C(Tokens.Bg), line = C(Tokens.Line), strong = C(Tokens.LineStrong), raised = C(Tokens.Raised), raised2 = C(Tokens.Raised2);
        foreach (string k in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
        {
            Assert.Equal(bg, ResColour(k));
        }

        Assert.Equal(line, ResColour("TextControlBorderBrush"));
        Assert.Equal(strong, ResColour("TextControlBorderBrushPointerOver"));
        Assert.Equal(C(Tokens.Accent), ResColour("TextControlBorderBrushFocused"));
        Assert.Equal(raised, ResColour("ButtonBackground"));
        Assert.Equal(raised2, ResColour("ButtonBackgroundPointerOver"));
        Assert.Equal(Color.Parse("#3B3B40"), ResColour("ButtonBackgroundPressed"));
        Assert.Equal(raised, ResColour("ButtonBackgroundDisabled"));
        Assert.Equal(line, ResColour("ButtonBorderBrush"));
        Assert.Equal(strong, ResColour("ButtonBorderBrushPointerOver"));
        Assert.Equal(strong, ResColour("ButtonBorderBrushPressed"));
        Assert.Equal(raised, ResColour("ComboBoxBackground"));
        Assert.Equal(C(Tokens.Selected), ResColour("ListBoxItemBackgroundSelected"));
        Assert.Equal(C(Tokens.Selected), ResColour("SystemControlHighlightListAccentLowBrush"));
        Assert.Equal(Color.Parse("#4A301A"), ResColour("ListBoxItemBackgroundSelectedPointerOver"));
        Assert.Equal(C(Tokens.Hover), ResColour("ListBoxItemBackgroundPointerOver"));
        Assert.Equal(C(Tokens.Hover), ResColour("SystemControlHighlightListLowBrush"));
        Assert.Equal(C(Tokens.Accent), ResColour("CheckBoxCheckBackgroundFillChecked"));
        Assert.Equal(C(Tokens.AccentInk), ResColour("CheckBoxCheckGlyphForegroundChecked"));
        Assert.Equal(10.0, Res("ScrollBarSize"));
        Assert.Equal(C(Tokens.Text), ResColour("SliderThumbBackground"));

        // A plain TextBox and a plain Button pick them up.
        var box = new TextBox();
        var button = new Button { Content = "Cancel" };
        var check = new CheckBox { IsChecked = true };
        var w = new Window { Content = new StackPanel { Children = { box, button, check } } };
        w.Show();
        Ui.Pump();
        Assert.Equal(bg, C(box.Background!));
        Assert.Equal(line, C(box.BorderBrush!));
        Assert.Equal(raised, C(button.Background!));
        Assert.Equal(line, C(button.BorderBrush!));
        w.Close();
    }

    // Keyboard focus: a 2 px ring in the focus colour, 2 px outside the control (a panel-coloured gap between).
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FocusRingIsTheAccentTwoPixelsOutside()
    {
        Assert.Equal(C(Theme.FocusRing), ResColour("SystemControlFocusVisualPrimaryBrush"));
        Assert.Equal(C(Theme.FocusRingInner), ResColour("SystemControlFocusVisualSecondaryBrush"));
        Assert.Equal(new Thickness(2), Res("SystemControlFocusVisualPrimaryThickness"));
        Assert.Equal(new Thickness(2), Res("SystemControlFocusVisualSecondaryThickness"));
        Assert.Equal(new Thickness(-4), Res("SystemControlFocusVisualMargin"));

        // Tab onto a plain button: its focus adorner is drawn in the focus colour, outside the button.
        var first = new Button { Content = "One" };
        var second = new Button { Content = "Two" };
        var w = new Window { Content = new StackPanel { Margin = new Thickness(20), Spacing = 20, Children = { first, second } } };
        w.Show();
        first.Focus();
        w.KeyPressQwerty(Avalonia.Input.PhysicalKey.Tab, Avalonia.Input.RawInputModifiers.None);
        Ui.Pump();
        Assert.True(second.IsFocused);
        Avalonia.Controls.Primitives.AdornerLayer layer = Avalonia.Controls.Primitives.AdornerLayer.GetAdornerLayer(second)!;
        Border ring = layer.GetVisualDescendants().OfType<Border>().First(b => b.BorderBrush is ISolidColorBrush { Color: var c } && c == C(Theme.FocusRing));
        Assert.Equal(new Thickness(2), ring.BorderThickness);
        Rect around = ring.Bounds;
        Assert.True(around.Width >= second.Bounds.Width + 8 - 0.5, $"{around} around {second.Bounds}");
        w.Close();
    }
}
