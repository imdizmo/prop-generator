using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Look;
using T = PropGenerator.Desktop.Look.Theme;

namespace PropGenerator.Desktop.View;

/// <summary>
/// The ZBrush-style history strip along the top of the 3D view (the prototype's .hist): a card with "History", one
/// small box per step of the undo history and, on the right, "3 / 12 · what changed". A click or a drag across the
/// boxes goes to that step; the steps after it (undone) are dark, the current one is the tall orange box. The arrow
/// keys step one at a time, Home and End go to the ends. While the pointer moves the marker follows at once, but the
/// step is shown only when the pointer is let go or rests for <see cref="SettleMs"/>, so a drag across twenty boxes
/// paints or builds once. Undo and Redo move the marker, as they move the history. Hidden while the history has a
/// single step (nothing to step through).
/// </summary>
internal sealed class HistoryStrip : Border
{
    /// <summary>How long the marker must rest during a drag (or between arrow keys) before the step is shown.</summary>
    internal const int SettleMs = 150;

    /// <summary>The strip's height (the prototype's 24).</summary>
    internal const double StripHeight = 24;

    private const string Tip = "History: click or drag across the boxes to step back and forward through your changes (or use the left and right arrow keys).";

    private readonly FileActions _files;
    private readonly Debounce _settle;

    // The step the marker shows while it waits to be shown (a drag or the arrow keys), and the history it belongs to.
    private int? _preview;
    private History? _held;

    // While a drag goes on, the first box shown stays put (a long history does not slide under the pointer).
    private int? _holdFirst;
    private bool _dragging;
    private int _hover = -1;

    // The focus ring shows only when the strip was reached or used with the keyboard.
    private bool _keys;

    /// <summary>Makes the strip; it follows the window's history from now on.</summary>
    /// <param name="files">The window's file actions, whose history it shows and steps.</param>
    internal HistoryStrip(FileActions files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
        _settle = new Debounce(TimeSpan.FromMilliseconds(SettleMs), Settle);

        Caps = new TextBlock
        {
            Text = "HISTORY",
            FontSize = Tokens.FsCaps,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = Tokens.CapsLetterSpacing,
            Foreground = Tokens.Mute,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        Label = new TextBlock
        {
            FontSize = Tokens.FsCaption,
            Foreground = Tokens.Dim,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        Boxes = new StepBoxes(this);
        DockPanel.SetDock(Caps, Dock.Left);
        DockPanel.SetDock(Label, Dock.Right);
        Child = new DockPanel { LastChildFill = true, Children = { Caps, Label, Boxes } };

        Height = StripHeight;
        Padding = new Thickness(10, 0);
        BorderThickness = new Thickness(1);
        BorderBrush = T.OverlayBorder;
        CornerRadius = new CornerRadius(Tokens.RMd);
        Background = T.OverlayBg;
        BoxShadow = T.Shadow1;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(T.Space2, T.Space2, T.Space2, 0);
        IsVisible = false;
        Chrome.Tip(Caps, Tip);

        _files.Changed += Refresh;
        _files.Entered += Refresh;
        Refresh();
    }

    /// <summary>Raised when the strip shows or hides, so the tools under it can move.</summary>
    internal event Action? ShownChanged;

    /// <summary>Gets the boxes, one per step.</summary>
    internal Control Boxes { get; }

    /// <summary>Gets "History" at the left.</summary>
    internal TextBlock Caps { get; }

    /// <summary>Gets "3 / 12 · what changed" at the right.</summary>
    internal TextBlock Label { get; }

    /// <summary>Gets the step the marker is on: the history's current step, or the one a drag is on.</summary>
    internal int At => _preview ?? _files.History.Index;

    /// <summary>Gets where the boxes lie now.</summary>
    internal HistoryLayout Layout => HistoryLayout.Of(Boxes.Bounds.Width, _files.History.Steps.Count, At, _holdFirst);

    /// <summary>Gets a value indicating whether a step is undone (after the marker), drawn dark.</summary>
    /// <param name="index">The step.</param>
    /// <returns>True when undone.</returns>
    internal bool IsUndone(int index) => index > At;

    /// <summary>The tooltip of a step's box: its number and what changed ("2. Seed 4").</summary>
    /// <param name="index">The step.</param>
    /// <returns>The tooltip.</returns>
    internal string TipFor(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{index + 1}. {_files.History.Steps[index].Label}");

    /// <summary>Gets the middle of a step's box, in the boxes' own coordinates.</summary>
    /// <param name="index">The step (one of those shown).</param>
    /// <returns>The point.</returns>
    internal Point BoxCentre(int index)
    {
        HistoryLayout l = Layout;
        return new Point(l.X(index) + (l.Box / 2), Boxes.Bounds.Height / 2);
    }

    /// <summary>Brings the strip up to date with the history: shown or not, the marker and the label.</summary>
    internal void Refresh()
    {
        History h = _files.History;
        if (_preview is not null && !ReferenceEquals(_held, h))
        {
            // Another piece was started or opened while a jump waited: the jump is dropped.
            Drop();
        }

        bool show = h.Steps.Count > 1;
        if (show != IsVisible)
        {
            IsVisible = show;
            ShownChanged?.Invoke();
        }

        int at = At;
        if (at >= 0 && at < h.Steps.Count)
        {
            string name = h.Steps[at].Label;
            Label.Text = string.Create(CultureInfo.InvariantCulture, $"{at + 1} / {h.Steps.Count} · {name}");
            ToolTip.SetTip(Label, name);
            AutomationProperties.SetName(Boxes, string.Create(CultureInfo.InvariantCulture, $"History, step {at + 1} of {h.Steps.Count}: {name}"));
        }

        Boxes.InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            // The prototype's max-width: 42% for the label.
            Label.MaxWidth = Math.Max(0, Bounds.Width * 0.42);
        }
    }

    // The marker goes to a step at once; the step is shown when things settle (or the pointer is let go).
    private void Pick(int index)
    {
        History h = _files.History;
        if (h.Steps.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, h.Steps.Count - 1);
        if (index == At)
        {
            return;
        }

        _preview = index;
        _held = h;
        _settle.Restart();
        Refresh();
    }

    // Shows the step the marker waits on, through the history's own Go: one show however many steps it passed.
    private void Settle()
    {
        if (_preview is not int i)
        {
            return;
        }

        bool same = ReferenceEquals(_held, _files.History);
        Drop();
        if (same)
        {
            _files.JumpTo(i);
        }

        Refresh();
    }

    private void Drop()
    {
        _settle.Cancel();
        _preview = null;
        _held = null;
    }

    private void Press(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Boxes).Properties.IsLeftButtonPressed || _files.History.Steps.Count == 0)
        {
            return;
        }

        e.Pointer.Capture(Boxes);
        e.Handled = true;
        _keys = false;
        Boxes.Focus();
        _dragging = true;
        _holdFirst = Layout.First;
        int i = Layout.IndexAt(e.GetPosition(Boxes).X);
        if (i == At)
        {
            // A click on the current step shows it again when something changed since it, as Undo does first.
            _files.JumpTo(i);
            return;
        }

        Pick(i);
    }

