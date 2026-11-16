using System.Numerics;
using System.Text.Json;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using PropGenerator.Nature.Export;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// The switch: easy-props' eight kinds come from its engine (<see cref="EngineRecipe"/>) in the app, saved work opens
/// unchanged, the view's builds are never dropped by pictures or by another window, the UI thread never waits on the
/// engine, part edits and Make it fit still work, and an engine error in Export many leaves out only that version.
/// These tests put a recipe double in place (<see cref="MeshRecipes.Override"/>), so they run alone.
/// </summary>
[Collection(MeshRecipesAlone.Name)]
public class EngineRecipeTests
{
    private static readonly string[] EngineIds = ["cut-rock", "grass", "plant", "bush", "cactus", "painted-tree", "branching-tree", "palm-tree"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IMeshRecipe Bush => MeshRecipes.Find("bush")!;

    private static StartingPoint RoundBush => Bush.StartingPoints.Single(p => p.Id == "round-bush");

    private static bool Waited(Func<bool> done, int ms = 60000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    private static MainWindow Open(StartingPoint point, Scratch? scratch = null)
    {
        MainWindow w = scratch is null ? Ui.Open(new FakePainter()) : Ui.Open(new FakePainter(), new FakeDialogs(), scratch);
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(point)));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true), "the prop was not shown");
        return w;
    }

    // Runs engine work on the pool while the UI thread keeps pumping. Not Task.Run(...).GetAwaiter().GetResult(): a
    // blocking wait may run a task not yet started inline, on the waiting (UI) thread, where the engine refuses it.
    private static T OffTheUiThread<T>(Func<T> work) => ClaudeKit.Run(Task.Run(work, Ct), 120000);

    // The view on the panel's settings, built (not on a lower level's leftover) and with no failure said.
    private static bool Settled(MainWindow w) => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true && w.Rocks.Mesh is not null;

    [Fact]
    public void EveryEngineKindIsRegisteredInDrawerOrder()
    {
        Assert.Equal(
            ["cut-rock", "slab-cliff", "stylized-rock", "grass", "plant", "bush", "cactus", "painted-tree", "branching-tree", "palm-tree", "tree-stump", "tree-log", "tree-branch", "tree-litter"],
            MeshRecipes.All.Select(r => r.Id));
        foreach (string id in EngineIds)
        {
            EngineRecipe recipe = Assert.IsType<EngineRecipe>(MeshRecipes.Find(id));
            Assert.Same(EngineRecipes.For(id), recipe);
            Assert.Same(recipe, EngineRecipes.WithParts(id));
        }

        Assert.Same(CliffRecipe.Instance, MeshRecipes.Find("slab-cliff"));
        Assert.Same(StylizedRockRecipe.Instance, MeshRecipes.Find("stylized-rock"));

        // The convenience statics are the engine's cut rock.
        Assert.Equal(EngineRecipes.For("cut-rock").Specs, RockRecipe.Specs);
    }

    [Fact]
    public void ABuildOffTheUiThreadCountsOnlyWhenAsked()
    {
        IMeshRecipe bush = Bush;
        Settings s = RoundBush.Settings;
        (ColouredMesh light, int? none) = bush.BuildCounted(s, 0.4, countFull: false, BuildPurpose.View, Ct, $"test-{Guid.NewGuid():N}");
        Assert.Null(none);
        (ColouredMesh again, int? faces) = bush.BuildCounted(s, 0.4, countFull: true, BuildPurpose.Count, Ct, $"test-{Guid.NewGuid():N}");
        ColouredMesh full = bush.Build(s, 1, BuildPurpose.View, Ct);
        Assert.Equal(full.Indices.Length / 3, faces);
        Assert.Equal(light.Indices, again.Indices);
        Assert.True(light.Indices.Length < full.Indices.Length);
        Assert.NotEmpty(bush.Collision(s, Ct));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void BuildIsNeverCalledOnTheUiThread()
    {
        EngineRecipe rock = EngineRecipes.For("cut-rock");
        Settings s = rock.StartingPoints[0].Settings;
        long queued = EngineHost.Default.Builds.JobsQueued(EngineJobKind.Live);
        long exports = EngineHost.Default.Builds.JobsQueued(EngineJobKind.Export);
        Assert.Throws<InvalidOperationException>(() => rock.Build(s, 1));
        Assert.Throws<InvalidOperationException>(() => rock.BuildCounted(s, 0.4, countFull: true, BuildPurpose.View, CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => rock.Glb(s, "rock"));
        Assert.Throws<InvalidOperationException>(() => rock.Collision(s));
        Assert.Equal(queued, EngineHost.Default.Builds.JobsQueued(EngineJobKind.Live));
        Assert.Equal(exports, EngineHost.Default.Builds.JobsQueued(EngineJobKind.Export));

        // Off the UI thread the same calls run.
        Assert.NotEmpty(OffTheUiThread(() => rock.Build(s, 1).Indices));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void PicturesDrawingWhileTheViewBuildsLeaveTheViewBuilt()
    {
        MainWindow w = Open(RoundBush);
        IMeshRecipe bush = Bush;

        // Pictures on the Thumbnail turn (as the drawer draws a user preset's), all at once on the pool, and a Show me
        // more page on its own turn (a card of its own: the window's closes once the panel is touched).
        Task[] pictures = [.. bush.StartingPoints.Take(6).Select(p => Task.Run(() => bush.Build(p.Settings, bush.PictureDetail, BuildPurpose.Picture, Ct), Ct))];
        using var more = new Cards.ShowMoreCard(bush, RoundBush.Settings, "bush", _ => { }, _ => { }, () => { });
        Ui.Pump();

        Ui.Drag(w, w.Panel.NumberRowFor("width")!.Slider, 10, () => Thread.Sleep(15));
        Assert.True(Waited(() => Settled(w)), "the view did not end on the last settings");
        Assert.True(Waited(() => more.Drawing.IsCompleted, 120000));
        Assert.True(Waited(() => pictures.All(t => t.IsCompleted), 120000));
        Assert.All(pictures, t => Assert.True(t.IsCompletedSuccessfully, t.Exception?.Message));
        Assert.True(Waited(() => Settled(w)));
        Assert.Null(w.LastError);
        Assert.Equal(9, more.Drawn);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TwoWindowsDraggingEndOnTheirOwnSettings()
    {
        MainWindow a = Open(RoundBush);
        MainWindow b = Open(RoundBush);
        Assert.NotEqual(a.Rocks.BuildKey, b.Rocks.BuildKey);
        double[] widths = [1.1, 1.3, 1.5, 1.7, 1.9, 2.1, 2.3, 2.5, 2.7, 2.9];
        int tick = 0;

        // One window dragged by the pointer while the other's width changes at every move.
        Ui.Drag(a, a.Panel.NumberRowFor("width")!.Slider, 10, () =>
        {
            _ = b.Panel.SetFromOutside("width", JsonSerializer.SerializeToElement(widths[tick++ % widths.Length]));
            Thread.Sleep(10);
        });

        Assert.True(Waited(() => Settled(a) && Settled(b)), "a view did not end on its last settings");
        Assert.NotEqual(a.Panel.Current, b.Panel.Current);
        Assert.Equal(widths[(tick - 1) % widths.Length], b.Panel.Current.GetFloat("width"), 4);
        Assert.Null(a.LastError);
        Assert.Null(b.LastError);
        a.Close();
        b.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void PartEditsStillWork()
    {
        MainWindow w = Open(RoundBush);
        const string Leaves = "cmp-bush-leaves";
        float[] before = w.Rocks.Mesh!.Positions;

        w.Editing.Select(Leaves);
        w.Editing.MoveBy(new Vector3(0.5f, 0, 0));
        Assert.True(Waited(() => Settled(w)));
        float[] moved = w.Rocks.Mesh!.Positions;
        Assert.False(before.AsSpan().SequenceEqual(moved), "the engine's mesh did not follow the move");
        Assert.Contains(w.Rocks.Mesh!.Parts!, p => p.Id == Leaves);

        w.Files.Undo();
        Assert.True(Waited(() => Settled(w) && w.Rocks.Mesh!.Positions.AsSpan().SequenceEqual(before)), "undo did not bring the bush back");
        w.Files.Redo();
        Assert.True(Waited(() => Settled(w) && w.Rocks.Mesh!.Positions.AsSpan().SequenceEqual(moved)), "redo did not move the leaves again");
        Assert.Null(w.LastError);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MakeItFitIsOfferedOnAnOverBudgetEngineBush()
    {
        IMeshRecipe bush = Bush;
        StartingPoint heavy = OffTheUiThread(() => bush.StartingPoints.MaxBy(p => bush.Build(p.Settings, 1).Indices.Length)!);
        MainWindow w = Open(heavy);
        w.ViewState.Budget = "mobile";
        Assert.True(Waited(() => w.Rocks.FullDetailFaces > 2000));
        Assert.Null(w.Versions.WhyNotFit());
        Assert.Null(CommandListTests.Find(w, "make-it-fit").Unavailable());
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ListPartsAnswersForAnEngineKind()
    {
        var (w, tools, _, _, _) = ClaudeKit.Make();
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(RoundBush)));
        Assert.IsType<EngineRecipe>(MeshRecipes.Find(w.Files.Recipe));
        string text = ClaudeKit.Text(ClaudeKit.Run(tools.ListParts()));
        using var parts = JsonDocument.Parse(text);
        Assert.Contains(parts.RootElement.EnumerateArray(), p => p.GetProperty("id").GetString() == "cmp-bush-leaves");
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportManySkipsAFailedVersion()
    {
        using var s = new Scratch();
        MainWindow w = Open(RoundBush, s);
        var items = ClaudeKit.Run(w.Versions.ItemsAsync(null, 3, TestContext.Current.CancellationToken), 60000);
        uint bad = items[1].Version.Seed;
        using IDisposable _ = MeshRecipes.Override(new FailingGlb(Bush, bad));

        string map = s.P("map");
        Directory.CreateDirectory(map);
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        w.Files.Prefs.MapFolder = map;
        int exported = ClaudeKit.Run(w.Versions.ExportAsync(null, 3), 120000);

        Assert.True(exported == 2, string.Join(" | ", w.RecentMessages));
        string[] glbs = [.. Directory.GetFiles(map, "*.glb", SearchOption.AllDirectories).Select(Path.GetFileName)!];
        Assert.Equal(["pg_prop_01_bush.glb", "pg_prop_03_bush.glb"], glbs.Order(StringComparer.Ordinal));
        string last = w.RecentMessages.Last();
        Assert.StartsWith("Exported 2 versions", last, StringComparison.Ordinal);
        Assert.Contains("prop_02 (the engine failed on this seed)", last, StringComparison.Ordinal);
        w.Close();
    }

    [Fact]
    public void ThePoolAddsThreadsAtOnceForTheEnginesWaits()
    {
        // The recipes block pool threads on engine jobs; a starved pool let V8's tasks pile up past 4 GB (see MinPoolThreads).
        _ = EngineRecipes.For("bush");
        ThreadPool.GetMinThreads(out int workers, out _);
        Assert.True(workers >= EngineRecipes.MinPoolThreads, $"the pool's minimum is {workers}");
    }

    [Fact]
    public void TheProblemReportNamesTheEngineAndWhyItFailed()
    {
        Assert.Equal([$"easy-props engine: {EnginePin.Tag} ({EnginePin.Commit})"], ProblemReport.EngineLines(null));
        IReadOnlyList<string> failed = ProblemReport.EngineLines(new EngineScriptException("no bundle"));
        Assert.Equal(2, failed.Count);
        Assert.Equal("easy-props engine failed to load, only slab cliffs and stylized rocks are offered: EngineScriptException: no bundle", failed[1]);
    }

    // The engine's bush, but its export fails in the engine for one seed.
    private sealed class FailingGlb(IMeshRecipe inner, uint bad) : IMeshRecipe
    {
        public string Id => inner.Id;

        public string Label => inner.Label;

        public string ExportName => inner.ExportName;

        public int Version => inner.Version;

        public IReadOnlyList<ParamSpec> Specs => inner.Specs;

        public IReadOnlyList<StartingPoint> StartingPoints => inner.StartingPoints;

        public IReadOnlyList<double> DetailShares => inner.DetailShares;

        public WindSway? Wind => inner.Wind;

        public IReadOnlyList<OwnColourGroup> OwnColourGroups => inner.OwnColourGroups;

        public IReadOnlyCollection<string> HiddenKeys(Settings s) => inner.HiddenKeys(s);

        public IReadOnlyList<ShownRow>? ShownRows(Settings s) => inner.ShownRows(s);

        public bool FarAway(Settings s) => inner.FarAway(s);

        public Settings Adjust(Settings before, Settings after) => inner.Adjust(before, after);

        public ColouredMesh Build(Settings s, double detail, BuildPurpose purpose = BuildPurpose.View, CancellationToken cancel = default, string? replaces = null) =>
            inner.Build(s, detail, purpose, cancel, replaces);

        public (ColouredMesh Mesh, int? FullFaces) BuildCounted(Settings s, double detail, bool countFull, BuildPurpose purpose, CancellationToken cancel, string? replaces = null) =>
            inner.BuildCounted(s, detail, countFull, purpose, cancel, replaces);

        public byte[] Glb(Settings s, string modelName, ExportOptions? options = null, CancellationToken cancel = default) =>
            s.GetSeed("seed") == bad ? throw new EngineScriptException("the engine failed on this seed") : inner.Glb(s, modelName, options, cancel);

        public IReadOnlyList<CollisionShape> Collision(Settings s, CancellationToken cancel = default, string? replaces = null) =>
            inner.Collision(s, cancel, replaces);
    }
}

/// <summary>
/// A tree's pieces as kinds (Task 8, E4, E12): a stump, log, fallen branch or leaf litter made from a tree opens, draws
/// its panel and builds as a prop of its own, carrying its tree in values the panel does not draw, through undo, Show me
/// more and the rule caches.
/// </summary>
public class PieceKindTests
{
    private static readonly string[] Kinds = ["stump", "log", "branch", "litter"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool Waited(Func<bool> done, int ms = 60000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    // A piece of a starting point's tree, made on the engine as the Pieces card makes it (one kind a job), off the UI thread.
    private static EnginePiece Make(string treeId, string kind)
    {
        string scene = EngineKit.Recipe("painted-tree").SceneJson(YourPropsTests.Point(treeId).Settings);
        return EngineKit.OnThePool(() => EngineHost.Default.Builds.MakePieceAsync(scene, treeId, kind, "low-poly", Ct).GetAwaiter().GetResult());
    }

    // The piece's settings, as a prop of its kind.
    private static Settings SettingsOf(EnginePiece piece) => EngineKit.Recipe("tree-" + piece.Kind).SettingsOf(piece.Scene);

    private static string SaveFile(Scratch s, string name, string recipe, Settings settings)
    {
        string path = s.P(name + ".propgen");
        var history = new History();
        history.Push("Made", settings);
        WorkFile.Save(path, new Piece(recipe, []), settings, history);
        return path;
    }

    private static MainWindow OpenFile(Scratch s, string path, MainWindow? w = null)
    {
        w ??= Ui.Open(new FakePainter(), new FakeDialogs(), s);
        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)), path);
        return w;
    }

    private static EngineMesh Built(string sceneJson) =>
        EngineKit.OnThePool(() => EngineHost.Default.Builds.BuildAsync(sceneJson, "low-poly", 1, EngineJobKind.Live, replaces: $"test-{Guid.NewGuid():N}", cancel: Ct).GetAwaiter().GetResult());

    // What the piece looks like: where its vertices are, their colours and the pictures its parts print.
    private static (float[] Positions, float[] Colours, string Maps) Look(EngineMesh mesh) =>
        (EngineKit.PlacedZUp(mesh), [.. mesh.Parts.Where(p => !p.Proxy).SelectMany(p => p.Colors ?? [])], string.Join(' ', mesh.Parts.Select(p => p.Surface.Map)));

    private static bool Same(float[] a, float[] b) => a.Length == b.Length && a.Zip(b).All(p => Math.Abs(p.First - p.Second) <= 1e-5);

    // "Opens and builds" alone would pass with the tree dropped: the opened piece is the made one, and not the same kind
    // made from upstream's default tree.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void APieceFileOpensWithItsOwnTree()
    {
        using var s = new Scratch();
        MainWindow? w = null;
        foreach (string tree in (string[])["oak", "pear"])
        {
            foreach (string kind in Kinds)
            {
                EnginePiece piece = Make(tree, kind);
                string recipe = "tree-" + kind;
                Assert.Equal(recipe, System.Text.Json.Nodes.JsonNode.Parse(piece.Scene)!["generatorId"]!.GetValue<string>());
                Settings made = SettingsOf(piece);
                w = OpenFile(s, SaveFile(s, $"{tree} {kind}", recipe, made), w);
                Assert.Equal(recipe, w.Files.Recipe);
                Settings opened = w.Panel.Current;
                Assert.Equal(made, opened);
                Assert.Equal(tree, opened.GetString("treeName"));

                EngineRecipe engine = EngineKit.Recipe(recipe);
                (float[] want, float[] colours, string maps) = Look(Built(piece.Scene));
                (float[] got, float[] gotColours, string gotMaps) = Look(Built(engine.SceneJson(opened)));
                Assert.True(want.Length > 0, $"{tree} {kind}: nothing built");
                EngineKit.Close(want, got);
                EngineKit.Close(colours, gotColours);
                Assert.Equal(maps, gotMaps);

                // The litter lies as the default tree's would, so its leaves (their colours and picture) tell the trees apart.
                (float[] plain, float[] plainColours, string plainMaps) = Look(Built(engine.SceneJson(opened.Without("tree").Without("treeSeed"))));
                Assert.False(Same(want, plain) && Same(colours, plainColours) && maps == plainMaps, $"{tree} {kind}: the same as the default tree's");
            }
        }

        Assert.Null(w!.LastError);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void CarriedKeysSurviveShowMeMoreUndoAndMakeItFit()
    {
        using var s = new Scratch();
        Settings made = SettingsOf(Make("pear", "litter"));
        string tree = made.GetString("tree");
        MainWindow w = OpenFile(s, SaveFile(s, "pear litter", "tree-litter", made));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true), "the piece was not shown");

        void Carried(string when)
        {
            Settings now = w.Panel.Current;
            foreach (string key in EngineKinds.PieceCarriedKeys)
            {
                Assert.True(now.Values.TryGetValue(key, out System.Text.Json.JsonElement v) && v.ToString() == made.Values[key].ToString(), $"{key} lost {when}");
            }
        }

        // Show me more: another seed of the same tree's litter.
        CommandListTests.Run(w, "show-more");
        Cards.ShowMoreCard card = Assert.IsType<Cards.ShowMoreCard>(w.Cards.Shown);
        uint other = card.Seeds[0];
        card.Tiles[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Ui.Pump();
        Assert.Equal(other, w.Panel.Current.GetSeed("seed"));
        Carried("taking a version from Show me more");

        // A change, undone and redone.
        w.Files.ChangeAll(w.Panel.Current.With("width", 4.0), "Width 4");
        Carried("after a change");
        w.Files.Undo();
        Assert.Equal(other, w.Panel.Current.GetSeed("seed"));
        Carried("after undo");
        w.Files.Redo();
        Assert.Equal(4.0, w.Panel.Current.Values["width"].GetDouble());
        Carried("after redo");
        w.Files.Undo();
        w.Files.Undo();
        Carried("after undoing back to the file");

        // Make it fit: upstream's pieces have no controls to lower (budgetAdvice only), so it is not offered, and the fit
        // scene still carries the tree.
        Assert.Equal("This kind of prop has to be lowered by hand", CommandListTests.Find(w, "make-it-fit").Unavailable());
        EngineRecipe litter = EngineKit.Recipe("tree-litter");
        Assert.Empty(litter.FitControls(w.Panel.Current));
        Assert.Equal(tree, System.Text.Json.Nodes.JsonNode.Parse(litter.SceneJson(w.Panel.Current))!["parameters"]!["tree"]!.GetValue<string>());
        EngineFit fit = EngineKit.OnThePool(() => litter.MakeItFitAsync(w.Panel.Current, 1, Timeout.InfiniteTimeSpan, Ct).GetAwaiter().GetResult());
        Assert.Empty(fit.Changes);
        Assert.Null(w.LastError);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void CarriedKeysAreNotDrawn()
    {
        using var s = new Scratch();
        Settings made = SettingsOf(Make("oak", "stump"));
        Assert.True(made.GetString("tree").Length > 200, $"the stump carries no tree: {made.GetString("tree")}");
        MainWindow w = OpenFile(s, SaveFile(s, "oak stump", "tree-stump", made));
        foreach (string key in EngineKinds.PieceCarriedKeys)
        {
            Assert.Null(w.Panel.RowFor(key));
            Assert.DoesNotContain(w.Panel.Specs, spec => spec.Key == key);
            Assert.Equal(made.Values[key].ToString(), w.Panel.Current.Values[key].ToString());
        }

        // Every value the engine reads from the settings: the panel's rows and the carried ones, nothing else.
        using var values = System.Text.Json.JsonDocument.Parse(EngineKit.Recipe("tree-stump").ValuesJson(w.Panel.Current));
        string[] keys = [.. values.RootElement.EnumerateObject().Select(p => p.Name)];
        Assert.Equal(["tree", "treeSeed", "treeName"], keys.TakeLast(3));
        Assert.All(keys.SkipLast(3), k => Assert.NotNull(w.Panel.RowFor(k)));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnOversizedTreeTextOpensWithANote()
    {
        using var s = new Scratch();
        Settings made = SettingsOf(Make("oak", "stump"));
        Settings huge = made.With("tree", "{\"height\": 9" + new string(' ', EngineKinds.MaxTreeChars) + "}");

        // Never sent: the engine makes the piece from its default tree.
        EngineRecipe stump = EngineKit.Recipe("tree-stump");
        using (var values = System.Text.Json.JsonDocument.Parse(stump.ValuesJson(huge)))
        {
            Assert.False(values.RootElement.TryGetProperty("tree", out _));
        }

        Assert.Contains("more than any tree's (524288)", stump.EditsProblem(huge), StringComparison.Ordinal);
        Assert.Null(stump.EditsProblem(made));

        MainWindow w = OpenFile(s, SaveFile(s, "huge stump", "tree-stump", huge));
        Assert.Equal("tree-stump", w.Files.Recipe);
        Assert.False(w.Panel.Current.Values.ContainsKey("tree"));
        Assert.Equal(made.GetString("treeName"), w.Panel.Current.GetString("treeName"));
        Assert.Contains(w.RecentMessages, m => m.StartsWith("Opened huge stump.propgen", StringComparison.Ordinal) && m.Contains("more than any tree's (524288)", StringComparison.Ordinal));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true && w.Rocks.Mesh is not null), "the stump was not built");
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ACachedPieceRuleKeyIsAHash()
    {
        var data = new EngineKindData(EngineKinds.Find("tree-stump")!, EngineHost.Default.Catalog, EngineHost.Default.Rules);
        Settings oak = SettingsOf(Make("oak", "stump"));
        Settings pear = SettingsOf(Make("pear", "stump"));
        Assert.True(oak.GetString("tree").Length > 200);
        _ = data.HiddenKeys(oak);
        _ = data.FarAway(oak);
        _ = data.HiddenKeys(pear);
        IReadOnlyList<string> keys = data.CachedKeys;
        Assert.Equal(3, keys.Count);
        Assert.Equal(3, keys.Distinct().Count());
        Assert.All(keys, k => Assert.True(k.Length <= 80, $"a rule key of {k.Length} characters"));

        // The same values hit the cache: no new key.
        _ = data.HiddenKeys(oak);
        Assert.Equal(3, data.Cached);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void APiecesPanelShowsItsControls()
    {
        using var s = new Scratch();
        MainWindow? w = null;
        (string Kind, string[] Keys, string[] Labels)[] panels =
        [
            ("stump", ["height", "snow", "moss", "mossFrom", "wet"], ["Height", "Snow", "Moss", "Moss grows", "Wet"]),
            ("litter", ["width", "depth", "amount", "dryness", "snow", "wet"], ["Width", "Depth", "Leaves", "Dryness", "Snow", "Wet"]),
        ];
        foreach ((string kind, string[] keys, string[] labels) in panels)
        {
            string recipe = "tree-" + kind;
            IReadOnlyList<ParamSpec> specs = MeshRecipes.Find(recipe)!.Specs;
            Assert.Equal([.. keys, "seed"], specs.Select(p => p.Key));
            Assert.Equal(labels, specs.SkipLast(1).Select(p => p.Label));

            w = OpenFile(s, SaveFile(s, "oak " + kind, recipe, SettingsOf(Make("oak", kind))), w);
            Assert.Equal(MeshRecipes.Find(recipe)!.Label, recipe == "tree-stump" ? "Stump" : "Leaf litter");
            bool moss = w.Panel.Current.Values.TryGetValue("moss", out System.Text.Json.JsonElement m) && m.GetDouble() > 0;
            foreach (string key in keys)
            {
                Assert.True(w.Panel.RowFor(key) is { } row && row.IsVisible == (key != "mossFrom" || moss), $"{kind}: {key}");
            }
        }

        w!.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void LitterTexturesFollowTheTreesLeafStyle()
    {
        foreach ((string tree, string map) in ((string, string)[])[("cherry-blossom", "dab"), ("pear", "tree-leaf:round"), ("oak", "tree-leaf:lobed")])
        {
            Assert.Equal(map == "dab" ? "painted" : map["tree-leaf:".Length..], YourPropsTests.Point(tree).Settings.GetString("leafStyle"));
            Settings litter = SettingsOf(Make(tree, "litter"));
            EngineRecipe recipe = EngineKit.Recipe("tree-litter");
            EngineMesh parts = EngineKit.Parts(recipe, litter, 1);
            Assert.Equal([map], parts.Parts.Where(p => !p.Proxy).Select(p => p.Surface.Map).Distinct());

            // The view prints the leaves with that picture.
            ColouredMesh mesh = EngineKit.OnThePool(() => recipe.Build(litter, 1, BuildPurpose.View, Ct));
            Assert.NotNull(mesh.Texture);
            Assert.Equal(EngineKit.Texture(map).Rgba, mesh.Texture!.Rgba);
        }
    }
}
