using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Look;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

// The design's tabs: one row that never wraps or scrolls; the tabs that do not fit collapse into a last "More (n)"
// tab whose menu lists them; the first ("All") and the chosen one never collapse.
public class TabRowTests
{
    [Fact]
    public void EverythingFitsNoMore()
    {
        bool[] shown = TabRow.Fit([40, 60, 50], spacing: 2, more: 50, available: 154, chosen: 1);
        Assert.Equal([true, true, true], shown);
    }

    [Fact]
    public void TheTailCollapsesUntilTheRestAndMoreFit()
    {
        // 5 tabs of 50, 2 apart: 258 in all. In 200: the first three and More (50 + 2) take 52*3 + 50 = 206 > 200, so
        // the first two and More: 52*2 + 50 = 154.
        bool[] shown = TabRow.Fit([50, 50, 50, 50, 50], spacing: 2, more: 50, available: 200, chosen: 0);
        Assert.Equal([true, true, true, false, false], TabRow.Fit([50, 50, 50, 50, 50], 2, 50, 210, 0));
        Assert.Equal([true, true, false, false, false], shown);
    }

    [Fact]
    public void TheFirstAndTheChosenNeverCollapse()
    {
        // The chosen one at the end stays, as the last shown tab, and the ones before it go first.
        Assert.Equal([true, false, false, false, true], TabRow.Fit([50, 50, 50, 50, 50], 2, 50, 160, chosen: 4));

        // Even with no room at all, the first and the chosen stay (and More).
        Assert.Equal([true, false, true], TabRow.Fit([50, 50, 50], 2, 50, 10, chosen: 2));
    }

    private static TextBlock Label(ToggleButton b) => b.GetVisualDescendants().OfType<TextBlock>().First();

    // At the bottom of a window at its narrowest the kind tabs do not all fit: the tail goes into More (n), one row,
    // no scrollbar; More's menu lists them and picking one shows it, chosen.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DrawerTabsCollapseIntoMore()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        w.Width = MainWindow.MinimumWidth;
        w.ViewState.DrawerOpen = true;
        w.Drawer.ChooseTab("All");
        Ui.Pump();
        Ui.Pump();

        TabRow row = w.Drawer.Tabs;
        Assert.DoesNotContain(row.GetVisualAncestors().TakeWhile(a => a != w.Drawer.Card), a => a is ScrollViewer);
        Assert.True(row.MoreShown, "the tabs do not all fit at 1024");
        IReadOnlyList<ToggleButton> collapsed = row.Collapsed;
        Assert.NotEmpty(collapsed);
        Assert.Equal($"More ({collapsed.Count})", row.More.Content as string);
        Assert.DoesNotContain(w.Drawer.TabButtons[0], collapsed);

        // One row, inside the row's bounds; collapsed tabs take no room and no focus.
        double y = w.Drawer.TabButtons[0].Bounds.Y;
        foreach (ToggleButton b in w.Drawer.TabButtons.Where(b => b.Parent == row))
        {
            if (collapsed.Contains(b))
            {
                Assert.Equal(0, b.Bounds.Width);
                Assert.False(b.Focusable);
            }
            else
            {
                Assert.Equal(y, b.Bounds.Y);
                Assert.True(b.Bounds.Right <= row.Bounds.Width + 0.5, $"{AutomationProperties.GetName(b)} ends at {b.Bounds.Right} of {row.Bounds.Width}");
            }
        }

        Assert.True(row.More.Bounds.Right <= row.Bounds.Width + 0.5);

        // More's menu: the collapsed tabs with their counts; picking the last shows it, chosen, as the last shown tab.
        IReadOnlyList<string> items = row.MoreItems;
        Assert.Equal(collapsed.Select(TabRow.Text), items);
        ToggleButton last = collapsed[^1];
        row.PickFromMore(collapsed.Count - 1);
        Ui.Pump();
        Ui.Pump();
        Assert.True(last.IsChecked);
        Assert.DoesNotContain(last, row.Collapsed);
        Assert.True(last.Bounds.Width > 0);
        Assert.True(last.Bounds.Right <= row.Bounds.Width + 0.5);

        // Docked on the left the row still never wraps.
        w.ViewState.DrawerLeft = true;
        Ui.Pump();
        Ui.Pump();
        y = w.Drawer.TabButtons[0].Bounds.Y;
        Assert.All(w.Drawer.TabButtons.Where(b => b.Parent == row && !row.Collapsed.Contains(b)), b => Assert.Equal(y, b.Bounds.Y));
        Assert.True(row.MoreShown);
        w.Close();
    }

    // The underline tab: 13 px in the dim text; the chosen one in the text colour, semibold, with a 2 px accent
    // underline as wide as its label; counts in the muted colour. The open Objects toggle is selected: the selected wash
    // with the accent text.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DrawerTabsAreUnderlineTabs()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        w.ViewState.DrawerOpen = true;
        w.Drawer.ChooseTab("All");
        Ui.Pump();

        static ContentPresenter Face(ToggleButton b) =>
            b.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

        ToggleButton chosen = w.Drawer.TabButtons[0], other = w.Drawer.TabButtons[5];
        Assert.Contains("ep-tab", chosen.Classes);
        Assert.Equal(Tokens.Tab, chosen.Bounds.Height);
        Assert.Equal(13, Label(other).FontSize);
        Assert.Equal(Brushes.Transparent, Face(chosen).Background);
        Assert.Equal(Theme.TextPrimary, Face(chosen).Foreground);
        Assert.Equal(FontWeight.SemiBold, Face(chosen).FontWeight);
        Assert.Equal(Theme.TextSecondary, Face(other).Foreground);
        Assert.Equal(FontWeight.Normal, Face(other).FontWeight);

        Border line = TabRow.Underline(chosen);
        Assert.True(line.IsVisible);
        Assert.Equal(2, line.Bounds.Height);
        Assert.Equal(Theme.Accent, line.Background);
        Assert.Equal(Label(chosen).Bounds.Width, line.Bounds.Width, 1);
        Assert.False(TabRow.Underline(other).IsVisible);

        // "Trees 56": the count in the muted colour, 12 px.
        var count = (Avalonia.Controls.Documents.Run)Label(other).Inlines![1];
        Assert.Equal(Theme.TextTertiary, count.Foreground);
        Assert.Equal(12, count.FontSize);

        Assert.True(w.Drawer.OpenToggle.IsChecked);
        Assert.Equal(Theme.SurfaceSelected, Face(w.Drawer.OpenToggle).Background);
        Assert.Equal(Theme.AccentText, Face(w.Drawer.OpenToggle).Foreground);
        w.Close();
    }
}
