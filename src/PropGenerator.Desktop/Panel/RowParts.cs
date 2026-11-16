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
/// The parts the rows of the controls panel share, as easy-props' <c>.prop</c> rows have them: the label with the
/// setting's tooltip, the reset button in its slot, a flat button, the sizes of the columns, and the decimals shown.
/// </summary>
internal static class RowParts
{
    /// <summary>The narrowest the label column gets (the shared LabelMin, 88).</summary>
    internal const double LabelMin = Look.Theme.LabelMin;

    /// <summary>The share of the row the label column takes: the design's slider row, minmax(88px, 36%).</summary>
    internal const double LabelShare = 0.36;

    /// <summary>The gap between the columns of a row.</summary>
    internal const double Gap = 6;

    /// <summary>The width of the number field (the design's value field, 56).</summary>
    internal const double FieldWidth = 56;

    /// <summary>The width (and height) of the reset slot.</summary>
    internal const double ResetSize = 18;

    /// <summary>
    /// Makes a row's label: quiet text, cut with an ellipsis, carrying the setting's own tooltip, shown to the left as
    /// in easy-props.
    /// </summary>
    /// <param name="text">The label.</param>
    /// <param name="tooltip">What the setting does.</param>
    /// <returns>The label.</returns>
    internal static TextBlock Label(string text, string tooltip)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = Look.Theme.Label,
            Foreground = Look.Theme.TextSecondary,
            Background = Brushes.Transparent,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        };
        Chrome.Tip(label, tooltip);
        ToolTip.SetPlacement(label, PlacementMode.Left);
        return label;
    }

    /// <summary>
    /// Makes the reset button of a row: the RotateCcw icon in an 18 px slot, shown only while the value differs from
    /// the starting point's.
    /// </summary>
    /// <param name="label">The setting's label, for its name ("Reset Width") and its tooltip.</param>
    /// <returns>The button, hidden.</returns>
    internal static Button Reset(string label)
    {
        var reset = Plain(new Icon("rotate-ccw", 12));
        reset.Width = ResetSize;
        reset.Height = ResetSize;
        reset.IsVisible = false;
        reset.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(reset, $"Reset {label}");
        Chrome.Tip(reset, $"Reset: put {label} back to what the starting point had. One undo step.");
        return reset;
    }

    /// <summary>
    /// A flat button drawn by a small template: its content on a transparent square with radius 4, a wash under the
    /// pointer, and the hand cursor. No Fluent chrome, so its size is its own.
    /// </summary>
    /// <param name="content">What it shows.</param>
    /// <returns>The button.</returns>
    internal static Button Plain(Control content)
    {
        var b = new Button
        {
            Content = content,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Look.Theme.TextTertiary,
            Cursor = new Cursor(StandardCursorType.Hand),
            Template = new FuncControlTemplate<Button>((button, _) =>
            {
                var face = new Border
                {
                    CornerRadius = new CornerRadius(Look.Theme.Radius1),
                    Child = new ContentControl
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                face.Bind(Border.BackgroundProperty, button.GetObservable(Button.BackgroundProperty));
                ((ContentControl)face.Child).Bind(ContentControl.ContentProperty, button.GetObservable(ContentControl.ContentProperty));
                return face;
            }),
        };
        b.PointerEntered += (_, _) =>
        {
            b.Background = Look.Theme.SurfaceActive;
            b.Foreground = Look.Theme.TextPrimary;
        };
        b.PointerExited += (_, _) =>
        {
            b.Background = Brushes.Transparent;
            b.Foreground = Look.Theme.TextTertiary;
        };
        return b;
    }

    /// <summary>
    /// The decimals shown for a step: easy-props' none from 1 up, one from 0.1, two from 0.01, and three below that
    /// (the floor's 0.005 steps), so every value on the step reads exactly.
    /// </summary>
    /// <param name="step">The setting's step.</param>
    /// <returns>0, 1, 2 or 3.</returns>
    internal static int Decimals(double step) => step >= 1 ? 0 : step >= 0.1 ? 1 : step >= 0.01 ? 2 : 3;
}
