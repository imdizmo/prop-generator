// Adapted from Asset Shelf's Splash (MIT).
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PropGenerator.Desktop;

/// <summary>The window shown while the app starts, with the step it is on and how far along it is.</summary>
public sealed class Splash : Window
{
    private readonly TextBlock _step = new() { FontSize = 12, Foreground = Look.Theme.TextSecondary };
    private readonly TextBlock _percent = new() { FontSize = 12, Foreground = Look.Theme.TextTertiary, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Height = 4, MinHeight = 4, IsIndeterminate = true };

    /// <summary>Builds the window, centred on the screen, on its first step.</summary>
    public Splash()
    {
        Title = "Prop Generator";
        Width = 520;
        Height = 260;
        CanResize = false;
        ShowInTaskbar = true;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Look.Theme.SurfacePanel;
        var logo = new Image { Width = 64, Height = 64 };
        if (AppIcon.Bitmap() is { } icon)
        {
            Icon = new WindowIcon(icon);
            logo.Source = icon;
        }

        var status = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        status.Children.Add(_step);
        Grid.SetColumn(_percent, 1);
        status.Children.Add(_percent);
        Content = new Border
        {
            BorderBrush = Look.Theme.BorderStrong,
            BorderThickness = new Thickness(1),
            Child = new DockPanel
            {
                Children =
                {
                    new StackPanel { [DockPanel.DockProperty] = Dock.Bottom, Margin = new Thickness(24, 0, 24, 20), Spacing = 8, Children = { status, _bar } },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 16,
                        Margin = new Thickness(28, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            logo,
                            new StackPanel
                            {
                                VerticalAlignment = VerticalAlignment.Center,
                                Children =
                                {
                                    new TextBlock { Text = "Prop Generator", FontSize = 26, FontWeight = FontWeight.SemiBold, Foreground = Look.Theme.TextPrimary },
                                    new TextBlock { Text = "Game props made live: floors, rocks, cliffs and nature", FontSize = 13, Foreground = Look.Theme.TextTertiary },
                                },
                            },
                        },
                    },
                },
            },
        };
        Show("Starting", null);
    }

    /// <summary>Gets the step shown.</summary>
    internal string Step => _step.Text ?? string.Empty;

    /// <summary>Gets the percentage shown, empty while the length is unknown.</summary>
    internal string Percent => _percent.Text ?? string.Empty;

    /// <summary>Gets a value indicating whether the bar runs without a length.</summary>
    internal bool Indeterminate => _bar.IsIndeterminate;

    /// <summary>Shows a step and how far along the launch is.</summary>
    /// <param name="step">What is happening, such as "Painting the floor".</param>
    /// <param name="fraction">From 0 to 1; null while the length is unknown.</param>
    public void Show(string step, double? fraction)
    {
        _step.Text = step;
        _bar.IsIndeterminate = fraction is null;
        _bar.Value = fraction ?? 0;
        _percent.Text = fraction is { } f ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(f * 100):0}%") : string.Empty;
    }
}
