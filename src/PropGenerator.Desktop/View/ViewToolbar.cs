// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Look;
using PropGenerator.Desktop.Recipes;

namespace PropGenerator.Desktop.View;

/// <summary>
/// The controls over the 3D view, as easy-props' ViewportOverlay and compass: top left the tools (Select, pressed;
/// Move and Turn, greyed until part editing); top right two groups, the look (Shading, Detail level) and the view
/// (Scene, Sun and shadow, Slope, Wind, Camera angle, Frame, Stats, A person for scale, What you can walk into, Maximize);
/// the stats card and the collision note under them; the compass bottom left, above the drawer. Every tool runs the
/// window's command, so the menus, the shortcuts and the toolbar always agree, and follows the view state.
/// </summary>
internal sealed class ViewToolbar
{
    private const string Nothing = "Nothing to walk into: this prop has no collision shapes.";
    private const string ShadingTip = "Shading: how the prop is drawn — lit, flat or as its triangles.";
    private const string LodTip = "Detail level shown: full detail, or the lighter levels an export writes.";
    private const string RestoreTip = "Restore the panels (Ctrl+Space): bring back the panel, the drawer and the status bar as they were.";

    private readonly MainWindow _w;
    private readonly Border _drawer;
    private bool _statsShown;

    /// <summary>Builds the controls; they follow the window's view state, piece and drawer from now on.</summary>
    /// <param name="window">The window: its commands, view state, view, rock and drawer.</param>
    internal ViewToolbar(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _w = window;
        _drawer = window.Drawer.Card;

        Select = Tool("select-tool", "mouse-pointer-2", "Select");
        Select.Pressed = true;
        Move = Tool("move-tool", "move", "Move");
        Turn = Tool("turn-tool", "rotate-3d", "Turn");
        Resize = Tool("resize-tool", "scale-3d", "Resize");
        Left = new ToolGroup(Select, Move, Turn, Resize) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(18, Theme.Space2, 0, 0) };

        Normal = Choice("build-normal", "Normal");
        LowPoly = Choice("build-low-poly", "Low-poly");
        Style = new ToolGroup(Normal, LowPoly) { IsVisible = false };

        Shading = Menu("shading", "sun", "Shading: Lit", ShadingTip, "Lit");
        Lod = Menu("lod", "layers", "Detail level 0", LodTip, "LOD0");
        GroupA = new ToolGroup(Shading.Trigger, Lod.Trigger);

