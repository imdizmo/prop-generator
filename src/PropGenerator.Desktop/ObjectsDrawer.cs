using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Pictures;
using PropGenerator.Desktop.Recipes;

namespace PropGenerator.Desktop;

/// <summary>
/// The objects drawer, after Groundwork's: every starting point (the floor and the rocks) as a picture with its name, in
/// tabs (All, Recent, Favourites, Mine, then each group) with a search over them. It floats as a card over the bottom of
/// the 3D view, or is docked down the left of the window (its head's Dock button, or View, Dock the drawer on the left);
/// its edge makes it taller (at the bottom) or wider (on the left), and the pictures flow in rows that fit it.
/// A click on a picture, or a picture dragged onto the view, starts a new piece from it; on Mine, a click opens one of
/// the user's own work files. Right click: favourite (a starting point) or open (a file). Ctrl+G opens and closes
/// it. Whether it is open is the view state's (kept in the prefs there); its tab, the favourites and the recent ones are
/// kept in the prefs.
/// </summary>
internal sealed class ObjectsDrawer : IDisposable
{
    private const double DragThreshold = 8;
    private const string All = "All";
    private const string Recent = "Recent";
    private const string Favourites = "Favourites";
    private const string Mine = "Mine";
    private const string MyPresets = UserPresets.Group;
    private static readonly Color NoColour = Color.Parse("#7A7A74");
    private static readonly IBrush TileBackground = Look.Theme.SurfaceRaised;

