using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using PropGenerator.Nature.Export;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// Painted trees in the app, from easy-props' engine: their 54 starting points under easy-props' headings, their mesh (printed leaves, plain
/// bark, no proxies), their wind (leaves flutter, wood sways, fruit swings whole), the rows they hide, their collision and
/// their New command.
/// </summary>
public class PaintedTreeRecipeTests
{
    private static IMeshRecipe Tree => MeshRecipes.Find("painted-tree")!;

    private static Settings Point(string id) => Tree.StartingPoints.Single(p => p.Id == id).Settings;

    [Fact]
    public void SixtyFourStartingPointsUnderEasyPropsHeadingsAfterTheCactus()
    {
        Assert.Equal(64, Tree.StartingPoints.Count);
        Assert.All(Tree.StartingPoints, p => Assert.Equal("painted-tree", p.Recipe));
        Assert.Equal(
            ["Broadleaf", "Blossom & fruit", "Palms & tropical", "Conifers", "Weeping", "Old & bare", "Shrubs & saplings", "Fantasy", "Ferns"],
            Tree.StartingPoints.Select(p => p.Group).Distinct());
        Assert.Equal([12, 12, 9, 8, 4, 9, 2, 6, 2], Tree.StartingPoints.GroupBy(p => p.Group).Select(g => g.Count()));
        Assert.Equal("spring-tree", Tree.StartingPoints[0].Id);
        Assert.Same(EngineRecipes.For("painted-tree"), Tree);
        Assert.Equal(MeshRecipes.All.ToList().IndexOf(MeshRecipes.Find("cactus")!) + 1, MeshRecipes.All.ToList().IndexOf(Tree));
        Assert.Equal(("Tree", "tree"), (Tree.Label, Tree.ExportName));
        Assert.True(Tree.LowPoly);
    }

    // easy-props' view: the whole tree sways, its leaves flutter as foliage and its wood only sways, as wood.
    [Fact]
    public void ItsLeavesFlutterAndItsWoodSways() =>
        Assert.Equal(new WindSway(Foliage: false, Evenly: false, ByPart: true), Tree.Wind);

    [Fact]
    public void TheLeavesArePrintedWithTheirLeafPictureAndFlutterTheBarkIsPrintedWithItsStrokesAndReliefAndStillAndTheProxiesAreLeftOut()
    {
        Settings s = Point("oak");
        ColouredMesh mesh = Tree.Build(s, 0.4);
        EngineMesh built = EngineKit.Parts(EngineKit.Recipe("painted-tree"), s, 0.4);
        EnginePart[] parts = [.. built.Parts.Where(p => !p.Proxy)];
        string barkKey = Assert.Single(parts.Select(p => p.Surface.Map).OfType<string>().Where(m => m.StartsWith("bark:", StringComparison.Ordinal)).Distinct());
        // easy-props 0.4.5 prints the oak's leaves with its lobed leaf picture (0.4.2 with brush dabs).
        const string leafKey = "tree-leaf:lobed";
        Assert.Contains(parts, p => p.RoleOfPart == "foliage" && p.Surface.Map == leafKey);

        // The limbs' picking tubes are kept apart: never drawn, but what the pointer lands on.
        Assert.Contains(built.Parts, p => p.Proxy);
        Assert.Equal(parts.Sum(p => p.VertexCount), mesh.Positions.Length / 3);
        Assert.Equal(built.Parts.Where(p => p.Proxy).Sum(p => p.VertexCount), mesh.Proxies!.Positions.Length / 3);
        Assert.NotNull(mesh.Texture);
        Assert.Equal(0.5f, mesh.Texture!.AlphaCut);
        MeshTexture leaves = EngineKit.Texture(leafKey);
        Assert.Equal((leaves.Width, leaves.Height), (mesh.Texture.Width, mesh.Texture.Height));
        Assert.Equal(leaves.Rgba, mesh.Texture.Rgba);
        float[] tex = mesh.TexCoords!;
        float[] flutter = mesh.Flutter!;
        Assert.Equal(mesh.Positions.Length / 3, flutter.Length);
        int at = 0;
        foreach (EnginePart part in parts)
        {
            bool bark = part.Surface.Map == barkKey;
            bool dabs = part.Surface.Map == leafKey;
            bool foliage = part.RoleOfPart == "foliage";
            for (int i = 0; i < part.VertexCount; i++, at++)
            {
                float layer = bark ? 2f : dabs ? 1f : 0f;
                Assert.Equal(layer, tex[(at * 3) + 2]);
                if (layer > 0)
                {
                    Assert.Equal((part.Uvs![i * 2], part.Uvs[(i * 2) + 1]), (tex[at * 3], tex[(at * 3) + 1]));
                }

                // The leaves flutter (fruit, marked rigid, never does); the wood only sways.
                Assert.Equal(foliage ? 1 - (part.Rigid?[i] ?? 0) : 0f, flutter[at]);
            }
        }

        // The bark as easy-props' view prints it: its strokes multiplied over its colours, its relief bending the light.
        MeshTexture strokes = EngineKit.Texture(barkKey);
        MeshTexture relief = EngineKit.Texture("relief:" + barkKey.Split(':')[1]);
        Assert.Equal(strokes.Rgba, mesh.Bark!.Rgba);
        Assert.Equal(relief.Rgba, mesh.Relief!.Rgba);
        Assert.Equal((relief.Width, relief.Height), (mesh.Relief.Width, mesh.Relief.Height));
    }