    private void Move(PointerEventArgs e)
    {
        int i = Layout.IndexAt(e.GetPosition(Boxes).X);
        if (_dragging)
        {
            Pick(i);
        }

        if (i != _hover)
        {
            _hover = i;
            ToolTip.SetTip(Boxes, i >= 0 ? TipFor(i) : Tip);
            Boxes.InvalidateVisual();
        }
    }

    private void Release()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _holdFirst = null;
        Settle();
    }

    private void OnKey(KeyEventArgs e)
    {
        int count = _files.History.Steps.Count;
        int? to = e.Key switch
        {
            Avalonia.Input.Key.Left => At - 1,
            Avalonia.Input.Key.Right => At + 1,
            Avalonia.Input.Key.Home => 0,
            Avalonia.Input.Key.End => count - 1,
            _ => null,
        };
        if (to is int i && e.KeyModifiers == KeyModifiers.None && count > 0)
        {
            e.Handled = true;
            _keys = true;
            Pick(i);
        }
    }

    /// <summary>The boxes, drawn rather than made of a control each, so a 200-step history costs no more than a few.</summary>
    private sealed class StepBoxes : Control
    {
        private const double Short = 10;
        private const double Tall = 16;
        private const double Round = 2;

        private readonly HistoryStrip _strip;

        internal StepBoxes(HistoryStrip strip)
        {
            _strip = strip;
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.SizeWestEast);
            ToolTip.SetShowDelay(this, T.TooltipDelayMs);
            ToolTip.SetTip(this, Tip);
            AutomationProperties.SetName(this, "History");
            VerticalAlignment = VerticalAlignment.Stretch;
            PointerPressed += (_, e) => _strip.Press(e);
            PointerMoved += (_, e) => _strip.Move(e);
            PointerReleased += (_, _) => _strip.Release();
            PointerCaptureLost += (_, _) => _strip.Release();
            PointerExited += (_, _) =>
            {
                _strip._hover = -1;
                ToolTip.SetTip(this, Tip);
                InvalidateVisual();
            };
            KeyDown += (_, e) =>
            {
                _strip.OnKey(e);
                InvalidateVisual();
            };
            GotFocus += (_, e) =>
            {
                _strip._keys = e.NavigationMethod == NavigationMethod.Tab;
                InvalidateVisual();
            };
            LostFocus += (_, _) => InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            // Hit testing takes the whole strip of boxes, gaps included.
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            HistoryLayout l = _strip.Layout;
            int at = _strip.At;
            double mid = Bounds.Height / 2;
            for (int i = l.First; i < l.First + l.Visible; i++)
            {
                IBrush fill = i == at ? Tokens.Accent : i > at ? Tokens.LineStrong : i == _strip._hover ? Tokens.Text : Tokens.Dim;
                double tall = i == at ? Tall : Short;
                context.DrawRectangle(fill, null, new RoundedRect(new Rect(l.X(i), mid - (tall / 2), l.Box, tall), Round));
            }

            if (IsFocused && _strip._keys)
            {
                context.DrawRectangle(null, new Pen(Tokens.Focus, 2), new RoundedRect(new Rect(Bounds.Size).Deflate(1), Tokens.RXs));
            }
        }
    }
}
