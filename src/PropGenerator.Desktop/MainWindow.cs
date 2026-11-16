using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PropGenerator.Desktop.Backups;
using PropGenerator.Desktop.Cards;
using PropGenerator.Desktop.Searching;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Details;
using PropGenerator.Desktop.Menus;
using PropGenerator.Desktop.Pictures;
using PropGenerator.Desktop.Props;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Status;

namespace PropGenerator.Desktop;

/// <summary>
/// The app's window: easy-props' top bar (the brand, the File, Edit, View, Prop and Help menus, the piece's name,
/// "Search or run…", Save and Export), the 3D view of the entry in the middle (the floor, or a rock) with the objects
/// drawer over its bottom (or docked down the left), the settings down the right, and a status line along the bottom. The menus, the keyboard
/// shortcuts and Claude's tools act through one command list. The app starts with
/// a new floor, painted at full detail with the default settings as soon as the window is made; the floor's painting
/// or the rock's building follows every change of the settings live.
/// </summary>
public sealed class MainWindow : Window
{
    /// <summary>The narrowest the window gets: the design's minimum for the dense tools, 1024.</summary>
    internal const double MinimumWidth = 1024;

    /// <summary>The lowest the window gets: the design's minimum for the dense tools, 640.</summary>
    internal const double MinimumHeight = 640;

    private const int MaxSaid = 20;

    private readonly IPainter _painter;
    private readonly Queue<string> _said = new();
    private readonly Prefs _prefs;

    // The view's column (the 3D view and everything over it) and the stage holding it, with the drawer before it when
    // the drawer is on the left; the shell is the whole window under the menus.
    private readonly Grid _stage = new();
    private readonly Grid _centre;
    private DockPanel _shell = null!;
    private SettingsWindow? _settingsWindow;
    private bool _closeApproved;

    /// <summary>Builds the window and starts the first paint.</summary>
    /// <param name="painter">Paints the tone layers (the worker in the app, a fake in the tests).</param>
    /// <param name="specs">The painter's settings list.</param>
    /// <param name="dialogs">Asks the user (the real file pickers and cards when null).</param>
    /// <param name="prefs">The remembered folders (~/.config/prop-generator/prefs.json when null).</param>
    /// <param name="library">The files opened before (~/.config/prop-generator/library.json when null).</param>
    /// <param name="bridgeRoot">The folder with the Blender inbox and outbox (~/.cache/prop-generator, or PROPGEN_BRIDGE_ROOT, when null).</param>
    public MainWindow(
        IPainter painter, IReadOnlyList<ParamSpec> specs, IDialogs? dialogs = null, Prefs? prefs = null, Library? library = null,
        string? bridgeRoot = null)
        : this(painter, specs, dialogs, prefs, library, bridgeRoot, null)
    {
    }

    /// <summary>Builds the window and starts the first paint, keeping the pictures of saved files where told.</summary>
    /// <param name="painter">Paints the tone layers (the worker in the app, a fake in the tests).</param>
    /// <param name="specs">The painter's settings list.</param>
    /// <param name="dialogs">Asks the user (the real file pickers and cards when null).</param>
    /// <param name="prefs">The remembered folders (~/.config/prop-generator/prefs.json when null).</param>
    /// <param name="library">The files opened before (~/.config/prop-generator/library.json when null).</param>
    /// <param name="bridgeRoot">The folder with the Blender inbox and outbox (~/.cache/prop-generator, or PROPGEN_BRIDGE_ROOT, when null).</param>
    /// <param name="thumbs">The picture cache (~/.cache/prop-generator/thumbs when null).</param>
    internal MainWindow(
        IPainter painter, IReadOnlyList<ParamSpec> specs, IDialogs? dialogs, Prefs? prefs, Library? library,
        string? bridgeRoot, ThumbCache? thumbs)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(specs);
        _painter = painter;
        Title = "Prop Generator";
        if (AppIcon.Bitmap() is { } icon)
        {
            Icon = new WindowIcon(icon);
        }

        CopyText = CopyAsync;
        Width = 1500;
        Height = 920;
        MinWidth = MinimumWidth;
        MinHeight = MinimumHeight;
        Background = Look.Theme.SurfacePage;

        FloorSpecs = specs;
        Panel = new SettingsPanel(specs);
        View = new TileView();
        Repaint = new LiveRepaint(Panel, painter, View);
        Rocks = new RockLive(Panel, View);
        StatusLine = new TextBlock { Text = "Painting the floor…" };
        // The OpenGL picture cannot be hit-tested: a see-through surface over it takes the mouse for the camera.
        var surface = new Border { Background = Brushes.Transparent };
        View.Attach(surface);
        var centre = new Grid();
        centre.Children.Add(View);
        centre.Children.Add(surface);

