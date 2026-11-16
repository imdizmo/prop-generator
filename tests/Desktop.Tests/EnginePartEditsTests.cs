using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Scenes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using PropGenerator.Nature.Kernel;
using Xunit;
using SceneTransform = PropGenerator.Desktop.Scenes.Transform;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// Part edits on upstream's scene operations (D1, part 1): every part edit is easy-props' own scene op run on the
/// engine, the scene is the engine's components with our part edits folded in, and Break on a tree's heavy limbs.
/// </summary>
public class EnginePartEditsTests
{
    private const string Style = "low-poly";
    private const string Leaves = "cmp-bush-leaves";
    private const string Core = "cmp-bush-core";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StartingPoint Point(string id) => YourPropsTests.Point(id);

    private static bool Waited(Func<bool> done, int ms = 30000)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    private static MainWindow Start(string pointId, FakeDialogs? dialogs = null, Scratch? scratch = null)
    {
        MainWindow w = scratch is null ? Ui.Open(new FakePainter()) : Ui.Open(new FakePainter(), dialogs ?? new FakeDialogs(), scratch);
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(Point(pointId))));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        return w;
    }

    private static (double X, double Y, double Z) Xyz(Vec3d v) => (v.X, v.Y, v.Z);

    private static string Edits(Settings s) =>
        s.Values.TryGetValue(SettingsPanel.TreeEditsKey, out JsonElement e) ? e.GetString()! : string.Empty;

    // The components as JSON, each read and written the one way, so two scenes compare field by field.
    private static string Normal(IEnumerable<SceneComponent> components) =>
        new JsonArray([.. components.Select(c => (JsonNode)ComponentJson.Write(c))]).ToJsonString();

    private static string Normal(JsonArray components) => Normal(components.Select(c => ComponentJson.Read(c!)));

    // The first of oak's limbs upstream says can break now.
    private static SceneComponent HeavyLimb(EngineRecipe oak, Settings s) =>
        oak.SceneOf(s).Components.First(c => c.GeneratorId == "painted-limb" && oak.Facts(s, c.Id).CanBreak);

    // The scene upstream's own op makes of the scene the app sends.
    private static JsonNode? Upstreams(EngineRecipe recipe, Settings s, SceneOp op)
    {
        string? after = EngineHost.Default.Rules.SceneOp(recipe.OpSceneJson(s), op.ToJson().ToJsonString(), Style, Ct);
        return after is null ? null : JsonNode.Parse(after);
    }

    [Fact]
    public void EveryOpEqualsUpstreams()
    {
        EngineRecipe bush = EngineKit.Recipe("bush");
        Settings b = Point("round-bush").Settings;
        EngineRecipe oak = EngineKit.Recipe("painted-tree");
        Settings o = Point("oak").Settings;
        Scene oakScene = oak.SceneOf(o);
        SceneComponent clump = oakScene.Components.First(c => c.GeneratorId == "painted-clump");
        SceneComponent branch = oakScene.Components.First(c => c.GeneratorId == "painted-limb" && c.Id != "cmp-limb-t");
        SceneComponent limb = HeavyLimb(oak, o);
        var turned = new SceneTransform(new Vec3d(0.1, 0.2, 0.3), new Vec3d(0, Math.PI / 4, 0), new Vec3d(1.5, 1.5, 1.5));

        (EngineRecipe Recipe, Settings Settings, SceneOp Op)[] ops =
        [
            (bush, b, new SceneOp.Copy(Leaves)),
            (bush, b, new SceneOp.Remove(Leaves)),
            (bush, b, new SceneOp.Restore(Leaves)),
            (bush, b, new SceneOp.Position(Leaves, new Vec3d(0.5, 0.25, -1))),
            (bush, b, new SceneOp.Transform(Core, turned)),
            (bush, b, new SceneOp.Parameters(Core, new Dictionary<string, object>(StringComparer.Ordinal) { ["seed"] = 7.0 })),
            (bush, b, new SceneOp.Break(Leaves)),
            (oak, o, new SceneOp.Position(clump.Id, new Vec3d(clump.Transform.Position.X + 0.5, clump.Transform.Position.Y, clump.Transform.Position.Z))),
            (oak, o, new SceneOp.Part(branch.Id, "patch", new Dictionary<string, object>(StringComparer.Ordinal) { ["sizeScale"] = 1.5 })),
            (oak, o, new SceneOp.Part(clump.Id, "remove")),
            (oak, o, new SceneOp.Remove(clump.Id)),
            (oak, o, new SceneOp.Restore(branch.Id)),
            (oak, o, new SceneOp.Break(limb.Id)),
            (oak, o, new SceneOp.Copy(clump.Id)),
        ];
        foreach ((EngineRecipe recipe, Settings s, SceneOp op) in ops)
        {
            JsonNode? want = Upstreams(recipe, s, op);
            Scene? got = recipe.Apply(s, op);
            string what = $"{recipe.Id} {op}";
            if (want is null)
            {
                Assert.Null(got);
                continue;
            }

            Assert.True(got is not null, what);
            Assert.Equal(Normal(want["components"]!.AsArray()), Normal(got.Components));
            Assert.Equal(want["seed"]!.GetValue<double>(), got.Seed);
            Assert.Equal(want["parameters"]?["edits"]?.GetValue<string>(), got.Parameters.TryGetValue("edits", out object? e) ? e as string : null);
        }

        // A painted part cannot be copied, and a part that is not a heavy limb does not break: upstream leaves the
        // prop as it was, and so does the app.
        Assert.Null(oak.Apply(o, new SceneOp.Copy(clump.Id)));
        Assert.Null(bush.Apply(b, new SceneOp.Break(Leaves)));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ABrokenLimbSurvivesTheNextEdit()
    {
        MainWindow w = Start("oak");
        var oak = (EngineRecipe)MeshRecipes.Find(w.Files.Recipe)!;
        SceneComponent limb = HeavyLimb(oak, w.Panel.Current);
        string key = (string)limb.Parameters["key"];
        w.Editing.Select(limb.Id);
        CommandListTests.Run(w, "break-part");
        Assert.True(JsonNode.Parse(Edits(w.Panel.Current))!["limbs"]![key]!["broken"]!.GetValue<bool>());

        // Another limb resized, then a clump moved: the break stays in the tree's edits.
        SceneComponent other = w.Editing.Scene!.Components.First(c => c.GeneratorId == "painted-limb" && c.Id != limb.Id && c.Id != "cmp-limb-t");
        w.Editing.Select(other.Id);
        w.Editing.ResizeBy(1.5);
        SceneComponent clump = w.Editing.Scene!.Components.First(c => c.GeneratorId == "painted-clump");
        w.Editing.Select(clump.Id);
        w.Editing.MoveBy(new Vector3(0.25f, 0, 0));
        Assert.Equal(["Break branch", "Resize part", "Move part"], w.Files.History.Steps.TakeLast(3).Select(s => s.Label));
        Assert.True(JsonNode.Parse(Edits(w.Panel.Current))!["limbs"]![key]!["broken"]!.GetValue<bool>());
        Assert.False(oak.Facts(w.Panel.Current, limb.Id).CanBreak);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void BreakIsOfferedOnlyOnHeavyLimbs()
    {
        MainWindow w = Start("oak");
        var oak = (EngineRecipe)MeshRecipes.Find(w.Files.Recipe)!;
        Command breaks = CommandListTests.Find(w, "break-part");
        Assert.Equal("Select a part first", breaks.Unavailable());
        Assert.False(w.PanelHead.Break.IsVisible);

        w.Editing.Select(w.Editing.Scene!.Components.First(c => c.GeneratorId == "painted-clump").Id);
        Assert.Equal("Only a tree's heavy limbs break", breaks.Unavailable());
        Assert.False(w.PanelHead.Break.IsVisible);
        w.Editing.Select("cmp-limb-t");
        Assert.Equal("Only a tree's heavy limbs break", breaks.Unavailable());

        SceneComponent limb = HeavyLimb(oak, w.Panel.Current);
        w.Editing.Select(limb.Id);
        Assert.Null(breaks.Unavailable());
        Assert.True(w.PanelHead.Break.IsVisible);

        // Once broken, it is not offered again.
        CommandListTests.Run(w, "break-part");
        Assert.Equal("This limb is broken already", breaks.Unavailable());
        Assert.False(w.PanelHead.Break.IsVisible);

        w.Close();

        // A bush has no limbs; a floor tile no parts.
        MainWindow bush = Start("round-bush");
        bush.Editing.Select(Leaves);
        Assert.Equal("Only a tree's heavy limbs break", CommandListTests.Find(bush, "break-part").Unavailable());
        bush.Close();

        MainWindow floor = Ui.Open(new FakePainter());
        Assert.Equal("This kind of piece has no parts to pick", CommandListTests.Find(floor, "break-part").Unavailable());
        floor.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void BreakIsOneUndoStep()
    {
        MainWindow w = Start("oak");
        var oak = (EngineRecipe)MeshRecipes.Find(w.Files.Recipe)!;
        Settings before = w.Panel.Current;
        int steps = w.Files.History.Steps.Count;
        SceneComponent limb = HeavyLimb(oak, before);
        w.Editing.Select(limb.Id);

        w.PanelHead.Break.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.True(Waited(() => w.Files.History.Steps.Count == steps + 1));
        Assert.Equal("Break branch", w.Files.History.Steps[^1].Label);
        Assert.Contains("Broke the branch", w.RecentMessages);
        Assert.Contains("\"broken\"", Edits(w.Panel.Current), StringComparison.Ordinal);
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));

        w.Files.Undo();
        Assert.Equal(before, w.Panel.Current);
        Assert.True(oak.Facts(w.Panel.Current, limb.Id).CanBreak);
        w.Files.Redo();
        Assert.False(oak.Facts(w.Panel.Current, limb.Id).CanBreak);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData("oak")]
    [InlineData("round-bush")]
    [InlineData("boulder")]
    public void PickedPartIdsAreInTheScene(string pointId)
    {
        MainWindow w = Start(pointId);
        var recipe = (EngineRecipe)MeshRecipes.Find(w.Files.Recipe)!;
        Settings s = w.Panel.Current;

        // The scene is the engine's own components, in its order.
        using (var engine = JsonDocument.Parse(EngineHost.Default.Rules.Components(recipe.Id, recipe.ValuesJson(s), s.GetSeed("seed"))))
        {
            Assert.Equal(engine.RootElement.EnumerateArray().Select(c => c.GetProperty("id").GetString()), w.Editing.Scene!.Components.Select(c => c.Id));
        }

        // Every part the view draws or picks by is one of them.
        HashSet<string> components = [.. w.Editing.Scene!.Components.Select(c => c.Id)];
        ColouredMesh mesh = w.Rocks.Mesh!;
        Assert.All(mesh.Parts!, p => Assert.Contains(p.Id, components));
        Assert.All(mesh.Proxies?.Parts ?? [], p => Assert.Contains(p.Id, components));

        // A click on the nearest drawn triangle picks a part of the scene.
        Vector3 eye = w.View.Orbit.Eye;
        Vector3 Corner(int i) => new(mesh.Positions[i * 3], mesh.Positions[(i * 3) + 1], mesh.Positions[(i * 3) + 2]);
        Vector3 nearest = Enumerable.Range(0, mesh.Indices.Length / 3)
            .Select(t => (Corner(mesh.Indices[t * 3]) + Corner(mesh.Indices[(t * 3) + 1]) + Corner(mesh.Indices[(t * 3) + 2])) / 3)
            .MinBy(p => Vector3.Distance(p, eye));
        w.Editing.Pick(w.View.ScreenAt(nearest)!.Value);
        Assert.NotNull(w.Editing.Selected);
        Assert.Contains(w.Editing.Selected!, components);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData("round-bush")]
    [InlineData("boulder")]
    public void AFileEditedOnThePortOpensWithTheSameParts(string pointId)
    {
        using var s = new Scratch();
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);

        // As the 0.4.2 port edited and saved it (a committed fixture): one part copied (the copy moved), another moved,
        // then the first part's own slot removed. The fixture keeps the port's scene after the edits.
        using JsonDocument expect = PortFixtures.Json($"{pointId}-edited.expect.json");
        string recipe = expect.RootElement.GetProperty("recipe").GetString()!;
        SceneComponent[] edited = [.. expect.RootElement.GetProperty("edited").EnumerateArray().Select(c => ComponentJson.Read(JsonNode.Parse(c.GetRawText())!))];
        string copy = expect.RootElement.GetProperty("copy").GetString()!;
        string[] generatedIds = [.. expect.RootElement.GetProperty("generated").EnumerateArray().Select(e => e.GetString()!)];
        string path = PortFixtures.Copy($"{pointId}-edited.propgen", s.P($"{pointId}.propgen"));
        Settings saved = WorkFile.Load(path).Settings;
        Assert.True(saved.Values.ContainsKey(SettingsPanel.PartEditsKey));

        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));
        Assert.Null(w.LastError);
        Scene opened = w.Editing.Scene!;
        Assert.Equal(edited.Select(c => c.Id), opened.Components.Select(c => c.Id));
        foreach (SceneComponent want in edited.Where(c => c.Slot is null || c.Moved is not null))
        {
            SceneComponent got = opened.Components.Single(c => c.Id == want.Id);
            Assert.Equal((want.Slot, want.Edited, want.GeneratorId), (got.Slot, got.Edited, got.GeneratorId));
            Assert.Equal(Xyz(want.Transform.Position), Xyz(got.Transform.Position));
            Assert.Equal(Xyz(want.Transform.Rotation), Xyz(got.Transform.Rotation));
            Assert.Equal(Xyz(want.Transform.Scale), Xyz(got.Transform.Scale));
        }

        // The copy keeps its whole part, its prop's settings as their own (read by the engine's validation), not the
        // prop's now.
        string whole = pointId == "boulder" ? "rock" : "bush";
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(opened.Components.Single(c => c.Id == copy).Parameters[whole]);
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true && w.Rocks.Mesh?.Parts is not null));
        Assert.Equal(opened.Components.Select(c => c.Id).Order(StringComparer.Ordinal), w.Rocks.Mesh!.Parts!.Select(p => p.Id).Order(StringComparer.Ordinal));

        // A slot 0.4.5 does not make opens with the usual note rather than removing another part.
        JsonObject edits = JsonNode.Parse(saved.Values[SettingsPanel.PartEditsKey].GetRawText())!.AsObject();
        edits["removed"] = new JsonArray("a-slot-gone-since");
        Settings gone = saved.With(SettingsPanel.PartEditsKey, JsonSerializer.SerializeToElement(edits));
        string gonePath = s.P($"{pointId}-gone.propgen");
        var goneHistory = new History();
        goneHistory.Push("Edited on the port", gone);
        WorkFile.Save(gonePath, new Piece(recipe, []), gone, goneHistory);
        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(gonePath)));
        Assert.StartsWith($"Opened {pointId}-gone.propgen without its part edits.", w.LastError, StringComparison.Ordinal);
        Assert.Equal(generatedIds, w.Editing.Scene!.Components.Select(c => c.Id));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnEditedOldStyleTrunkOpensAsBefore()
    {
        // An old-style tree with its trunk bent and a limb moved, as the port saved it: it opens to the components master
        // gave it (the edits folded in with the C# merge, which rebuilt the trunk's spine), kept in the fixture.
        using var s = new Scratch();
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        using JsonDocument expect = PortFixtures.Json("old-tree-edited.expect.json");
        string path = PortFixtures.Copy("old-tree-edited.propgen", s.P("old-tree.propgen"));

        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));
        Assert.Null(w.LastError);
        Assert.Equal(Normal(JsonNode.Parse(expect.RootElement.GetProperty("components").GetRawText())!.AsArray()), Normal(w.Editing.Scene!.Components));
        w.Close();
    }

    [Fact]
    public void AnEditedBranchWithAPointCountOutsideItsLimitIsRefused()
    {
        // The old-style tree as the port saved it, its moved branch asking for another number of spine points: inside
        // 3 to 12 it is kept, outside it the part edits are refused (a crafted file must not build a huge spine).
        using JsonDocument file = PortFixtures.Json("old-tree-edited.propgen");
        EngineRecipe tree = EngineKit.Recipe("branching-tree");
        Settings With(double count)
        {
            var settings = JsonNode.Parse(file.RootElement.GetProperty("settings").GetRawText())!.AsObject();
            settings["partEdits"]!["kept"]![0]!["parameters"]!["controlPoints"] = count;
            using var doc = JsonDocument.Parse(settings.ToJsonString());
            return new Settings(doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal));
        }

        foreach (double count in new[] { 1e9, 13, 2, -5 })
        {
            Assert.Contains("controlPoints", tree.EditsProblem(With(count)), StringComparison.Ordinal);
        }

        Assert.Null(tree.EditsProblem(With(PartEditsCheck.MaxControlPoints)));
        Assert.Null(tree.EditsProblem(With(PartEditsCheck.MinControlPoints)));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnOakEditedOnThePortOpensWithTheSameParts()
    {
        using var s = new Scratch();
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        StartingPoint point = Point("oak");
        EngineRecipe oak = EngineKit.Recipe("painted-tree");

        // A painted tree's parts cannot be copied; its moves and removals go to its own edits. As the port saved it (a
        // committed fixture): one clump moved 0.5 m along x, another removed.
        using JsonDocument expect = PortFixtures.Json("oak-edited.expect.json");
        string moved = expect.RootElement.GetProperty("moved").GetString()!;
        string removed = expect.RootElement.GetProperty("removed").GetString()!;
        double by = expect.RootElement.GetProperty("by").GetDouble();
        string path = PortFixtures.Copy("oak-edited.propgen", s.P("oak.propgen"));

        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));
        Assert.Null(w.LastError);
        Scene plain = oak.SceneOf(point.Settings);
        Scene opened = w.Editing.Scene!;
        Assert.DoesNotContain(opened.Components, c => c.Id == removed);
        Vec3d was = plain.Components.Single(c => c.Id == moved).Transform.Position;
        Vec3d now = opened.Components.Single(c => c.Id == moved).Transform.Position;
        Assert.Equal(was.X + by, now.X, 6);
        Assert.Equal(was.Y, now.Y, 6);
        Assert.Equal(was.Z, now.Z, 6);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TreeAndSeedOpsNeverRunOnTheUiThread()
    {
        EngineRecipe oak = EngineKit.Recipe("painted-tree");
        Settings s = Point("oak").Settings;
        _ = oak.SceneOf(s);
        Assert.Throws<InvalidOperationException>(() => oak.Apply(s, new SceneOp.Seed(7)));
        Assert.Throws<InvalidOperationException>(() => oak.Apply(s, new SceneOp.Tree(new Dictionary<string, object>(StringComparer.Ordinal) { ["height"] = 9.0 })));

        // Through ApplyAsync they go to the engine's thread as a live job; the UI thread does not wait.
        long live = EngineHost.Default.Builds.JobsQueued(EngineJobKind.Live);
        Task<Scene?> seeded = oak.ApplyAsync(s, new SceneOp.Seed(7), Ct);
        Assert.True(EngineHost.Default.Builds.JobsQueued(EngineJobKind.Live) >= live + 1);  // the queue is shared: other tests' windows queue too
        Scene? after = ClaudeKit.Run(seeded, 60000);
        Assert.NotNull(after);
        Assert.Equal(7.0, after.Seed);

        Task<Scene?> taller = oak.ApplyAsync(s, new SceneOp.Tree(new Dictionary<string, object>(StringComparer.Ordinal) { ["height"] = 9.0 }), Ct);
        Assert.True(EngineHost.Default.Builds.JobsQueued(EngineJobKind.Live) >= live + 2);  // the queue is shared: other tests' windows queue too
        Assert.Equal(9.0, Convert.ToDouble(ClaudeKit.Run(taller, 60000)!.Parameters["height"], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ASceneOpTimeoutLeavesTheSceneAndSaysSo()
    {
        MainWindow w = Start("round-bush");
        w.Editing.Select(Leaves);
        Settings before = w.Panel.Current;
        int steps = w.Files.History.Steps.Count;
        EnginePartScenes.OpForTests.Value = (_, _) => throw new TimeoutException("easy-props' rules call took longer than 1 s");
        try
        {
            w.Editing.MoveBy(new Vector3(0.5f, 0, 0));
            Assert.Equal(before, w.Panel.Current);
            Assert.Equal(steps, w.Files.History.Steps.Count);
            Assert.StartsWith("The part was left as it was", w.LastError, StringComparison.Ordinal);

            // A drag says so too, and makes no step.
            CommandListTests.Run(w, "move-tool");
            Assert.True(w.Editing.Begin(new Point(10, 10)));
            w.Editing.Move(new Point(40, 10), new Point(10, 10), shift: false);
            w.Editing.Move(new Point(60, 10), new Point(10, 10), shift: false);
            w.Editing.End();
            Assert.Equal(before, w.Panel.Current);
            Assert.Equal(steps, w.Files.History.Steps.Count);
        }
        finally
        {
            EnginePartScenes.OpForTests.Value = null;
        }

        w.Editing.MoveBy(new Vector3(0.5f, 0, 0));
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void APartDragOf100TicksStaysUnder100MsOfSceneOps()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(YourPropsTests.Point("round-bush"))));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        var bush = (EngineRecipe)MeshRecipes.Find(w.Files.Recipe)!;
        w.Editing.Select("cmp-bush-leaves");
        CommandListTests.Run(w, "move-tool");

        // Warm: one drag first.
        Assert.True(w.Editing.Begin(new Point(10, 10)));
        w.Editing.Move(new Point(20, 10), new Point(10, 10), shift: false);
        w.Editing.End();

        int ops = bush.SceneOpsRun;
        TimeSpan time = bush.SceneOpTime;
        Assert.True(w.Editing.Begin(new Point(10, 10)));
        for (int i = 1; i <= 100; i++)
        {
            w.Editing.Move(new Point(10 + i, 10 + (i % 7)), new Point(10, 10), shift: i % 10 == 0);
        }

        w.Editing.End();
        Assert.Equal(ops + 100, bush.SceneOpsRun);
        TimeSpan spent = bush.SceneOpTime - time;
        Assert.True(spent < TimeSpan.FromMilliseconds(100), $"{spent.TotalMilliseconds:F1} ms of scene ops for 100 ticks");
        Assert.Equal("Move part", w.Files.History.Steps[^1].Label);
        w.Close();
    }
}
