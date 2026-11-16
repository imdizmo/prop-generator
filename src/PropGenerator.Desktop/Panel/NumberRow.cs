// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Details;

/// <summary>
/// One number on one line, as easy-props' PropertyRow: the label, a slider, the number field with its unit, and the
/// reset slot, with the clamp note on a second line. The row only shows: the settings panel moves the values.
/// </summary>
internal sealed class NumberRow : PanelRow
{
    /// <summary>How long the "Kept to…" note stays.</summary>
    internal static readonly TimeSpan NoteTime = TimeSpan.FromMilliseconds(2600);

    private long _note;

    /// <summary>Builds the row for a decimal or a whole number.</summary>
    /// <param name="spec">The setting.</param>
    internal NumberRow(ParamSpec spec)
        : base(spec, GridLength.Star, new GridLength(RowParts.FieldWidth), new GridLength(RowParts.ResetSize))
    {

        double step = spec.Step ?? (spec.Type == "int" ? 1 : 0.01);
        Slider = new Slider
        {
            Minimum = spec.Min ?? 0,
            Maximum = spec.Max ?? (spec.Type == "int" ? 100 : 1),
            SmallChange = step,
            LargeChange = step * 10,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            Value = spec.Default.GetDouble(),
            MinHeight = 0,
            Height = Look.Theme.RowHeight,
            VerticalAlignment = VerticalAlignment.Center,
            Template = SliderTemplate(),
        };
        AutomationProperties.SetName(Slider, spec.Label);
        Chrome.Tip(Slider, spec.Tooltip + " Drag, or click along the bar; hold Shift while dragging for fine steps.");
        SetColumn(Slider, 1);
        Children.Add(Slider);

        Box = new TextBox
        {
            MinWidth = 0,
            MinHeight = 0,
            Height = 20,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = Look.Theme.Label,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Bare(Box);
        AutomationProperties.SetName(Box, spec.Label + " value");
        Chrome.Tip(Box, spec.Tooltip + " Type an exact value and press Enter; ↑ and ↓ step it (with Shift, ten steps); Esc leaves it as it was.");
        var inside = new DockPanel { LastChildFill = true };
        if (spec.Unit is string unit)
        {
            Unit = new TextBlock
            {
                Text = unit,
                FontSize = Look.Theme.Caption,
                Foreground = Look.Theme.TextTertiary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
            };
            DockPanel.SetDock(Unit, Dock.Right);
            inside.Children.Add(Unit);
        }

        inside.Children.Add(Box);
        Field = new Border
        {
            Height = Look.Theme.ControlSmallHeight,
            Padding = new Thickness(6, 0),
            CornerRadius = new CornerRadius(Look.Theme.Radius2),
            Background = Look.Theme.SurfaceSunken,
            BorderBrush = Look.Theme.BorderSubtle,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = inside,
        };
        Field.PointerEntered += (_, _) => FieldBorder();
        Field.PointerExited += (_, _) => FieldBorder();
        Box.GotFocus += (_, _) =>
        {
            FieldBorder();

            // Focus selects everything, after the click that gave it (which would place the caret).
            Dispatcher.UIThread.Post(Box.SelectAll);
        };
        Box.LostFocus += (_, _) => FieldBorder();
        SetColumn(Field, 2);
        Children.Add(Field);

        Reset = RowParts.Reset(spec.Label);
        SetColumn(Reset, 3);
        Children.Add(Reset);

        Note = new TextBlock
        {
            FontSize = Look.Theme.Caption,
            Foreground = Look.Theme.Warning,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 3),
            IsVisible = false,
        };
        SetRow(Note, 1);
        SetColumnSpan(Note, 4);
        Children.Add(Note);
    }

    /// <summary>Gets the slider.</summary>
    internal Slider Slider { get; }

    /// <summary>Gets the number field's text box.</summary>
    internal TextBox Box { get; }

    /// <summary>Gets the number field around the box (and its unit).</summary>
    internal Border Field { get; }

    /// <summary>Gets the unit after the number, or null for a count or a plain amount.</summary>
    internal TextBlock? Unit { get; }

    /// <summary>Gets the reset button (shown only while the value differs from the starting point's).</summary>
    internal Button Reset { get; }

    /// <summary>Gets the note under the row ("Kept to 0.2–40.0 m"), hidden but for 2.6 s after a clamp.</summary>
    internal TextBlock Note { get; }

    /// <summary>Shows a note under the row for 2.6 s, in the warning colour.</summary>
    /// <param name="text">The note.</param>
    internal void ShowNote(string text)
    {
        long mine = ++_note;
        Note.Text = text;
        Note.IsVisible = true;
        _ = HideNote(mine);
    }