        var overlay = new Grid();
        Library = library ?? new Library(Library.DefaultPath());
        _prefs = prefs ?? new Prefs(Prefs.DefaultPath());
        ViewState = new ViewState(_prefs);
        Files = new FileActions(Panel, Repaint, Rocks, dialogs ?? new WindowDialogs(this, overlay), Library, _prefs, Say)
        {
            Thumbs = thumbs ?? new ThumbCache(ThumbCache.DefaultDir()),
            Capture = View.CaptureAsync,
        };
        ConfigFolder = Path.GetDirectoryName(Path.GetFullPath(Library.FilePath))!;
        Colours = new Colours.ColourBook(Path.Combine(ConfigFolder, "colours.json"));
        Presets = new UserPresets(Path.Combine(ConfigFolder, "presets.json"));
        Props = new YourProps(Library, new RemovedBin(Path.Combine(ConfigFolder, "removed"))) { Thumbs = Files.Thumbs };
        try
        {
            Props.Bin.ForgetOld();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TraceLog.Write($"Recently removed was not tidied: {e.Message}");
        }

        Drawer = new ObjectsDrawer(Files, _prefs, ViewState, Library, Files.Thumbs, StartingPoints, surface, m => Say(m, error: true), Presets, RenamePresetAsync, RemovePreset);
        Backups = new BackupActions(Library, _prefs, Files.Dialogs, ConfigFolder, Say)
        {
            MergeExtras = extras =>
            {
                if (extras.TryGetValue("colours.json", out byte[]? colours))
                {
                    Colours.MergeFrom(colours);
                }

                if (extras.TryGetValue("presets.json", out byte[]? presets))
                {
                    Presets.MergeFrom(presets);
                }
            },
        };
        Files.Changed += () =>
        {
            // After a save (or an open) the piece has no unsaved changes: the daily backup is made if it is due.
            if (!Files.Dirty && Files.FilePath is not null)
            {
                _ = Backups.RunDailyAsync();
            }
        };
        centre.Children.Add(Drawer.Card);
        Cards = new CardOverlay(overlay);
        Palette = new CommandPalette(this, _prefs);
        Editing = new Parts.PartEditing(this);
        Versions = new Batch.PropBatch(this);
        Pieces = new Batch.TreePieces(this);
        Rocks.Decorate = Editing.Decorate;
        Rocks.FarCard = full =>
        {
            Nature.Export.FarFrame frame = PropGenerator.Desktop.View.FarCardMesh.Frame(full);
            return View.PaintFar(full, frame) is { } pictures ? PropGenerator.Desktop.View.FarCardMesh.Card(frame, pictures) : null;
        };
        View.PartDrag = Editing;
        View.Clicked += Editing.Pick;
        Commands = CommandList.For(this);
        Shortcuts = new Shortcuts(Commands);
        var menus = new MenuLayer(Look.Theme.MenuBarHeight);
        TopBar = new TopBar(Commands, Files, menus, () => ShowDrawer("All"));
        Panel.UseMemory(_prefs, () => Files.Recipe);
        Panel.UseColours(Colours);
        Panel.SetStartingPoint(StartingSettings());
        PanelHead = new PanelHeader(Panel.Filter, Commands);
        PanelHead.ShowKind(Panel.KindLabel);
        PanelFoot = new PanelFooter(Commands);
        Edge = new PanelEdge(_prefs);
        Edge.Changed += ApplyView;
        Edge.UnfoldAsked += MakeRoomForThePanel;
        Drawer.Edge.Changed += ApplyView;
        StatusBar = new StatusBar(this, StatusLine);
        StatusBar.Show("Painting the floor…", error: false);
        Toolbar = new View.ViewToolbar(this);
        Editing.Changed += Toolbar.RefreshTools;
        Editing.Changed += PanelHead.RefreshBreak;
        Files.Changed += PanelHead.RefreshBreak;

        // Over the view and under the drawer: the tools, the toolbar, the cards and the compass.
        centre.Children.Insert(centre.Children.IndexOf(Drawer.Card), Toolbar.Overlay);
        _centre = centre;
        var root = new Grid();
        root.Children.Add(Layout(centre));
        root.Children.Add(menus);
        root.Children.Add(overlay);
        Content = root;
        Claude = new ClaudeConnection(this, _prefs, m => Say(m, error: true));
        BridgeRoot = bridgeRoot ?? Bridge.DefaultRoot();
        Inbox = new InboxWatcher(Bridge.Inbox(BridgeRoot));
        Inbox.Loaded += OnInboxLoaded;
        Inbox.Failed += m => Say(m, error: true);
        Files.FloorShown += View.ShowFloor;
        Panel.Committed += Files.OnCommitted;
        Panel.ToneChanged += Files.Refresh;
        Panel.PaintChanged += _ => Files.Refresh();
        Files.Changed += () => Title = Files.TitleText;
        Files.Changed += () => Toolbar.RefreshStyle();
        Panel.Committed += _ => Toolbar.RefreshStyle();
        Files.Entered += OnEntered;
        Files.RecipeChanged += OnRecipeChanged;
        Title = Files.TitleText;
        ViewState.Changed += ApplyView;
        View.Follow(ViewState);
        ViewState.Changed += () =>
        {
            ApplyDetail();
            Rocks.WantShapes = ViewState.ShowCollision;
        };

