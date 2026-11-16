using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Details;
using PropGenerator.Desktop.Pictures;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Nature.Cliffs;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

/// <summary>The slab cliff in the app: the drawer, the panel, the live view, the detail levels and the export.</summary>
public class CliffRecipeTests
{
    private static IMeshRecipe Cliff => MeshRecipes.Find(CliffParameterSet.Id)!;

    private static StartingPoint LongCliff => Cliff.StartingPoints.Single(p => p.Name == "Long cliff");

    private static StartingPoint TallCliff => Cliff.StartingPoints.Single(p => p.Name == "Tall cliff");

    private static bool Waited(Func<bool> done, int ms = 20000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    private static MainWindow Started(StartingPoint point)
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.True(Run(w.Files.StartFromAsync(point), 30000));
        Ui.Pump();
        return w;
    }

    private static void Same(ColouredMesh expected, ColouredMesh actual)
    {
        Assert.Equal(expected.Positions, actual.Positions);
        Assert.Equal(expected.Normals, actual.Normals);
        Assert.Equal(expected.Colours, actual.Colours);
        Assert.Equal(expected.Indices, actual.Indices);
    }

    private static IEnumerable<double> Shapes(IReadOnlyList<PropGenerator.Nature.Export.CollisionShape> shapes) =>
        shapes.SelectMany(c => new[] { c.Start.X, c.Start.Y, c.Start.Z, c.End.X, c.End.Y, c.End.Z, c.R0, c.R1 });

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheCliffsGroupShowsEasyPropsCliffsThenOurs()
    {
        MainWindow w = Ui.Open(new FakePainter());

        Assert.Contains("Cliffs 7", w.Drawer.TabNames);
        Assert.DoesNotContain(w.Drawer.TabNames, t => t.StartsWith("Cliff blocks", StringComparison.Ordinal));
        w.Drawer.ChooseTab("Cliffs");
        Assert.Equal(
            ["Cliff face", "Sea stack", "Red mesa", "Long cliff", "Tall cliff", "Cliff top", "Layered cliff"],
            w.Drawer.VisibleTileNames);
        Assert.Equal(["long-cliff", "tall-cliff", "cliff-top", "layered-cliff"], Cliff.StartingPoints.Select(p => p.Id));
        Assert.All(Cliff.StartingPoints, p => Assert.Equal(("Cliffs", "slab-cliff"), (p.Group, p.Recipe)));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void StartingACliffShowsItsPanel()
    {
        MainWindow w = Started(LongCliff);

        Assert.Equal(CliffParameterSet.Id, w.Files.Recipe);
        Assert.Equal("Cliff", w.PanelHead.Title.Text);
        Assert.Contains(w.PanelHead.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Whole prop");
        string[] groups = [.. CliffParameterSet.Instance.Controls.Select(c => c.Group!).Distinct()];
        Assert.Equal(
            ["Proportions", "Blocks", "Shape", "Cuts", "Surface", "Cracks", "Mesh", "Colour"],
            groups);
        Assert.Equal(groups, w.Panel.Groups.Select(g => g.Title));
        Assert.Equal(["Proportions", "Blocks"], w.Panel.Groups.Where(g => g.IsOpen).Select(g => g.Title));
        Assert.Equal(CliffParameterSet.Instance.Controls.Count, w.Panel.Rows.Count);
        Assert.Equal(LongCliff.Settings, w.Panel.Current);

        // The cliff itself is built and shown, at full detail, from the preset's own settings.
        Assert.True(w.View.ShowingRock);
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        CliffSettings preset = CliffPresets.All.Single(p => p.Id == "long-cliff").Settings;
        CliffBlock block = CliffGenerator.Build(preset, 1);
        Same(new ColouredMesh(block.Positions, block.Normals, block.Colours, block.Indices), w.Rocks.Mesh!);
        Assert.Contains("Cliff built", w.StatusLine.Text, StringComparison.Ordinal);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheShapeSlidersShowInTheCliffPanel()
    {
        // The shape sliders added for the hand-made pieces sit in the Shape group, and show there as sliders.
        MainWindow w = Started(LongCliff);
        foreach (var (key, label) in new[] { ("taper", "Taper"), ("end-turn", "End turn"), ("foot", "Foot"), ("underside-tilt", "Underside tilt") })
        {
            var group = w.Panel.GroupFor(key)!;
            Assert.Equal("Shape", group.Title);
            group.IsOpen = true;
            Ui.Wait(50);
            Assert.True(Ui.Named<Slider>(w, label).IsVisible, label);
        }

        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DraggingACliffSliderShowsItWhileTheHandMoves()
    {
        MainWindow w = Started(TallCliff);
        var shown = new List<Settings>();
        w.Rocks.Showed += (_, s) => shown.Add(s);
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        Settings preset = w.Panel.Current;
        ColouredMesh before = w.Rocks.Mesh!;
        int changedBeforeLettingGo = 0;
        int moves = 0;
        w.Panel.GroupFor("lean")!.IsOpen = true;
        Ui.Wait(100);

        Ui.Drag(w, Ui.Named<Slider>(w, "Lean"), 10, () =>
        {
            // The hand rests on each of the first moves until a newer block is shown, the button still down.
            moves++;
            int seen = shown.Count;
            if (moves <= 3)
            {
                Waited(() => shown.Count > seen, 5000);
            }

            changedBeforeLettingGo = shown.Count(s => !s.Equals(preset));
        });
        Assert.True(Waited(() => shown.Count > 0 && shown[^1].Equals(w.Panel.Current)), $"{shown.Count} shown; {w.StatusLine.Text}");
        Ui.Wait(300);

        Assert.True(changedBeforeLettingGo >= 2, $"{changedBeforeLettingGo} blocks shown during the drag");
        Assert.NotEqual(preset, w.Panel.Current);
        Assert.NotEqual(before.Positions, w.Rocks.Mesh!.Positions);
        Assert.Equal(w.Panel.Current, w.Rocks.ShownSettings);
        Same(Cliff.Build(w.Panel.Current, 1), w.Rocks.Mesh!);
        Assert.Equal(2, w.Files.History.Steps.Count);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ACliffExportsAGlb()
    {
        var (w, _, _, dialogs, s) = Make();
        string map = s.P("map");
        Directory.CreateDirectory(Path.Combine(map, "models"));
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        dialogs.SavePath = s.P("Big Cliff.propgen");
        dialogs.Folder = map;
        Assert.True(Run(w.Files.StartFromAsync(LongCliff), 30000));
        FileActionsTests.Press(w, PhysicalKey.S);

        _ = w.Files.ExportAsync();
        Assert.True(Waited(() => w.StatusLine.Text?.StartsWith("Exported", StringComparison.Ordinal) == true, 30000), w.StatusLine.Text);

        Assert.Empty(dialogs.Told);
        string glb = Assert.Single(Directory.GetFiles(Path.Combine(map, "models")));
        Assert.Equal("pg_big_cliff_cliff.glb", Path.GetFileName(glb));
        byte[] bytes = File.ReadAllBytes(glb);
        Assert.True(bytes.Length > 1000);
        Assert.Equal("glTF"u8.ToArray(), bytes[..4]);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(map, "catalog.json")));
        JsonElement kind = doc.RootElement.GetProperty("kinds").EnumerateArray().Single();
        Assert.Equal("pg_big_cliff_cliff", kind.GetProperty("model").GetString());
        Assert.Equal("Big Cliff cliff", kind.GetProperty("label").GetString());
        Assert.Equal("Rocks", kind.GetProperty("category").GetString());
        Assert.Contains("Exported Big Cliff cliff", w.StatusLine.Text, StringComparison.Ordinal);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EveryCliffSliderHasATooltip()
    {
        MainWindow w = Started(TallCliff);

        IReadOnlyList<PanelRow> rows = [.. w.Panel.Rows.Cast<PanelRow>()];
        Assert.Equal(CliffParameterSet.Instance.Controls.Count, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.True(ToolTip.GetTip(r.Label) is string { Length: > 0 }, r.Spec.Key);
            Assert.Equal(CliffTooltips.For(r.Spec.Key), ToolTip.GetTip(r.Label));
        });
        var touchable = w.Panel.GetVisualDescendants().OfType<Control>()
            .Where(c => c.TemplatedParent is null && c is Slider or NumericUpDown or ComboBox or TextBox)
            .ToList();
        Assert.True(touchable.Count >= CliffParameterSet.Instance.Controls.Count);
        Assert.All(touchable, c => Assert.True(ToolTip.GetTip(c) is string { Length: >= 40 }, c.GetType().Name));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ACliffHasDetailLevelsStatsShadingAndShapes()
    {
        MainWindow w = Started(LongCliff);
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        Assert.Null(w.ShadingUnavailable);
        int full = Cliff.Build(w.Panel.Current, 1).Indices.Length / 3;
        Assert.Equal(full, w.StatusBar.FullFaces());

        // Level 2: a lighter block is shown, and the status bar still counts the full one.
        Assert.Equal(["lod-0", "lod-1", "lod-2"], w.Commands.Single(c => c.Id == "lod").Children!.Select(c => c.Id));
        w.ViewState.Lod = 2;
        Assert.True(Waited(() => w.Rocks.ShownDetail == Cliff.DetailShares[2] && w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        Assert.Equal(Cliff.Build(w.Panel.Current, Cliff.DetailShares[2]).Indices.Length / 3, w.StatusBar.ShownFaces());
        Assert.True(w.StatusBar.ShownFaces() < full);
        Assert.Equal(full, w.StatusBar.FullFaces());

        // The shapes you can walk into: one per part, as the export chooses them.
        w.Toolbar.Collision.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
        Assert.True(Waited(() => w.View.Prisms is not null));
        IReadOnlyList<PropGenerator.Nature.Export.CollisionShape> shapes = Cliff.Collision(w.Panel.Current);
        Assert.NotEmpty(shapes);
        Assert.Equal(shapes.Count, w.View.Prisms!.Count);
        Assert.False(w.Toolbar.Note.IsVisible);
        w.Close();
    }

    [Fact]
    public void ACliffPresetsPictureIsKeyedByItsRecipe()
    {
        Assert.Equal(CliffExport.Version, Cliff.Version);
        Assert.Equal(6, Cliff.Version);
        Assert.Equal("preset:long-cliff:slab-cliff:v6", ThumbCache.PresetKey("long-cliff", Cliff.Id, Cliff.Version));
        Assert.Null(ShippedThumbs.EngineTag(Cliff.Id));

        // An easy-props kind's own pictures are keyed with the engine bundle's tag and commit too (D6).
        IMeshRecipe rock = MeshRecipes.Find("cut-rock")!;
        string tag = ShippedThumbs.EngineTag(rock.Id)!;
        Assert.Equal($"{EnginePin.Tag}-{EnginePin.Commit}", tag);
        Assert.Equal($"preset:boulder:cut-rock:v{rock.Version}:{tag}", ThumbCache.PresetKey("boulder", rock.Id, rock.Version, tag));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheDrawerDrawsTheCliffPicturesUnderTheirRecipe()
    {
        using var s = new Scratch();
        var thumbs = new ThumbCache(s.P("own-thumbs"));
        var w = new MainWindow(
            new FakePainter(), Ui.Specs(), new FakeDialogs(), new Prefs(s.P("prefs.json")), new Library(s.P("library.json")),
            s.P("bridge"), thumbs);
        w.Show();
        Ui.Pump();

        Assert.True(Waited(() => Cliff.StartingPoints.All(p => thumbs.Get(ThumbCache.PresetKey(p.Id, "slab-cliff", CliffExport.Version)) is not null), 60000));
        IMeshRecipe rock = MeshRecipes.Find("cut-rock")!;
        Assert.Null(thumbs.Get(ThumbCache.PresetKey("long-cliff", rock.Id, rock.Version, ShippedThumbs.EngineTag(rock.Id))));
        Assert.Null(thumbs.Get(ThumbCache.PresetKey("long-cliff", rock.Id, rock.Version)));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SwitchingBetweenARockAndACliffBlockShowsEachOnesMesh()
    {
        MainWindow w = Ui.Open(new FakePainter());
        StartingPoint boulder = RockRecipe.StartingPoints.Single(p => p.Id == "boulder");
        foreach ((StartingPoint point, IMeshRecipe recipe) in new[]
        {
            (boulder, MeshRecipes.Find(RockRecipe.Id)!),
            (LongCliff, Cliff),
            (boulder, MeshRecipes.Find(RockRecipe.Id)!),
        })
        {
            Assert.True(Run(w.Files.StartFromAsync(point), 30000));
            Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
            Assert.Equal(recipe.Id, w.Files.Recipe);
            Assert.Equal(recipe.Label, w.PanelHead.Title.Text);
            Assert.Equal(recipe.Specs, w.Panel.Specs);
            Assert.Equal(point.Settings, w.Panel.Current);
            Same(EngineKit.OnThePool(() => recipe.Build(point.Settings, 1)), w.Rocks.Mesh!);
        }

        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ACliffBlockSavedReopenedAndUndoneKeepsItsSettings()
    {
        var (w, _, _, dialogs, s) = Make();
        dialogs.SavePath = s.P("Crag.propgen");
        Assert.True(Run(w.Files.StartFromAsync(LongCliff), 30000));
        double start = w.Panel.Current.Values["lean"].GetDouble();
        Assert.Null(w.Panel.SetFromOutside("lean", JsonSerializer.SerializeToElement(1.5)));
        Assert.Equal(2, w.Files.History.Steps.Count);
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));

        FileActionsTests.Press(w, PhysicalKey.Z);
        Assert.Equal(start, w.Panel.Current.Values["lean"].GetDouble());
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        FileActionsTests.Press(w, PhysicalKey.Z, shift: true);
        Assert.Equal(1.5, w.Panel.Current.Values["lean"].GetDouble());
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));

        FileActionsTests.Press(w, PhysicalKey.S);
        Assert.False(w.Files.Dirty);
        Settings saved = w.Panel.Current;
        string[] labels = [.. w.Files.History.Steps.Select(x => x.Label)];
        Assert.True(Run(w.Files.NewAsync()));
        Assert.Equal(FloorRecipe.Id, w.Files.Recipe);

        Assert.True(Run(w.Files.OpenPathAsync(s.P("Crag.propgen")), 30000));
        Assert.Equal(CliffParameterSet.Id, w.Files.Recipe);
        Assert.Equal("Cliff", w.PanelHead.Title.Text);
        Assert.Equal(saved, w.Panel.Current);
        Assert.Equal(labels, w.Files.History.Steps.Select(x => x.Label));
        Assert.False(w.Files.Dirty);
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        Same(Cliff.Build(saved, 1), w.Rocks.Mesh!);

        // Undo after reopening walks the saved history, and the block follows.
        FileActionsTests.Press(w, PhysicalKey.Z);
        Assert.Equal(start, w.Panel.Current.Values["lean"].GetDouble());
        Assert.Equal(LongCliff.Settings, w.Panel.Current);
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        Same(Cliff.Build(LongCliff.Settings, 1), w.Rocks.Mesh!);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnOldCliffBlockFileShowsTheUnknownRecipeMessage()
    {
        // The box-built cliff block is gone, and its files are not migrated: one opens as any recipe the app lacks does.
        var (w, _, _, dialogs, s) = Make();
        var h = new History();
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["width"] = JsonSerializer.SerializeToElement(6.24),
            ["slab-turn"] = JsonSerializer.SerializeToElement(12.5),
            ["seed"] = JsonSerializer.SerializeToElement(13),
        };
        h.Push("Cliff 01", new Settings(values));
        WorkFile.Save(s.P("Old crag.propgen"), new Piece("cliff-block", []), new Settings(values), h);
        Settings before = w.Panel.Current;

        Assert.False(Run(w.Files.OpenPathAsync(s.P("Old crag.propgen")), 30000));

        Assert.Equal(["Old crag.propgen is an Unknown kind (cliff-block): this version of the app cannot open it (a newer version made it, or the kind is gone)."], dialogs.Told);
        Assert.Contains("could not be opened", w.StatusLine.Text, StringComparison.Ordinal);
        Assert.Equal(FloorRecipe.Id, w.Files.Recipe);
        Assert.Equal(before, w.Panel.Current);
        Assert.Equal("untitled.propgen — Prop Generator", w.Title);
        w.Close();
    }

    [Fact]
    public void RocksStillWork()
    {
        IMeshRecipe rock = MeshRecipes.Find("cut-rock")!;
        Assert.Equal(["cut-rock", "slab-cliff", "stylized-rock", "grass", "plant", "bush", "cactus", "painted-tree", "branching-tree", "palm-tree", "tree-stump", "tree-log", "tree-branch", "tree-litter"], MeshRecipes.All.Select(r => r.Id));
        Assert.Null(MeshRecipes.Find(FloorRecipe.Id));
        Assert.Null(MeshRecipes.Find(null));
        Assert.Equal(RockRecipe.Label, rock.Label);
        Assert.Same(RockRecipe.Specs, rock.Specs);
        Assert.Same(RockRecipe.StartingPoints, rock.StartingPoints);
        Assert.Equal(RockRecipe.DetailShares, rock.DetailShares);

        Settings boulder = RockRecipe.StartingPoints.Single(p => p.Id == "boulder").Settings;
        Same(RockRecipe.Build(boulder), rock.Build(boulder, 1));
        Same(RockRecipe.Build(boulder, RockRecipe.DetailShares[1]), rock.Build(boulder, rock.DetailShares[1]));
        Assert.Equal(RockRecipe.Glb(boulder, "rock"), rock.Glb(boulder, "rock"));
        Assert.Equal(Shapes(RockRecipe.Collision(boulder)), Shapes(rock.Collision(boulder)));
    }

    [Fact]
    public void TheCliffStartingPointsBuildTheirPresets()
    {
        Assert.Equal(("Cliff", "cliff"), (Cliff.Label, Cliff.ExportName));
        Assert.Equal([1, 0.4, 0.12], Cliff.DetailShares);
        ParamSpec seed = Cliff.Specs[^1];
        Assert.Equal(("seed", "seed"), (seed.Key, seed.Type));
        Assert.Equal(
            [.. CliffParameterSet.Instance.Controls.Select(c => c.Key), "seed"],
            Cliff.Specs.Select(s => s.Key));
        foreach ((string id, string _, CliffSettings settings) in CliffPresets.All)
        {
            StartingPoint p = Cliff.StartingPoints.Single(x => x.Id == id);
            CliffBlock block = CliffGenerator.Build(settings, 1);
            Same(new ColouredMesh(block.Positions, block.Normals, block.Colours, block.Indices), Cliff.Build(p.Settings, 1));
        }

        byte[] glb = Cliff.Glb(LongCliff.Settings, "pg_cliff");
        Assert.Equal("glTF"u8.ToArray(), glb[..4]);
    }
}