    // The note goes once its time is up, unless another came meanwhile.
    private async Task HideNote(long mine)
    {
        await Task.Delay(NoteTime).ConfigureAwait(true);
        if (_note == mine)
        {
            Note.IsVisible = false;
        }
    }

    /// <summary>The note easy-props shows when a typed value was out of range: "Kept to {min}–{max} {unit}".</summary>
    /// <param name="spec">The setting.</param>
    /// <returns>The note.</returns>
    internal static string KeptTo(ParamSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        string f = "F" + RowParts.Decimals(spec.Step ?? 0.01).ToString(CultureInfo.InvariantCulture);
        string min = (spec.Min ?? 0).ToString(f, CultureInfo.InvariantCulture);
        string max = (spec.Max ?? 1).ToString(f, CultureInfo.InvariantCulture);
        return $"Kept to {min}–{max}{(spec.Unit is null ? string.Empty : " " + spec.Unit)}";
    }

    // The design's field: a hairline at rest, the strong line under the pointer, the accent while it has the keyboard.
    private void FieldBorder() => Field.BorderBrush = Box.IsFocused ? Look.Theme.Accent : Field.IsPointerOver ? Look.Theme.BorderStrong : Look.Theme.BorderSubtle;

    // The Fluent text box draws its own border, background and focus underline: here the field around it does.
    private static void Bare(TextBox box)
    {
        foreach (string key in new[]
        {
            "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
        })
        {
            box.Resources[key] = Brushes.Transparent;
        }

        box.Resources["TextControlForeground"] = Look.Theme.TextPrimary;
        box.Resources["TextControlForegroundPointerOver"] = Look.Theme.TextPrimary;
        box.Resources["TextControlForegroundFocused"] = Look.Theme.TextPrimary;
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        box.Resources["TextControlThemeMinHeight"] = 0d;
        box.Resources["TextControlThemePadding"] = new Thickness(0);
    }

    // The design's slider: a 4 px pill track (the strong line) with its fill in the accent (lighter under the pointer)
    // and a 14 px white thumb with a 2 px ring of the window colour. The parts carry the names the slider looks for, so
    // a press anywhere on the bar sets the value and a drag follows the pointer.
    private static FuncControlTemplate<Slider> SliderTemplate() => new((slider, scope) =>
    {
        var fill = Bar("PART_DecreaseButton", Look.Theme.Accent);
        var rest = Bar("PART_IncreaseButton", Look.Theme.BorderStrong);
        var thumb = new Thumb
        {
            Name = "thumb",
            Width = 14,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
            Template = new FuncControlTemplate<Thumb>((_, _) => new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(Look.Theme.RadiusPill),
                Background = Look.Theme.TextPrimary,
                BoxShadow = new BoxShadows(new BoxShadow { Spread = 2, Color = ((ISolidColorBrush)Look.Theme.SurfacePage).Color }),
                VerticalAlignment = VerticalAlignment.Center,
            }),
        };
        var track = new Track
        {
            Name = "PART_Track",
            Orientation = Orientation.Horizontal,
            DecreaseButton = fill,
            IncreaseButton = rest,
            Thumb = thumb,
        };
        track.Bind(Track.MinimumProperty, slider.GetObservable(RangeBase.MinimumProperty));
        track.Bind(Track.MaximumProperty, slider.GetObservable(RangeBase.MaximumProperty));
        track.Bind(Track.ValueProperty, slider.GetObservable(RangeBase.ValueProperty));
        scope.Register(track.Name, track);
        scope.Register(fill.Name!, fill);
        scope.Register(rest.Name!, rest);
        scope.Register(thumb.Name, thumb);
        slider.PointerEntered += (_, _) => fill.Background = Look.Theme.AccentHover;
        slider.PointerExited += (_, _) => fill.Background = Look.Theme.Accent;
        return new Panel { Background = Brushes.Transparent, Children = { track } };
    });

    // One side of the track: a 4 px pill in the middle of a press area as high as the row.
    private static RepeatButton Bar(string name, IBrush colour) => new()
    {
        Name = name,
        Focusable = false,
        Background = colour,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Template = new FuncControlTemplate<RepeatButton>((button, _) =>
        {
            var pill = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(Look.Theme.RadiusPill),
                VerticalAlignment = VerticalAlignment.Center,
            };
            pill.Bind(Border.BackgroundProperty, button.GetObservable(TemplatedControl.BackgroundProperty));
            return new Border { Background = Brushes.Transparent, Child = pill };
        }),
    };
}
