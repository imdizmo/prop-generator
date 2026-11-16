using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Pictures;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

public class ObjectsDrawerTests
{
    private static bool Waited(Func<bool> done, int ms = 20000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    // A work file written straight to disk and noted in the library, as a save would.
    private static string WriteFile(Scratch s, string name, Piece piece, Settings settings)
    {
        string path = s.P(name + ".propgen");
        var h = new History();
        h.Push("Started", settings);
        WorkFile.Save(path, piece, settings, h);
        new Library(s.P("library.json")).Add(path);
        return path;
    }

    private static MenuItem FavouriteItem(DrawerTile t) => t.Border.ContextMenu!.Items.OfType<MenuItem>().First();

    // As easy-props 0.4.5's: a row of kinds, and under it the groups of the chosen kind when it has more than one.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TabsAreAllRecentFavouritesMineThenKindsAndTheKindsGroupsBelow()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);

        Assert.Equal(
            ["All", "Recent 0", "Favourites 0", "Mine 0", "My presets 0", "Trees 56", "Palms 9", "Bushes 20", "Flowers 6", "Grass 9", "Cacti 7", "Rocks 27", "Floors 1"],
            w.Drawer.KindNames);
        Assert.Empty(w.Drawer.GroupNames);
        w.Drawer.ChooseTab("Rocks");
        Assert.Equal(["All", "Boulders 6", "Outcrops 3", "Stones 8", "Cliffs 7", "Stylized 3"], w.Drawer.GroupNames);
        Assert.Equal(27, w.Drawer.VisibleTileNames.Count);

        // A group is chosen under its kind; a kind of one group has no row of groups.
        w.Drawer.ChooseTab("Conifers");
        Assert.Equal(8, w.Drawer.VisibleTileNames.Count);
        Assert.True(w.Drawer.TabButtons.Single(b => AutomationProperties.GetName(b) == "Trees").IsChecked);
        Assert.Contains("Old-style trees 1", w.Drawer.GroupNames);
        w.Drawer.ChooseTab("Bushes");
        Assert.Empty(w.Drawer.GroupNames);
        w.Drawer.ChooseTab("All");
        Assert.Equal(135, w.Drawer.VisibleTileNames.Count);
        Assert.Equal("Floor tile (v5)", w.Drawer.VisibleTileNames[0]);
        Assert.Equal("Objects 135", w.Drawer.ToggleText);
        Assert.Equal("chevron-down", w.Drawer.ToggleIcon);
        Assert.Equal("search", Assert.IsType<PropGenerator.Desktop.Look.Icon>(w.Drawer.SearchBox.InnerLeftContent).IconName);

        w.Drawer.ChooseTab("Boulders");
        Assert.Equal(["Boulder", "Mossy boulder", "Snowy boulder", "Round boulder", "Sharp boulder", "Cracked boulder"], w.Drawer.VisibleTileNames);
        w.Drawer.ChooseTab("Mine");
        Assert.Empty(w.Drawer.VisibleTileNames);
        w.Close();
    }

    // Ruling 5: a tree's pieces are kinds (a saved one opens, edits and exports) but the New drawer never lists them.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void PiecesAreKindsButNotInTheDrawer()
    {
        string[] pieces = ["tree-stump", "tree-log", "tree-branch", "tree-litter"];
        Assert.Equal(pieces, EngineKinds.All.Where(k => !k.InDrawer).Select(k => k.Id));
        Assert.All(EngineKinds.All.Where(k => k.InDrawer), k => Assert.Empty(k.CarriedKeys));
        Assert.Equal(
            [("Stump", "stump"), ("Fallen log", "log"), ("Fallen branch", "branch"), ("Leaf litter", "litter")],
            pieces.Select(id => (MeshRecipes.Find(id)!.Label, MeshRecipes.Find(id)!.ExportName)));
        foreach (string id in pieces)
        {
            EngineKind kind = EngineKinds.Find(id)!;
            Assert.Equal(["tree", "treeSeed", "treeName"], kind.CarriedKeys);
            EngineRecipe recipe = Assert.IsType<EngineRecipe>(MeshRecipes.Find(id));
            Assert.Empty(recipe.StartingPoints);
            Assert.NotEmpty(recipe.Specs);
            Assert.False(EngineKinds.InDrawer(recipe));

            // Still, as upstream's pieces are; the litter's leaves move as one in the wind.
            Assert.Equal(id == "tree-litter" ? new WindSway(Foliage: true, Evenly: true) : null, recipe.Wind);
        }

        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        Assert.DoesNotContain(w.StartingPoints, p => pieces.Contains(p.Recipe));
        Assert.Equal(
            ["All", "Recent 0", "Favourites 0", "Mine 0", "My presets 0", "Trees 56", "Palms 9", "Bushes 20", "Flowers 6", "Grass 9", "Cacti 7", "Rocks 27", "Floors 1"],
            w.Drawer.KindNames);
        w.Drawer.ChooseTab("All");
        Assert.Equal(135, w.Drawer.VisibleTileNames.Count);
        w.Close();
    }