        // The far-away card can depend on the settings: the Far level is left when the settings no longer offer it.
        Panel.CurrentChanged += () =>
        {
            if (ViewState.Lod == ViewState.FarLevel && MeshRecipes.Find(Files.Recipe)?.FarAway(Panel.Current) != true)
            {
                ApplyDetail();
            }
        };

        // Any button down in the window (a slider, the view) keeps the camera still while a rock changes height, and
        // spares a rock below full detail its second, counting build.
        AddHandler(
            PointerPressedEvent,
            (_, _) =>
            {
                View.SetPointerHeld(true);
                Rocks.PointerHeld = true;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            PointerReleasedEvent,
            (_, e) =>
            {
                PointerPointProperties still = e.GetCurrentPoint(this).Properties;
                bool held = still.IsLeftButtonPressed || still.IsRightButtonPressed || still.IsMiddleButtonPressed;
                View.SetPointerHeld(held);
                Rocks.PointerHeld = held;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        ApplyView();
        AddHandler(KeyDownEvent, OnKeys, RoutingStrategies.Tunnel);
        Closing += OnClosing;

        painter.Failed += OnPainterFailed;
        Repaint.Failed += m => Say(m, error: true);
        Repaint.Shown += OnShown;
        Rocks.Failed += m => Say(m, error: true);
        Rocks.Built += OnRockBuilt;
        View.Failed += m => Say(m, error: true);
        View.LayersDrawn += OnDrawn;
        View.Recomposed += ms => TraceLog.Write(string.Create(CultureInfo.InvariantCulture, $"recomposed: {ms:0.0} ms"));
        Closed += (_, _) =>
        {
            // The clocks stop with the window, and a sun moved just before closing is still saved.
            Toolbar.StatsCard.Timer.Stop();
            StatusBar.Stop();
            Versions.Stop();
            Pieces.Dispose();
            ViewState.Flush();
            _painter.Failed -= OnPainterFailed;
            Repaint.Dispose();
            Rocks.Dispose();
            Drawer.Dispose();
            Claude.Dispose();
            Inbox.Dispose();
            Backups.Dispose();
        };
        Repaint.Shown += (_, _) => PictureReady();
        Repaint.Failed += _ => PictureReady();
        Rocks.Built += _ => PictureReady();
        Rocks.Failed += _ => PictureReady();
        _painter.Failed += _ => Dispatcher.UIThread.Post(PictureReady);
        Repaint.Start();
        _ = Claude.StartIfEnabledAsync();
        Dispatcher.UIThread.Post(() => _ = Backups.RunDailyAsync(), DispatcherPriority.Background);
        if (Inbox.StartError is string notWatched)
        {
            Say(notWatched, error: true);
        }
    }

    /// <summary>
    /// Gets the folder the library file is in (normally ~/.config/prop-generator), which also holds Recently removed,
    /// the colours and palettes, and My presets.
    /// </summary>
    internal string ConfigFolder { get; }

    /// <summary>Gets Your props: the saved props with their details, and Recently removed.</summary>
    internal YourProps Props { get; }

    /// <summary>Gets the colours kept: Recent, My colours and the saved palettes.</summary>
    internal Colours.ColourBook Colours { get; }

    /// <summary>Gets the backups: to a file, the daily folder and the cloud.</summary>
    internal BackupActions Backups { get; }

    /// <summary>Shows the cloud backup's setup (File, Cloud backup…).</summary>
    internal void ShowCloudSetup() => Cards.Show(new CloudSetupCard(Backups, _prefs, Cards.Close));

    /// <summary>Gets what the last remove took (Undo puts it back), or null when there is nothing to undo.</summary>
    internal IReadOnlyList<RemovedProp>? LastRemoved { get; private set; }

    /// <summary>
    /// Opens Your props (File, My props), or Recently removed, once every prop's details are read (off the window's
    /// thread: a large library or a slow drive must not freeze the window).
    /// </summary>
    /// <param name="bin">True to open it on Recently removed.</param>
    /// <returns>Done when it is shown.</returns>
    /// <param name="pick">A prop to pick on the page (its work file), or null.</param>
    internal async Task ShowPropsAsync(bool bin = false, string? pick = null)
    {
        await Props.LoadAsync();
        var page = new PropsPage(this);
        if (bin)
        {
            page.ShowBin(true);
        }
        else if (pick is not null)
        {
            page.PickPath(pick);
        }

        Cards.Show(page);
    }

    /// <summary>
    /// Moves props into Recently removed (Your props, Remove), to wait 30 days; Undo puts them back. The prop being worked
    /// on is not removed: the line on the status bar says to start or open another one first.
    /// </summary>
    /// <param name="paths">The work files.</param>
    internal void RemoveProps(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return;
        }

        if (Files.FilePath is { } open && paths.Contains(open))
        {
            Say($"{Path.GetFileNameWithoutExtension(open)} is the prop you are working on: start or open another one first, then remove it.", error: true);
            return;
        }

        IReadOnlyList<RemovedProp> gone = Props.Remove(paths);
        LastRemoved = gone;
        Say(gone.Count == 1 ? $"Removed \"{gone[0].Name}\"" : $"Removed {gone.Count} props");
        Drawer.RefreshMine();
    }

    /// <summary>Puts back what the last remove took (Undo remove).</summary>
    internal void UndoRemove()
    {
        if (LastRemoved is not { Count: > 0 } last)
        {
            return;
        }

        LastRemoved = null;
        try
        {
            Props.PutBack(last);
            Say(last.Count == 1 ? $"Put back \"{last[0].Name}\"" : $"Put back {last.Count} props");
        }
        catch (IOException e)
        {
            Say(e.Message, error: true);
        }

        Drawer.RefreshMine();
    }

    /// <summary>Gets the folder that holds the Blender inbox and outbox.</summary>
    internal string BridgeRoot { get; }

    /// <summary>Gets what watches the inbox for meshes sent from Blender.</summary>
    internal InboxWatcher Inbox { get; }

    /// <summary>Gets the settings panel.</summary>
    internal SettingsPanel Panel { get; }

    /// <summary>Gets the 3D view.</summary>
    internal TileView View { get; }

    /// <summary>Gets the picking and changing of the prop's parts (Move, Turn, Resize, Copy, Remove, Restore).</summary>
    internal Parts.PartEditing Editing { get; }

    /// <summary>Gets Show me more, Make it fit and Export many for the prop.</summary>
    internal Batch.PropBatch Versions { get; }

    /// <summary>Gets Make pieces from this tree: a stump, a log, a branch and leaf litter, each saved as its own prop.</summary>
    internal Batch.TreePieces Pieces { get; }

    /// <summary>Gets what decides when the floor is repainted.</summary>
    internal LiveRepaint Repaint { get; }

    /// <summary>Gets what builds and shows the rock while a rock is the entry.</summary>
    internal RockLive Rocks { get; }

    /// <summary>Gets the painter's settings list, the floor's.</summary>
    internal IReadOnlyList<ParamSpec> FloorSpecs { get; }

    /// <summary>
    /// Gets every starting point the app offers: the floor first, then the rocks in easy-props' order, then the cliff
    /// blocks.
    /// </summary>
    internal IReadOnlyList<StartingPoint> StartingPoints =>
        [FloorRecipe.StartingPoint(FloorSpecs), .. MeshRecipes.All.Where(EngineKinds.InDrawer).SelectMany(r => r.StartingPoints)];

    /// <summary>Gets My presets: the user's own starting points.</summary>
    internal UserPresets Presets { get; }

    /// <summary>Gets every starting point the app offers, then My presets.</summary>
    internal IReadOnlyList<StartingPoint> AllStartingPoints => [.. StartingPoints, .. Presets.All];

    /// <summary>Asks for a name, then saves the piece's settings (its seed too) as one of My presets.</summary>
    /// <returns>Done when saved or cancelled.</returns>
    internal async Task SavePresetAsync()
    {
        string suggested = Files.FilePath is null ? string.Empty : Path.GetFileNameWithoutExtension(Files.FilePath);
        string? typed = await Files.Dialogs.AskTextAsync("Save as my preset", "Name", suggested);
        string name = (typed ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return;
        }

        if (name.Length > YourProps.MaxName)
        {
            name = name[..YourProps.MaxName];
        }

        Presets.Add(name, Files.Recipe, Panel.Current);
        Say($"Saved \"{name}\" to My presets");
    }

    /// <summary>Asks for a new name for one of My presets, then renames it.</summary>
    /// <param name="p">The preset.</param>
    /// <returns>Done when renamed or cancelled.</returns>
    internal async Task RenamePresetAsync(StartingPoint p)
    {
        ArgumentNullException.ThrowIfNull(p);
        string? typed = await Files.Dialogs.AskTextAsync($"Rename {p.Name}", "Name", p.Name);
        string name = (typed ?? string.Empty).Trim();
        if (name.Length > 0 && name != p.Name)
        {
            Presets.Rename(p.Id, name.Length > YourProps.MaxName ? name[..YourProps.MaxName] : name);
            Say($"Renamed to \"{name}\"");
        }
    }

    /// <summary>Removes one of My presets; pieces started from it keep their settings.</summary>
    /// <param name="p">The preset.</param>
    internal void RemovePreset(StartingPoint p)
    {
        ArgumentNullException.ThrowIfNull(p);
        Presets.Remove(p.Id);
        Say($"Took \"{p.Name}\" off My presets");
    }

    /// <summary>Gets the work files opened and saved before.</summary>
    internal Library Library { get; }

    /// <summary>Gets where the pictures of saved files (and of the starting points) are kept.</summary>
    internal ThumbCache Thumbs => Files.Thumbs;

    /// <summary>Gets the objects drawer, over the bottom of the 3D view or docked on its left (Ctrl+G opens and closes it).</summary>
    internal ObjectsDrawer Drawer { get; }

    /// <summary>Gets the status line along the bottom.</summary>
    internal TextBlock StatusLine { get; }

    /// <summary>Gets the piece as a file: its name, history, and what the file buttons do.</summary>
    internal FileActions Files { get; }

    /// <summary>Gets Claude's connection: the switch, the address and token, and the server.</summary>
    internal ClaudeConnection Claude { get; }

    /// <summary>Gets the bar along the top: the brand, the menus, the piece's name, the palette button, Save and Export.</summary>
    internal TopBar TopBar { get; }

    /// <summary>Gets every command: what the menus, the shortcuts and Claude's tools run.</summary>
    internal IReadOnlyList<Command> Commands { get; }

    /// <summary>Gets the keyboard shortcuts, read from the commands' keys.</summary>
    internal Shortcuts Shortcuts { get; }

    /// <summary>Gets how the window shows the piece: shading, detail level, scene, light, overlays, layout and budget.</summary>
    internal ViewState ViewState { get; }

    /// <summary>
    /// Gets why Flat and Wireframe (and Z, which steps through them) cannot be chosen now: they draw rocks (and cliff
    /// blocks) only, so the floor and a mesh from Blender are always lit. Null while the piece is a rock or a cliff.
    /// </summary>
    internal string? ShadingUnavailable => View.ShowingPiece
        ? "Shading is for rocks"
        : MeshRecipes.Find(Files.Recipe) is not null ? null : "The floor is painted by hand; shading is for rocks";

    /// <summary>Gets the shading the view draws: the one chosen while the piece is a rock, Lit for anything else.</summary>
    internal string ShadingShown => ShadingUnavailable is null ? ViewState.Shading : ViewState.Lit;

    /// <summary>Gets where the small cards (keyboard shortcuts, About) are shown.</summary>
    internal CardOverlay Cards { get; }

    /// <summary>Gets the command palette (Ctrl+K), shown on <see cref="Cards"/>.</summary>
    internal CommandPalette Palette { get; }

    /// <summary>
    /// Gets the settings panel's column down the right: the header, the groups and the footer (hidden at its edge, by
    /// Hide panels and by Maximize).
    /// </summary>
    internal Border SidePanel { get; private set; } = null!;

    /// <summary>Gets the head of the controls panel: the kind of prop, Surprise me, Show me more and the filter.</summary>
    internal PanelHeader PanelHead { get; }

    /// <summary>Gets the foot of the controls panel, with Restore to preset.</summary>
    internal PanelFooter PanelFoot { get; }

    /// <summary>Gets the edge between the view and the controls panel, which resizes and hides it.</summary>
    internal PanelEdge Edge { get; }

    /// <summary>Gets the controls over the 3D view: the tools, the view toolbar, the stats and collision cards and the compass.</summary>
    internal View.ViewToolbar Toolbar { get; }

    /// <summary>Gets the status bar along the bottom (hidden by Maximize).</summary>
    internal StatusBar StatusBar { get; private set; } = null!;

    /// <summary>Writes this piece to the Blender outbox (File, Send to Blender).</summary>
    /// <returns>Done when it is written or the problem is said.</returns>
    internal async Task SendToBlenderAsync()
    {
        try
        {
            await Files.SendToBlenderAsync(Bridge.Outbox(BridgeRoot));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Say($"Could not send to Blender: {e.Message}", error: true);
        }
    }

    /// <summary>Shows the piece again after a mesh from Blender was shown (View, Back to the piece).</summary>
    /// <returns>Done when it is shown or the problem is said.</returns>
    internal async Task BackToThePieceAsync()
    {
        try
        {
            await Files.BackToThePieceAsync();
            StatusBar.Refresh();
            Toolbar.PieceChanged();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Say($"Could not show the piece: {e.Message}", error: true);
        }
    }

    private void OnInboxLoaded(InboxLoad e)
    {
        try
        {
            View.ShowPiece(e.Result.Mesh);
            StatusBar.Refresh();
            Toolbar.PieceChanged();
        }
        catch (ArgumentException ex)
        {
            Say($"{e.Name}.glb from Blender cannot be shown: {ex.Message}", error: true);
            return;
        }

        int triangles = e.Result.Mesh.Indices.Length / 3;
        string note = e.Result.Warning is null ? string.Empty : " " + e.Result.Warning;
        Say($"Showing {e.Name} from Blender ({triangles} triangles, {e.Result.Piece.Faces.Count} faces, unpainted). Painting shapes from Blender comes in v2.{note}");
    }

    /// <summary>Opens the settings window, or brings it to the front (File, Settings).</summary>
    internal void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(Claude, _prefs, ViewState);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
    }

