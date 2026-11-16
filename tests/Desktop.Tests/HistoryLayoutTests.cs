using PropGenerator.Desktop.View;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class HistoryLayoutTests
{
    [Fact]
    public void FewStepsAreFullSizeBoxesFromTheLeft()
    {
        var l = HistoryLayout.Of(200, 3, 2);

        Assert.Equal((0, 3), (l.First, l.Visible));
        Assert.Equal(HistoryLayout.MaxBox, l.Box);
        Assert.Equal(0, l.X(0));
        Assert.Equal(HistoryLayout.MaxBox + HistoryLayout.Gap, l.X(1));
    }

    [Fact]
    public void APositionMapsToTheBoxUnderItAndClampsPastTheEnds()
    {
        var l = HistoryLayout.Of(200, 3, 0);
        double step = HistoryLayout.MaxBox + HistoryLayout.Gap;

        Assert.Equal(0, l.IndexAt(0));
        Assert.Equal(0, l.IndexAt(step - 0.5));
        Assert.Equal(1, l.IndexAt(step));
        Assert.Equal(2, l.IndexAt((2 * step) + 3));
        Assert.Equal(2, l.IndexAt(500));
        Assert.Equal(0, l.IndexAt(-20));
        Assert.Equal(-1, HistoryLayout.Of(200, 0, -1).IndexAt(10));
    }

    [Fact]
    public void ManyStepsShrinkTheBoxesToFit()
    {
        var l = HistoryLayout.Of(100, 20, 19);

        Assert.Equal((0, 20), (l.First, l.Visible));
        Assert.InRange(l.Box, HistoryLayout.MinBox, HistoryLayout.MaxBox);
        Assert.True(l.X(19) + l.Box <= 100.001);
        Assert.Equal(19, l.IndexAt(99));
    }

    [Fact]
    public void TooManyStepsScrollToKeepTheCurrentOneInSight()
    {
        // 200 steps at the smallest box do not fit 100 wide: a window of them shows, around the current one.
        var end = HistoryLayout.Of(100, 200, 199);
        Assert.Equal(HistoryLayout.MinBox, end.Box);
        Assert.Equal(20, end.Visible);
        Assert.Equal(180, end.First);
        Assert.Equal(199, end.IndexAt(99));
        Assert.Equal(180, end.IndexAt(0));

        Assert.Equal(0, HistoryLayout.Of(100, 200, 0).First);
        var mid = HistoryLayout.Of(100, 200, 100);
        Assert.InRange(100, mid.First, mid.First + mid.Visible - 1);

        // While a drag goes on the window stays where it was when the drag began, so the boxes do not slide under the
        // pointer.
        var held = HistoryLayout.Of(100, 200, 150, first: 90);
        Assert.Equal(90, held.First);
        Assert.Equal(90, held.IndexAt(0));
    }
}
