// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PropGenerator.Desktop.Menus;

/// <summary>
/// The layer over the window where open menus are drawn. While a menu is open, a see-through sheet below the top bar
/// takes a click anywhere else and closes the menus (the click does nothing more); the top bar itself stays live, so
/// another menu can be hovered or clicked.
/// </summary>
internal sealed class MenuLayer : Panel
{
    private readonly Canvas _canvas = new();

    /// <summary>Makes the layer, hidden until a menu opens.</summary>
    /// <param name="barHeight">The height of the top bar, which the sheet leaves uncovered.</param>
    internal MenuLayer(double barHeight)
    {
        var sheet = new Border { Background = Brushes.Transparent, Margin = new Thickness(0, barHeight, 0, 0) };
        sheet.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Dismissed?.Invoke();
        };
        Children.Add(sheet);
        Children.Add(_canvas);
        IsVisible = false;
    }

    /// <summary>Raised when the user clicked outside the menus.</summary>
    internal event Action? Dismissed;

    /// <summary>Shows a menu card with its top left corner at a point of the layer.</summary>
    /// <param name="card">The card.</param>
    /// <param name="at">Where its top left corner goes.</param>
    internal void Show(Control card, Point at)
    {
        Canvas.SetLeft(card, Math.Max(0, at.X));
        Canvas.SetTop(card, Math.Max(0, at.Y));
        _canvas.Children.Add(card);
        IsVisible = true;
    }

    /// <summary>Takes one card away; the layer hides with the last one.</summary>
    /// <param name="card">The card.</param>
    internal void Remove(Control card)
    {
        _canvas.Children.Remove(card);
        IsVisible = _canvas.Children.Count > 0;
    }
}