    /// <summary>Shows the list of keyboard shortcuts (Help, Keyboard shortcuts).</summary>
    internal void ShowShortcuts() => Cards.Show(new ShortcutsCard(Commands, Cards.Close));

    /// <summary>Shows the version and the credits (Help, About Prop Generator).</summary>
    internal void ShowAbout() => Cards.Show(new AboutCard(Cards.Close));

    /// <summary>Opens the drawer on a tab, leaving the maximized view (My props, the brand button).</summary>
    /// <param name="tab">The tab, such as "Mine" or "All".</param>
    internal void ShowDrawer(string tab)
    {
        ViewState.Maximized = false;
        Drawer.ShowTab(tab);
    }

    /// <summary>
    /// Brings the layout back as it first was: the panel shown at 320 px, the drawer at the bottom 220 high (300 wide when
    /// docked on the left again), the view not maximized.
    /// </summary>
    internal void ResetLayout()
    {
        ViewState.PanelsHidden = false;
        ViewState.Maximized = false;
        _prefs.PanelWidth = Prefs.DefaultPanelWidth;
        _prefs.PanelOpen = true;
        _prefs.DrawerHeight = Prefs.DefaultDrawerHeight;
        _prefs.DrawerWidth = Prefs.DefaultDrawerWidth;
        ViewState.DrawerLeft = false;
        _prefs.Save();
        Edge.Refresh();
        ApplyView();
    }