    // Fruit is marked rigid: it sways with its clump but never flutters, as in easy-props' view.
    [Fact]
    public void FruitSwingsWhole()
    {
        ColouredMesh mesh = Tree.Build(Point("apple"), 0.4);
        Assert.Contains(mesh.Flutter!, f => f == 0f);
        Assert.Contains(mesh.Flutter!, f => f == 1f);
    }

    [Fact]
    public void ALowPolyTreePrintsNothingAndItsFacetedCrownSwingsWhole()
    {
        ColouredMesh mesh = Tree.Build(Point("oak").With("build", "low-poly"), 0.12);
        Assert.Equal(new PieceFrame((float)Point("oak").Values["height"].GetDouble(), null), mesh.Frame);
        Assert.Null(mesh.TexCoords);
        Assert.Null(mesh.Texture);
        Assert.Null(mesh.Bark);
        Assert.True(mesh.Positions.Length > 0);
        Assert.All(mesh.Flutter!, f => Assert.Equal(0f, f));
    }

    [Fact]
    public void ItsCollisionIsTheTrunkAndHeavyLimbs()
    {
        Settings s = Point("oak");
        var shapes = Tree.Collision(s);
        Assert.NotEmpty(shapes);
        static IEnumerable<double> Numbers(IReadOnlyList<CollisionShape> list) =>
            list.SelectMany(c => new[] { c.Start.X, c.Start.Y, c.Start.Z, c.End.X, c.End.Y, c.End.Z, c.R0, c.R1 });
        EngineCollision engine = EngineKit.Collision(EngineKit.Recipe("painted-tree"), s);
        Assert.NotEmpty(engine.Offered);
        Assert.Equal(Numbers(EngineMeshes.ToShapes(engine)), Numbers(shapes));
    }

    [Fact]
    public void HidesTheStyleAlwaysWhatTheFormDoesNotUseAndTheBarkDetailWhenLowPoly()
    {
        var oak = Tree.HiddenKeys(Point("oak"));
        Assert.Contains("build", oak);
        Assert.Contains("stem", oak);
        Assert.DoesNotContain("crownShape", oak);

        var palm = Tree.HiddenKeys(Point("coconut-palm-painted"));
        Assert.Contains("crownShape", palm);
        Assert.DoesNotContain("stem", palm);

        var lowPoly = Tree.HiddenKeys(Point("oak").With("build", "low-poly"));
        Assert.Contains("barkDetail", lowPoly);
        Assert.Contains("leafSize", lowPoly);
        Assert.DoesNotContain("clumpCount", lowPoly);
    }

    // My own colours, as easy-props' tree has them: the five palette colours under Colours, shown when the Palette is
    // on My own colours, starting from the colours that were showing (the bark tone's wood included), and reaching the mesh.
    [Fact]
    public void MyOwnColoursShowTheFivePaletteRowsStartFromWhatWasShowingAndReachTheMesh()
    {
        string[] rows = ["barkDeep", "barkLight", "leafDeep", "leafMid", "leafLight"];
        ParamSpec[] specs = [.. rows.Select(key => Tree.Specs.Single(spec => spec.Key == key))];
        Assert.Equal(["Bark shadow", "Bark light", "Leaf shadow", "Leaf middle", "Leaf highlight"], specs.Select(spec => spec.Label));
        Assert.All(specs, spec => Assert.Equal(("colour", "Colours"), (spec.Type, spec.Group)));
        Assert.Equal("Warm brown", specs[0].Swatches![0].Label);
        Assert.Equal("Fresh green", specs[2].Swatches![0].Label);
        OwnColourGroup group = Assert.Single(Tree.OwnColourGroups);
        Assert.Equal(("tree", "Trees", "palette"), (group.Set, group.Title, group.ChoiceKey));
        Assert.Equal(rows, group.Keys);

        // The oak is Forest green with oak-brown wood.
        Settings oak = Point("oak");
        Assert.All(rows, key => Assert.Contains(key, Tree.HiddenKeys(oak)));
        Assert.DoesNotContain("barkTone", Tree.HiddenKeys(oak));
        Settings own = Tree.Adjust(oak, oak.With("palette", "custom"));
        Assert.All(rows, key => Assert.DoesNotContain(key, Tree.HiddenKeys(own)));
        Assert.Contains("barkTone", Tree.HiddenKeys(own));
        Assert.Equal("forest", oak.Values["palette"].GetString());
        Assert.Equal(rows[2..].Select(key => EngineKit.Named("painted-tree", key, "forest")), rows[2..].Select(own.GetColour));

        // The wood is the oak bark tone's, not the palette's own bark.
        Assert.NotEqual(rows[..2].Select(key => EngineKit.Named("painted-tree", key, "forest")), rows[..2].Select(own.GetColour));

        // Starting from what was showing, nothing jumps; an own colour then reaches the mesh.
        Assert.Equal(Tree.Build(oak, 0.2).Colours, Tree.Build(own, 0.2).Colours);
        Settings red = own.With("leafMid", Rgb.FromHex("#ff0000")).With("barkLight", Rgb.FromHex("#0000ff"));
        Assert.NotEqual(Tree.Build(own, 0.2).Colours, Tree.Build(red, 0.2).Colours);
    }

    [Fact]
    public void ItExportsAGlb()
    {
        byte[] glb = Tree.Glb(Point("pygmy-palm"), "tree");
        Assert.Equal("glTF"u8.ToArray(), glb[..4]);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void NewTreeStartsTheOldOak()
    {
        MainWindow w = Ui.Open(new FakePainter());
        CommandListTests.Run(w, "new-tree");
        Ui.Pump();
        Assert.Equal("painted-tree", w.Files.Recipe);
    }
}
