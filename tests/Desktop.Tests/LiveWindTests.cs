using System.Numerics;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Desktop.View;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>Wind in the view, as easy-props' live wind: how strong it is now, eased, and the prop's measured height and reach.</summary>
public class LiveWindTests
{
    private static readonly WindSway Grass = new(Foliage: true, Evenly: true);

    // RF4: wind switched on with a still prop (a rock, the floor) moves nothing.
    [Fact]
    public void AStillPropNeverMoves()
    {
        var wind = new LiveWind { On = true, Wanted = 1, Sway = null };
        for (int i = 0; i < 20; i++)
        {
            wind.Step(0.1);
        }

        Assert.Equal(0, wind.Strength);
        Assert.False(wind.Moving);
    }

    // From review: a rock shown after swaying grass must not bend while the wind eases out.
    [Fact]
    public void AStillPropShownAfterGrassStopsAtOnce()
    {
        var wind = new LiveWind { On = true, Wanted = 1, Sway = Grass };
        wind.Step(0.1);
        Assert.True(wind.Strength > 0);
        wind.Sway = null;
        Assert.Equal(0, wind.Strength);
        Assert.False(wind.Moving);
    }

    [Fact]
    public void WindOffMovesNothing()
    {
        var wind = new LiveWind { On = false, Wanted = 1, Sway = Grass };
        wind.Step(0.1);
        Assert.Equal(0, wind.Strength);
        Assert.False(wind.Moving);
    }

    [Fact]
    public void GrassEasesInToTheChosenStrengthAndOutAgain()
    {
        var wind = new LiveWind { On = true, Wanted = 1.5, Sway = Grass };
        wind.Step(0.1);

        // easy-props eases by three times the elapsed seconds: a tenth of a second closes 30 % of the gap.
        Assert.Equal(0.45, wind.Strength, 6);
        Assert.True(wind.Moving);
        for (int i = 0; i < 100; i++)
        {
            wind.Step(0.1);
        }

        Assert.Equal(1.5, wind.Strength, 3);
        wind.On = false;
        for (int i = 0; i < 100; i++)
        {
            wind.Step(0.1);
        }

        Assert.Equal(0, wind.Strength);
        Assert.False(wind.Moving);
    }

    [Fact]
    public void ALongPauseEasesNoFasterThanATenthOfASecond()
    {
        var wind = new LiveWind { On = true, Wanted = 1, Sway = Grass };
        wind.Step(5);
        Assert.Equal(0.3, wind.Strength, 6);
    }

    [Fact]
    public void GrassReachesEvenlyAndATreeToItsFarthestCorner()
    {
        var wind = new LiveWind { Sway = Grass };
        wind.Measure(new Vector3(-1, -2, 0), new Vector3(3, 1, 0.7f));
        Assert.Equal(0.7f, wind.Height);
        Assert.Equal(1e6f, wind.Reach);

        wind.Sway = new WindSway(Foliage: true, Evenly: false);
        wind.Measure(new Vector3(-1, -2, 0), new Vector3(3, 1, 0.7f));
        Assert.Equal(MathF.Sqrt(10), wind.Reach, 5);
    }

    [Fact]
    public void ThePrefsKeepWindAndItsStrength()
    {
        using var s = new Scratch();
        var prefs = new Prefs(s.P("prefs.json")) { Wind = true, WindStrength = 1.7 };
        prefs.Save();
        var again = new Prefs(s.P("prefs.json"));
        Assert.True(again.Wind);
        Assert.Equal(1.7, again.WindStrength);
        Assert.False(new Prefs(s.P("none.json")).Wind);
        Assert.Equal(1, new Prefs(s.P("none.json")).WindStrength);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheWindCommandSwitchesItAndRemembersIt()
    {
        using var s = new Scratch();
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        Assert.False(w.ViewState.Wind);
        CommandListTests.Run(w, "wind");
        Assert.True(w.ViewState.Wind);
        Assert.True(new Prefs(s.P("prefs.json")).Wind);
        Assert.True(w.View.Wind.On);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void GrassSwaysAndARockStandsStill()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(EngineKit.Recipe("grass").StartingPoints[0])));
        Ui.Pump();
        Assert.Equal(Grass, w.View.Wind.Sway);
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(RockRecipe.StartingPoints[0])));
        Ui.Pump();
        Assert.Null(w.View.Wind.Sway);
    }
}
