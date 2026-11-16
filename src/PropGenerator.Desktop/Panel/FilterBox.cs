// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Details;

/// <summary>
/// The filter box over the controls, as easy-props': 26 high, sunken, a search icon, "Filter controls", and a clear
/// button while there is text. The border turns to the accent while it has the keyboard; Esc clears it.
/// </summary>
internal sealed class FilterBox : Border
{
    /// <summary>Builds the box, empty.</summary>
    internal FilterBox()
    {
        Height = Look.Theme.ControlHeight;
        Padding = new Thickness(8, 0);
        CornerRadius = new CornerRadius(Look.Theme.Radius2);
        Background = Look.Theme.SurfaceSunken;
        BorderBrush = Look.Theme.BorderSubtle;
        BorderThickness = new Thickness(1);

        Box = new TextBox
        {
            PlaceholderText = "Filter controls",
            FontSize = Look.Theme.Label,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (string key in new[]
        {
            "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
        })
        {
            Box.Resources[key] = Brushes.Transparent;
        }

        Box.Resources["TextControlPlaceholderForeground"] = Look.Theme.TextTertiary;
        Box.Resources["TextControlPlaceholderForegroundPointerOver"] = Look.Theme.TextTertiary;
        Box.Resources["TextControlPlaceholderForegroundFocused"] = Look.Theme.TextTertiary;
        Box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        Box.Resources["TextControlThemeMinHeight"] = 0d;
        AutomationProperties.SetName(Box, "Filter controls");
        Chrome.Tip(Box, "Type part of a control's name, of what it does or of its group to show only the controls that match. Esc clears it.");

        Clear = new Button
        {
            Width = 18,
            Height = 18,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new Icon("x", 12),
            IsVisible = false,
        };
        Clear.Classes.Add("ep");
        Clear.Classes.Add("ep-clear");
        AutomationProperties.SetName(Clear, "Clear filter");
        Chrome.Tip(Clear, "Clear the filter and show every control again.");
        Clear.Click += (_, _) =>
        {
            Text = string.Empty;
            Box.Focus();
        };

        var icon = new Icon("search", 13) { Foreground = Look.Theme.TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(Clear, Dock.Right);
        icon.Margin = new Thickness(0, 0, 6, 0);
        Clear.Margin = new Thickness(6, 0, -4, 0);
        row.Children.Add(icon);
        row.Children.Add(Clear);
        row.Children.Add(Box);
        Child = row;

        Box.TextChanged += (_, _) =>
        {
            Clear.IsVisible = !string.IsNullOrEmpty(Box.Text);
            Changed?.Invoke(Box.Text ?? string.Empty);
        };
        Box.GotFocus += (_, _) => BorderBrush = Look.Theme.Accent;
        Box.LostFocus += (_, _) => BorderBrush = Look.Theme.BorderSubtle;
        Box.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    /// <summary>Raised with the new text whenever it changes (typed, cleared, or set).</summary>
    internal event Action<string>? Changed;

    /// <summary>Gets the text box typed in.</summary>
    internal TextBox Box { get; }

    /// <summary>Gets the clear button (shown while there is text).</summary>
    internal Button Clear { get; }

    /// <summary>Gets or sets the text of the filter.</summary>
    internal string Text
    {
        get => Box.Text ?? string.Empty;
        set => Box.Text = value;
    }

    // Esc with text clears it and goes no further; an empty box leaves Esc to the window.
    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Text.Length > 0)
        {
            Text = string.Empty;
            e.Handled = true;
        }
    }
}
