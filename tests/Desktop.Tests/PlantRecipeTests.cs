using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>Flowers in the app, from easy-props' engine: their starting points, their mesh in both builds, the rows they hide and their own colours.</summary>
public class PlantRecipeTests
{
    private static IMeshRecipe Plant => MeshRecipes.Find("plant")!;

    private static Settings Pampas => Plant.StartingPoints.Single(p => p.Id == "pampas").Settings;

    [Fact]
    public void SixStartingPointsUnderFlowersAfterGrass()
    {
        Assert.Equal(
            ["flower-clump", "daisies", "flame-flowers", "red-spikes", "violet-spikes", "pampas"],
            Plant.StartingPoints.Select(p => p.Id));
        Assert.All(Plant.StartingPoints, p => Assert.Equal(("Flowers", "plant"), (p.Group, p.Recipe)));
        Assert.Same(EngineRecipes.For("plant"), Plant);
        Assert.Equal(MeshRecipes.All.ToList().IndexOf(MeshRecipes.Find("grass")!) + 1, MeshRecipes.All.ToList().IndexOf(Plant));
        Assert.Equal(("Flowers", "plant"), (Plant.Label, Plant.ExportName));
        Assert.True(Plant.LowPoly);
        Assert.Equal(new WindSway(Foliage: true, Evenly: true), Plant.Wind);
        Assert.Empty(Plant.Collision(Pampas));
    }

    [Theory]
    [InlineData("painted")]
    [InlineData("low-poly")]
    public void BuildIsTheEnginesPlantTurnedZUp(string build)
    {
        Settings s = Pampas.With("build", build);
        ColouredMesh mesh = Plant.Build(s, 1);
        EngineKit.Close(EngineKit.PlacedZUp(EngineKit.Parts(EngineKit.Recipe("plant"), s, 1)), mesh.Positions);
        Assert.Contains($"\"build\":\"{build}\"", EngineKit.Recipe("plant").SceneJson(s), StringComparison.Ordinal);
    }

    [Fact]
    public void HidesTheStyleAndTheOwnColoursUntilChosen()
    {
        var normal = Plant.HiddenKeys(Pampas);
        Assert.Contains("build", normal);
        Assert.Contains("flowerDeep", normal);
        Assert.DoesNotContain("kind", normal);
        Assert.DoesNotContain("headSize", normal);
        Assert.DoesNotContain("leafKind", normal);

        // Low-poly hides the kind of leaves too, as easy-props 0.4.5 does.
        Assert.Equal(
            normal.Append("leafKind").Order(StringComparer.Ordinal),
            Plant.HiddenKeys(Pampas.With("build", "low-poly")).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OwnColoursStartFromThePaletteAndReachTheMesh()
    {
        Settings before = Pampas;
        Settings own = Plant.Adjust(before, before.With("palette", "custom"));
        Assert.DoesNotContain("flowerDeep", Plant.HiddenKeys(own));
        Assert.Equal(EngineKit.Named("plant", "flowerDeep", "cream"), own.GetColour("flowerDeep"));

        Settings red = own.With("flowerDeep", Rgb.FromHex("#ff0000")).With("flowerMiddle", Rgb.FromHex("#ff0000")).With("flowerLight", Rgb.FromHex("#ff0000"));
        Assert.NotEqual(Plant.Build(own, 1).Colours, Plant.Build(red, 1).Colours);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ThePampasTileStartsAPlant()
    {
        MainWindow w = Ui.Open(new FakePainter());
        w.Drawer.ChooseTab("Flowers");
        Assert.Equal(["Pink blooms", "Daisies", "Flame flowers", "Red spikes", "Violet spikes", "Pampas plumes"], w.Drawer.VisibleTileNames);
        ClaudeKit.Run(w.Drawer.ClickTileAsync("Pampas plumes"));
        Ui.Pump();
        Assert.Equal("plant", w.Files.Recipe);
    }
}