    /// <summary>
    /// Shows the user where a setting is (the palette's controls): the controls panel is brought back if it was hidden
    /// (at its edge, by Hide panels or by Maximize), then the row is revealed.
    /// </summary>
    /// <param name="key">The setting.</param>
    /// <returns>False when the panel shows no row for the setting.</returns>
    internal bool RevealSetting(string key)
    {
        if (Panel.SeedSpec is { } seed && seed.Key == key)
        {
            // The seed has no row: its panel in the status bar opens instead.
            ShowSeed();
            return true;
        }

        if (Panel.RowFor(key) is null)
        {
            return false;
        }

        if (!_prefs.PanelOpen)
        {
            Edge.Toggle();
        }

        ViewState.PanelsHidden = false;
        ViewState.Maximized = false;
        ApplyView();
        if (Edge.Folded)
        {
            MakeRoomForThePanel();
        }

        return Panel.Reveal(key);
    }

    /// <summary>Gets or sets what puts text on the clipboard (<see cref="CopyAsync"/>; the tests give theirs).</summary>
    internal Func<string, Task<bool>> CopyText { get; set; }

    /// <summary>Shows Report a problem (Help): the report filled in, to copy or save. Nothing is sent.</summary>
    internal void ShowReport() => Cards.Show(new ReportCard(this));