    // The strip never runs past the drawer: in a narrow window it scrolls instead of drawing over the panel.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheTabsStayInsideTheDrawer()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        w.Width = 700;
        w.ViewState.DrawerOpen = true;
        w.Drawer.ChooseTab("Trees");
        Ui.Pump();
        Rect card = w.Drawer.Card.Bounds;
        foreach (Control strip in w.Drawer.Strips)
        {
            Point? right = strip.TranslatePoint(new Point(strip.Bounds.Width, 0), w.Drawer.Card);
            Assert.True(right!.Value.X <= card.Width, $"{strip.GetType().Name} ends at {right.Value.X}, the drawer at {card.Width}");
        }

        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SearchFindsAcrossGroups()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        w.Drawer.ChooseTab("Wood");

        w.Drawer.SetSearch("stone");
        Assert.Equal(["Field stone", "Damp stone", "River stone", "Stepping stone", "Cracked slab", "Mossy rubble", "Riverbank stones", "Standing stone"], w.Drawer.VisibleTileNames);

        // A group's name finds every tile in it, in any case.
        w.Drawer.SetSearch("CLIFF");
        Assert.Equal(
            ["Cliff face", "Sea stack", "Red mesa", "Long cliff", "Tall cliff", "Cliff top", "Layered cliff"],
            w.Drawer.VisibleTileNames);

        w.Drawer.SetSearch("");
        Assert.Equal(["Floor tile (v5)"], w.Drawer.VisibleTileNames);

