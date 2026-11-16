using Xunit;

namespace PropGenerator.Desktop.Tests;

// The design's rule: the 3D view is never below 480 wide. The settings panel gives way first (narrower, down to its
// 240, then folded), then a drawer docked on the left (narrower, down to its own minimum). Nothing is remembered: the
// prefs keep what the user chose.
public class ShellFitTests
{
    [Fact]
    public void RoomEnoughChangesNothing()
    {
        Assert.Equal(new ShellWidths(320, false, 300), ShellFit.Widths(1500, 320, 300, 220));
        Assert.Equal(new ShellWidths(520, false, 0), ShellFit.Widths(1023, 520, 0, 0));
    }

    [Fact]
    public void ThePanelNarrowsFirst()
    {
        // 1023 − 320 − 300 leaves 403: the panel gives 77.
        Assert.Equal(new ShellWidths(243, false, 300), ShellFit.Widths(1023, 320, 300, 220));
    }

    [Fact]
    public void ThePanelFoldsBeforeTheDrawerNarrows()
    {
        // With a 560 drawer the panel would be under 240: it folds, then the drawer gives the last 17.
        Assert.Equal(new ShellWidths(0, true, 543), ShellFit.Widths(1023, 320, 560, 220));
    }

    [Fact]
    public void AHiddenPanelLeavesItToTheDrawer()
    {
        Assert.Equal(new ShellWidths(0, false, 220), ShellFit.Widths(700, 0, 300, 220));
    }

    [Fact]
    public void NothingGoesBelowItsMinimum()
    {
        Assert.Equal(new ShellWidths(0, true, 220), ShellFit.Widths(600, 320, 300, 220));

        // A closed drawer (its strip) does not narrow.
        Assert.Equal(new ShellWidths(0, true, 120), ShellFit.Widths(560, 320, 120, 120));
    }
}
