using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

// The objects drawer is made taller or shorter (wider or narrower on the left) by its 6 px edge, docks at the bottom
// of the view or on its left, and remembers both; the 3D view is never below 480 wide.
public class DrawerDockTests
{
    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
    }

    private static void Key(MainWindow w, PhysicalKey key)
    {
        w.KeyPressQwerty(key, RawInputModifiers.None);
        Ui.Pump();
    }

    private static Point Centre(Visual v, Visual root) => v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), root)!.Value;

    private static void Drag(MainWindow w, Visual handle, double dx, double dy)
    {
        Point p = Centre(handle, w);
        w.MouseMove(p);
        w.MouseDown(p, MouseButton.Left);
        w.MouseMove(new Point(p.X + dx, p.Y + dy));
        w.MouseUp(new Point(p.X + dx, p.Y + dy), MouseButton.Left);
        Ui.Pump();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheEdgeMakesTheDrawerTallerWithinItsRangeAndIsRemembered()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        w.ViewState.DrawerOpen = true;
        Ui.Pump();
        Assert.Equal(220, w.Drawer.Card.Bounds.Height);

        // A 6 px edge along the top of the drawer, resizing up and down.
        Border edge = w.Drawer.Edge.Handle;
        Assert.Equal(6, edge.Bounds.Height);
        Drag(w, edge, 0, -60);
        Assert.Equal(280, w.Drawer.Card.Bounds.Height);
        Drag(w, edge, 0, -400);
        Assert.Equal(360, w.Drawer.Card.Bounds.Height);
        Drag(w, edge, 0, 600);
        Assert.Equal(160, w.Drawer.Card.Bounds.Height);
        Assert.Equal(160, new Prefs(s.P("prefs.json")).DrawerHeight);

        // ↑ and ↓ move it 16 px.
        edge.Focus();
        Key(w, PhysicalKey.ArrowUp);
        Assert.Equal(176, w.Drawer.Card.Bounds.Height);
        Assert.Equal(176, new Prefs(s.P("prefs.json")).DrawerHeight);

        // The pictures fill what the drawer gives them.
        w.Close();
        MainWindow again = BridgeKit.Open(s);
        Ui.Pump();
        Assert.Equal(176, again.Drawer.Card.Bounds.Height);
        again.Close();
    }

    // The edge shows a line-strong line while the pointer is on it.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheEdgeShowsAStrongLineOnHover()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        Ui.Pump();
        Border edge = w.Drawer.Edge.Handle;
        Assert.False(w.Drawer.Edge.Line.IsVisible);
        w.MouseMove(Centre(edge, w));
        Ui.Pump();
        Assert.True(w.Drawer.Edge.Line.IsVisible);
        Assert.Same(Look.Theme.BorderStrong, w.Drawer.Edge.Line.Background);
        w.MouseMove(new Point(700, 300));
        Ui.Pump();
        Assert.False(w.Drawer.Edge.Line.IsVisible);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheDockButtonMovesTheDrawerToTheLeftAndBack()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        w.ViewState.DrawerOpen = true;
        Ui.Pump();
        Assert.Equal("Dock on the left", w.Drawer.DockButton.Content);

        Click(w.Drawer.DockButton);
        Assert.True(w.ViewState.DrawerLeft);
        Assert.Equal("Dock at the bottom", w.Drawer.DockButton.Content);
        Assert.True(new Prefs(s.P("prefs.json")).DrawerLeft);

        // On the left: 300 wide, from under the top bar to the status bar, and the view starts after it.
        Rect card = new(w.Drawer.Card.TranslatePoint(default, w)!.Value, w.Drawer.Card.Bounds.Size);
        Assert.Equal(0, card.X);
        Assert.Equal(300, card.Width);
        Assert.Equal(w.TopBar.Bounds.Height, card.Y);
        Assert.Equal(w.StatusBar.TranslatePoint(default, w)!.Value.Y, card.Bottom);
        Assert.True(w.View.TranslatePoint(default, w)!.Value.X >= card.Right);

        // Its edge now runs down its right side and resizes it in width.
        Border edge = w.Drawer.Edge.Handle;
        Assert.Equal(6, edge.Bounds.Width);
        Drag(w, edge, 100, 0);
        Assert.Equal(400, w.Drawer.Card.Bounds.Width);
        Drag(w, edge, -400, 0);
        Assert.Equal(220, w.Drawer.Card.Bounds.Width);
        edge.Focus();
        Key(w, PhysicalKey.ArrowRight);
        Assert.Equal(236, w.Drawer.Card.Bounds.Width);
        Assert.Equal(236, new Prefs(s.P("prefs.json")).DrawerWidth);

        // The pictures flow in rows that fit the column, and the tabs wrap inside it.
        foreach (Control strip in w.Drawer.Strips)
        {
            Point? right = strip.TranslatePoint(new Point(strip.Bounds.Width, 0), w.Drawer.Card);
            Assert.True(right!.Value.X <= w.Drawer.Card.Bounds.Width, $"{strip.GetType().Name} ends at {right.Value.X}");
        }

        // The bottom height was not touched; remembered across a restart.
        Assert.Equal(220, new Prefs(s.P("prefs.json")).DrawerHeight);
        w.Close();
        MainWindow again = BridgeKit.Open(s);
        Ui.Pump();
        Assert.True(again.ViewState.DrawerLeft);
        Assert.Equal(236, again.Drawer.Card.Bounds.Width);

        // Back at the bottom; a closed drawer is opened by the move.
        again.ViewState.DrawerOpen = false;
        Click(again.Drawer.DockButton);
        Assert.False(again.ViewState.DrawerLeft);
        Assert.True(again.Drawer.IsOpen);
        Assert.Equal(220, again.Drawer.Card.Bounds.Height);
        again.Close();
    }

    // The View menu (and so the palette and Claude) has the dock side as an on/off item.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheViewMenuDocksTheDrawerOnTheLeft()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        Commands.Command c = Commands.CommandList.Find(w.Commands, "drawer-left")!;
        Assert.Equal("Dock the drawer on the left", c.Label);
        Assert.Equal(false, c.Checked());
        CommandListTests.Run(w, "drawer-left");
        Assert.True(w.ViewState.DrawerLeft);
        Assert.Equal(true, c.Checked());

        // Reset layout brings the drawer back to the bottom at its first sizes.
        w.Drawer.Edge.Handle.Focus();
        Key(w, PhysicalKey.ArrowRight);
        CommandListTests.Run(w, "reset-layout");
        Assert.False(w.ViewState.DrawerLeft);
        var prefs = new Prefs(s.P("prefs.json"));
        Assert.Equal(220, prefs.DrawerHeight);
        Assert.Equal(300, prefs.DrawerWidth);
        w.Close();
    }

    // At the narrowest window, a wide drawer on the left and the panel never leave the view under 480: the panel folds
    // to its edge first (the prefs keep it open), then the drawer narrows (the prefs keep its width).
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheViewIsNeverUnder480()
    {
        using var s = new Scratch();
        new Prefs(s.P("prefs.json")) { DrawerLeft = true, DrawerWidth = 560, DrawerOpen = true }.Save();
        MainWindow w = BridgeKit.Open(s);
        w.Width = MainWindow.MinimumWidth;
        Ui.Pump();

        Assert.True(w.View.Bounds.Width >= 480, $"the view is {w.View.Bounds.Width} wide");
        Assert.False(w.SidePanel.IsVisible);
        Assert.True(w.Edge.IsVisible);
        Assert.True(w.Drawer.Card.Bounds.Width < 560);
        var prefs = new Prefs(s.P("prefs.json"));
        Assert.True(prefs.PanelOpen);
        Assert.Equal(560, prefs.DrawerWidth);

        // Asking for the panel makes room by closing the drawer to its strip.
        Click(w.Edge.Tab);
        Assert.True(w.SidePanel.IsVisible);
        Assert.False(w.Drawer.IsOpen);
        Assert.True(w.View.Bounds.Width >= 480, $"the view is {w.View.Bounds.Width} wide");

        // A wider window gives the panel and the drawer back.
        w.Width = 1600;
        w.ViewState.DrawerOpen = true;
        Ui.Pump();
        Assert.True(w.SidePanel.IsVisible);
        Assert.Equal(560, w.Drawer.Card.Bounds.Width);
        Assert.Equal(320, w.SidePanel.Bounds.Width);
        w.Close();
    }
}
