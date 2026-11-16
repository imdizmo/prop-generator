using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>Bushes in the app, from easy-props' engine: their starting points, their mesh with its cut-out leaves, the rows they hide, their wind and collision.</summary>
public class BushRecipeTests
{
    private static IMeshRecipe Bush => MeshRecipes.Find("bush")!;

    private static Settings Round => Bush.StartingPoints.Single(p => p.Id == "round-bush").Settings;

    [Fact]
    public void TwentyStartingPointsUnderBushesAfterFlowers()
    {
        Assert.Equal(20, Bush.StartingPoints.Count);
        Assert.Equal(EngineHost.Default.Catalog.Presets.Presets.Where(p => p.GeneratorId == "bush").Select(p => p.Id), Bush.StartingPoints.Select(p => p.Id));
        Assert.All(Bush.StartingPoints, p => Assert.Equal(("Bushes", "bush"), (p.Group, p.Recipe)));
        Assert.Same(EngineRecipes.For("bush"), Bush);
        Assert.Equal(MeshRecipes.All.ToList().IndexOf(MeshRecipes.Find("plant")!) + 1, MeshRecipes.All.ToList().IndexOf(Bush));
        Assert.Equal(("Bush", "bush"), (Bush.Label, Bush.ExportName));
        Assert.Equal(new WindSway(Foliage: true, Evenly: false), Bush.Wind);
        Assert.True(Bush.LowPoly);
    }

    [Fact]
    public void ItsCollisionIsTheCapsuleEasyPropsOffers()
    {
        var shape = Assert.Single(Bush.Collision(Round));
        EngineCapsule offered = Assert.Single(EngineKit.Collision(EngineKit.Recipe("bush"), Round).Offered);
        Assert.Equal(offered.End[1], shape.End.Y, 12);
        Assert.Equal(offered.StartRadius, shape.R0, 12);
        Assert.Equal(1.8, shape.End.Y, 12);
        Assert.Equal((3 + 2.6) / 4 * 0.85, shape.R0, 12);
    }

    // The leaves are printed: each of their vertices reads the leaf atlas where the engine's UVs say, and its alpha cuts
    // them out at 0.5. The core is drawn in its colours alone.
    [Fact]
    public void TheLeavesCarryTheirUvsAndTheLeafAtlasCutOutAtAHalf()
    {
        Settings s = Round;
        ColouredMesh mesh = Bush.Build(s, 1);
        EnginePart[] parts = [.. EngineKit.Parts(EngineKit.Recipe("bush"), s, 1).Parts.Where(p => !p.Proxy)];
        EnginePart[] leaves = [.. parts.Where(p => p.Surface.Map == "leaf")];
        Assert.NotEmpty(leaves);

        Assert.NotNull(mesh.Texture);
        Assert.Equal((512, 512, 0.5f), (mesh.Texture!.Width, mesh.Texture.Height, mesh.Texture.AlphaCut));
        Assert.Equal(512 * 512 * 4, mesh.Texture.Rgba.Length);
        Assert.Contains(mesh.Texture.Rgba.Where((_, i) => i % 4 == 3), a => a == 0);
        float[] tex = mesh.TexCoords!;
        Assert.Equal(mesh.Positions.Length / 3 * 3, tex.Length);
        Assert.Equal(parts.Sum(p => p.VertexCount), mesh.Positions.Length / 3);
        int at = 0;
        foreach (EnginePart part in parts)
        {
            bool printed = part.Surface.Map == "leaf";
            for (int i = 0; i < part.VertexCount; i++, at++)
            {
                if (printed)
                {
                    Assert.Equal((part.Uvs![i * 2], part.Uvs[(i * 2) + 1], 1f), (tex[at * 3], tex[(at * 3) + 1], tex[(at * 3) + 2]));
                }
                else
                {
                    Assert.Equal(0f, tex[(at * 3) + 2]);
                }
            }
        }
    }

    // The drawer's picture cuts the leaves out too: printed, it differs from the same mesh with its cards drawn whole.
    [Fact]
    public void TheDrawerPictureCutsTheLeavesOut()
    {
        ColouredMesh mesh = Bush.Build(Round, 0.12);
        Assert.NotEqual(
            PropGenerator.Desktop.Pictures.MeshThumb.Png(mesh with { Texture = null }, 64),
            PropGenerator.Desktop.Pictures.MeshThumb.Png(mesh, 64));
    }

    [Fact]
    public void ALowPolyBushAndARockPrintNothing()
    {
        ColouredMesh lowPoly = Bush.Build(Round.With("build", "low-poly"), 1);
        Assert.Null(lowPoly.TexCoords);
        Assert.Null(lowPoly.Texture);

        ColouredMesh rock = RockRecipe.Build(RockRecipe.StartingPoints[0].Settings);
        Assert.Null(rock.TexCoords);
        Assert.Null(rock.Texture);
    }

    [Fact]
    public void HidesTheStyleAlwaysAndTheLeafCardRowsWhenLowPoly()
    {
        var normal = Bush.HiddenKeys(Round);
        Assert.Contains("build", normal);
        Assert.DoesNotContain("fullness", normal);
        Assert.Contains("leafShadow", normal);

        var lowPoly = Bush.HiddenKeys(Round.With("build", "low-poly"));
        Assert.Contains("inside", lowPoly);
        Assert.Contains("fullness", lowPoly);
        Assert.Contains("leafSize", lowPoly);
        Assert.DoesNotContain("flowers", lowPoly);
    }
}
