// Adapted from easy-props (MIT, © 2026 AHS12); the sizes and colours are the shared tokens (Look/Tokens.cs).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PropGenerator.Desktop.Look;

/// <summary>Small factories for the chrome: buttons, key chips, dividers, popovers and tooltips. Every button gets a tooltip.</summary>
public static class Chrome
{
    /// <summary>
    /// Gives a control its tooltip, shown after 450 ms. The text is kept as the tip itself (the tooltip style
    /// wraps it at 260 px), so it can be read back as the control's tooltip.
    /// </summary>
    /// <param name="control">The control to describe.</param>
    /// <param name="text">What the tooltip says.</param>
    public static void Tip(Control control, string text)
    {
        ArgumentNullException.ThrowIfNull(control);
        ToolTip.SetTip(control, text);
        ToolTip.SetShowDelay(control, Theme.TooltipDelayMs);
    }

    /// <summary>A flat button with text only (a menu title, say): quiet text, a wash on hover.</summary>
    /// <param name="text">The label.</param>
    /// <param name="tooltip">The tooltip.</param>
    public static Button TextButton(string text, string tooltip) =>
        Make(new TextBlock { Text = text, FontSize = Theme.Body, VerticalAlignment = VerticalAlignment.Center }, tooltip, "ep");

    /// <summary>A flat square button with one icon.</summary>
    /// <param name="icon">The lucide icon name.</param>
    /// <param name="tooltip">The tooltip.</param>
    public static Button IconButton(string icon, string tooltip)
    {
        Button b = Make(new Icon(icon, 16), tooltip, "ep");
        b.Padding = new Thickness(0);
        b.Width = Theme.ControlHeight;
        return b;
    }

    /// <summary>A secondary button with an icon and a label (Save, say): raised surface, a hairline border.</summary>
    /// <param name="icon">The lucide icon name.</param>
    /// <param name="text">The label.</param>
    /// <param name="tooltip">The tooltip.</param>
    public static Button IconTextButton(string icon, string text, string tooltip) =>
        Make(IconAndText(icon, text), tooltip, "ep", "ep-raised");

    /// <summary>The one primary button of the window (Export): the accent with ink text, semibold.</summary>
    /// <param name="icon">The lucide icon name.</param>
    /// <param name="text">The label.</param>
    /// <param name="tooltip">The tooltip.</param>
    public static Button PrimaryButton(string icon, string text, string tooltip)
    {
        Button b = Make(IconAndText(icon, text), tooltip, "ep", "ep-primary");
        b.FontWeight = FontWeight.SemiBold;
        return b;
    }

    /// <summary>A small chip showing a keyboard shortcut such as <c>Ctrl+K</c>.</summary>
    /// <param name="keys">The shortcut text.</param>
    public static Border KeyChip(string keys) => new()
    {
        Padding = new Thickness(5, 1),
        BorderBrush = Theme.BorderSubtle,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(Theme.Radius1),
        Background = Theme.SurfaceSunken,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = keys, FontSize = Theme.Keys, Foreground = Theme.TextSecondary },
    };

    /// <summary>A one-pixel line across a menu or panel, 4 px above and below it and 2 px in from the sides.</summary>
    public static Border Divider() => new()
    {
        Height = 1,
        Margin = new Thickness(2, 4),
        Background = Theme.BorderSubtle,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>Dresses a border as a menu or popover: raised surface, strong border, radius 8, padding 4, shadow 2, at least 208 wide.</summary>
    /// <param name="popover">The border to style.</param>
    /// <returns>The same border.</returns>
    public static Border StylePopover(Border popover)
    {
        popover.MinWidth = 208;
        popover.Padding = new Thickness(4);
        popover.Background = Theme.SurfaceRaised;
        popover.BorderBrush = Theme.BorderStrong;
        popover.BorderThickness = new Thickness(1);
        popover.CornerRadius = new CornerRadius(Theme.Radius3);
        popover.BoxShadow = Theme.Shadow2;
        return popover;
    }

    private static StackPanel IconAndText(string icon, string text) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children =
        {
            new Icon(icon, 16) { VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = text, FontSize = Theme.Body, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    private static Button Make(Control content, string tooltip, params string[] classes)
    {
        var b = new Button
        {
            Content = content,
            Height = Theme.ControlHeight,
            Padding = new Thickness(Theme.Space3, 0),
            MinHeight = 0,
            MinWidth = 0,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Focusable = true,
        };
        foreach (string c in classes)
        {
            b.Classes.Add(c);
        }

        Tip(b, tooltip);
        return b;
    }
}
