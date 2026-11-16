using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Desktop.View;
using PropGenerator.Nature.Export;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// The Far away detail level: offered where easy-props offers its far card (ruling 4: the old-style tree and palm, the
/// painted tree, the bush and the column cactus), shown as the export's card.
/// </summary>
[Collection(MeshRecipesAlone.Name)]
public class FarAwayTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FarAwayIsOfferedForAPalmAndNotForGrass()
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(EngineRecipes.PalmDefaults)));
        Assert.Equal(["Level 0 (LOD0)", "Level 1 (LOD1)", "Level 2 (LOD2)", "Far away"], CommandListTests.Find(w, "lod").Children!.Select(c => c.Label));

        CommandListTests.Run(w, "lod-far");
        Assert.Equal(ViewState.FarLevel, w.ViewState.Lod);
        Assert.True(w.Rocks.Far);
        Assert.Equal(1, w.Rocks.Detail);

        // A kind without a card goes back to full detail.
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(MeshRecipes.Find("grass")!.StartingPoints[0])));
        Assert.Equal(0, w.ViewState.Lod);
        Assert.False(w.Rocks.Far);
        Assert.DoesNotContain(CommandListTests.Find(w, "lod").Children!, c => c.Label == "Far away");
        w.Close();
    }

    // Upstream's farAway: the trees, the palm, the bush and the column cactus; not grass, flowers, a barrel cactus or a rock.
    [Fact]
    public void FarAwayIsOfferedWhereEasyPropsOffersItsCard()
    {
        static bool Far(string id, string? point = null)
        {
            IMeshRecipe recipe = MeshRecipes.Find(id)!;
            Settings s = point is not null ? recipe.StartingPoints.Single(p => p.Id == point).Settings
                : recipe.StartingPoints.Count > 0 ? recipe.StartingPoints[0].Settings : EngineRecipes.PalmDefaults.Settings;
            return recipe.FarAway(s);
        }

        Assert.True(Far("palm-tree"));
        Assert.True(Far("branching-tree"));
        Assert.True(Far("painted-tree", "oak"));
        Assert.True(Far("bush", "round-bush"));
        Assert.True(Far("cactus", "saguaro"));
        Assert.False(Far("cactus", "golden-barrel"));
        Assert.False(Far("grass"));
        Assert.False(Far("plant"));
        Assert.False(Far(RockRecipe.Id, "boulder"));
    }

    [Fact]
    public void TheCardIsTheExportsCrossedCardsZUpPrintedWithItsPictures()
    {
        ColouredMesh palm = MeshRecipes.Find("palm-tree")!.Build(EngineRecipes.PalmDefaults.Settings, 1);
        FarFrame frame = FarCardMesh.Frame(palm);
        Assert.True(frame.Top > 1 && frame.Reach > 0.5 && frame.Bottom <= 0);

        var pictures = new FarPictures(512, new byte[512 * 512 * 4], new byte[512 * 512 * 4]);
        ColouredMesh card = FarCardMesh.Card(frame, pictures);
        Assert.Equal(FarCard.Faces, card.Indices.Length / 3);
        Assert.Equal((float)frame.Top, card.Positions.Where((_, i) => i % 3 == 2).Max(), 5);
        Assert.Equal((512, 512, 0.5f), (card.Texture!.Width, card.Texture.Height, card.Texture.AlphaCut));
        Assert.All(card.TexCoords!.Where((_, i) => i % 3 == 2), t => Assert.Equal(1f, t));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FarAwayFollowsTheSettings()
    {
        // A rock that offers its card only while it is taller than 3 m.
        var tall = new SlowRecipe(RockRecipe.Engine, TimeSpan.Zero) { Far = s => s.GetFloat("height") > 3 };
        using IDisposable _ = MeshRecipes.Override(tall);
        MainWindow w = Ui.Open(new FakePainter());
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(RockRecipe.StartingPoints.Single(p => p.Id == "boulder"))));
        Assert.Null(w.Panel.SetFromOutside("height", System.Text.Json.JsonSerializer.SerializeToElement(2.0)));
        Ui.Pump();
        Assert.DoesNotContain(CommandListTests.Find(w, "lod").Children!, c => c.Label == "Far away");

        Assert.Null(w.Panel.SetFromOutside("height", System.Text.Json.JsonSerializer.SerializeToElement(4.0)));
        Ui.Pump();
        Assert.Contains(CommandListTests.Find(w, "lod").Children!, c => c.Label == "Far away");
        CommandListTests.Run(w, "lod-far");
        Assert.Equal(ViewState.FarLevel, w.ViewState.Lod);
        Assert.True(w.Rocks.Far);

        // The setting flips back: the Far level is left for full detail.
        Assert.Null(w.Panel.SetFromOutside("height", System.Text.Json.JsonSerializer.SerializeToElement(2.0)));
        Ui.Pump();
        Assert.Equal(0, w.ViewState.Lod);
        Assert.False(w.Rocks.Far);
        Assert.DoesNotContain(CommandListTests.Find(w, "lod").Children!, c => c.Label == "Far away");
        w.Close();
    }
}
