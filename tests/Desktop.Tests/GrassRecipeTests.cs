using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>Grass in the app, from easy-props' engine: its starting points, its mesh, the rows it hides, its own colours and its New command.</summary>
public class GrassRecipeTests
{
    private static IMeshRecipe Grass => MeshRecipes.Find("grass")!;

    private static Settings Meadow => Grass.StartingPoints.Single(p => p.Id == "meadow-patch").Settings;

    [Fact]
    public void NineStartingPointsUnderGrassAfterTheStylizedRock()
    {
        Assert.Equal(9, Grass.StartingPoints.Count);
        Assert.All(Grass.StartingPoints, p => Assert.Equal("Grass", p.Group));
        Assert.Same(EngineRecipes.For("grass"), Grass);
        Assert.Equal(MeshRecipes.All.ToList().IndexOf(StylizedRockRecipe.Instance) + 1, MeshRecipes.All.ToList().IndexOf(Grass));
        Assert.Equal(new WindSway(Foliage: true, Evenly: true), Grass.Wind);
        Assert.Null(MeshRecipes.Find(RockRecipe.Id)!.Wind);
    }

    [Fact]
    public void BuildIsTheEnginesPatchTurnedZUp()
    {
        Settings s = Meadow;
        ColouredMesh mesh = Grass.Build(s, 1);
        EngineKit.Close(EngineKit.PlacedZUp(EngineKit.Parts(EngineKit.Recipe("grass"), s, 1)), mesh.Positions);
    }

    [Fact]
    public void HidesTheStyleAlwaysAndSmoothnessWhenLowPoly()
    {
        var normal = Grass.HiddenKeys(Meadow);
        Assert.Contains("build", normal);
        Assert.DoesNotContain("smoothness", normal);
        Assert.Contains("grassRoot", normal);
        Assert.Contains("flowerDeep", normal);

        var lowPoly = Grass.HiddenKeys(Meadow.With("build", "low-poly"));
        Assert.Contains("smoothness", lowPoly);
    }

    // RF3: each colour choice on My own colours shows its own rows and starts them from the palette that was showing.
    [Fact]
    public void BothOwnColourChoicesShowTheirRowsAndStartFromThePalette()
    {
        Settings before = Meadow;
        Settings grassOwn = Grass.Adjust(before, before.With("palette", "custom"));
        Assert.DoesNotContain("grassRoot", Grass.HiddenKeys(grassOwn));
        Assert.Contains("flowerDeep", Grass.HiddenKeys(grassOwn));
        Assert.Equal(EngineKit.Named("grass", "grassRoot", "meadow"), grassOwn.GetColour("grassRoot"));

        Settings bothOwn = Grass.Adjust(grassOwn, grassOwn.With("flowerColour", "custom"));
        Assert.DoesNotContain("flowerDeep", Grass.HiddenKeys(bothOwn));
        Assert.Equal(EngineKit.Named("grass", "flowerDeep", "white"), bothOwn.GetColour("flowerDeep"));

        // An own colour reaches the mesh.
        Settings red = bothOwn.With("grassRoot", Rgb.FromHex("#ff0000")).With("grassLow", Rgb.FromHex("#ff0000")).With("grassHigh", Rgb.FromHex("#ff0000")).With("grassTip", Rgb.FromHex("#ff0000"));
        Assert.NotEqual(Grass.Build(bothOwn, 1).Colours, Grass.Build(red, 1).Colours);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void NewGrassStartsTheFirstGrassStartingPoint()
    {
        MainWindow w = Ui.Open(new FakePainter());
        CommandListTests.Run(w, "new-grass");
        Ui.Pump();
        Assert.Equal("grass", w.Files.Recipe);
    }
}