    private readonly FileActions _files;
    private readonly Prefs _prefs;
    private readonly ViewState _view;
    private readonly Library _library;
    private readonly ThumbCache _thumbs;
    private readonly Visual _surface;
    private readonly Action<string> _say;
    private readonly List<DrawerTile> _points = [];
    private readonly List<DrawerTile> _presetTiles = [];
    private readonly UserPresets _presets;
    private readonly Func<StartingPoint, Task> _renamePreset;
    private readonly Action<StartingPoint> _removePreset;
    private readonly Dictionary<string, DrawerTile> _mine = new(StringComparer.Ordinal);
    private readonly List<(string Tab, ToggleButton Button)> _tabs = [];
    private readonly Dictionary<string, string> _kindOfGroup = new(StringComparer.Ordinal);
    private readonly HashSet<string> _kinds = new(StringComparer.Ordinal);
    private readonly WrapPanel _groupRow = new();
    private readonly ToggleButton _allOfKind = Chip();
    private readonly WrapPanel _wrap = new();
    private readonly DockPanel _head = new() { LastChildFill = true };
    private readonly StackPanel _top = new() { Spacing = 8 };
    private readonly DockPanel _body = new();
    /// <summary>Gets the search box, with its magnifier on the left.</summary>
    internal TextBox SearchBox { get; } = new()
    {
        PlaceholderText = "Search",
        Width = 170,
        FontSize = 12,
        InnerLeftContent = new Look.Icon("search", 13) { Foreground = Look.Theme.TextTertiary, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
    };

    private readonly TextBlock _toggleLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _toggleRow = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private Look.Icon? _toggleIcon;
    private readonly ScrollViewer _scroll;
    private readonly CancellationTokenSource _stop = new();
    private List<DrawerTile> _mineOrder = [];
    private List<DrawerTile> _shown = [];
    private Task _pictures = Task.CompletedTask;
    private string _tab;
    private (DrawerTile Tile, Point From)? _press;
    private bool _dragging;
    private bool _disposed;

    /// <summary>Makes the drawer, shows the user's files and starts drawing the pictures that are not kept yet.</summary>
    /// <param name="files">Starts and opens pieces.</param>
    /// <param name="prefs">Where the drawer's tab, favourites and recent starting points are kept.</param>
    /// <param name="view">Holds whether the drawer is open.</param>
    /// <param name="library">The user's own work files.</param>
    /// <param name="thumbs">Where the pictures are kept.</param>
    /// <param name="points">Every starting point, in the order shown.</param>
    /// <param name="surface">The see-through surface over the 3D view (a picture let go on it starts a piece).</param>
    /// <param name="say">Shows a line in the status bar.</param>
    /// <param name="presets">My presets, shown on their own tab.</param>
    /// <param name="renamePreset">Asks for a preset's new name and renames it (its tile's menu).</param>
    /// <param name="removePreset">Removes a preset (its tile's menu).</param>
    internal ObjectsDrawer(
        FileActions files, Prefs prefs, ViewState view, Library library, ThumbCache thumbs, IReadOnlyList<StartingPoint> points,
        Visual surface, Action<string> say, UserPresets presets, Func<StartingPoint, Task> renamePreset, Action<StartingPoint> removePreset)
    {
        _presets = presets;
        _renamePreset = renamePreset;
        _removePreset = removePreset;
        _files = files;
        _prefs = prefs;
        _view = view;
        _library = library;
        _thumbs = thumbs;
        _surface = surface;
        _say = say;
        foreach (StartingPoint p in points)
        {
            DrawerTile t = PointTile(p);
            _points.Add(t);
            Draw(t, p);
        }

        // As easy-props 0.4.5: a row of kinds, and the chosen kind's groups under it when it has more than one.
        foreach (StartingPoint p in points)
        {
            _kindOfGroup.TryAdd(p.Group, KindOf(p));
        }

        string[] kinds = [.. Kinds.Where(_kindOfGroup.ContainsValue)];
        _kinds.UnionWith(kinds);
        string[] groups = [.. _kindOfGroup.Keys.Where(g => _kindOfGroup.Values.Count(k => k == _kindOfGroup[g]) > 1)];
        foreach (string tab in (string[])[All, Recent, Favourites, Mine, MyPresets, .. kinds, .. groups])
        {
            ToggleButton b = groups.Contains(tab) ? Chip() : Look.TabRow.MakeTab();
            ToolTip.SetTip(b, TabTip(tab));
            Avalonia.Automation.AutomationProperties.SetName(b, tab);
            b.Click += (_, _) => ChooseTab(tab);
            _tabs.Add((tab, b));
        }

        _allOfKind.Content = All;
        _allOfKind.Click += (_, _) => ChooseTab(Kind ?? All);
        ToolTip.SetTip(_allOfKind, "Click to show every starting point of this kind.");
        _tab = Resolve(prefs.DrawerTab) ?? All;
        _scroll = new ScrollViewer
        {
            Content = _wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 8, 0, 0),
        };
        foreach ((string _, ToggleButton b) in _tabs.Where(t => !groups.Contains(t.Tab)))
        {
            Tabs.Add(b);
        }

        _groupRow.Children.Add(_allOfKind);
        _groupRow.Children.AddRange(_tabs.Where(t => groups.Contains(t.Tab)).Select(t => t.Button));
        foreach (Control chip in _groupRow.Children)
        {
            chip.Margin = new Thickness(0, 0, 3, 3);
        }

        ToolTip.SetTip(
            SearchBox,
            "Type part of a name or of a group to find starting points in every tab (your own files too, on Mine). Click a tab to clear it.");
        Avalonia.Automation.AutomationProperties.SetName(SearchBox, "Search");
        SearchBox.TextChanged += (_, _) => Filter();
        ToolTip.SetTip(
            OpenToggle,
            "Click to open or close the objects drawer, with every starting point and your own work files. Shortcut: Ctrl+G.");
        Avalonia.Automation.AutomationProperties.SetName(OpenToggle, "Objects");
        OpenToggle.Classes.Add("ep-toggle");
        OpenToggle.Classes.Add("ep-objects");
        OpenToggle.IsChecked = view.DrawerOpen;
        OpenToggle.IsCheckedChanged += (_, _) =>
        {
            _view.DrawerOpen = IsOpen;
            Opened();
        };
        view.Changed += OnViewChanged;

        DockButton.Classes.Add("ep");
        DockButton.Click += (_, _) =>
        {
            _view.DrawerLeft = !_view.DrawerLeft;
            _view.DrawerOpen = true;
        };
        Edge = new DrawerEdge(prefs, () => OnLeft ? Card!.Bounds.Width : Card!.Bounds.Height);

        // At the bottom the tabs fill what is left of the head's row, the ones that do not fit under More (n); on the
        // left the search and the tabs go under the row, in one row too.
        DockPanel.SetDock(OpenToggle, Dock.Left);
        DockPanel.SetDock(DockButton, Dock.Right);
        DockPanel.SetDock(SearchBox, Dock.Left);
        DockPanel.SetDock(_top, Dock.Top);
        _body.Children.Add(_top);
        _body.Children.Add(_scroll);
        Card = new Border
        {
            Child = new Panel { Children = { _body, Edge.Handle } },
            Padding = new Thickness(10),
        };
        Arrange(left: false);

        files.Changed += OnFilesChanged;
        files.Renamed += RefreshMine;
        files.RecipeChanged += OnRecipeChanged;
        presets.Changed += RefreshPresets;
        Opened();
        RefreshMine();
        RefreshPresets();
    }

