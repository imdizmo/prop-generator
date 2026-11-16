// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Menus;

/// <summary>
/// The bar along the top, as easy-props' menu bar: the brand button (the drawer on All), the File, Edit, View, Prop
/// and Help menus, the piece's name, the "Search or run…" button, then Save and Export. Every menu item comes from the
/// command list. One menu is open at a time; moving along the bar with one open switches to the menu pointed at.
/// </summary>
internal sealed class TopBar : Border
{
    private static readonly Dictionary<string, string> MenuTips = new(StringComparer.Ordinal)
    {
        ["File"] = "File: start a new floor tile or rock, open and save work files, export or send to Groundwork and send to Blender.",
        ["Edit"] = "Edit: undo and redo the changes to the settings; the part commands come with part editing.",
        ["View"] = "View: how the piece is drawn and looked at, what is shown over it, and the layout of the window.",
        ["Prop"] = "Prop: try another version of the piece with Surprise me, use or copy its seed, or go back to the preset.",
        ["Help"] = "Help: search for any command, list the keyboard shortcuts, and see the version and credits.",
    };

    private readonly List<MenuButton> _menus = [];
    private MenuButton? _open;

    /// <summary>Builds the bar for a window.</summary>
    /// <param name="commands">The window's command list.</param>
    /// <param name="files">The piece as a file, whose name the bar shows.</param>
    /// <param name="layer">Where the menus' cards are drawn.</param>
    /// <param name="showAll">Opens the drawer on every starting point (the brand button).</param>
    internal TopBar(IReadOnlyList<Command> commands, FileActions files, MenuLayer layer, Action showAll)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(showAll);
        Layer = layer;
        layer.Dismissed += CloseMenus;

