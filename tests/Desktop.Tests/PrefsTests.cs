using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class PrefsTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ThePrefsFileRoundTripsAndABrokenOneReadsAsNothing()
    {
        using var s = new Scratch();
        var p = new Prefs(s.P("sub", "prefs.json")) { MapFolder = "/a", SaveFolder = "/b" };
        p.Save();
        var again = new Prefs(s.P("sub", "prefs.json"));
        Assert.Equal(("/a", "/b"), (again.MapFolder, again.SaveFolder));

        File.WriteAllText(s.P("bad.json"), "[1,");
        Assert.Null(new Prefs(s.P("bad.json")).MapFolder);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void PrefsHoldTheTokenSoTheFolderIsPrivateAndTheFileIsOwnerOnly()
    {
        using var s = new Scratch();
        string dir = s.P("cfg", "prop-generator");
        string file = Path.Combine(dir, "prefs.json");
        var p = new Prefs(file) { ClaudeToken = "secret" };
        p.Save();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir));
        p.Save();
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void LoadingTightensAnExistingPrefsFileAndFolder()
    {
        using var s = new Scratch();
        string dir = s.P("old");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "prefs.json");
        File.WriteAllText(file, "{\"claudeToken\":\"secret\"}");
        File.SetUnixFileMode(file, (UnixFileMode)0b110_100_100);
        File.SetUnixFileMode(dir, (UnixFileMode)0b111_101_101);

        var p = new Prefs(file);

        Assert.Equal("secret", p.ClaudeToken);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir));
    }

    [Fact]
    public void TheDrawerIsOpenOnAllWithNoFavouritesOrRecentUntilToldOtherwise()
    {
        using var s = new Scratch();
        var fresh = new Prefs(s.P("none.json"));
        Assert.True(fresh.DrawerOpen);
        Assert.Equal("All", fresh.DrawerTab);
        Assert.Empty(fresh.Favourites);
        Assert.Empty(fresh.Recent);

        var p = new Prefs(s.P("prefs.json"))
        {
            DrawerOpen = false,
            DrawerTab = "Boulders",
            Favourites = ["boulder", "sea-stack"],
            Recent = ["red-mesa", "boulder"],
        };
        p.Save();
        var again = new Prefs(s.P("prefs.json"));
        Assert.False(again.DrawerOpen);
        Assert.Equal("Boulders", again.DrawerTab);
        Assert.Equal(["boulder", "sea-stack"], again.Favourites);
        Assert.Equal(["red-mesa", "boulder"], again.Recent);
    }

    [Fact]
    public void ABrokenDrawerPartReadsAsTheDefaultsAndRecentKeepsTwelve()
    {
        using var s = new Scratch();
        string[] many = [.. Enumerable.Range(0, 20).Select(i => $"r{i}")];
        File.WriteAllText(
            s.P("odd.json"),
            "{\"drawerOpen\":\"yes\",\"drawerTab\":7,\"favourites\":[1,\"boulder\",null,\"\",\"boulder\"],\"recent\":["
                + string.Join(",", many.Select(m => $"\"{m}\"")) + "]}");
        var p = new Prefs(s.P("odd.json"));
        Assert.True(p.DrawerOpen);
        Assert.Equal("All", p.DrawerTab);
        Assert.Equal(["boulder"], p.Favourites);
        Assert.Equal(many.Take(12), p.Recent);

        File.WriteAllText(s.P("bad.json"), "{\"drawerOpen\":false,");
        var bad = new Prefs(s.P("bad.json"));
        Assert.True(bad.DrawerOpen);
        Assert.Empty(bad.Favourites);
    }

    [Fact]
    public void TheViewAndPanelPrefsStartAtEasyPropsDefaults()
    {
        using var s = new Scratch();
        var fresh = new Prefs(s.P("none.json"));
        Assert.Equal("studio-sky", fresh.Scene);
        Assert.Equal("standard", fresh.Budget);
        Assert.Equal(new Commands.Lighting(52, 46, 0.55, 0.8), fresh.Lighting);
        Assert.Equal(320, fresh.PanelWidth);
        Assert.True(fresh.PanelOpen);
        Assert.Equal(["Shape"], fresh.OpenGroups["cut-rock"]);
        Assert.Equal(["Wood", "Carving"], fresh.OpenGroups["floor_tile"]);

        var p = new Prefs(s.P("prefs.json"))
        {
            Scene = "grey",
            Budget = "mobile",
            Lighting = new Commands.Lighting(232, 18, 1, 0.8),
            PanelWidth = 400,
            PanelOpen = false,
            OpenGroups = new Dictionary<string, IReadOnlyList<string>> { ["cut-rock"] = ["Surface"] },
        };
        p.Save();
        var again = new Prefs(s.P("prefs.json"));
        Assert.Equal("grey", again.Scene);
        Assert.Equal("mobile", again.Budget);
        Assert.Equal(new Commands.Lighting(232, 18, 1, 0.8), again.Lighting);
        Assert.Equal(400, again.PanelWidth);
        Assert.False(again.PanelOpen);
        Assert.Equal(["Surface"], again.OpenGroups["cut-rock"]);
        Assert.Equal(["Wood", "Carving"], again.OpenGroups["floor_tile"]);
    }

    [Fact]
    public void OddViewAndPanelPrefsReadAsTheDefaults()
    {
        using var s = new Scratch();
        File.WriteAllText(
            s.P("odd.json"),
            "{\"scene\":\"meadow\",\"budget\":3,\"lighting\":{\"azimuth\":\"x\"},\"panelWidth\":9999,\"panelOpen\":\"no\","
                + "\"openGroups\":{\"cut-rock\":[1,\"Shape\",\"Shape\"],\"floor_tile\":\"Wood\"}}");
        var p = new Prefs(s.P("odd.json"));
        Assert.Equal("studio-sky", p.Scene);
        Assert.Equal("standard", p.Budget);
        Assert.Equal(Commands.Lighting.Default, p.Lighting);
        Assert.Equal(520, p.PanelWidth);
        Assert.True(p.PanelOpen);
        Assert.Equal(["Shape"], p.OpenGroups["cut-rock"]);
        Assert.Equal(["Wood", "Carving"], p.OpenGroups["floor_tile"]);
    }

    // The drawer starts at the bottom, 220 high (300 wide when docked on the left); its side and both sizes are kept.
    [Fact]
    public void TheDrawersSideAndSizesAreKept()
    {
        using var s = new Scratch();
        var fresh = new Prefs(s.P("none.json"));
        Assert.False(fresh.DrawerLeft);
        Assert.Equal(220, fresh.DrawerHeight);
        Assert.Equal(300, fresh.DrawerWidth);

        new Prefs(s.P("prefs.json")) { DrawerLeft = true, DrawerHeight = 300, DrawerWidth = 420 }.Save();
        var again = new Prefs(s.P("prefs.json"));
        Assert.True(again.DrawerLeft);
        Assert.Equal(300, again.DrawerHeight);
        Assert.Equal(420, again.DrawerWidth);
        Assert.Contains("\"drawerSide\": \"left\"", File.ReadAllText(s.P("prefs.json")), StringComparison.Ordinal);
    }

    // Sizes from outside the ranges are brought into them (160 to 360 high, 220 to 560 wide); anything else reads as
    // the default, and a side that is not "left" is the bottom.
    [Theory]
    [InlineData("{\"drawerSide\":\"top\",\"drawerHeight\":9999,\"drawerWidth\":10}", false, 360, 220)]
    [InlineData("{\"drawerSide\":\"left\",\"drawerHeight\":20,\"drawerWidth\":9999}", true, 160, 560)]
    [InlineData("{\"drawerSide\":3,\"drawerHeight\":\"tall\",\"drawerWidth\":null}", false, 220, 300)]
    public void OddDrawerPrefsAreClampedOrReadAsTheDefaults(string json, bool left, double height, double width)
    {
        using var s = new Scratch();
        File.WriteAllText(s.P("odd.json"), json);
        var p = new Prefs(s.P("odd.json"));
        Assert.Equal(left, p.DrawerLeft);
        Assert.Equal(height, p.DrawerHeight);
        Assert.Equal(width, p.DrawerWidth);
    }

    // A sun moved just before the window closes is saved, though its 300 ms wait had not run out.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ALightMovedJustBeforeClosingIsSaved()
    {
        using var s = new Scratch();
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        var moved = new Commands.Lighting(201, 33, 0.3, 0.6);
        w.ViewState.Lighting = moved;
        Assert.NotEqual(moved, new Prefs(s.P("prefs.json")).Lighting);

        w.Close();

        Assert.Equal(moved, new Prefs(s.P("prefs.json")).Lighting);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheViewStateRemembersSceneBudgetLightingAndTheDrawer()
    {
        using var s = new Scratch();
        var prefs = new Prefs(s.P("prefs.json"));
        var view = new Commands.ViewState(prefs);
        int changes = 0;
        view.Changed += () => changes++;
        view.Scene = "grey";
        view.Budget = "hero";
        view.DrawerOpen = false;
        view.Lighting = new Commands.Lighting(165, 68, 0.2, 0.5);
        view.Shading = Commands.ViewState.Flat;
        view.Shading = Commands.ViewState.Flat;
        Assert.Equal(5, changes);
        Ui.Wait(450);

        var again = new Commands.ViewState(new Prefs(s.P("prefs.json")));
        Assert.Equal(("grey", "hero", false), (again.Scene, again.Budget, again.DrawerOpen));
        Assert.Equal(new Commands.Lighting(165, 68, 0.2, 0.5), again.Lighting);
        Assert.Equal(Commands.ViewState.Lit, again.Shading);
        Assert.Throws<ArgumentException>(() => view.Scene = "meadow");
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Lod = Commands.ViewState.FarLevel + 1);
    }

    [Fact]
    public void TheExportLookIsRememberedAndAnUnknownOneIsLit()
    {
        using var s = new Scratch();
        var p = new Prefs(s.P("prefs.json")) { ExportLook = "cel" };
        p.Save();
        Assert.Equal("cel", new Prefs(s.P("prefs.json")).ExportLook);

        File.WriteAllText(s.P("odd.json"), "{\"exportLook\":\"sketchy\"}");
        Assert.Equal("lit", new Prefs(s.P("odd.json")).ExportLook);
        Assert.Equal("lit", new Prefs(s.P("none.json")).ExportLook);
    }
}
