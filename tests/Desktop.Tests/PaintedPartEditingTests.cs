using System.Numerics;
using System.Text.Json;
using Avalonia;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Scenes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// Picking a painted tree's limb or clump and changing it, as easy-props does: each change goes into the tree's own edits
/// (its <c>edits</c> parameter), is one undo step, and is saved with the work file.
/// </summary>
public class PaintedPartEditingTests
{
    private const string Trunk = "cmp-limb-t";

    private static StartingPoint Oak => MeshRecipes.Find("painted-tree")!.StartingPoints.Single(p => p.Id == "oak");

    private static bool Waited(Func<bool> done, int ms = 30000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    private static MainWindow Tree(FakeDialogs? dialogs = null, Scratch? scratch = null)
    {
        MainWindow w = scratch is null ? Ui.Open(new FakePainter()) : Ui.Open(new FakePainter(), dialogs ?? new FakeDialogs(), scratch);
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(Oak)));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        return w;
    }

    private static SceneComponent FirstClump(MainWindow w) => w.Editing.Scene!.Components.First(c => c.GeneratorId == "painted-clump");

    private static SceneComponent FirstBranch(MainWindow w) =>
        w.Editing.Scene!.Components.First(c => c.GeneratorId == "painted-limb" && c.Id != Trunk);

    private static SceneComponent? Part(MainWindow w, string id) => w.Editing.Scene!.Components.SingleOrDefault(c => c.Id == id);

    private static string Edits(MainWindow w) =>
        w.Panel.Current.Values.TryGetValue(SettingsPanel.TreeEditsKey, out JsonElement e) ? e.GetString()! : string.Empty;

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ThePartCommandsSayWhyAsEasyPropsDoes()
    {
        MainWindow w = Tree();
        w.Editing.Select(FirstClump(w).Id);
        Assert.Equal("Parts of a painted tree cannot be copied", CommandListTests.Find(w, "copy-part").Unavailable());
        Assert.Equal("Only branches and roots turn", CommandListTests.Find(w, "turn-tool").Unavailable());
        foreach (string id in new[] { "remove-part", "restore-part", "move-tool", "resize-tool" })
        {
            Assert.Null(CommandListTests.Find(w, id).Unavailable());
        }

        w.Editing.Select(FirstBranch(w).Id);
        Assert.Equal("Reshape this part with its handles instead", CommandListTests.Find(w, "move-tool").Unavailable());
        Assert.Null(CommandListTests.Find(w, "turn-tool").Unavailable());
        Assert.Null(CommandListTests.Find(w, "resize-tool").Unavailable());

        w.Editing.Select(Trunk);
        Assert.Equal("This part is the whole prop", CommandListTests.Find(w, "remove-part").Unavailable());
        Assert.Equal("This part is the whole prop. Use Lean to tip it", CommandListTests.Find(w, "turn-tool").Unavailable());
        Assert.Equal("This part is the whole prop. Use Height to size it", CommandListTests.Find(w, "resize-tool").Unavailable());
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RemovingAClumpGoesIntoTheTreesEditsAndUndoes()
    {
        MainWindow w = Tree();
        Settings before = w.Panel.Current;
        SceneComponent clump = FirstClump(w);
        string key = (string)clump.Parameters["key"];
        w.Editing.Select(clump.Id);

        CommandListTests.Run(w, "remove-part");

        Assert.Equal("Remove part", w.Files.History.Steps[^1].Label);
        Assert.Null(Part(w, clump.Id));
        Assert.Null(w.Editing.Selected);
        using (var edits = JsonDocument.Parse(Edits(w)))
        {
            Assert.True(edits.RootElement.GetProperty("clumps").GetProperty(key).GetProperty("removed").GetBoolean());
        }

        // The tree is built again from its edits: nothing else to keep about its parts.
        Assert.False(w.Panel.Current.Values.ContainsKey(SettingsPanel.PartEditsKey));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        Assert.DoesNotContain(w.Rocks.Mesh!.Parts!, p => p.Id == clump.Id);

        // It stays removed when a control changes; undo brings it back.
        Assert.Null(w.Panel.SetFromOutside("height", JsonSerializer.SerializeToElement(9.0)));
        Assert.Null(Part(w, clump.Id));
        w.Files.Undo();
        w.Files.Undo();
        Assert.Equal(before, w.Panel.Current);
        Assert.NotNull(Part(w, clump.Id));
        w.Files.Redo();
        Assert.Null(Part(w, clump.Id));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MovingAClumpTurningAndResizingABranchAreStepsInTheEditsAndRestoreTakesThemBack()
    {
        MainWindow w = Tree();
        SceneComponent clump = FirstClump(w);
        w.Editing.Select(clump.Id);
        w.Editing.MoveBy(new Vector3(0.5f, 0, 0));
        Assert.Equal("Move part", w.Files.History.Steps[^1].Label);
        Assert.Equal(clump.Transform.Position.X + 0.5, Part(w, clump.Id)!.Transform.Position.X, 4);
        Assert.Contains("\"offset\"", Edits(w), StringComparison.Ordinal);

        SceneComponent branch = FirstBranch(w);
        w.Editing.Select(branch.Id);
        w.Editing.TurnBy(Vector3.UnitZ, 45);
        Assert.Equal([0.0, 45.0, 0.0], PartTurning.TurnOf(Part(w, branch.Id)));
        w.Editing.ResizeBy(1.5);
        Assert.Equal(1.5, PartResizing.SizeOf(Part(w, branch.Id)));
        Assert.Equal(["Move part", "Turn part", "Resize part"], w.Files.History.Steps.TakeLast(3).Select(s => s.Label));
        Assert.False(w.Panel.Current.Values.ContainsKey(SettingsPanel.PartEditsKey));

        // A drag with Resize on is one step, its size within the branch's limits, to a hundredth.
        CommandListTests.Run(w, "resize-tool");
        int steps = w.Files.History.Steps.Count;
        Assert.True(w.Editing.Begin(new Point(10, 10)));
        w.Editing.Move(new Point(10, -1000), new Point(10, 10), shift: false);
        w.Editing.End();
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal(PartResizing.LimbSize.Max, PartResizing.SizeOf(Part(w, branch.Id)));

        CommandListTests.Run(w, "restore-part");
        Assert.Equal("Restore part", w.Files.History.Steps[^1].Label);
        Assert.Equal(1.0, PartResizing.SizeOf(Part(w, branch.Id)));
        Assert.Equal([0.0, 0.0, 0.0], PartTurning.TurnOf(Part(w, branch.Id)));
        Assert.Contains("\"offset\"", Edits(w), StringComparison.Ordinal);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheTreesEditsAreSavedAndOpenAgain()
    {
        using var s = new Scratch();
        MainWindow w = Tree(new FakeDialogs(), s);
        SceneComponent clump = FirstClump(w);
        w.Editing.Select(clump.Id);
        CommandListTests.Run(w, "remove-part");
        Settings edited = w.Panel.Current;
        string path = s.P("tree.propgen");
        Assert.Null(w.Files.SaveWithoutAsking(path, overwrite: true));

        Assert.True(CommandListTests.RunAsync(w.Files.NewAsync()));
        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));
        Assert.Equal(edited, w.Panel.Current);
        Assert.Null(Part(w, clump.Id));
        Assert.Contains("Remove part", w.Files.History.Steps.Select(x => x.Label));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AClickOnTheWoodPicksTheLimbAndLightsItUp()
    {
        MainWindow w = Tree();
        ColouredMesh mesh = w.Rocks.Mesh!;
        ColouredMesh proxies = mesh.Proxies!;
        MeshPart trunk = proxies.Parts!.Single(p => p.Id == Trunk);

        // A corner of the trunk's proxy, halfway up where neither its flare nor leaves hide it, on the side facing the
        // camera. Not a triangle's middle: upstream's proxy is a six-sided tube 1.14 times the limb's radius, so a
        // face's middle lies at about the bark's own radius, and easy-props 0.4.5's bark stands out past it in places;
        // the pick, as upstream's, takes whichever is nearer, and that is the bark.
        Vector3 eye = w.View.Orbit.Eye;
        Vector3 Corner(int i) => new(proxies.Positions[i * 3], proxies.Positions[(i * 3) + 1], proxies.Positions[(i * 3) + 2]);
        Vector3[] corners = [.. Enumerable.Range(trunk.FirstTriangle * 3, trunk.Triangles * 3).Select(i => Corner(proxies.Indices[i]))];
        float bottom = corners.Min(p => p.Z), top = corners.Max(p => p.Z);
        Vector3 middle = corners.Where(p => p.Z > bottom + ((top - bottom) * 0.3f) && p.Z < bottom + ((top - bottom) * 0.5f)).MinBy(p => Vector3.Distance(p, eye));

        w.Editing.Pick(w.View.ScreenAt(middle)!.Value);
        Assert.Equal(Trunk, w.Editing.Selected);

        // The limb is drawn over in the highlight: a sleeve just outside its bark.
        ColouredMesh lit = w.Editing.Decorate(mesh);
        Assert.True(lit.Positions.Length > mesh.Positions.Length);
        Assert.Equal(mesh.Indices.Length + (trunk.Triangles * 3), lit.Indices.Length);
        Assert.Equal(lit.Positions.Length, lit.Colours.Length);
        Assert.Equal(lit.Positions.Length / 3, lit.Flutter!.Length);
        Assert.Equal(lit.Positions.Length, lit.TexCoords!.Length);
        w.Close();
    }

    // D1: the engine builds the tree while its part edits run on the port's scene, so every part the engine draws or
    // picks by must be a component of that scene, and a limb picked through the engine's proxies is one of them.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheEnginesPartsAndProxiesAreTheScenesComponents()
    {
        MainWindow w = Tree();
        Assert.IsType<EngineRecipe>(MeshRecipes.Find(w.Files.Recipe));
        ColouredMesh mesh = w.Rocks.Mesh!;
        HashSet<string> components = [.. w.Editing.Scene!.Components.Select(c => c.Id)];
        Assert.All(mesh.Parts!, p => Assert.Contains(p.Id, components));
        Assert.NotEmpty(mesh.Proxies!.Parts!);
        Assert.All(mesh.Proxies.Parts!, p => Assert.Contains(p.Id, components));

        // A limb other than the trunk, picked at the middle of its proxy's triangle nearest the camera.
        ColouredMesh proxies = mesh.Proxies;
        Vector3 eye = w.View.Orbit.Eye;
        Vector3 Corner(int i) => new(proxies.Positions[i * 3], proxies.Positions[(i * 3) + 1], proxies.Positions[(i * 3) + 2]);
        (MeshPart Part, Vector3 At) nearest = proxies.Parts!
            .SelectMany(part => Enumerable.Range(part.FirstTriangle, part.Triangles)
                .Select(t => (part, (Corner(proxies.Indices[t * 3]) + Corner(proxies.Indices[(t * 3) + 1]) + Corner(proxies.Indices[(t * 3) + 2])) / 3)))
            .MinBy(p => Vector3.Distance(p.Item2, eye));
        w.Editing.Pick(w.View.ScreenAt(nearest.At)!.Value);
        Assert.NotNull(w.Editing.Selected);
        Assert.Contains(w.Editing.Selected!, components);
        Assert.Contains(w.Editing.Scene!.Components, c => c.Id == w.Editing.Selected);
        w.Close();
    }

    // A work file is shared: a tree's own edits that are far larger than any tree's open as no edits, in the settings
    // and every undo step, and the status line says so (they would be parsed on every rebuild).
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AWorkFileWithHugeTreeEditsOpensWithoutThem()
    {
        using var s = new Scratch();
        MainWindow w = Tree(new FakeDialogs(), s);
        w.Editing.Select(FirstClump(w).Id);
        CommandListTests.Run(w, "remove-part");
        string path = s.P("crafted-tree.propgen");
        Assert.Null(w.Files.SaveWithoutAsking(path, overwrite: true));

        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var clumps = new System.Text.Json.Nodes.JsonObject();
        for (int i = 0; i < 20000; i++)
        {
            clumps[$"c{i}"] = new System.Text.Json.Nodes.JsonObject { ["removed"] = true };
        }

        string huge = new System.Text.Json.Nodes.JsonObject { ["limbs"] = new System.Text.Json.Nodes.JsonObject(), ["clumps"] = clumps }.ToJsonString();
        root["settings"]![SettingsPanel.TreeEditsKey] = huge;
        foreach (var step in root["history"]!["steps"]!.AsArray())
        {
            if (step!["settings"]![SettingsPanel.TreeEditsKey] is not null)
            {
                step["settings"]![SettingsPanel.TreeEditsKey] = huge;
            }
        }

        File.WriteAllText(path, root.ToJsonString());

        Assert.True(CommandListTests.RunAsync(w.Files.NewAsync()));
        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));
        Assert.Equal(string.Empty, Edits(w));
        Assert.All(w.Files.History.Steps, step => Assert.False(
            step.Settings.Values.TryGetValue(SettingsPanel.TreeEditsKey, out JsonElement e) && e.GetString()!.Length > 0));
        Assert.StartsWith("Opened crafted-tree.propgen without its part edits.", w.LastError, StringComparison.Ordinal);
        w.Close();
    }
}