        Scene = Menu("scene", "tree-pine", "Scene", "Scene around the prop: a plain studio, the studio under a blue sky, or neutral grey.", null);
        Sun = new SunPopover(window.ViewState);
        Slope = new SlopePopover(window.ViewState, () => FloorShown ? SlopePopover.FloorLevel : null, m => window.Say(m));
        Wind = new WindPopover(window.ViewState, () => Recipes.MeshRecipes.Find(window.Files.Recipe)?.Wind is not null);
        Camera = Menu("camera-angle", "camera", "Camera angle", "Camera angle: look from the front, side, top or the usual three-quarter view.", null, help: false);
        Frame = Tool("frame", "frame", "Frame");
        Stats = Tool("stats", "info", "Stats");
        Person = Tool("person", "person-standing", "A person for scale");
        Collision = Tool("collision", "brick-wall", "What you can walk into");
        Maximize = Tool("maximize", "maximize", "Maximize");
        GroupB = new ToolGroup(Scene.Trigger, Sun.Trigger, Slope.Trigger, Wind.Trigger, Camera.Trigger, Frame, Stats, Person, Collision, Maximize);
        Right = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = Theme.Space2,
            LineSpacing = Theme.Space2,
            ItemsAlignment = WrapPanelItemsAlignment.End,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, Theme.Space2, 18, 0),
            Children = { Style, GroupA, GroupB },
        };

        StatsCard = new StatsCard(() => (window.StatusBar.ShownFaces(), window.View.DrawCalls, window.View.ShownSize));
        NoteText = new TextBlock { Text = Nothing, TextWrapping = TextWrapping.Wrap };
        Note = new Border
        {
            Child = NoteText,
            MaxWidth = 320,
            Padding = new Thickness(Theme.Space3),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.OverlayBorder,
            CornerRadius = new CornerRadius(Theme.RadiusCard),
            Background = Theme.OverlayBg,
            BoxShadow = Theme.Shadow1,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(18, (Theme.Space2 * 2) + Theme.ControlHeight + (Theme.Space1 * 2) + 2, 0, 0),
            IsHitTestVisible = false,
            IsVisible = false,
        };
        TextElement.SetForeground(Note, Theme.OverlayText);
        TextElement.SetFontSize(Note, Theme.Caption);

        Compass = new Compass(window.View.Orbit, window.View.LookFrom)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(18, 0, 0, Theme.Space2),
        };

        Strip = new HistoryStrip(window.Files);
        Strip.ShownChanged += PlaceUnderStrip;

        Overlay = new Grid
        {
            Children = { Strip, Left, Right, StatsCard, Note, Compass, Shading.Popup, Lod.Popup, Scene.Popup, Camera.Popup, Sun.Popup, Slope.Popup, Wind.Popup },
        };

        window.ViewState.Changed += Refresh;
        window.Files.RecipeChanged += _ => Refresh();
        window.Rocks.Showed += (_, _) => PieceChanged();
        window.Rocks.ShapesReady += RefreshCollision;
        window.View.FrameDrawn += Compass.Follow;
        _drawer.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty || e.Property == Visual.IsVisibleProperty)
            {
                PlaceCompass();
            }
        };
        Overlay.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty)
            {
                FitRight();
                PlaceCompass();
            }
        };
        // The view's tools wrap onto a second line when the view is narrow; the cards under them follow their height.
        Right.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty)
            {
                PlaceUnderStrip();
            }
        };
        Left.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty)
            {
                FitRight();
                PlaceUnderStrip();
            }
        };
        PlaceUnderStrip();
        Refresh();
    }

    /// <summary>Gets everything over the view, to lay over it (it takes clicks only on its own controls).</summary>
    internal Grid Overlay { get; }

    /// <summary>Gets the history strip along the top of the view, over the tools.</summary>
    internal HistoryStrip Strip { get; }

    /// <summary>Gets the tools top left: Select, Move, Turn and Resize.</summary>
    internal ToolGroup Left { get; }

    /// <summary>Gets the two groups top right; they wrap onto a second line, right-aligned, when the view is too narrow.</summary>
    internal WrapPanel Right { get; }

    /// <summary>Gets the style switch (easy-props 0.4.2): Normal and Low-poly, shown for a kind that has both.</summary>
    internal ToolGroup Style { get; }

    /// <summary>Gets the switch's Normal button.</summary>
    internal ToolButton Normal { get; }

    /// <summary>Gets the switch's Low-poly button.</summary>
    internal ToolButton LowPoly { get; }

    /// <summary>Gets the look's group: Shading and Detail level.</summary>
    internal ToolGroup GroupA { get; }

    /// <summary>Gets the view's group: Scene to Maximize.</summary>
    internal ToolGroup GroupB { get; }

    /// <summary>Gets the Resize tool (pressed while on).</summary>
    internal ToolButton Resize { get; }

    /// <summary>
    /// Shows which part tool is on and which can be used for the part selected now (Move, Turn and Resize are greyed
    /// with the reason while they cannot).
    /// </summary>
    internal void RefreshTools()
    {
        string tool = _w.Editing.Tool;
        Select.Pressed = tool == Parts.PartEditing.SelectTool;
        foreach ((ToolButton button, string id, string name) in new[] { (Move, "move-tool", Parts.PartEditing.MoveTool), (Turn, "turn-tool", Parts.PartEditing.TurnTool), (Resize, "resize-tool", Parts.PartEditing.ResizeTool) })
        {
            button.Unavailable = Find(id).Unavailable();
            button.Pressed = tool == name;
        }
    }

    /// <summary>Gets the Select tool (on unless a part tool is).</summary>
    internal ToolButton Select { get; }

    /// <summary>Gets the Move tool (greyed until a part that moves is selected; pressed while on).</summary>
    internal ToolButton Move { get; }

    /// <summary>Gets the Turn tool (greyed until a part that turns is selected; pressed while on).</summary>
    internal ToolButton Turn { get; }

    /// <summary>Gets the Shading menu.</summary>
    internal ToolMenu Shading { get; }

    /// <summary>Gets the Detail level menu.</summary>
    internal ToolMenu Lod { get; }

    /// <summary>Gets the Scene menu.</summary>
    internal ToolMenu Scene { get; }

    /// <summary>Gets the Sun and shadow tool and its popover.</summary>
    internal SunPopover Sun { get; }

    /// <summary>Gets the Slope tool and its popover.</summary>
    internal SlopePopover Slope { get; }

    /// <summary>Gets the Wind tool and its popover.</summary>
    internal WindPopover Wind { get; }

    /// <summary>Gets the Camera angle menu.</summary>
    internal ToolMenu Camera { get; }

    /// <summary>Gets the Frame tool.</summary>
    internal ToolButton Frame { get; }

    /// <summary>Gets the Stats tool.</summary>
    internal ToolButton Stats { get; }

    /// <summary>Gets the person-for-scale tool.</summary>
    internal ToolButton Person { get; }

    /// <summary>Gets the What you can walk into tool.</summary>
    internal ToolButton Collision { get; }

    /// <summary>Gets the Maximize tool (Restore the panels while maximized).</summary>
    internal ToolButton Maximize { get; }

    /// <summary>Gets the stats card.</summary>
    internal StatsCard StatsCard { get; }

    /// <summary>Gets the collision note's card, shown while the shapes are asked for and there are none.</summary>
    internal Border Note { get; }

    /// <summary>Gets the collision note's text.</summary>
    internal TextBlock NoteText { get; }

    /// <summary>Gets the compass.</summary>
    internal Compass Compass { get; }

    /// <summary>Closes whichever of the toolbar's menus and popovers is open.</summary>
    /// <returns>True when one was open.</returns>
    internal bool ClosePopups()
    {
        bool any = false;
        foreach (Avalonia.Controls.Primitives.Popup p in new[] { Shading.Popup, Lod.Popup, Scene.Popup, Camera.Popup, Sun.Popup, Slope.Popup, Wind.Popup })
        {
            any |= p.IsOpen;
            p.IsOpen = false;
        }

        return any;
    }

    /// <summary>Brings every tool, card and the shapes up to date with the view state and the piece.</summary>
    internal void Refresh()
    {
        ViewState s = _w.ViewState;
        RefreshTools();
        RefreshShading();
        RefreshStyle();
        bool far = s.Lod == ViewState.FarLevel;
        Lod.Trigger.Show("layers", far ? "Detail level: far away" : $"Detail level {s.Lod}", LodTip, far ? "Far" : $"LOD{s.Lod}");
        Stats.Pressed = s.ShowStats;
        Person.Pressed = s.ShowPerson;
        Collision.Pressed = s.ShowCollision;
        Wind.Refresh();
        Slope.Refresh();
        Maximize.Pressed = s.Maximized;
        Maximize.Show(
            s.Maximized ? "minimize" : "maximize",
            s.Maximized ? "Restore the panels" : "Maximize",
            s.Maximized ? RestoreTip : Tip("Maximize", Find("maximize")));
        if (s.ShowStats != _statsShown)
        {
            _statsShown = s.ShowStats;
            StatsCard.Show(s.ShowStats);
        }

        StatsCard.Refresh();
        RefreshCollision();
        PlaceCompass();
    }

    /// <summary>
    /// Brings what follows the piece shown up to date after it changed (a rock built, a mesh from Blender shown, the
    /// piece shown again): the shading tool, the shapes you can walk into and the stats card.
    /// </summary>
    internal void PieceChanged()
    {
        Slope.Refresh();
        RefreshTools();
        RefreshShading();
        RefreshStyle();
        RefreshCollision();
        StatsCard.Refresh();
    }

    /// <summary>
    /// Shows the rock's shapes you can walk into while they are asked for (the rock works them out off the window's
    /// thread, with each build), or the note when the piece has none: the floor, a mesh from Blender, a rock that offers
    /// none. While the shapes of the rock shown are still being worked out, the ones shown stay and the note waits.
    /// </summary>
    internal void RefreshCollision()
    {
        bool rock = MeshRecipes.Find(_w.Files.Recipe) is not null && !_w.View.ShowingPiece;
        if (!_w.ViewState.ShowCollision)
        {
            Note.IsVisible = false;
            if (_w.View.Prisms is not null)
            {
                _w.View.SetPrisms(null);
            }

            return;
        }

        CollisionPrisms? shown = null;
        bool known = !rock;
        if (rock && _w.Rocks.ShownSettings is Settings settings)
        {
            known = settings.Equals(_w.Rocks.ShapesOf);
            shown = known ? _w.Rocks.Shapes : _w.View.Prisms;
        }

        // A rock not built yet, or whose shapes are not worked out yet, says nothing until they are.
        Note.IsVisible = shown is null && known;
        if (!ReferenceEquals(shown, _w.View.Prisms))
        {
            _w.View.SetPrisms(shown);
        }
    }

    /// <summary>Shows the style switch for a kind that has it, with the style the piece is in pressed.</summary>
    internal void RefreshStyle()
    {
        Style.IsVisible = MeshRecipes.Find(_w.Files.Recipe) is { LowPoly: true };
        string? build = _w.Panel.Current.Values.TryGetValue("build", out System.Text.Json.JsonElement b) ? b.GetString() : null;
        Normal.Pressed = build == "painted";
        LowPoly.Pressed = build == "low-poly";
    }

    // The floor tiles are shown: no mesh kind is open and no mesh from Blender is shown (the floor stays level).
    private bool FloorShown => MeshRecipes.Find(_w.Files.Recipe) is null && !_w.View.ShowingPiece;

    // The shading tool shows how the piece is drawn: the shading chosen for a rock, Lit for anything else.
    private void RefreshShading()
    {
        (string icon, string label) = _w.ShadingShown switch
        {
            ViewState.Flat => ("box", "Flat"),
            ViewState.Wireframe => ("grid-2x2", "Wireframe"),
            _ => ("sun", "Lit"),
        };
        Shading.Trigger.Show(icon, "Shading: " + label, ShadingTip, label);
    }

    // The right-hand tools wrap before they reach the left tools: the view's width less their own right margin, the
    // left tools' margin and width (4 tools until laid out) and a gap of 8 (easy-props' calc(100% − 112px) assumed
    // a narrower left group, so a view of about 820, the drawer docked on the left, drew Normal / Low-poly over it).
    private void FitRight()
    {
        double leftTools = Left.Bounds.Width > 0 ? Left.Bounds.Width : (Theme.ControlHeight * 4) + 16;
        double room = Overlay.Bounds.Width - Right.Margin.Right - Left.Margin.Left - leftTools - Theme.Space2;
        Right.MaxWidth = Math.Max(0, room);
    }

    // The tools, the stats card and the collision note start under the history strip while it shows (the prototype
    // moves the view's toolbars down the same way), and at the top of the view when it hides.
    private void PlaceUnderStrip()
    {
        double top = Theme.Space2 + (Strip.IsVisible ? HistoryStrip.StripHeight + Theme.Space2 : 0);
        Thickness tools = new(0, top, 18, 0), left = new(18, top, 0, 0);
        if (Right.Margin != tools)
        {
            Right.Margin = tools;
        }

        if (Left.Margin != left)
        {
            Left.Margin = left;
        }

        // 8 under the tools' last line: one toolbar card high until laid out, then the wrapped height of each side
        // (the note on the left goes under the right tools too, whose second line can reach over to the left).
        double one = Theme.ControlHeight + (Theme.Space1 * 2) + 2;
        double right = top + (Right.Bounds.Height > 0 ? Right.Bounds.Height : one);
        double leftBottom = top + (Left.Bounds.Height > 0 ? Left.Bounds.Height : one);
        Thickness card = new(0, right + Theme.Space2, 18, 0), note = new(18, Math.Max(right, leftBottom) + Theme.Space2, 0, 0);
        if (StatsCard.Margin != card)
        {
            StatsCard.Margin = card;
        }

        if (Note.Margin != note)
        {
            Note.Margin = note;
        }
    }

    // easy-props' compass sits 8 above the bottom; here the drawer lies over the bottom of the view, so the compass
    // stands 8 above the drawer while the drawer shows there (on the left it is beside the view, not over it).
    private void PlaceCompass()
    {
        double bottom = Theme.Space2;
        if (_drawer.IsVisible && !_w.ViewState.DrawerLeft && Overlay.Bounds.Height > 0)
        {
            bottom += Math.Max(0, Overlay.Bounds.Height - _drawer.Bounds.Y);
        }

        if (Math.Abs(Compass.Margin.Bottom - bottom) > 0.01)
        {
            Compass.Margin = new Thickness(18, 0, 0, bottom);
        }
    }

    private Command Find(string id) =>
        CommandList.Find(_w.Commands, id) ?? throw new InvalidOperationException($"The view toolbar names '{id}', which is no command.");

    // easy-props' "Label (keys)", then what the command does (its own tooltip, from a small letter).
    private static string Tip(string label, Command c)
    {
        string keys = c.Keys is null ? string.Empty : $" ({c.Keys})";
        string what = c.Tooltip ?? string.Empty;
        return what.Length == 0 ? label + keys : $"{label}{keys}: {char.ToLowerInvariant(what[0])}{what[1..]}";
    }

    // A tool that runs a command: its tooltip says what it does, or the reason it cannot run yet.
    private ToolButton Tool(string id, string icon, string label)
    {
        Command c = Find(id);
        var b = new ToolButton(icon, label, Tip(label, c)) { Unavailable = c.Unavailable() };
        b.Click += (_, _) => _ = c.Run();
        return b;
    }

    // One side of the segmented control: a pill with its label that runs its command.
    private ToolButton Choice(string id, string label)
    {
        Command c = Find(id);
        var b = new ToolButton("shapes", label, Tip("Style: " + label, c), label, segment: true);
        b.Click += (_, _) => _ = c.Run();
        return b;
    }

    // A tool that opens the command's sub-list as a menu.
    private ToolMenu Menu(string id, string icon, string name, string tooltip, string? text, bool help = true)
    {
        Command c = Find(id);
        return new ToolMenu(new ToolButton(icon, name, tooltip, text, menu: true), () => c.Children ?? [], help);
    }
}