        Brand = new Button
        {
            Height = Look.Theme.ControlHeight,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(Look.Theme.Space1, 0, Look.Theme.Space2, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = AppMark(),
        };
        Brand.Classes.Add("ep");
        AutomationProperties.SetName(Brand, "Every starting point");
        Chrome.Tip(Brand, "Show every starting point (Ctrl+G)");
        Brand.Click += (_, _) =>
        {
            CloseMenus();
            showAll();
        };

        var menus = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        foreach (MenuLayout m in CommandList.Menus)
        {
            var b = new MenuButton(this, layer, m, commands, MenuTips[m.Title]);
            _menus.Add(b);
            menus.Children.Add(b);
        }

        Prop = new PropName(files);

        Palette = new Button
        {
            Height = Look.Theme.ControlHeight,
            MinHeight = 0,
            MinWidth = 150,
            MaxWidth = 340,
            Padding = new Thickness(Look.Theme.Space2, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Look.Theme.Space2, 0),
        };
        var search = new DockPanel { LastChildFill = true };
        var icon = new Icon("search", 14) { Margin = new Thickness(0, 0, Look.Theme.Space2, 0), VerticalAlignment = VerticalAlignment.Center };
        Border chip = Chrome.KeyChip("Ctrl+K");
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(chip, Dock.Right);
        search.Children.Add(icon);
        search.Children.Add(chip);
        search.Children.Add(new TextBlock
        {
            Text = "Search or run…",
            FontSize = Look.Theme.Label,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Palette.Content = search;
        Palette.Classes.Add("ep");
        Palette.Classes.Add("ep-palette");
        AutomationProperties.SetName(Palette, "Search or run");
        Chrome.Tip(Palette, "Search for any command, setting or starting point by typing part of its name, and run it (Ctrl+K).");
        Palette.Click += (_, _) => RunCommand(commands, "palette");

        Save = Chrome.IconTextButton("save", "Save", "Save (Ctrl+S)");
        Save.Margin = new Thickness(0, 0, 2, 0);
        AutomationProperties.SetName(Save, "Save");
        Save.Click += (_, _) => RunCommand(commands, "save");
        Export = Chrome.PrimaryButton("download", "Export", "Export to a Groundwork map (Ctrl+E)");
        Export.Margin = new Thickness(0, 0, Look.Theme.Space2, 0);
        AutomationProperties.SetName(Export, "Export");
        Export.Click += (_, _) => RunCommand(commands, "export");

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Look.Theme.Space2, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Brand);
        left.Children.Add(menus);
        left.Children.Add(new Border { Width = 1, Height = Look.Theme.Space4, Background = Look.Theme.BorderSubtle, VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(Prop);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Look.Theme.Space2, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(Save);
        right.Children.Add(Export);
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(left);
        row.Children.Add(right);
        row.Children.Add(Palette);

        Child = row;

        // A press anywhere on the bar but on a menu's title closes the open menu (a title opens or switches it).
        AddHandler(
            PointerPressedEvent,
            (_, e) =>
            {
                if (e.Source is not Visual v || !_menus.Any(m => m == v || m.IsVisualAncestorOf(v)))
                {
                    CloseMenus();
                }
            },
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            handledEventsToo: true);
        Height = Look.Theme.MenuBarHeight;
        Padding = new Thickness(Look.Theme.Space2, 0, 0, 0);
        Background = Look.Theme.SurfacePanel;
        BorderBrush = Look.Theme.BorderSubtle;
        BorderThickness = new Thickness(0, 0, 0, 1);
    }

    /// <summary>Gets the layer the menus are drawn in (the window puts it over everything else).</summary>
    internal MenuLayer Layer { get; }

    /// <summary>Gets the app mark at the left (the icon and the name), which opens the drawer on every starting point.</summary>
    internal Button Brand { get; }

    /// <summary>Gets the menus, left to right.</summary>
    internal IReadOnlyList<MenuButton> Menus => _menus;

    /// <summary>Gets the piece's name with its unsaved dot.</summary>
    internal PropName Prop { get; }

    /// <summary>Gets the "Search or run…" button.</summary>
    internal Button Palette { get; }

    /// <summary>Gets the Save button.</summary>
    internal Button Save { get; }

    /// <summary>Gets the Export button.</summary>
    internal Button Export { get; }

    /// <summary>Gets a value indicating whether a menu is open.</summary>
    internal bool MenuOpen => _open is not null;

    /// <summary>The menu with this title.</summary>
    /// <param name="title">"File", "Edit", "View", "Prop" or "Help".</param>
    /// <returns>The menu.</returns>
    internal MenuButton Menu(string title) => _menus.Single(m => m.Title == title);

    /// <summary>Closes the open menu, if any.</summary>
    internal void CloseMenus()
    {
        _open?.Close();
        _open = null;
    }

    /// <summary>Opens a menu, or closes it when it is the one open (its title was clicked).</summary>
    /// <param name="menu">The menu.</param>
    internal void Toggle(MenuButton menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        bool wasOpen = _open == menu;
        CloseMenus();
        if (!wasOpen)
        {
            _open = menu;
            menu.Open();
        }
    }

    /// <summary>The pointer came onto a menu's title: with another menu open, this one opens instead.</summary>
    /// <param name="menu">The menu.</param>
    internal void Hovered(MenuButton menu)
    {
        if (_open is not null && _open != menu)
        {
            Toggle(menu);
        }
    }

    /// <summary>A key pressed while a menu is open: Esc closes it, the arrows move in it.</summary>
    /// <param name="key">The key.</param>
    /// <returns>True when the key was used.</returns>
    internal bool HandleMenuKey(Key key)
    {
        if (_open is null)
        {
            return false;
        }

        if (key == Key.Escape)
        {
            CloseMenus();
            return true;
        }

        return _open.Move(key);
    }

    // The app mark: the app's icon at 20 (radius 5) and its name, 13 bold, as every Jatify tool's top bar starts.
    private static StackPanel AppMark()
    {
        var mark = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Look.Theme.Space2 };
        if (AppIcon.Bitmap() is { } bitmap)
        {
            mark.Children.Add(new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(5),
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill },
            });
        }
        else
        {
            mark.Children.Add(new Icon("trees", 16) { VerticalAlignment = VerticalAlignment.Center });
        }

        mark.Children.Add(new TextBlock
        {
            Text = "Prop Generator",
            FontSize = Look.Theme.Body,
            FontWeight = FontWeight.Bold,
            Foreground = Look.Theme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return mark;
    }

    private void RunCommand(IReadOnlyList<Command> commands, string id)
    {
        CloseMenus();
        _ = CommandList.Find(commands, id)!.Run();
    }
}