    /// <summary>Puts text on the clipboard.</summary>
    /// <param name="text">The text.</param>
    /// <returns>False when there is no clipboard to put it on.</returns>
    internal async Task<bool> CopyAsync(string text)
    {
        if (Clipboard is not { } clipboard)
        {
            return false;
        }

        await clipboard.SetTextAsync(text);
        return true;
    }

    /// <summary>
    /// Shows a line on the status bar's message slot, in place of the hint: for 6 s, or for a failure (in red) until the
    /// next action. A failure said while the view is maximized leaves the maximized view, so the status bar shows it.
    /// Also written to the trace log.
    /// </summary>
    /// <param name="message">The line.</param>
    /// <param name="error">True when the line says something failed.</param>
    internal void Say(string message, bool error = false)
    {
        StatusBar.Show(message, error);
        _said.Enqueue(message);
        while (_said.Count > MaxSaid)
        {
            _said.Dequeue();
        }
        TraceLog.Write($"status: {message}");
        if (error)
        {
            ErrorsSaid++;
            LastError = message;
            ViewState.Maximized = false;
        }
    }

    /// <summary>
    /// Gets the last lines said on the status bar, oldest first (at most 20): a line can be replaced at once by the
    /// next (a rock built), so the tests read what was said here.
    /// </summary>
    internal IReadOnlyCollection<string> RecentMessages => _said;

    /// <summary>Gets how many failures were said on the status bar since the window opened (Claude's run_command reads it).</summary>
    internal int ErrorsSaid { get; private set; }

    /// <summary>Gets the last failure said on the status bar, or null when none was.</summary>
    internal string? LastError { get; private set; }

    /// <summary>Notes that the user did something, so an error on the status bar goes and the hint comes back.</summary>
    internal void Acted() => StatusBar.Acted();

    /// <summary>Opens the status bar's seed panel (Use a seed…, the palette's seed), bringing the bar back if the view is maximized.</summary>
    internal void ShowSeed()
    {
        if (ViewState.Maximized)
        {
            ViewState.Maximized = false;
            ApplyView();
        }

        StatusBar.Seed.Open();
    }

