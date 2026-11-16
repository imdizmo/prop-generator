using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>Cacti in the app, from easy-props' engine: their starting points, their mesh, the rows they hide, their own colours and their New command.</summary>
public class CactusRecipeTests
{
    private static IMeshRecipe Cactus => MeshRecipes.Find("cactus")!;

    private static Settings Saguaro => Cactus.StartingPoints.Single(p => p.Id == "saguaro").Settings;

    [Fact]
    public void SevenStartingPointsUnderCactiAfterBushes()
    {
        Assert.Equal(["saguaro", "young-saguaro", "organ-pipe", "golden-barrel", "flowering-barrel", "prickly-pear", "fallen-saguaro"], Cactus.StartingPoints.Select(p => p.Id));
        Assert.All(Cactus.StartingPoints, p => Assert.Equal(("Cacti", "cactus"), (p.Group, p.Recipe)));
        Assert.Same(EngineRecipes.For("cactus"), Cactus);
        Assert.Equal("Cactus", Cactus.Label);
        Assert.Equal("cactus", Cactus.ExportName);
        Assert.Equal(MeshRecipes.All.ToList().IndexOf(MeshRecipes.Find("bush")!) + 1, MeshRecipes.All.ToList().IndexOf(Cactus));
    }

    // easy-props' cactus is still: nothing sways in the view.
    [Fact]
    public void StandsStillInTheWind() => Assert.Null(Cactus.Wind);

    [Theory]
    [InlineData("saguaro")]
    [InlineData("golden-barrel")]
    [InlineData("prickly-pear")]
    public void BuildIsTheEnginesCactusTurnedZUp(string id)
    {
        Settings s = Cactus.StartingPoints.Single(p => p.Id == id).Settings;
        ColouredMesh mesh = Cactus.Build(s, 1);
        EngineKit.Close(EngineKit.PlacedZUp(EngineKit.Parts(EngineKit.Recipe("cactus"), s, 1)), mesh.Positions);
    }

    [Fact]
    public void HidesTheStyleWhatTheKindDoesNotUseAndFlowerColourWithoutFlowers()
    {
        var column = Cactus.HiddenKeys(Saguaro);
        Assert.Contains("build", column);
        Assert.Contains("pads", column);
        Assert.DoesNotContain("arms", column);
        Assert.Contains("flowerColour", column);
        Assert.Contains("flowerDeep", column);
        Assert.Contains("cactusShadow", column);

        var pads = Cactus.HiddenKeys(Saguaro.With("kind", "paddle").With("flowers", 3.0));
        Assert.DoesNotContain("pads", pads);
        Assert.Contains("arms", pads);
        Assert.Contains("ribs", pads);
        Assert.Contains("lean", pads);
        Assert.DoesNotContain("flowerColour", pads);

        var barrel = Cactus.HiddenKeys(Saguaro.With("kind", "barrel"));
        Assert.Contains("armHeight", barrel);
        Assert.DoesNotContain("ribs", barrel);
    }

    [Fact]
    public void BothOwnColourChoicesShowTheirRowsAndStartFromThePalette()
    {
        Settings before = Saguaro.With("flowers", 4.0);
        Settings cactusOwn = Cactus.Adjust(before, before.With("palette", "custom"));
        Assert.DoesNotContain("cactusShadow", Cactus.HiddenKeys(cactusOwn));
        Assert.DoesNotContain("spineColour", Cactus.HiddenKeys(cactusOwn));
        Assert.Contains("flowerDeep", Cactus.HiddenKeys(cactusOwn));
        Assert.Equal(EngineKit.Named("cactus", "cactusShadow", "desert"), cactusOwn.GetColour("cactusShadow"));

        Settings bothOwn = Cactus.Adjust(cactusOwn, cactusOwn.With("flowerColour", "custom"));
        Assert.DoesNotContain("flowerDeep", Cactus.HiddenKeys(bothOwn));
        Assert.Equal(EngineKit.Named("cactus", "flowerDeep", "pink"), bothOwn.GetColour("flowerDeep"));

        // An own colour reaches the mesh.
        Settings red = bothOwn.With("spineColour", Rgb.FromHex("#ff0000"));
        Assert.NotEqual(Cactus.Build(bothOwn, 1).Colours, Cactus.Build(red, 1).Colours);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void NewCactusStartsTheSaguaro()
    {
        MainWindow w = Ui.Open(new FakePainter());
        CommandListTests.Run(w, "new-cactus");
        Ui.Pump();
        Assert.Equal("cactus", w.Files.Recipe);
    }
}
