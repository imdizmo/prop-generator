using Avalonia;
using Avalonia.Controls;
using PropGenerator.Desktop.Status;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>The status bar's overflow rule: the right side never shrinks; below its own width, counters drop from its left end.</summary>
public class StatusOverflowTests
{
    [Fact]
    public void NothingDropsWhileEverythingFits()
    {
        // 40 + 16 + 60 + 16 + 30 = 162.
        Assert.Equal(0, StatusRight.Dropped([40, 60, 30], 16, 162));
        Assert.Equal(0, StatusRight.Dropped([40, 60, 30], 16, 1000));
    }

    [Fact]
    public void CountersDropFromTheLeftEndFirst()
    {
        Assert.Equal(1, StatusRight.Dropped([40, 60, 30], 16, 161));
        Assert.Equal(1, StatusRight.Dropped([40, 60, 30], 16, 106));
        Assert.Equal(2, StatusRight.Dropped([40, 60, 30], 16, 105));
        Assert.Equal(3, StatusRight.Dropped([40, 60, 30], 16, 29));
        Assert.Equal(3, StatusRight.Dropped([40, 60, 30], 16, 0));
    }

    [Fact]
    public void HiddenItemsTakeNoSpaceAndNoGap()
    {
        // The middle one is hidden (0 wide): 40 + 16 + 30 = 86 fits.
        Assert.Equal(0, StatusRight.Dropped([40, 0, 30], 16, 86));
        Assert.Equal(1, StatusRight.Dropped([40, 0, 30], 16, 85));
        Assert.Equal(0, StatusRight.Dropped([], 16, 0));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ThePanelDropsTheLeftmostAndKeepsTheRestAtTheirWidth()
    {
        var a = new Border { Width = 40 };
        var b = new Border { Width = 60 };
        var c = new Border { Width = 30 };
        var right = new StatusRight { Spacing = 16, Children = { a, b, c } };

        right.Measure(new Size(120, 24));
        right.Arrange(new Rect(0, 0, 120, 24));

        Assert.Equal(1, right.DroppedCount);
        Assert.Equal(106, right.DesiredSize.Width);
        Assert.True(a.Bounds.Right <= 0, "the dropped counter lies outside the panel, which clips it");
        Assert.True(right.ClipToBounds);

        // Right-aligned in the space given.
        Assert.Equal(new Rect(14, 0, 60, 24), b.Bounds);
        Assert.Equal(new Rect(90, 0, 30, 24), c.Bounds);

        right.Measure(new Size(500, 24));
        right.Arrange(new Rect(0, 0, 500, 24));
        Assert.Equal(0, right.DroppedCount);
        Assert.Equal(162, right.DesiredSize.Width);
    }
}