    // A menu that is open takes Esc and the arrows; any other key closes it and works as usual. A card takes Esc. A
    // question open in a card gets every key. Otherwise the shortcuts run (only the palette's while typing in a box).
    private void OnKeys(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        if (TopBar.MenuOpen)
        {
            if (TopBar.HandleMenuKey(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Enter or Key.Space && e.KeyModifiers == KeyModifiers.None)
            {
                // The row that has the focus is pressed.
                return;
            }

            TopBar.CloseMenus();
        }

        if (e.Key == Key.Escape && Toolbar.ClosePopups())
        {
            e.Handled = true;
            return;
        }

        if (Palette.IsOpen)
        {
            // The palette takes its keys wherever the focus is; the rest go to its box.
            e.Handled = Palette.HandleKey(e.Key, e.KeyModifiers);
            return;
        }

        if (Cards.Shown is not null)
        {
            if (e.Key == Key.Escape)
            {
                Cards.CloseUnlessBusy();
                e.Handled = true;
            }

            return;
        }

        if (Files.Asking)
        {
            return;
        }

        bool typing = FocusManager?.GetFocusedElement() is TextBox;
        if (Shortcuts.TryRun(e.Key, e.KeyModifiers, typing))
        {
            e.Handled = true;
        }
    }

    // The layout follows the view state: Hide panels takes the panel and its edge away, Maximize the panel, its edge,
    // the drawer and the status bar; they come back as they were. The panel has the width and the open state its
    // edge keeps in the prefs, the drawer its side and sizes.
    private void ApplyView()
    {
        bool hidden = ViewState.PanelsHidden || ViewState.Maximized;
        PanelHead.Refresh();
        Edge.IsVisible = !hidden;
        StatusBar.IsVisible = !ViewState.Maximized;
        Drawer.Card.IsVisible = !ViewState.Maximized;
        DockDrawer(ViewState.DrawerLeft);
        Fit();
    }

    // The drawer at the bottom is a card over the view (in the centre); on the left it is a column of the stage, before
    // the centre.
    private void DockDrawer(bool left)
    {
        Avalonia.Controls.Panel want = left ? _stage : _centre;
        if (Drawer.Card.Parent != want)
        {
            ((Avalonia.Controls.Panel)Drawer.Card.Parent!).Children.Remove(Drawer.Card);
            want.Children.Add(Drawer.Card);
        }

        // On the left the edge reaches 3 px over the view: the drawer lies over it there.
        Drawer.Card.ZIndex = left ? 1 : 0;
    }

    // The widths across the window, so the view is never under 480 (ShellFit), and the drawer's height at the bottom:
    // what the user chose, but at most 60% of the view's height (the prototype's rule).
    private void Fit()
    {
        bool hidden = ViewState.PanelsHidden || ViewState.Maximized;
        bool left = ViewState.DrawerLeft;
        double panel = !hidden && _prefs.PanelOpen ? _prefs.PanelWidth : 0;
        double drawer = 0;
        double drawerMin = 0;
        if (left && Drawer.Card.IsVisible)
        {
            if (ViewState.DrawerOpen)
            {
                drawer = _prefs.DrawerWidth;
                drawerMin = Prefs.MinDrawerWidth;
            }
            else
            {
                // Closed, it is as wide as its head: measured as it is now.
                Drawer.Place(left, 0);
                Drawer.Card.Measure(Avalonia.Size.Infinity);
                drawer = drawerMin = Drawer.Card.DesiredSize.Width;
            }
        }

        double room = _shell.Bounds.Width - (hidden ? 0 : Edge.Width);
        ShellWidths shown = room > 0 ? ShellFit.Widths(room, panel, drawer, drawerMin) : new ShellWidths(panel, false, drawer);
        SidePanel.Width = shown.Panel > 0 ? shown.Panel : _prefs.PanelWidth;
        SidePanel.IsVisible = shown.Panel > 0;
        Edge.Fold(shown.PanelFolded, SidePanel.Width);
        double tall = _centre.Bounds.Height > 0
            ? Math.Min(_prefs.DrawerHeight, Math.Max(Prefs.MinDrawerHeight, Math.Floor(_centre.Bounds.Height * 0.6)))
            : _prefs.DrawerHeight;
        Drawer.Place(left, left ? shown.Drawer : tall);
    }

    // Show details on a panel folded for room: the drawer on the left closes to its strip, which gives the room back.
    private void MakeRoomForThePanel()
    {
        if (ViewState.DrawerLeft && ViewState.DrawerOpen)
        {
            ViewState.DrawerOpen = false;
            Say("Closed the objects drawer to make room for the settings (Ctrl+G opens it again).");
        }
        else
        {
            Say("The window is too narrow for the settings beside the view: make it wider.");
        }
    }

    // As soon as an entry is started or opened (before its first paint or build), the panel's header names its kind
    // and its resets go back to the starting point it came from (each setting's default for an entry opened from a
    // file).
    // Before the entry is shown: its recipe's share for the detail level, so it is built at that level from the start.
    private void OnEntered()
    {
        Editing.Reset();
        ApplyDetail();
        Panel.SetStartingPoint(StartingSettings());
        PanelHead.ShowKind(Panel.KindLabel);
        PanelHead.Refresh();
    }

    // The share of full detail the rock (or cliff) is built at: its recipe's share for the level shown, 1 for the floor.
    private void ApplyDetail()
    {
        IMeshRecipe? mesh = MeshRecipes.Find(Files.Recipe);
        if (ViewState.Lod == ViewState.FarLevel && mesh?.FarAway(Panel.Current) != true)
        {
            ViewState.Lod = 0;
        }

        bool far = ViewState.Lod == ViewState.FarLevel;
        Rocks.Far = far;
        Rocks.Detail = mesh is null || far ? 1 : mesh.DetailShares[ViewState.Lod];
    }

    // The floor has only its full detail.
    private void OnRecipeChanged(string recipe)
    {
        if (recipe == FloorRecipe.Id)
        {
            ViewState.Lod = 0;
        }

        View.Wind.Sway = MeshRecipes.Find(recipe)?.Wind;
        PanelHead.Refresh();
    }

    // The settings of the starting point the entry came from, or null when it was opened from a file.
    private Settings? StartingSettings()
    {
        string? id = Files.StartedFrom;
        if (id is null)
        {
            return null;
        }

        return Files.Recipe == FloorRecipe.Id
            ? FloorRecipe.StartingPoint(FloorSpecs).Settings
            : AllStartingPoints.FirstOrDefault(p => p.Id == id && p.Recipe == Files.Recipe)?.Settings;
    }

    // Closing with unsaved changes asks first: the close is stopped, and done again once the answer is in.
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved || (!Files.Dirty && !Files.Asking))
        {
            return;
        }

