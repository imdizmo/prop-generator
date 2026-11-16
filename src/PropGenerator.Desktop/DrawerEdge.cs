using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop;

/// <summary>
/// The objects drawer's 6 px edge (the design's splitter): along its top while it is at the bottom of the view, it
/// makes it taller or shorter (160 to 360); down its right side while it is on the left, wider or narrower (220 to 560).
/// It shows a line-strong line while the pointer is on it. ↑ and ↓ (→ and ← on the left) move it 16 px. The sizes are
/// kept in the prefs.
/// </summary>
internal sealed class DrawerEdge
{
    /// <summary>How far one press of an arrow moves the edge.</summary>
    internal const double KeyStep = 16;

    /// <summary>The edge's thickness.</summary>
    internal const double Thickness = 6;

    private readonly Prefs _prefs;
    private readonly Func<double> _shown;
    private (Point From, double Size)? _drag;

    /// <summary>Makes the edge.</summary>
    /// <param name="prefs">Where the drawer's sizes are kept.</param>
    /// <param name="shown">The drawer's size as shown now (its height at the bottom, its width on the left).</param>
    internal DrawerEdge(Prefs prefs, Func<double> shown)
    {
        _prefs = prefs;
        _shown = shown;
        Line = new Border { Background = Theme.BorderStrong, IsVisible = false, IsHitTestVisible = false };
        Handle = new Border
        {
            Background = Brushes.Transparent,
            Focusable = true,
            ZIndex = 5,
            Child = Line,
        };
        AutomationProperties.SetName(Handle, "Resize the objects drawer");
        Handle.PointerEntered += (_, _) => Line.IsVisible = true;
        Handle.PointerExited += (_, _) => Line.IsVisible = _drag is not null;
        Handle.PointerPressed += OnPressed;
        Handle.PointerMoved += OnMoved;
        Handle.PointerReleased += (_, _) => EndDrag();
        Handle.PointerCaptureLost += (_, _) => EndDrag();
        Handle.KeyDown += OnKey;
        Place(left: false, padding: 0);
    }

    /// <summary>Raised when a size changed (the prefs hold it).</summary>
    internal event Action? Changed;

    /// <summary>Gets the 6 px handle that is dragged.</summary>
    internal Border Handle { get; }

    /// <summary>Gets the line shown on the handle while the pointer is on it.</summary>
    internal Border Line { get; }

    /// <summary>Gets a value indicating whether the drawer is on the left (the edge runs down its right side).</summary>
    internal bool Left { get; private set; }

    /// <summary>Lays the edge along the drawer's top or down its right side, half outside the drawer's border.</summary>
    /// <param name="left">Whether the drawer is on the left.</param>
    /// <param name="padding">The drawer's padding, which the edge reaches past to its border.</param>
    internal void Place(bool left, double padding)
    {
        Left = left;
        double half = Thickness / 2;
        if (left)
        {
            Handle.Width = Thickness;
            Handle.Height = double.NaN;
            Handle.HorizontalAlignment = HorizontalAlignment.Right;
            Handle.VerticalAlignment = VerticalAlignment.Stretch;
            Handle.Margin = new Avalonia.Thickness(0, -padding, -padding - half, -padding);
            Handle.Cursor = new Cursor(StandardCursorType.SizeWestEast);
            Line.Width = 2;
            Line.Height = double.NaN;
            Line.HorizontalAlignment = HorizontalAlignment.Center;
            Line.VerticalAlignment = VerticalAlignment.Stretch;
            Chrome.Tip(Handle, "Drag to make the objects drawer wider or narrower (220 to 560 px); with the keyboard, → and ← move it 16 px.");
        }
        else
        {
            Handle.Width = double.NaN;
            Handle.Height = Thickness;
            Handle.HorizontalAlignment = HorizontalAlignment.Stretch;
            Handle.VerticalAlignment = VerticalAlignment.Top;
            Handle.Margin = new Avalonia.Thickness(-padding, -padding - half, -padding, 0);
            Handle.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
            Line.Width = double.NaN;
            Line.Height = 2;
            Line.HorizontalAlignment = HorizontalAlignment.Stretch;
            Line.VerticalAlignment = VerticalAlignment.Center;
            Chrome.Tip(Handle, "Drag to make the objects drawer taller or shorter (160 to 360 px); with the keyboard, ↑ and ↓ move it 16 px.");
        }
    }

    private void SetSize(double next)
    {
        if (Left)
        {
            double clamped = Math.Clamp(Math.Round(next), Prefs.MinDrawerWidth, Prefs.MaxDrawerWidth);
            if (clamped != _prefs.DrawerWidth)
            {
                _prefs.DrawerWidth = clamped;
                Changed?.Invoke();
            }
        }
        else
        {
            double clamped = Math.Clamp(Math.Round(next), Prefs.MinDrawerHeight, Prefs.MaxDrawerHeight);
            if (clamped != _prefs.DrawerHeight)
            {
                _prefs.DrawerHeight = clamped;
                Changed?.Invoke();
            }
        }
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Handle).Properties.IsLeftButtonPressed || TopLevel.GetTopLevel(Handle) is not { } top)
        {
            return;
        }

        _drag = (e.GetPosition(top), _shown());
        e.Pointer.Capture(Handle);
        e.Handled = true;
    }

    // At the bottom, moving the edge up makes the drawer taller; on the left, moving it right makes it wider.
    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_drag is not { } d || TopLevel.GetTopLevel(Handle) is not { } top)
        {
            return;
        }

        Point at = e.GetPosition(top);
        SetSize(Left ? d.Size + (at.X - d.From.X) : d.Size - (at.Y - d.From.Y));
    }

    private void EndDrag()
    {
        if (_drag is null)
        {
            return;
        }

        _drag = null;
        _prefs.Save();
        Line.IsVisible = Handle.IsPointerOver;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        double step = (Left, e.Key) switch
        {
            (true, Key.Right) or (false, Key.Up) => KeyStep,
            (true, Key.Left) or (false, Key.Down) => -KeyStep,
            _ => 0,
        };
        if (step == 0)
        {
            return;
        }

        SetSize((Left ? _prefs.DrawerWidth : _prefs.DrawerHeight) + step);
        _prefs.Save();
        e.Handled = true;
    }
}