        // Choosing a tab empties the search.
        w.Drawer.SetSearch("mesa");
        w.Drawer.ChooseTab("Outcrops");
        Assert.Equal(["Grassy outcrop", "Overhang", "Low ledge"], w.Drawer.VisibleTileNames);
        w.Close();
    }

    // The drawer keeps its layout but takes the look: no accent blue. The tabs are underline tabs (TabRowTests), the
    // open drawer's toggle is selected, and the piece being worked on is outlined in the selection amber.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheDrawerTakesTheTheme()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        w.ViewState.DrawerOpen = true;
        w.Drawer.ChooseTab("All");
        Ui.Pump();

        foreach (ToggleButton b in w.Drawer.TabButtons.Append(w.Drawer.OpenToggle))
        {
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(b) as string));
        }

        Assert.Contains("ep-toggle", w.Drawer.OpenToggle.Classes);
        Assert.True(w.Drawer.TabButtons[0].IsChecked);
        Assert.False(w.Drawer.TabButtons[1].IsChecked);

        DrawerTile floor = w.Drawer.Tile("Floor tile (v5)");
        Assert.True(floor.Outlined);
        Assert.Equal(Look.Theme.Selection, floor.Border.BorderBrush);
        Assert.Equal(Brushes.Transparent, w.Drawer.Tile("Boulder").Border.BorderBrush);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ClickingABoulderStartsIt()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        Assert.True(w.Drawer.Tile("Floor tile (v5)").Outlined);

        Run(w.Drawer.ClickTileAsync("Boulder"));

        Assert.Equal("cut-rock", w.Files.Recipe);
        Assert.Equal(["Boulder"], w.Files.History.Steps.Select(x => x.Label));
        Assert.Equal("boulder", new Prefs(s.P("prefs.json")).Recent[0]);
        Assert.Equal("Recent 1", w.Drawer.TabNames[1]);
        Assert.True(w.Drawer.Tile("Boulder").Outlined);
        Assert.False(w.Drawer.Tile("Floor tile (v5)").Outlined);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TileClickWhileAskingDoesNothing()
    {
        using var s = new Scratch();
        var hold = new TaskCompletionSource();
        var dialogs = new FakeDialogs { Hold = hold.Task, Choice = SaveChoice.Discard };
        MainWindow w = BridgeKit.Open(s, dialogs);
        FileActionsTests.SetSeed(w, 9);
        Task<bool> asking = w.Files.NewAsync();
        Ui.Pump();
        Assert.True(w.Files.Asking);

        Run(w.Drawer.ClickTileAsync("Boulder"));

        Assert.Equal(FloorRecipe.Id, w.Files.Recipe);
        Assert.Equal("Recent 0", w.Drawer.TabNames[1]);
        Assert.Equal(1, dialogs.SaveChangesAsked);
        hold.SetResult();
        Assert.True(Run(asking));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FavouritesSurviveARestart()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        DrawerTile boulder = w.Drawer.Tile("Boulder");
        Assert.False(boulder.Favourite);
        Assert.Equal("Add to Favourites", FavouriteItem(boulder).Header);

        FavouriteItem(boulder).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        FavouriteItem(w.Drawer.Tile("Sea stack")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.True(boulder.Favourite);
        Assert.Equal("Take out of Favourites", FavouriteItem(boulder).Header);
        Assert.Equal("Favourites 2", w.Drawer.TabNames[2]);
        w.Close();

        Assert.Equal(["boulder", "sea-stack"], new Prefs(s.P("prefs.json")).Favourites);
        MainWindow again = BridgeKit.Open(s);
        Assert.Equal("Favourites 2", again.Drawer.TabNames[2]);
        again.Drawer.ChooseTab("Favourites");
        Assert.Equal(["Boulder", "Sea stack"], again.Drawer.VisibleTileNames);
        Assert.True(again.Drawer.Tile("Sea stack").Favourite);

        // Taken out again.
        FavouriteItem(again.Drawer.Tile("Boulder")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(["Sea stack"], again.Drawer.VisibleTileNames);
        Assert.Equal(["sea-stack"], new Prefs(s.P("prefs.json")).Favourites);
        again.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RecentKeepsTwelveNewestFirst()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        Assert.Equal(135, w.StartingPoints.Count);

        // Fourteen quick ones (no slab cliffs, which take seconds to build): more than the twelve kept.
        IReadOnlyList<StartingPoint> all = [.. w.StartingPoints.Where(p => p.Recipe != "slab-cliff").Take(14)];
        Assert.Contains(all, p => p.Id == "boulder");
        foreach (StartingPoint p in all)
        {
            Run(w.Drawer.ClickTileAsync(p.Name));
        }

        // The boulder again: it moves to the front, it is not listed twice.
        Run(w.Drawer.ClickTileAsync("Boulder"));

        string[] expected = ["boulder", .. all.Reverse().Select(p => p.Id).Where(id => id != "boulder").Take(11)];
        Assert.Equal(expected, new Prefs(s.P("prefs.json")).Recent);
        Assert.Equal("Recent 12", w.Drawer.TabNames[1]);
        w.Drawer.ChooseTab("Recent");
        Assert.Equal(expected.Select(id => all.Single(p => p.Id == id).Name), w.Drawer.VisibleTileNames);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MineListsTheLibraryNewestFirstAndOpensOnClick()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("walnut.propgen") };
        MainWindow w = BridgeKit.Open(s, dialogs);
        FileActionsTests.SetSeed(w, 4);
        w.Panel.ValueBoxFor("meander").Text = "3.5";
        w.Panel.CommitValueBox("meander");
        Ui.Pump();
        Settings saved = w.Panel.Current;
        FileActionsTests.Press(w, PhysicalKey.S);
        Assert.Equal("Mine 1", w.Drawer.TabNames[3]);

        FileActionsTests.Press(w, PhysicalKey.N);
        dialogs.SavePath = s.P("pine.propgen");
        FileActionsTests.SetSeed(w, 11);
        FileActionsTests.Press(w, PhysicalKey.S);
        FileActionsTests.Press(w, PhysicalKey.N);
        Assert.Equal("untitled.propgen — Prop Generator", w.Title);

        w.Drawer.ChooseTab("Mine");
        Assert.Equal("Mine 2", w.Drawer.TabNames[3]);
        Assert.Equal(["pine", "walnut"], w.Drawer.VisibleTileNames);
        string tip = (string)ToolTip.GetTip(w.Drawer.Tile("walnut").Border)!;
        Assert.StartsWith($"walnut.propgen\n{s.Dir}\nLast opened ", tip);
        Assert.EndsWith("\nClick: open it", tip);

        // Search on Mine finds files by name too.
        w.Drawer.SetSearch("walN");
        Assert.Equal(["walnut"], w.Drawer.VisibleTileNames);

        Run(w.Drawer.ClickTileAsync("walnut"));

        Assert.Equal(saved, w.Panel.Current);
        Assert.Equal("walnut.propgen — Prop Generator", w.Title);
        Assert.Equal(["Opened", "Seed 4", "Edge bends 3.5"], w.Files.History.Steps.Select(x => x.Label));
        Assert.Equal(4, w.Panel.Current.GetInt("seed"));
        Assert.True(w.Drawer.Tile("walnut").Outlined);

        // Opening it put it first; a Mine tile's menu opens it as well.
        w.Drawer.SetSearch("");
        Assert.Equal(["walnut", "pine"], w.Drawer.VisibleTileNames);
        Assert.Equal("Recent 0", w.Drawer.TabNames[1]);
        MenuItem open = w.Drawer.Tile("pine").Border.ContextMenu!.Items.OfType<MenuItem>().Single();
        Assert.Equal("Open", open.Header);
        open.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        // The open is finished once pine is outlined as the file being worked on.
        Assert.True(Waited(() => w.Drawer.Tile("pine").Outlined));
        Assert.Equal("pine.propgen — Prop Generator", w.Title);
        Assert.Equal(11, w.Panel.Current.GetInt("seed"));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MineWithoutPicturesShowsSwatches()
    {
        using var s = new Scratch();
        IReadOnlyList<ParamSpec> specs = Ui.Specs();
        var oak = Settings.Defaults(specs);
        WriteFile(s, "granite", new Piece(RockRecipe.Id, []), RockRecipe.StartingPoints[0].Settings);
        WriteFile(s, "oak", TileMesh.FloorPiece(), oak);
        string hex = new Library(s.P("library.json")).List().Single(e => e.Name == "oak").ColourHex!;

        MainWindow w = BridgeKit.Open(s);
        w.Drawer.ChooseTab("Mine");

        Assert.Equal(["oak", "granite"], w.Drawer.VisibleTileNames);
        DrawerTile o = w.Drawer.Tile("oak"), g = w.Drawer.Tile("granite");
        Assert.True(o.SwatchShown);
        Assert.True(g.SwatchShown);
        Assert.Equal(Color.Parse(hex), o.Swatch);
        Assert.Equal(Color.Parse("#7A7A74"), g.Swatch);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void CtrlGTogglesAndIsRemembered()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        Assert.True(w.Drawer.IsOpen);

        w.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
        Ui.Pump();

        Assert.False(w.Drawer.IsOpen);
        Assert.Equal("Objects 135", w.Drawer.ToggleText);
        Assert.Equal("chevron-up", w.Drawer.ToggleIcon);
        Assert.False(new Prefs(s.P("prefs.json")).DrawerOpen);
        Assert.True(w.Drawer.Card.IsVisible);
        Assert.DoesNotContain(w.Drawer.Card.GetVisualDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible);
        w.Close();

        MainWindow again = BridgeKit.Open(s);
        Assert.False(again.Drawer.IsOpen);
        again.Drawer.Toggle();
        Assert.True(again.Drawer.IsOpen);
        Assert.True(new Prefs(s.P("prefs.json")).DrawerOpen);

        // The tab is remembered too.
        again.Drawer.ChooseTab("Stones");
        again.Close();
        MainWindow third = BridgeKit.Open(s);
        Assert.Equal(["Field stone", "Damp stone", "River stone", "Stepping stone", "Cracked slab", "Mossy rubble", "Riverbank stones", "Standing stone"], third.Drawer.VisibleTileNames);
        third.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EveryDrawerControlHasATooltip()
    {
        using var s = new Scratch();
        WriteFile(s, "oak", TileMesh.FloorPiece(), Settings.Defaults(Ui.Specs()));
        MainWindow w = BridgeKit.Open(s);

        string boulderTip = (string)ToolTip.GetTip(w.Drawer.Tile("Boulder").Border)!;
        Assert.Equal("Boulder (Boulders)\nClick: start a new piece from it · drag onto the view: the same · right click: favourite", boulderTip);

        var controls = new List<Control>();
        controls.AddRange(w.Drawer.Card.GetVisualDescendants().OfType<Control>()
            .Where(c => c.TemplatedParent is null && c is ToggleButton or Button or TextBox));
        // The Objects toggle, thirteen tabs (All to My presets, eight kinds), every kind's groups and the kind's All.
        Assert.Equal(1 + 13 + 14 + 1, controls.OfType<ToggleButton>().Count());
        Assert.Single(controls.OfType<TextBox>());
        Assert.Equal("Search", controls.OfType<TextBox>().Single().PlaceholderText);
        w.Drawer.ChooseTab("Mine");
        controls.Add(w.Drawer.Tile("oak").Border);
        w.Drawer.ChooseTab("All");
        controls.AddRange(w.Drawer.VisibleTileNames.Select(n => w.Drawer.Tile(n).Border));
        // The toggles, the search, the Dock button, the tabs' More, the file on Mine and every starting point.
        Assert.Contains(w.Drawer.DockButton, controls);
        Assert.Contains(w.Drawer.Tabs.More, controls);
        Assert.Equal(29 + 1 + 1 + 1 + 1 + 135, controls.Count);

        Assert.All(controls, c =>
        {
            string? tip = ToolTip.GetTip(c) as string;
            Assert.False(string.IsNullOrWhiteSpace(tip), $"{c.GetType().Name} '{AutomationProperties.GetName(c)}' has no tooltip");
            Assert.True(tip!.Length >= 40, $"tooltip too short: '{tip}'");
            Assert.Contains("lick", tip, StringComparison.Ordinal);
        });
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void NoLibraryButtonAnymore()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        Assert.DoesNotContain(
            w.GetVisualDescendants().OfType<Button>(),
            b => b.Content is string text && text.Contains("Library", StringComparison.OrdinalIgnoreCase));
        Assert.Null(typeof(MainWindow).Assembly.GetType("PropGenerator.Desktop.LibraryWindow"));
        w.Close();
    }

    // A window whose pictures are kept in a folder of its own, so they are drawn (or not) by this window alone.
    private static MainWindow OwnThumbs(Scratch s, ThumbCache thumbs)
    {
        var w = new MainWindow(
            new FakePainter(), Ui.Specs(), new FakeDialogs(), new Prefs(s.P("prefs.json")), new Library(s.P("library.json")),
            s.P("bridge"), thumbs);
        w.Show();
        Ui.Pump();
        return w;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheOaksPictureIsEasyPropsOwnAndNotDrawn()
    {
        using var s = new Scratch();
        var thumbs = new ThumbCache(s.P("own-thumbs"));
        MainWindow w = OwnThumbs(s, thumbs);
        IMeshRecipe tree = MeshRecipes.Find("painted-tree")!;
        StartingPoint oak = tree.StartingPoints.Single(p => p.Id == "oak");

        Assert.True(Waited(() => w.Drawer.Tile(oak.Name).ShownPng is not null, 60000));
        Assert.Equal(PropGenerator.Engine.EngineOptions.Thumbnail("oak"), w.Drawer.Tile(oak.Name).ShownPng);
        Assert.Null(thumbs.Get(ThumbCache.PresetKey("oak", tree.Id, tree.Version, ShippedThumbs.EngineTag(tree.Id))));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AUserPresetIsDrawnByOurPainterKeyedWithTheEngineTag()
    {
        using var s = new Scratch();
        IMeshRecipe bush = MeshRecipes.Find("bush")!;
        StartingPoint mine = new UserPresets(s.P("presets.json")).Add("My round bush", bush.Id, bush.StartingPoints[0].Settings);
        var thumbs = new ThumbCache(s.P("own-thumbs"));
        MainWindow w = OwnThumbs(s, thumbs);
        string key = ThumbCache.PresetKey(mine.Id, bush.Id, bush.Version, ShippedThumbs.EngineTag(bush.Id));
        Assert.EndsWith(":" + ShippedThumbs.EngineTag(bush.Id), key, StringComparison.Ordinal);

        Assert.True(Waited(() => thumbs.Get(key) is not null, 60000));
        Assert.True(Waited(() => w.Drawer.Tile("My round bush").ShownPng is not null, 60000));
        Assert.Equal(thumbs.Get(key), w.Drawer.Tile("My round bush").ShownPng);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AFileOnMineStillShowsTheViewKeptAtItsLastSave()
    {
        using var s = new Scratch();
        string path = WriteFile(s, "granite", new Piece(RockRecipe.Id, []), RockRecipe.StartingPoints[0].Settings);
        var thumbs = new ThumbCache(s.P("own-thumbs"));
        byte[] kept = MeshThumb.Png(new ColouredMesh([0, 0, 0, 1, 0, 0, 0, 0, 1], [0, -1, 0, 0, -1, 0, 0, -1, 0], [1, 1, 1, 1, 1, 1, 1, 1, 1], [0, 1, 2]), 16);
        thumbs.Put(ThumbCache.FileKey(Path.GetFullPath(path)), kept);
        MainWindow w = OwnThumbs(s, thumbs);
        w.Drawer.ChooseTab("Mine");

        // After the starting points' pictures (the cliffs' and stylized rocks' are drawn) comes the file's.
        Assert.True(Waited(() => w.Drawer.Tile("granite").ShownPng is not null, 60000));
        Assert.Equal(kept, w.Drawer.Tile("granite").ShownPng);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DraggingATileOntoTheViewStartsItAndLettingGoOnTheDrawerDoesNot()
    {
        using var s = new Scratch();
        MainWindow w = BridgeKit.Open(s);
        Border tile = w.Drawer.Tile("River stone").Border;
        Point from = tile.TranslatePoint(new Point(20, 20), w)!.Value;
        Point onDrawer = new(from.X + 30, from.Y);
        Point onView = w.Drawer.Card.TranslatePoint(new Point(200, -150), w)!.Value;

        // Let go on the drawer itself after a drag: nothing.
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(onDrawer);
        w.MouseUp(onDrawer, MouseButton.Left);
        Ui.Pump();
        Assert.Equal(FloorRecipe.Id, w.Files.Recipe);

        // Dropped on the view: started.
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(new Point(from.X, from.Y - 40));
        w.MouseMove(onView);
        w.MouseUp(onView, MouseButton.Left);
        // The start is finished (the rock built and shown) once the stone is first in Recent.
        Assert.True(Waited(() => w.Drawer.TabNames[1] == "Recent 1"));
        Assert.Equal(RockRecipe.Id, w.Files.Recipe);
        Assert.Equal(["River stone"], w.Files.History.Steps.Select(x => x.Label));
        Assert.Equal("river-stone", new Prefs(s.P("prefs.json")).Recent[0]);
        w.Close();
    }
}
