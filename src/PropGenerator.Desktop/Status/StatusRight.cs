using Avalonia;
using Avalonia.Controls;

namespace PropGenerator.Desktop.Status;

/// <summary>
/// The right side of the status bar, by the design's overflow rule: its items side by side, a gap apart, right-aligned,
/// each at its own width (the side never shrinks them). When there is less room than the side needs, items drop from
/// its left end until the rest fit; a dropped item is laid outside the panel, which clips it. Hidden items take no
/// room and no gap.
/// </summary>
internal sealed class StatusRight : Panel
{
    // Where a dropped item is laid: far outside the panel, which clips it.
    private const double Away = -100_000;

    // The widths of the visible items at the last measure, reused (one per child, so bounded by the children).
    private readonly List<double> _widths = [];

    /// <summary>Initializes a new instance of the <see cref="StatusRight"/> class.</summary>
    internal StatusRight()
    {
        ClipToBounds = true;
    }

    /// <summary>Gets or sets the gap between the items.</summary>
    internal double Spacing { get; set; }

    /// <summary>Gets how many items were dropped at the last measure.</summary>
    internal int DroppedCount { get; private set; }

    /// <summary>How many items drop from the left end so that the rest fit: none while they all fit.</summary>
    /// <param name="widths">The items' widths, left to right; 0 for a hidden item, which takes no gap either.</param>
    /// <param name="spacing">The gap between two shown items.</param>
    /// <param name="available">The width there is.</param>
    /// <returns>The number of items dropped, counted from the left end (hidden ones included in the count).</returns>
    internal static int Dropped(IReadOnlyList<double> widths, double spacing, double available)
    {
        ArgumentNullException.ThrowIfNull(widths);
        int dropped = 0;
        while (dropped < widths.Count && Needed(widths, dropped, spacing) > available)
        {
            dropped++;
        }

        return dropped;
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        _widths.Clear();
        double height = 0;
        foreach (Control child in Children)
        {
            child.Measure(Size.Infinity);
            _widths.Add(child.IsVisible ? child.DesiredSize.Width : 0);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        DroppedCount = Dropped(_widths, Spacing, availableSize.Width);
        return new Size(Needed(_widths, DroppedCount, Spacing), height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = finalSize.Width - Needed(_widths, DroppedCount, Spacing);
        for (int i = 0; i < Children.Count; i++)
        {
            Control child = Children[i];
            double width = i < _widths.Count ? _widths[i] : 0;
            if (i < DroppedCount || width <= 0)
            {
                child.Arrange(new Rect(Away, 0, child.DesiredSize.Width, finalSize.Height));
                continue;
            }

            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }

        return finalSize;
    }

    // The width the items from this one on need: their widths and a gap between each two shown.
    private static double Needed(IReadOnlyList<double> widths, int from, double spacing)
    {
        double sum = 0;
        int shown = 0;
        for (int i = from; i < widths.Count; i++)
        {
            if (widths[i] > 0)
            {
                sum += widths[i];
                shown++;
            }
        }

        return shown == 0 ? 0 : sum + (spacing * (shown - 1));
    }
}
