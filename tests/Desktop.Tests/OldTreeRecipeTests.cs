using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using PropGenerator.Nature.Export;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// The old-style tree and palm in the app, from easy-props' engine: Round canopy as the one starting point, the palm
/// reached through its New command, their mesh (bark and leaves in their tints, lightened at the lower levels), wind and
/// the shapes the engine's export writes (ruling 4: no longer none).
/// </summary>
public class OldTreeRecipeTests
{
    private static IMeshRecipe Tree => MeshRecipes.Find("branching-tree")!;

    private static IMeshRecipe Palm => MeshRecipes.Find("palm-tree")!;

    private static Settings RoundCanopy => Tree.StartingPoints.Single().Settings;

    [Fact]
    public void RoundCanopyIsTheOneStartingPointAndThePalmHasNone()
    {
        StartingPoint point = Assert.Single(Tree.StartingPoints);
        Assert.Equal(("round-canopy", "Round canopy", "Old-style trees", "branching-tree"), (point.Id, point.Name, point.Group, point.Recipe));
        Assert.Equal(12345u, point.Settings.GetSeed("seed"));
        Assert.Empty(Palm.StartingPoints);
        Assert.Equal(("palm-tree", "Old-style trees"), (EngineRecipes.PalmDefaults.Recipe, EngineRecipes.PalmDefaults.Group));
    }

    [Fact]
    public void BothComeAfterThePaintedTreeWithTheirNames()
    {
        var all = MeshRecipes.All.ToList();
        Assert.Equal(all.IndexOf(MeshRecipes.Find("painted-tree")!) + 1, all.IndexOf(Tree));
        Assert.Equal(all.IndexOf(Tree) + 1, all.IndexOf(Palm));
        Assert.Same(EngineRecipes.For("branching-tree"), Tree);
        Assert.Same(EngineRecipes.For("palm-tree"), Palm);
        Assert.Equal(("Old-style tree", "tree"), (Tree.Label, Tree.ExportName));
        Assert.Equal(("Old-style palm", "palm"), (Palm.Label, Palm.ExportName));
        Assert.False(Tree.LowPoly);
        Assert.False(Palm.LowPoly);
    }

    // easy-props: wood sways and leaves flutter. One mesh carries one sway in the view, so the whole tree flutters a
    // little as the bushes do; neither sways evenly, and nothing hides a row. Their shapes are the engine's export's.
    [Fact]
    public void TheySwayAndFlutterShowEveryRowAndCollideAsTheEnginesExport()
    {
        Assert.Equal(new WindSway(Foliage: true, Evenly: false), Tree.Wind);
        Assert.Equal(new WindSway(Foliage: true, Evenly: false), Palm.Wind);
        Assert.Empty(Tree.HiddenKeys(RoundCanopy));
        Assert.Empty(Palm.HiddenKeys(EngineRecipes.PalmDefaults.Settings));
        static IEnumerable<double> Numbers(IReadOnlyList<CollisionShape> list) =>
            list.SelectMany(c => new[] { c.Start.X, c.Start.Y, c.Start.Z, c.End.X, c.End.Y, c.End.Z, c.R0, c.R1 });
        foreach ((string id, Settings s) in new[] { ("branching-tree", RoundCanopy), ("palm-tree", EngineRecipes.PalmDefaults.Settings) })
        {
            IReadOnlyList<CollisionShape> shapes = MeshRecipes.Find(id)!.Collision(s);
            Assert.NotEmpty(shapes);
            Assert.Equal(Numbers(EngineMeshes.ToShapes(EngineKit.Collision(EngineKit.Recipe(id), s))), Numbers(shapes));
        }
    }

    // The view's mesh is the engine's parts, placed, turned Z-up and coloured by their material's tint, as three draws a
    // part without vertex colours.
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.12)]
    public void BuildIsTheEnginesPartsTurnedZUpInTheirTints(double detail)
    {
        EngineMesh built = EngineKit.Parts(EngineKit.Recipe("branching-tree"), RoundCanopy, detail);
        ColouredMesh mesh = Tree.Build(RoundCanopy, detail);
        EngineKit.Close(EngineKit.PlacedZUp(built), mesh.Positions);
        EnginePart trunk = built.Parts.First(p => !p.Proxy);
        Assert.Null(trunk.Colors);
        IReadOnlyList<double> bark = trunk.Tint!;
        Assert.Equal(((float)bark[0], (float)bark[1], (float)bark[2]), (mesh.Colours[0], mesh.Colours[1], mesh.Colours[2]));
        Assert.Contains(built.Parts, p => p.Material is "leaves" or "leaves-dark");
    }

    [Fact]
    public void ThePalmBuildsWithFrondsAndCoconutsAndThinsThemWhenLight()
    {
        Settings s = EngineRecipes.PalmDefaults.Settings;
        ColouredMesh full = Palm.Build(s, 1);
        ColouredMesh light = Palm.Build(s, 0.12);
        Assert.True(light.Positions.Length < full.Positions.Length);
        Assert.All(full.Normals, n => Assert.True(float.IsFinite(n)));
        byte[] glb = Palm.Glb(s, "palm");
        Assert.Equal("glTF"u8.ToArray(), glb[..4]);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void NewOldStylePalmStartsAPalm()
    {
        MainWindow w = Ui.Open(new FakePainter());
        CommandListTests.Run(w, "new-palm");
        Ui.Pump();
        Assert.Equal("palm-tree", w.Files.Recipe);
    }
}
