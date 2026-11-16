// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Controls;

namespace PropGenerator.Desktop.Details;

/// <summary>
/// One row of the controls panel, as easy-props' <c>.prop</c> grid laid out to the design's slider row: the label in a
/// column that flexes with the panel (36% of the row, at least 88 px), then the row's own columns, 6 apart, at least 28
/// high, with a second line for a note.
/// </summary>
internal abstract class PanelRow : Grid
{
    private readonly ColumnDefinition _label = new(new GridLength(RowParts.LabelMin));

    /// <summary>Lays out the row: its label first, then these columns.</summary>
    /// <param name="spec">The setting the row shows.</param>
    /// <param name="columns">The other columns' widths after the label (a star for the one that fills).</param>
    protected PanelRow(ParamSpec spec, params GridLength[] columns)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(columns);
        Spec = spec;
        Label = RowParts.Label(spec.Label, spec.Tooltip);
        ColumnDefinitions.Add(_label);
        foreach (GridLength c in columns)
        {
            ColumnDefinitions.Add(new ColumnDefinition(c));
        }

        RowDefinitions.Add(new RowDefinition(new GridLength(Look.Theme.RowHeight)));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        ColumnSpacing = RowParts.Gap;
        MinHeight = Look.Theme.RowHeight;
        Children.Add(Label);

        // The label lights up while the pointer is on the row.
        PointerEntered += (_, _) => Label.Foreground = Look.Theme.TextPrimary;
        PointerExited += (_, _) => Label.Foreground = Look.Theme.TextSecondary;
    }

    /// <summary>Gets the setting the row shows.</summary>
    internal ParamSpec Spec { get; }

    /// <summary>Gets the label, which carries the setting's tooltip.</summary>
    internal TextBlock Label { get; }

    /// <summary>
    /// Gets or sets a fixed width for the label's column (the sun popover's Shadow row has easy-props' 50 px), or null
    /// for the panel's minmax(88px, 36%).
    /// </summary>
    internal double? LabelColumn { get; set; }

    /// <summary>The label column's width for a row this wide: minmax(88px, 36%).</summary>
    /// <param name="width">The row's width.</param>
    /// <returns>The label column's width.</returns>
    internal static double LabelWidth(double width) => Math.Max(RowParts.LabelMin, Math.Round(width * RowParts.LabelShare));

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsFinite(availableSize.Width))
        {
            _label.Width = new GridLength(LabelColumn ?? LabelWidth(availableSize.Width));
        }

        return base.MeasureOverride(availableSize);
    }
}
