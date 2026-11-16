namespace PropGenerator.Desktop;

/// <summary>The widths shown across the window: the settings panel (0 when not shown) and a drawer docked on the left.</summary>
/// <param name="Panel">The settings panel's width, 0 when it is hidden or folded.</param>
/// <param name="PanelFolded">Whether the panel was folded to make room (it is open in the prefs).</param>
/// <param name="Drawer">The drawer's width on the left, 0 when it is at the bottom.</param>
internal readonly record struct ShellWidths(double Panel, bool PanelFolded, double Drawer);

/// <summary>
/// The design's rule for the window's width: the 3D view is never below 480 wide. The settings panel gives way first
/// (narrower, down to 240, then folded to its edge), then a drawer docked on the left (narrower, down to its minimum).
/// Only what is shown changes: the prefs keep what the user chose.
/// </summary>
internal static class ShellFit
{
    /// <summary>The narrowest the 3D view is ever made.</summary>
    internal const double MinView = 480;

    /// <summary>The widths to show.</summary>
    /// <param name="room">The width the view, the panel and a left drawer share.</param>
    /// <param name="panel">The panel's chosen width, 0 when it is hidden.</param>
    /// <param name="drawer">The drawer's chosen width on the left, 0 when it is at the bottom.</param>
    /// <param name="drawerMin">How narrow the drawer may be made (its own width when it cannot narrow).</param>
    /// <returns>What to show.</returns>
    internal static ShellWidths Widths(double room, double panel, double drawer, double drawerMin)
    {
        bool folded = false;
        if (panel > 0 && room - panel - drawer < MinView)
        {
            panel = room - drawer - MinView;
            if (panel < Prefs.MinPanelWidth)
            {
                panel = 0;
                folded = true;
            }
        }

        if (drawer > 0 && room - panel - drawer < MinView)
        {
            drawer = Math.Max(Math.Min(drawer, drawerMin), room - panel - MinView);
        }

        return new ShellWidths(panel, folded, drawer);
    }
}