    /// <summary>Gets the Objects toggle, which opens and closes the drawer.</summary>
    internal ToggleButton OpenToggle { get; } = new()
    {
        FontSize = Look.Theme.Body,
        Height = Look.Theme.ControlHeight,
        MinHeight = 0,
        Padding = new Thickness(9, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    /// <summary>Gets the kind tabs: one row of underline tabs, the ones that do not fit under More (n).</summary>
    internal Look.TabRow Tabs { get; } = new();

    /// <summary>Gets the button in the drawer's head that docks it on the left, or back at the bottom.</summary>
    internal Button DockButton { get; } = new()
    {
        FontSize = Look.Theme.Body,
        Height = Look.Theme.ControlHeight,
        MinHeight = 0,
        Padding = new Thickness(Look.Theme.Space3, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    /// <summary>Gets the drawer's edge, which makes it taller (at the bottom) or wider (on the left).</summary>
    internal DrawerEdge Edge { get; }

    /// <summary>
    /// Gets the drawer's card: a card over the bottom of the 3D view (it takes its own clicks), or a docked column down
    /// the left of the window.
    /// </summary>
    public Border Card { get; }

    /// <summary>Gets a value indicating whether the drawer is laid out on the left.</summary>
    internal bool OnLeft { get; private set; }

    /// <summary>
    /// Lays the drawer out at the bottom of the view (a card over it) or on the left (a docked column), at the size it
    /// is shown: its height at the bottom, its width on the left (a closed drawer takes only what its head needs).
    /// </summary>
    /// <param name="left">Whether it is on the left.</param>
    /// <param name="size">Its height at the bottom or its width on the left, while it is open.</param>
    internal void Place(bool left, double size)
    {
        if (left != OnLeft)
        {
            Arrange(left);
        }

        bool open = _view.DrawerOpen;
        Card.Height = !left && open ? size : double.NaN;
        Card.Width = left && open ? size : double.NaN;
    }

    /// <summary>Gets a value indicating whether the drawer is open (closed, only its header row shows).</summary>
    internal bool IsOpen => OpenToggle.IsChecked == true;

    /// <summary>Gets the tab buttons, in order.</summary>
    internal IReadOnlyList<ToggleButton> TabButtons => [.. _tabs.Select(t => t.Button)];

    /// <summary>Gets what the toggle says, such as "Objects 13".</summary>
    internal string ToggleText => _toggleLabel.Text ?? string.Empty;

    /// <summary>Gets the chevron the toggle shows: down while open at the bottom, up while shut there, left and right on the left.</summary>
    internal string ToggleIcon => _toggleIcon?.IconName ?? string.Empty;

    /// <summary>Gets the tabs as they read, in order: "All", "Recent 2", ..., "Trees 47", ..., then every kind's groups, "Boulders 3".</summary>
    internal IReadOnlyList<string> TabNames => [.. _tabs.Select(t => Label(t.Tab))];

    /// <summary>Gets the first row as it reads: "All", "Recent 2", ..., then the kinds, "Trees 47", ..., "Floors 1".</summary>
    internal IReadOnlyList<string> KindNames => [.. _tabs.Where(t => !_kindOfGroup.ContainsKey(t.Tab) || _kinds.Contains(t.Tab)).Select(t => Label(t.Tab))];

    /// <summary>Gets the row of groups under the kinds as it reads ("All", "Boulders 3", ...); empty when it is hidden.</summary>
    internal IReadOnlyList<string> GroupNames => _groupRow.IsVisible
        ? [.. _groupRow.Children.OfType<ToggleButton>().Where(b => b.IsVisible).Select(b => b.Content as string ?? string.Empty)]
        : [];

    /// <summary>Gets the two rows of tabs, for checking that they stay inside the drawer.</summary>
    internal IReadOnlyList<Control> Strips => [Tabs, _groupRow];

    /// <summary>Gets the names of the pictures shown now, in order.</summary>
    internal IReadOnlyList<string> VisibleTileNames => [.. _shown.Select(t => t.Name)];

    /// <summary>Opens or closes it (Ctrl+G).</summary>
    public void Toggle() => _view.DrawerOpen = !_view.DrawerOpen;

    /// <summary>Opens it on a tab (My props opens Mine, the brand button All); the search is emptied.</summary>
    /// <param name="name">The tab, by its name ("Mine") or as it reads ("Mine 3").</param>
    /// <exception cref="ArgumentException">There is no such tab.</exception>
    internal void ShowTab(string name)
    {
        ChooseTab(name);
        _view.DrawerOpen = true;
    }

    /// <summary>Reads the library again: Mine shows the user's files as they are now, newest first.</summary>
    public void RefreshMine()
    {
        if (_disposed)
        {
            return;
        }

        IReadOnlyList<LibraryEntry> entries = _library.List();
        var next = new List<DrawerTile>(entries.Count);
        var kept = new Dictionary<string, DrawerTile>(StringComparer.Ordinal);
        foreach (LibraryEntry e in entries)
        {
            if (kept.ContainsKey(e.Path))
            {
                continue;
            }

            if (_mine.TryGetValue(e.Path, out DrawerTile? t) && t.Entry == e)
            {
                // The same file: its picture is read again only when it may have changed (the file being worked on is
                // pictured after each save) or is not there yet.
                if (t.SwatchShown || e.Path == _files.FilePath)
                {
                    Draw(t, e);
                }
            }
            else
            {
                t = FileTile(e);
                Draw(t, e);
            }

            kept[e.Path] = t;
            next.Add(t);
        }

        foreach ((string path, DrawerTile old) in _mine)
        {
            if (!kept.TryGetValue(path, out DrawerTile? now) || !ReferenceEquals(now, old))
            {
                old.Forget();
            }
        }

        _mine.Clear();
        foreach ((string path, DrawerTile t) in kept)
        {
            _mine[path] = t;
        }

        _mineOrder = next;
        Marked();
        UpdateTabs();
        Filter();
    }

    /// <summary>Makes My presets' tiles again from the presets as they are now.</summary>
    internal void RefreshPresets()
    {
        if (_disposed)
        {
            return;
        }

        foreach (DrawerTile old in _presetTiles)
        {
            old.Forget();
        }

        _presetTiles.Clear();
        foreach (StartingPoint p in _presets.All)
        {
            var t = new DrawerTile(
                p.Name,
                $"{p.Name} (one of My presets)\nClick: start a new piece from it · right click: rename or remove it",
                NoColour, TileBackground, p, null);
            var rename = new MenuItem { Header = "Rename…" };
            rename.Click += (_, _) => _ = _renamePreset(p);
            var remove = new MenuItem { Header = "Remove" };
            remove.Click += (_, _) => _removePreset(p);
            t.Border.ContextMenu = new ContextMenu { ItemsSource = new[] { rename, remove } };
            Pointer(t);
            _presetTiles.Add(t);
            Draw(t, p);
        }

        Marked();
        UpdateTabs();
        Filter();
    }

    /// <summary>Shows a tab, by its name ("Boulders") or as it reads ("Boulders 3"); the search is emptied.</summary>
    /// <param name="name">The tab.</param>
    /// <exception cref="ArgumentException">There is no such tab.</exception>
    internal void ChooseTab(string name)
    {
        string tab = Resolve(name) ?? throw new ArgumentException($"The drawer has no tab '{name}'.", nameof(name));
        _tab = tab;
        if (_prefs.DrawerTab != tab)
        {
            _prefs.DrawerTab = tab;
            _prefs.Save();
        }

        SearchBox.Text = string.Empty;
        UpdateTabs();
        Filter();
        if (tab == MyPresets && _presets.Hidden is { } hidden)
        {
            _say(hidden);
        }
    }

    /// <summary>Types into the search box.</summary>
    /// <param name="text">What it then says.</param>
    internal void SetSearch(string text)
    {
        SearchBox.Text = text;
        Filter();
    }

    /// <summary>A picture, by its name: one shown now first, else any.</summary>
    /// <param name="name">The name under the picture.</param>
    /// <returns>The tile.</returns>
    internal DrawerTile Tile(string name) =>
        _shown.FirstOrDefault(t => t.Name == name) ?? _points.Concat(_presetTiles).Concat(_mineOrder).First(t => t.Name == name);

    /// <summary>Does what a click on a picture does.</summary>
    /// <param name="name">The name under the picture.</param>
    /// <returns>Done when the piece is started or opened, or nothing was done.</returns>
    internal Task ClickTileAsync(string name) => UseAsync(Tile(name));

    /// <summary>Stops drawing pictures and lets the drawer's pictures go.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _files.Changed -= OnFilesChanged;
        _files.Renamed -= RefreshMine;
        _files.RecipeChanged -= OnRecipeChanged;
        _presets.Changed -= RefreshPresets;
        _view.Changed -= OnViewChanged;
        _ = StopDrawing();
        _stop.Dispose();
        foreach (DrawerTile t in _points.Concat(_presetTiles).Concat(_mineOrder))
        {
            t.Forget();
        }
    }

    /// <summary>
    /// Stops drawing pictures: none is started after this and none still coming is shown. Safe from any thread (the
    /// tests stop every window's drawer before their scratch folders go).
    /// </summary>
    /// <returns>Done when the picture being drawn now, if any, is finished.</returns>
    internal Task StopDrawing()
    {
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Stopped and let go already.
        }

        return Volatile.Read(ref _pictures);
    }

    // The kinds of the first row, in easy-props 0.4.5's order, with the floor last.
    private static readonly string[] Kinds = ["Trees", "Palms", "Bushes", "Flowers", "Grass", "Cacti", "Rocks", "Floors"];

    // A group under the chosen kind: a chip (a pill 22 high).
    private static ToggleButton Chip()
    {
        var b = new ToggleButton
        {
            FontSize = Look.Theme.Label,
            Height = 22,
            Padding = new Thickness(10, 0),
            MinHeight = 0,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        b.Classes.Add("ep-chip");
        return b;
    }

    // ponytail: kinds by recipe id; a new recipe lands under Trees until it is named here.
    private static string KindOf(StartingPoint p) => p.Group == "Palms & tropical" ? "Palms" : p.Recipe switch
    {
        "grass" => "Grass",
        "plant" => "Flowers",
        "bush" => "Bushes",
        "cactus" => "Cacti",
        "cut-rock" or "slab-cliff" or "stylized-rock" => "Rocks",
        FloorRecipe.Id => "Floors",
        _ => "Trees",
    };

    // The kind shown now: the chosen kind, or the kind of the chosen group; null for All, Recent and the like.
    private string? Kind => _kinds.Contains(_tab) ? _tab : _kindOfGroup.TryGetValue(_tab, out string? kind) && _tabs.Any(t => t.Tab == _tab) ? kind : null;

    // A tab by its name or as it reads; a group of a kind that has no other is its kind (Wood is Floors).
    private string? Resolve(string? name) =>
        _tabs.Select(t => t.Tab).FirstOrDefault(t => t == name || Label(t) == name)
        ?? (name is not null && _kindOfGroup.TryGetValue(name, out string? kind) ? kind : null);

    private static string TabTip(string tab) => tab switch
    {
        All => "Click to show every starting point, of every kind.",
        Recent => "Click to show the starting points you used last, newest first (the last twelve).",
        Favourites => "Click to show your favourite starting points. Right click a picture to add it or take it out.",
        Mine => "Click to show your own work files, newest first; click one to open it. Saving a piece adds it here.",
        MyPresets => "Click to show your own starting points (Prop, Save as my preset…). Right click one to rename or remove it.",
        _ when Kinds.Contains(tab) => $"Click to show all the {tab.ToLowerInvariant()}; the row under the tabs then picks one of their groups.",
        _ => $"Click to show the starting points in the {tab} group.",
    };

    private static byte[]? FloorPicture()
    {
        using Stream? s = typeof(ObjectsDrawer).Assembly.GetManifestResourceStream("floor_v5.png");
        if (s is null)
        {
            return null;
        }

        using var copy = new MemoryStream();
        s.CopyTo(copy);
        return copy.ToArray();
    }

    private static bool Has(string text, string part) => text.Contains(part, StringComparison.OrdinalIgnoreCase);

    private DrawerTile PointTile(StartingPoint p)
    {
        var t = new DrawerTile(
            p.Name,
            $"{p.Name} ({p.Group})\nClick: start a new piece from it · drag onto the view: the same · right click: favourite",
            NoColour, TileBackground, p, null);
        var favourite = new MenuItem();
        favourite.Click += (_, _) => Favour(p.Id);
        t.Border.ContextMenu = new ContextMenu { ItemsSource = new[] { favourite } };
        Pointer(t);
        return t;
    }

    private DrawerTile FileTile(LibraryEntry e)
    {
        Color swatch = e.ColourHex is not null && Color.TryParse(e.ColourHex, out Color c) ? c : NoColour;
        string when = e.LastOpened.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var t = new DrawerTile(
            e.Name,
            $"{Path.GetFileName(e.Path)}\n{Path.GetDirectoryName(e.Path)}\nLast opened {when}\nClick: open it",
            swatch, TileBackground, null, e);
        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => _ = UseAsync(t);
        t.Border.ContextMenu = new ContextMenu { ItemsSource = new[] { open } };
        Pointer(t);
        return t;
    }

    // A press on a picture: a click if the hand lets go within a few pixels, else a drag, which starts the piece only
    // when let go over the view (not on the drawer).
    private void Pointer(DrawerTile t)
    {
        Border b = t.Border;
        b.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(b).Properties.IsLeftButtonPressed)
            {
                _press = (t, e.GetPosition(_surface));
                _dragging = false;
                e.Pointer.Capture(b);
                e.Handled = true;
            }
        };
        b.PointerMoved += (_, e) =>
        {
            if (_press is var (tile, from) && ReferenceEquals(tile, t) && !_dragging
                && Point.Distance(e.GetPosition(_surface), from) >= DragThreshold)
            {
                _dragging = true;
            }
        };
        b.PointerReleased += (_, e) =>
        {
            if (_press is not var (tile, _) || !ReferenceEquals(tile, t))
            {
                return;
            }

            bool dragged = _dragging;
            _press = null;
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            if (!dragged || OverView(e.GetPosition(_surface)))
            {
                _ = UseAsync(t);
            }
        };
        b.PointerCaptureLost += (_, _) =>
        {
            if (_press is var (tile, _) && ReferenceEquals(tile, t))
            {
                _press = null;
                _dragging = false;
            }
        };
    }

    // Whether a point of the view's surface is on the view and not on the drawer.
    private bool OverView(Point at)
    {
        Rect view = new(_surface.Bounds.Size);
        Point? corner = Card.TranslatePoint(default, _surface);
        return view.Contains(at) && corner is { } c && !new Rect(c, Card.Bounds.Size).Contains(at);
    }

    private async Task UseAsync(DrawerTile t)
    {
        if (_files.Asking || _disposed)
        {
            return;
        }

        try
        {
            if (t.Point is { } p)
            {
                // A drawer let go while the piece was being started (its window closed) no longer keeps anything.
                if (await _files.StartFromAsync(p) && !_disposed)
                {
                    Used(p.Id);
                }
            }
            else if (t.Entry is { } e)
            {
                await _files.OpenPathAsync(e.Path);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _say($"Could not start {t.Name}: {e.Message}");
        }

        if (!_disposed)
        {
            Marked();
        }
    }

    // A starting point used: first in Recent, twelve kept.
    private void Used(string id)
    {
        _prefs.Recent = [id, .. _prefs.Recent.Where(r => r != id).Take(Prefs.MaxRecent - 1)];
        _prefs.Save();
        UpdateTabs();
        Filter();
    }

    private void Favour(string id)
    {
        _prefs.Favourites = _prefs.Favourites.Contains(id) ? [.. _prefs.Favourites.Where(f => f != id)] : [.. _prefs.Favourites, id];
        _prefs.Save();
        Marked();
        UpdateTabs();
        Filter();
    }

    // The view state is the one place the drawer's open state is kept: the toggle follows it.
    private void OnViewChanged()
    {
        if (IsOpen != _view.DrawerOpen)
        {
            OpenToggle.IsChecked = _view.DrawerOpen;
        }
    }

    private void Opened()
    {
        bool open = IsOpen;
        string chevron = OnLeft ? (open ? "chevron-left" : "chevron-right") : (open ? "chevron-down" : "chevron-up");
        if (_toggleIcon?.IconName != chevron)
        {
            _toggleIcon = new Look.Icon(chevron, 14) { VerticalAlignment = VerticalAlignment.Center };
            _toggleRow.Children.Clear();
            _toggleRow.Children.Add(_toggleIcon);
            _toggleRow.Children.Add(_toggleLabel);
        }

        _toggleLabel.Text = $"Objects {_points.Count}";
        OpenToggle.Content ??= _toggleRow;
        _scroll.IsVisible = SearchBox.IsVisible = Tabs.IsVisible = DockButton.IsVisible = Edge.Handle.IsVisible = open;
    }

    // The two layouts. At the bottom: a card over the view (the design's card over a 3D view), its head one row (the
    // toggle, the search, the tabs, the Dock button). On the left: a docked panel with a hairline on its right, the
    // head's row holding the toggle and the Dock button, the search and the tabs under it. The tabs never wrap or
    // scroll: the ones that do not fit go under More (n).
    private void Arrange(bool left)
    {
        OnLeft = left;
        _head.Children.Clear();
        _top.Children.Clear();
        _head.Children.Add(OpenToggle);
        _head.Children.Add(DockButton);
        _top.Children.Add(_head);
        if (left)
        {
            SearchBox.Width = double.NaN;
            SearchBox.Margin = default;
            _top.Children.Add(SearchBox);
            _top.Children.Add(Tabs);
            DockButton.Content = "Dock at the bottom";
            Look.Chrome.Tip(DockButton, "Click to move the objects drawer back along the bottom of the view.");
            Card.Background = Look.Theme.SurfacePanel;
            Card.BorderBrush = Look.Theme.BorderSubtle;
            Card.BorderThickness = new Thickness(0, 0, 1, 0);
            Card.CornerRadius = default;
            Card.BoxShadow = default;
            Card.Margin = default;
            Card.VerticalAlignment = VerticalAlignment.Stretch;
            Card.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            SearchBox.Width = 170;
            SearchBox.Margin = new Thickness(10, 0, 8, 0);
            _head.Children.Add(SearchBox);
            _head.Children.Add(Tabs);
            DockButton.Content = "Dock on the left";
            Look.Chrome.Tip(DockButton, "Click to dock the objects drawer down the left of the window, its pictures in a column you can make wider.");
            Card.Background = Look.Theme.OverlayBg;
            Card.BorderBrush = Look.Theme.OverlayBorder;
            Card.BorderThickness = new Thickness(1);
            Card.CornerRadius = new CornerRadius(Look.Theme.RadiusCard);
            Card.BoxShadow = Look.Theme.Shadow1;
            Card.Margin = new Thickness(Look.Theme.Space2, 0, Look.Theme.Space2, Look.Theme.Space2);
            Card.VerticalAlignment = VerticalAlignment.Bottom;
            Card.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        _top.Children.Add(_groupRow);
        Edge.Place(left, Card.Padding.Left);
        Opened();
    }

    private void OnFilesChanged()
    {
        // Changed comes at every step of a slider too: the library is only read again when a file may have been
        // saved or opened, which leaves the piece without unsaved changes.
        if (!_files.Dirty)
        {
            RefreshMine();
        }
    }

    private void OnRecipeChanged(string recipe) => Marked();

    // The stars, the menus and the outline of the piece being worked on.
    private void Marked()
    {
        foreach (DrawerTile t in _points)
        {
            string id = t.Point!.Id;
            t.Favourite = _prefs.Favourites.Contains(id);
            t.Outlined = id == _files.StartedFrom;
            ((MenuItem)t.Border.ContextMenu!.Items[0]!).Header = t.Favourite ? "Take out of Favourites" : "Add to Favourites";
        }

        foreach (DrawerTile t in _mineOrder)
        {
            t.Outlined = t.Entry!.Path == _files.FilePath;
        }

        foreach (DrawerTile t in _presetTiles)
        {
            t.Outlined = t.Point!.Id == _files.StartedFrom;
        }
    }

    private int Count(string tab) => tab switch
    {
        All => _points.Count,
        Recent => RecentTiles().Count(),
        Favourites => _points.Count(t => _prefs.Favourites.Contains(t.Point!.Id)),
        Mine => _mineOrder.Count,
        MyPresets => _presetTiles.Count,
        _ when _kinds.Contains(tab) => _points.Count(t => KindOf(t.Point!) == tab),
        _ => _points.Count(t => t.Point!.Group == tab),
    };

    private string Label(string tab) => tab == All ? All : $"{tab} {Count(tab)}";

    private IEnumerable<DrawerTile> RecentTiles() =>
        _prefs.Recent.Select(id => _points.FirstOrDefault(t => t.Point!.Id == id)).OfType<DrawerTile>();

    private void UpdateTabs()
    {
        string? kind = Kind;
        foreach ((string tab, ToggleButton b) in _tabs)
        {
            if (b.Classes.Contains("ep-tab"))
            {
                Look.TabRow.SetText(b, tab, tab == All ? null : Count(tab));
            }
            else
            {
                b.Content = Label(tab);
            }

            b.IsChecked = tab == _tab || tab == kind;
            if (_kindOfGroup.TryGetValue(tab, out string? of) && !_kinds.Contains(tab))
            {
                b.IsVisible = of == kind;
            }
        }

        _allOfKind.IsChecked = kind == _tab;
        Tabs.InvalidateMeasure();
        _groupRow.IsVisible = _groupRow.Children.OfType<ToggleButton>().Any(b => b != _allOfKind && b.IsVisible);
    }

    private void Filter()
    {
        string q = SearchBox.Text?.Trim() ?? string.Empty;
        IEnumerable<DrawerTile> shown;
        if (q.Length > 0)
        {
            IEnumerable<DrawerTile> points = _points.Concat(_presetTiles).Where(t => Has(t.Name, q) || Has(t.Point!.Group, q));
            shown = _tab == Mine ? _mineOrder.Where(t => Has(t.Name, q)).Concat(points) : points;
        }
        else
        {
            shown = _tab switch
            {
                All => _points,
                Recent => RecentTiles(),
                Favourites => _points.Where(t => _prefs.Favourites.Contains(t.Point!.Id)),
                Mine => _mineOrder,
                MyPresets => _presetTiles,
                _ when _kinds.Contains(_tab) => _points.Where(t => KindOf(t.Point!) == _tab),
                _ => _points.Where(t => t.Point!.Group == _tab),
            };
        }

        _shown = [.. shown];
        if (!_wrap.Children.SequenceEqual(_shown.Select(t => t.Border)))
        {
            _wrap.Children.Clear();
            _wrap.Children.AddRange(_shown.Select(t => t.Border));
        }
    }

    // A starting point's picture: the floor's ships with the app, and so do easy-props' presets' own (in the engine's
    // bundle, while they still show the preset); the rest (cliffs, stylized rocks, the user's presets, a preset whose
    // shipped picture is stale) is read from the cache, or drawn by our painter and kept.
    private void Draw(DrawerTile t, StartingPoint p)
    {
        if (p.Recipe == FloorRecipe.Id)
        {
            Queue(t, _ => FloorPicture());
        }
        else if (MeshRecipes.Find(p.Recipe) is { } recipe)
        {
            ThumbCache thumbs = _thumbs;
            bool own = p.Group == UserPresets.Group;
            Queue(t, stop =>
            {
                if (!own && ShippedThumbs.For(p.Id, recipe.Id) is { } shipped)
                {
                    return shipped;
                }

                string key = ThumbCache.PresetKey(p.Id, recipe.Id, recipe.Version, ShippedThumbs.EngineTag(recipe.Id));
                if (thumbs.Get(key) is { } kept)
                {
                    return kept;
                }

                byte[] png = MeshThumb.Png(recipe.Build(p.Settings, recipe.PictureDetail, BuildPurpose.Picture, stop), 128);
                thumbs.Put(key, png);
                return png;
            });
        }
    }

    // A file's picture: the view as it was at its last save, if one was kept.
    private void Draw(DrawerTile t, LibraryEntry e)
    {
        ThumbCache thumbs = _thumbs;
        string key = ThumbCache.FileKey(e.Path);
        Queue(t, _ => thumbs.Get(key));
    }

    // Pictures are read or drawn off the UI thread one after another, each shown as soon as it is done (if its tile is
    // still in the drawer); nothing is kept here but the tiles' own pictures. The token, taken here on the UI thread
    // (the source is let go once the drawer is disposed), stops a picture being drawn when the drawer stops.
    private void Queue(DrawerTile t, Func<CancellationToken, byte[]?> make)
    {
        if (_disposed)
        {
            return;
        }

        CancellationToken stop = _stop.Token;
        _pictures = _pictures.ContinueWith(
            _ =>
            {
                if (stop.IsCancellationRequested)
                {
                    return;
                }

                byte[]? png = null;
                try
                {
                    png = make(stop);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    // The drawer stopped: nothing to say.
                    return;
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    TraceLog.Write($"no picture drawn for {t.Name}: {e.Message}");
                }

                if (png is not null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!stop.IsCancellationRequested && (t.Point is not null || (t.Entry is { } e && _mine.TryGetValue(e.Path, out DrawerTile? now) && ReferenceEquals(now, t))))
                        {
                            t.Show(png);
                        }
                    });
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }
}
