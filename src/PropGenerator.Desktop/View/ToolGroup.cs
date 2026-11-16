// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using T = PropGenerator.Desktop.Look.Theme;

namespace PropGenerator.Desktop.View;

/// <summary>
/// A group of tools over the 3D view, drawn as the design's toolbar over a 3D view: the tools side by side 2 apart, in a
/// card (radius 12, padding 4) with the overlay's border, its dark see-through background and the floating shadow. The
/// view's tools (Select, Move, Turn, Resize) are one such group top left; the view toolbar's are top right.
/// </summary>
internal sealed class ToolGroup : Border
{
    /// <summary>Makes the group.</summary>
    /// <param name="tools">The tools, left to right.</param>
    internal ToolGroup(params Control[] tools)
    {
        Tools = tools;
        Padding = new Thickness(T.Space1);
        BorderThickness = new Thickness(1);
        BorderBrush = T.OverlayBorder;
        CornerRadius = new CornerRadius(T.RadiusCard);
        Background = T.OverlayBg;
        BoxShadow = T.Shadow1;
        VerticalAlignment = VerticalAlignment.Top;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        row.Children.AddRange(tools);
        Child = row;
    }

    /// <summary>Gets the tools, left to right.</summary>
    internal IReadOnlyList<Control> Tools { get; }
}
