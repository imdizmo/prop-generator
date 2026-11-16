// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Details;

/// <summary>
/// The edge between the 3D view and the controls panel, as easy-props' PanelEdge: a one-pixel line with a 7 px handle
/// over it (drag it, or press ← and →, to make the panel 240 to 520 px wide) and a small tab at mid-height that hides
/// and shows the panel. The width and whether the panel is open are kept in the prefs.
/// </summary>
internal sealed class PanelEdge : Panel
{
    /// <summary>How far one press of ← or → moves the edge.</summary>
    internal const double KeyStep = 16;

    private readonly Prefs _prefs;
    private readonly Icon _chevronRight = new("chevron-right", 11);
    private readonly Icon _chevronLeft = new("chevron-left", 11);
    private (double X, double Width)? _drag;
    private double _shown;

    /// <summary>Builds the edge for the panel whose width and open state the prefs hold.</summary>
    /// <param name="prefs">Where the width and the open state are kept.</param>
    internal PanelEdge(Prefs prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        _prefs = prefs;
        Width = 1;
        Background = Look.Theme.BorderSubtle;
        ClipToBounds = false;
        ZIndex = 4;

        Handle = new Border
        {
            Margin = new Thickness(-3, 0, -3, 0),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
            Focusable = true,
        };
        AutomationProperties.SetName(Handle, "Resize details");
        Chrome.Tip(Handle, "Drag to make the controls panel wider or narrower (240 to 520 px); with the keyboard, ← and → move it 16 px.");
        Handle.PointerEntered += (_, _) => Handle.Background = Look.Theme.Accent;
        Handle.PointerExited += (_, _) =>
        {
            if (_drag is null)
            {
                Handle.Background = Brushes.Transparent;
            }
        };
        Handle.PointerPressed += OnPressed;
        Handle.PointerMoved += OnMoved;
        Handle.PointerReleased += (_, _) => EndDrag();
        Handle.PointerCaptureLost += (_, _) => EndDrag();
        Handle.KeyDown += OnKey;

        Tab = new Button
        {
            Width = 13,
            Height = 44,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(-12, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Look.Theme.TextTertiary,
            Opacity = 0.55,
            Template = new FuncControlTemplate<Button>((button, _) =>
            {
                var content = new ContentControl
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                content.Bind(ContentControl.ContentProperty, button.GetObservable(ContentControl.ContentProperty));
                return new Border
                {
                    Background = Look.Theme.SurfacePanel,
                    BorderBrush = Look.Theme.BorderSubtle,
                    BorderThickness = new Thickness(1, 1, 0, 1),
                    CornerRadius = new CornerRadius(Look.Theme.Radius2, 0, 0, Look.Theme.Radius2),
                    Child = content,
                };
            }),
        };
        Tab.PointerEntered += (_, _) =>
        {
            Tab.Opacity = 1;
            Tab.Foreground = Look.Theme.TextPrimary;
        };
        Tab.PointerExited += (_, _) =>
        {
            Tab.Opacity = 0.55;
            Tab.Foreground = Look.Theme.TextTertiary;
        };
        Tab.Click += (_, _) =>
        {
            if (Folded)
            {
                UnfoldAsked?.Invoke();
            }
            else
            {
                Toggle();
            }
        };
        Children.Add(Handle);
        Children.Add(Tab);
        Refresh();
    }

    /// <summary>Raised when Show details is clicked on a panel folded to make room for the view.</summary>
    internal event Action? UnfoldAsked;

    /// <summary>Gets a value indicating whether the panel is folded to keep the view 480 wide (open in the prefs, not shown).</summary>
    internal bool Folded { get; private set; }

    /// <summary>Raised when the panel's width or its open state changed (the prefs hold the new values).</summary>
    internal event Action? Changed;

    /// <summary>Gets the 7 px handle that is dragged to resize the panel (only while the panel is open).</summary>
    internal Border Handle { get; }

    /// <summary>Gets the tab that hides and shows the panel.</summary>
    internal Button Tab { get; }

    /// <summary>Hides the panel when it is open, shows it when it is hidden; remembered.</summary>
    internal void Toggle()
    {
        _prefs.PanelOpen = !_prefs.PanelOpen;
        _prefs.Save();
        Refresh();
        Changed?.Invoke();
    }

    /// <summary>Notes how the panel is shown: folded to make room (or not), and its width (a narrow window narrows it).</summary>
    /// <param name="folded">Whether it is folded for room.</param>
    /// <param name="width">Its width as shown.</param>
    internal void Fold(bool folded, double width)
    {
        _shown = width;
        if (folded != Folded)
        {
            Folded = folded;
            Refresh();
        }
    }

    /// <summary>Shows the prefs' state again (after Reset layout set them).</summary>
    internal void Refresh()
    {
        bool open = _prefs.PanelOpen && !Folded;
        Handle.IsVisible = open;
        Tab.Content = open ? _chevronRight : _chevronLeft;
        string tip = open
            ? "Hide details: fold the controls panel away to give the view more room."
            : "Show details: bring the controls panel back.";
        AutomationProperties.SetName(Tab, open ? "Hide details" : "Show details");
        Chrome.Tip(Tab, tip);
    }

    private void SetWidth(double next)
    {
        double clamped = Math.Clamp(Math.Round(next), Prefs.MinPanelWidth, Prefs.MaxPanelWidth);
        if (clamped == _prefs.PanelWidth)
        {
            return;
        }

        _prefs.PanelWidth = clamped;
        Changed?.Invoke();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Handle).Properties.IsLeftButtonPressed || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        _drag = (e.GetPosition(top).X, _shown > 0 ? _shown : _prefs.PanelWidth);
        e.Pointer.Capture(Handle);
        e.Handled = true;
    }

    // The panel is on the right: moving the edge left makes it wider.
    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_drag is not { } d || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        SetWidth(d.Width - (e.GetPosition(top).X - d.X));
    }

    private void EndDrag()
    {
        if (_drag is null)
        {
            return;
        }

        _drag = null;
        _prefs.Save();
        if (!Handle.IsPointerOver)
        {
            Handle.Background = Brushes.Transparent;
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right))
        {
            return;
        }

        SetWidth(_prefs.PanelWidth + (e.Key == Key.Left ? KeyStep : -KeyStep));
        _prefs.Save();
        e.Handled = true;
    }
}
