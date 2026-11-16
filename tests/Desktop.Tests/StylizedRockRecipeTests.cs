using Avalonia.Controls;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Nature.Stylized;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

/// <summary>The stylized rock in the app: the drawer, the panel, the live view and the export.</summary>
public class StylizedRockRecipeTests
{
    private static IMeshRecipe Stylized => MeshRecipes.Find(StylizedRockParameterSet.Id)!;

    private static StartingPoint Rock => Stylized.StartingPoints.Single(p => p.Id == "stylized-rock");

    private static bool Waited(Func<bool> done, int ms = 20000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheStylizedGroupFollowsTheCliffs()
    {
        MainWindow w = Ui.Open(new FakePainter());

        Assert.Contains("Stylized 3", w.Drawer.TabNames);
        w.Drawer.ChooseTab("Stylized");
        Assert.Equal(["Pebble", "Stylized rock", "Stylized boulder"], w.Drawer.VisibleTileNames);
        Assert.All(Stylized.StartingPoints, p => Assert.Equal(("Stylized", "stylized-rock"), (p.Group, p.Recipe)));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void StartingAStylizedRockShowsItsPanelAndBuildsIt()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.True(Run(w.Files.StartFromAsync(Rock), 30000));
        Ui.Pump();

        Assert.Equal(StylizedRockParameterSet.Id, w.Files.Recipe);
        Assert.Equal("Stylized rock", w.PanelHead.Title.Text);
        Assert.Contains(w.PanelHead.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Whole prop");
        Assert.Equal(["Shape", "Mesh", "Colour"], w.Panel.Groups.Select(g => g.Title));
        Assert.Equal(["Shape"], w.Panel.Groups.Where(g => g.IsOpen).Select(g => g.Title));
        Assert.Equal(StylizedRockParameterSet.Instance.Controls.Count, w.Panel.Rows.Count);

        Assert.True(w.View.ShowingRock);
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        StylizedRock rock = StylizedRockGenerator.Build(StylizedRockPresets.All.Single(p => p.Id == "stylized-rock").Settings, 1);
        Assert.Equal(rock.Positions, w.Rocks.Mesh!.Positions);
        Assert.Equal(rock.Colours, w.Rocks.Mesh!.Colours);

        byte[] glb = Stylized.Glb(w.Panel.Current, "pg_test_rock");
        Assert.Equal("glTF", System.Text.Encoding.ASCII.GetString(glb, 0, 4));
        Assert.Single(Stylized.Collision(w.Panel.Current));
        w.Close();
    }
}
