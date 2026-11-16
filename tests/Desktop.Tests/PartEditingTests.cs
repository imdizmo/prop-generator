using System.Numerics;
using System.Text.Json;
using Avalonia;
using PropGenerator.Desktop.Parts;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Scenes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>Picking a part of the prop and changing it: Move, Turn, Resize, Copy, Remove, Restore, undo and the work file.</summary>
public class PartEditingTests
{
    private const string Leaves = "cmp-bush-leaves";
    private const string Core = "cmp-bush-core";

    private static StartingPoint RoundBush => MeshRecipes.Find("bush")!.StartingPoints.Single(p => p.Id == "round-bush");

    private static StartingPoint Boulder => RockRecipe.StartingPoints.Single(p => p.Id == "boulder");

    private static bool Waited(Func<bool> done, int ms = 20000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    private static MainWindow Bush(FakeDialogs? dialogs = null, Scratch? scratch = null)
    {
        MainWindow w = scratch is null ? Ui.Open(new FakePainter()) : Ui.Open(new FakePainter(), dialogs ?? new FakeDialogs(), scratch);
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(RoundBush)));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        return w;
    }

    private static SceneComponent Part(MainWindow w, string id) => w.Editing.Scene!.Components.Single(c => c.Id == id);

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ThePartCommandsSayWhyUntilAPartIsPicked()
    {
        MainWindow w = Ui.Open(new FakePainter());
        foreach (string id in new[] { "copy-part", "remove-part", "restore-part", "move-tool", "turn-tool", "resize-tool" })
        {
            Assert.Equal("This kind of piece has no parts to pick", CommandListTests.Find(w, id).Unavailable());
        }

        Assert.Equal("Nothing is selected", CommandListTests.Find(w, "clear-selection").Unavailable());
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(RoundBush)));
        Assert.Equal("Select a part first", CommandListTests.Find(w, "copy-part").Unavailable());

        w.Editing.Select(Leaves);
        foreach (string id in new[] { "copy-part", "remove-part", "restore-part", "move-tool", "turn-tool", "resize-tool", "clear-selection" })
        {
            Assert.Null(CommandListTests.Find(w, id).Unavailable());
        }

        Assert.False(w.Toolbar.Move.Unavailable is not null);

        // A cut rock is one part: it is the whole prop, so it cannot be removed.
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(Boulder)));
        Assert.Null(w.Editing.Selected);
        w.Editing.Select("cmp-rock-0");
        Assert.Equal("This part is the whole prop", CommandListTests.Find(w, "remove-part").Unavailable());
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AClickOnAPartPicksItAndLightsItUpAndAClickBesideLetsGo()
    {
        MainWindow w = Bush();
        ColouredMesh mesh = w.Rocks.Mesh!;
        MeshPart leaves = mesh.Parts!.Single(p => p.Id == Leaves);

        // The mesh is the engine's, and its parts are the part scene's components (D1: the scene stays the port's).
        Assert.IsType<EngineRecipe>(MeshRecipes.Find(w.Files.Recipe));
        Assert.Equal([Core, Leaves], mesh.Parts!.Select(p => p.Id));
        Assert.Equal(w.Editing.Scene!.Components.Select(c => c.Id), mesh.Parts!.Select(p => p.Id));

        // The middle of one of the leaves' triangles nearest the camera.
        Vector3 eye = w.View.Orbit.Eye;
        Vector3 Corner(int i) => new(mesh.Positions[i * 3], mesh.Positions[(i * 3) + 1], mesh.Positions[(i * 3) + 2]);
        Vector3 middle = Enumerable.Range(leaves.FirstTriangle, leaves.Triangles)
            .Select(t => (Corner(mesh.Indices[t * 3]) + Corner(mesh.Indices[(t * 3) + 1]) + Corner(mesh.Indices[(t * 3) + 2])) / 3)
            .MinBy(p => Vector3.Distance(p, eye));
        Point at = w.View.ScreenAt(middle)!.Value;

        w.Editing.Pick(at);
        Assert.Equal(Leaves, w.Editing.Selected);
        ColouredMesh lit = w.Editing.Decorate(mesh);
        Assert.NotEqual(mesh.Colours, lit.Colours);
        Assert.Equal(mesh.Positions, lit.Positions);
        Assert.Same(mesh, w.Rocks.Mesh);

        w.Editing.Pick(new Point(2, 2));
        Assert.Null(w.Editing.Selected);
        Assert.Same(mesh, w.Editing.Decorate(mesh));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void MovingAPartIsOneStepThatUndoes()
    {
        MainWindow w = Bush();
        Settings before = w.Panel.Current;
        int steps = w.Files.History.Steps.Count;
        w.Editing.Select(Leaves);

        w.Editing.MoveBy(new Vector3(0.5f, 0, 0.25f));

        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal("Move part", w.Files.History.Steps[^1].Label);
        Assert.Equal((0.5, 0.25, 0.0), (Part(w, Leaves).Transform.Position.X, Part(w, Leaves).Transform.Position.Y, Part(w, Leaves).Transform.Position.Z));
        Assert.Equal(0.0, Part(w, Core).Transform.Position.X);
        Assert.True(w.Panel.Current.Values.ContainsKey(SettingsPanel.PartEditsKey));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));

        w.Files.Undo();
        Assert.Equal(before, w.Panel.Current);
        Assert.False(w.Panel.Current.Values.ContainsKey(SettingsPanel.PartEditsKey));
        w.Files.Redo();
        Assert.Equal(0.5, Part(w, Leaves).Transform.Position.X);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ADragWithAToolOnIsOneStepWhenLetGo()
    {
        MainWindow w = Bush();
        w.Editing.Select(Leaves);
        int steps = w.Files.History.Steps.Count;
        PartEditing drag = w.Editing;

        // Select is on: the left button stays with the camera.
        Assert.False(drag.Begin(new Point(10, 10)));
        CommandListTests.Run(w, "resize-tool");
        Assert.Equal(PartEditing.ResizeTool, w.Editing.Tool);
        Assert.True(w.Toolbar.Resize.Pressed);
        Assert.True(drag.Begin(new Point(10, 10)));
        drag.Move(new Point(10, 0), new Point(10, 10), shift: false);
        drag.Move(new Point(10, -90), new Point(10, 10), shift: false);
        Assert.Equal(steps, w.Files.History.Steps.Count);
        drag.End();

        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal("Resize part", w.Files.History.Steps[^1].Label);
        Assert.Equal(Math.Exp(0.5), Part(w, Leaves).Sized!.Value, 12);

        // Esc puts the tool down first, then lets go of the part.
        CommandListTests.Run(w, "clear-selection");
        Assert.Equal((PartEditing.SelectTool, Leaves), (w.Editing.Tool, w.Editing.Selected));
        CommandListTests.Run(w, "clear-selection");
        Assert.Null(w.Editing.Selected);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ATurnedAndResizedPartStaysSoWhenAControlChanges()
    {
        MainWindow w = Bush();
        w.Editing.Select(Leaves);
        w.Editing.TurnBy(Vector3.UnitZ, 90);
        w.Editing.ResizeBy(1.5);
        Assert.Equal(["Turn part", "Resize part"], w.Files.History.Steps.TakeLast(2).Select(s => s.Label));

        Assert.Null(w.Panel.SetFromOutside("height", JsonSerializer.SerializeToElement(2.4)));
        SceneComponent leaves = Part(w, Leaves);
        Assert.Equal(1.5, leaves.Sized);
        Assert.Equal(Math.PI / 2, leaves.Turned!.Y, 12);
        Assert.Equal(1.5, leaves.Transform.Scale.X, 12);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void CopyRemoveAndRestoreAreStepsAndTheCopyIsSelected()
    {
        MainWindow w = Bush();
        w.Editing.Select(Leaves);

        CommandListTests.Run(w, "copy-part");
        Scene scene = w.Editing.Scene!;
        Assert.Equal(3, scene.Components.Count);
        SceneComponent copy = scene.Components[^1];
        Assert.StartsWith($"man-{copy.Type}-", copy.Id, StringComparison.Ordinal);
        Assert.Equal(copy.Id, w.Editing.Selected);
        Assert.Null(copy.Slot);
        Assert.Equal("Copy part", w.Files.History.Steps[^1].Label);

        CommandListTests.Run(w, "remove-part");
        Assert.Equal(2, w.Editing.Scene!.Components.Count);
        Assert.Null(w.Editing.Selected);

        w.Editing.Select(Core);
        CommandListTests.Run(w, "remove-part");
        Assert.Equal([Leaves], w.Editing.Scene!.Components.Select(c => c.Id));
        Assert.Equal("Remove part", w.Files.History.Steps[^1].Label);

        // A removed part stays removed when a control changes; undo brings it back.
        Assert.Null(w.Panel.SetFromOutside("height", JsonSerializer.SerializeToElement(2.4)));
        Assert.Equal([Leaves], w.Editing.Scene!.Components.Select(c => c.Id));
        w.Files.Undo();
        w.Files.Undo();
        Assert.Equal([Core, Leaves], w.Editing.Scene!.Components.Select(c => c.Id));

        w.Editing.Select(Leaves);
        CommandListTests.Run(w, "restore-part");
        Assert.Equal("Restore part", w.Files.History.Steps[^1].Label);
        Assert.True(Part(w, Leaves).Edited);
        Assert.Empty(Part(w, Leaves).Parameters);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheEditsAreSavedAndOldWorkFilesStillOpen()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs();
        MainWindow w = Bush(dialogs, s);
        string plain = s.P("plain.propgen");
        Assert.Null(w.Files.SaveWithoutAsking(plain, overwrite: true));
        w.Editing.Select(Leaves);
        w.Editing.MoveBy(new Vector3(0, 1, 0));
        w.Editing.ResizeBy(0.5);
        Settings edited = w.Panel.Current;
        string path = s.P("edited.propgen");
        Assert.Null(w.Files.SaveWithoutAsking(path, overwrite: true));
        Assert.Contains("\"partEdits\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.DoesNotContain("\"partEdits\"", File.ReadAllText(plain), StringComparison.Ordinal);

        Assert.True(CommandListTests.RunAsync(w.Files.NewAsync()));
        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));
        Assert.Equal(edited, w.Panel.Current);
        Assert.Equal(-1.0, Part(w, Leaves).Transform.Position.Z, 6);
        Assert.Equal(0.5, Part(w, Leaves).Sized);
        Assert.Contains("Move part", w.Files.History.Steps.Select(x => x.Label));
        Assert.Contains("Resize part", w.Files.History.Steps.Select(x => x.Label));

        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(plain)));
        Assert.False(w.Panel.Current.Values.ContainsKey(SettingsPanel.PartEditsKey));
        Assert.Equal(0.0, Part(w, Leaves).Transform.Position.Z);
        w.Close();
    }

    // A work file is shared: edits out of bounds open as no edits, in the settings and every undo step, and the status
    // line says so. Each case is a file someone could craft.
    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData("size")]
    [InlineData("copies")]
    [InlineData("far")]
    [InlineData("generator")]
    [InlineData("junk")]
    public void AWorkFileWithEditsOutOfBoundsOpensWithoutThem(string crafted)
    {
        using var s = new Scratch();
        MainWindow w = Bush(new FakeDialogs(), s);
        w.Editing.Select(Leaves);
        w.Editing.MoveBy(new Vector3(0.5f, 0, 0));
        string path = s.P("crafted.propgen");
        Assert.Null(w.Files.SaveWithoutAsking(path, overwrite: true));

        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        System.Text.Json.Nodes.JsonNode edits = root["settings"]![SettingsPanel.PartEditsKey]!;
        var kept = edits["kept"]!.AsArray();
        var leaves = kept[0]!;
        switch (crafted)
        {
            case "size":
                leaves["sized"] = 1e9;
                break;
            case "copies":
                for (int i = 0; i < 300; i++)
                {
                    var copy = leaves.DeepClone();
                    copy["id"] = $"man-{i}";
                    copy["slot"] = null;
                    kept.Add(copy);
                }

                break;
            case "far":
                leaves["moved"] = new System.Text.Json.Nodes.JsonArray(1e300, 0, 0);
                break;
            case "generator":
                leaves["generatorId"] = "trunk";
                break;
            default:
                root["settings"]![SettingsPanel.PartEditsKey] = "not edits";
                break;
        }

        // Every undo step carries the same crafted edits.
        foreach (var step in root["history"]!["steps"]!.AsArray())
        {
            if (step!["settings"]![SettingsPanel.PartEditsKey] is not null)
            {
                step["settings"]![SettingsPanel.PartEditsKey] = root["settings"]![SettingsPanel.PartEditsKey]!.DeepClone();
            }
        }

        File.WriteAllText(path, root.ToJsonString());

        Assert.True(CommandListTests.RunAsync(w.Files.NewAsync()));
        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));
        Assert.False(w.Panel.Current.Values.ContainsKey(SettingsPanel.PartEditsKey));
        Assert.All(w.Files.History.Steps, step => Assert.False(step.Settings.Values.ContainsKey(SettingsPanel.PartEditsKey)));
        Assert.StartsWith("Opened crafted.propgen without its part edits.", w.LastError, StringComparison.Ordinal);
        Assert.Equal([Core, Leaves], w.Editing.Scene!.Components.Select(c => c.Id));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        w.Close();
    }
}