        e.Cancel = true;
        if (!Files.Asking)
        {
            _ = AskThenClose();
        }
    }

    private async Task AskThenClose()
    {
        if (await Files.ConfirmDiscardAsync())
        {
            _closeApproved = true;
            Close();
        }
    }

    private DockPanel Layout(Grid centre)
    {
        _stage.ColumnDefinitions = new ColumnDefinitions("Auto,*");
        Grid.SetColumn(centre, 1);
        _stage.Children.Add(centre);
        Border top = TopBar;
        Border bottom = StatusBar;
        var column = new DockPanel();
        DockPanel.SetDock(PanelHead, Dock.Top);
        DockPanel.SetDock(PanelFoot, Dock.Bottom);
        column.Children.Add(PanelHead);
        column.Children.Add(PanelFoot);
        column.Children.Add(new ScrollViewer { Content = Panel });
        var side = new Border
        {
            Width = _prefs.PanelWidth,
            Background = Look.Theme.SurfacePanel,
            Child = column,
        };

        SidePanel = side;
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(side, Dock.Right);
        DockPanel.SetDock(Edge, Dock.Right);
        dock.Children.Add(top);
        dock.Children.Add(bottom);
        dock.Children.Add(side);
        dock.Children.Add(Edge);
        dock.Children.Add(_stage);
        dock.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                Fit();
            }
        };
        _shell = dock;
        return dock;
    }

    /// <summary>Raised once, on the UI thread, when the window first has something to show: the floor painted, a prop
    /// built, or a failure to say. The launch keeps its loading window up until then.</summary>
    internal event Action? FirstPictureReady;

    /// <summary>Gets a value indicating whether the first picture is ready (see <see cref="FirstPictureReady"/>).</summary>
    internal bool HasFirstPicture { get; private set; }

    private void PictureReady()
    {
        if (!HasFirstPicture)
        {
            HasFirstPicture = true;
            FirstPictureReady?.Invoke();
        }
    }

    // The worker client raises this on a thread-pool thread.
    private void OnPainterFailed(string message) => Dispatcher.UIThread.Post(() => Say(message, error: true));

    private void OnShown(int size, double ms)
    {
        if (size == LiveRepaint.FullSize)
        {
            Say(string.Create(CultureInfo.InvariantCulture, $"Full detail painted in {ms:0} ms."));
        }

        TraceLog.Write(string.Create(CultureInfo.InvariantCulture, $"painted {size} px: {ms:0.0} ms from asking to having it"));
    }

    // Also the line that replaces an earlier failure, or the floor's "Painting the floor…" when a rock starts at once.
    private void OnRockBuilt(double ms)
    {
        Acted();
        Say(string.Create(CultureInfo.InvariantCulture, $"{Rocks.Recipe.Label} built in {ms:0} ms."));
    }

    private void OnDrawn(int size, double uploadMs) =>
        TraceLog.Write(string.Create(CultureInfo.InvariantCulture, $"drawn {size} px: upload and compose {uploadMs:0.0} ms"));
}
