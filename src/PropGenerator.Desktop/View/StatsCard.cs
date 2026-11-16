// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Threading;
using T = PropGenerator.Desktop.Look.Theme;

namespace PropGenerator.Desktop.View;

/// <summary>
/// easy-props' stats card, top right under the toolbar (top 46, right 18): the faces of the piece at the detail shown,
/// the view's draw calls and the piece's size (width × height × depth) in metres to one decimal, read again every
/// 500 ms while it shows. It takes no clicks.
/// </summary>
internal sealed class StatsCard : Border
{
    /// <summary>How often the card reads the numbers again, in milliseconds.</summary>
    internal const int RefreshMs = 500;

    private readonly Func<(int Faces, int DrawCalls, Vector3 Size)> _read;
    private readonly TextBlock[] _values = new TextBlock[3];

    /// <summary>Makes the card, hidden.</summary>
    /// <param name="read">Reads the faces, the draw calls and the size as they are now.</param>
    internal StatsCard(Func<(int Faces, int DrawCalls, Vector3 Size)> read)
    {
        _read = read;
        Labels = ["Faces", "Draw calls", "Size"];
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = T.Space3,
            RowSpacing = 2,
        };
        for (int i = 0; i < Labels.Count; i++)
        {
            var label = new TextBlock { Text = Labels[i], Foreground = T.OverlayMuted };
            var value = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
            _values[i] = value;
        }

        Child = grid;
        Padding = new Thickness(T.Space2, 6);
        BorderThickness = new Thickness(1);
        BorderBrush = T.OverlayBorder;
        CornerRadius = new CornerRadius(T.Radius3);
        Background = T.OverlayBg;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, (T.Space2 * 2) + T.ControlHeight + (T.Space1 * 2) + 2, 18, 0);
        IsHitTestVisible = false;
        IsVisible = false;
        TextElement.SetForeground(this, T.OverlayText);
        TextElement.SetFontSize(this, T.Caption);
        Timer.Tick += (_, _) => Refresh();
    }

    /// <summary>Gets the clock that reads the numbers again every 500 ms while the card shows.</summary>
    internal DispatcherTimer Timer { get; } = new() { Interval = TimeSpan.FromMilliseconds(RefreshMs) };

    /// <summary>Gets the names down the left: Faces, Draw calls and Size.</summary>
    internal IReadOnlyList<string> Labels { get; }

    /// <summary>Gets the numbers down the right, as shown.</summary>
    internal IReadOnlyList<string> Values => [.. _values.Select(v => v.Text ?? string.Empty)];

    /// <summary>
    /// A size as the card writes it, as easy-props does: "width × height × depth m", each to one decimal. Here Z is up,
    /// so that is X × Z × Y.
    /// </summary>
    /// <param name="size">The size along X, Y and Z (Z up), in metres.</param>
    /// <returns>The text.</returns>
    internal static string Size(Vector3 size) =>
        string.Create(CultureInfo.InvariantCulture, $"{size.X:0.0} × {size.Z:0.0} × {size.Y:0.0} m");

    /// <summary>Shows the card and starts reading the numbers every 500 ms, or hides it and stops.</summary>
    /// <param name="show">True to show it.</param>
    internal void Show(bool show)
    {
        IsVisible = show;
        if (show)
        {
            Refresh();
            Timer.Start();
        }
        else
        {
            Timer.Stop();
        }
    }

    /// <summary>Reads the numbers now (nothing while the card is hidden).</summary>
    internal void Refresh()
    {
        if (!IsVisible)
        {
            return;
        }

        (int faces, int calls, Vector3 size) = _read();
        _values[0].Text = faces.ToString("N0", CultureInfo.InvariantCulture);
        _values[1].Text = calls.ToString(CultureInfo.InvariantCulture);
        _values[2].Text = Size(size);
    }
}
