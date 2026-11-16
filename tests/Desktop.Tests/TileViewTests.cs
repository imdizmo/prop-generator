using System.Numerics;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Desktop.View;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class TileViewTests
{
    private const float S = TileMesh.S;

    private static bool Turned(Matrix4x4 m) => m.M11 < 0;

    [Fact]
    public void TheFloorIsAThreeByThreeCheckerOfSquaresThenTwoColumnsOfTrianglePairs()
    {
        var tiles = TileView.FloorLayout().ToList();
        var squares = tiles.Where(t => !t.Triangle).ToList();
        var triangles = tiles.Where(t => t.Triangle).ToList();

        Assert.Equal(9, squares.Count);
        foreach ((_, Matrix4x4 m) in squares)
        {
            int i = (int)MathF.Round(m.M41 / S), j = (int)MathF.Round(m.M42 / S);
            Assert.InRange(i, 0, 2);
            Assert.InRange(j, 0, 2);
            Assert.Equal((i + j) % 2 == 1, Turned(m));
        }

        Assert.Equal(12, triangles.Count);
        foreach (float x in new[] { 3 * S, 4 * S })
        {
            for (int j = 0; j < 3; j++)
            {
                var pair = triangles.Where(t => MathF.Abs(t.Model.M41 - x) < 1e-4 && MathF.Abs(t.Model.M42 - (j * S)) < 1e-4).ToList();
                Assert.Equal(2, pair.Count);
                Assert.Single(pair, t => Turned(t.Model));
            }
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void NewLayersReplaceTheOnesBefore()
    {
        var view = new TileView();
        Assert.Null(view.Layers);
        view.SetLayers(FakePainter.Blank(256));
        Layers full = FakePainter.Blank(1024);
        view.SetLayers(full);
        Assert.Same(full, view.Layers);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AShownPieceIsKeptUntilTheFloorIsShownAgain()
    {
        var view = new TileView();
        Assert.False(view.ShowingPiece);
        Mesh m = TileMesh.Square();
        view.ShowPiece(m);
        Assert.True(view.ShowingPiece);
        Assert.Same(m, view.PieceMesh);
        view.ShowPiece(TileMesh.Triangle());
        Assert.NotSame(m, view.PieceMesh);
        view.ShowFloor();
        Assert.False(view.ShowingPiece);
        Assert.Null(view.PieceMesh);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AMeshWithMismatchedArraysIsRefused()
    {
        var view = new TileView();
        Assert.Throws<ArgumentException>(() => view.ShowPiece(new Mesh([0, 0, 0], [0], [0, 0, 0], [0])));
        Assert.False(view.ShowingPiece);
    }

    private static ColouredMesh Rock(float size) => new(
        [0, 0, 0, size, 0, 0, 0, size, size],
        [0, 0, 1, 0, 0, 1, 0, 0, 1],
        [0.5f, 0.4f, 0.3f, 0.5f, 0.4f, 0.3f, 0.5f, 0.4f, 0.3f],
        [0, 1, 2]);

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ShowRockSwitchesModeAndBackToFloor()
    {
        var view = new TileView();
        Assert.False(view.ShowingRock);
        view.ShowRock(Rock(10), frame: true);
        Assert.True(view.ShowingRock);
        Assert.False(view.ShowingPiece);
        view.ShowFloor();
        Assert.False(view.ShowingRock);
        Assert.False(view.ShowingPiece);
        view.ShowRock(Rock(10), frame: true);
        view.ShowPiece(TileMesh.Square());
        Assert.False(view.ShowingRock);
        Assert.True(view.ShowingPiece);
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ShowRockRejectsBrokenMeshes(int fault)
    {
        var view = new TileView();
        ColouredMesh good = Rock(1);
        ColouredMesh bad = fault switch
        {
            0 => good with { Indices = [0, 1, 3] },
            1 => good with { Indices = [0, 1] },
            2 => good with { Colours = [0, 0, 0] },
            4 => good with { Roughness = [1, 0.5f] },
            _ => good with { Indices = [] },
        };
        Assert.Throws<ArgumentException>(() => view.ShowRock(bad, frame: true));
        Assert.False(view.ShowingRock);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void FramingOnlyWhenAsked()
    {
        var view = new TileView();
        view.ShowRock(Rock(2), frame: true);
        Assert.True(Vector3.Distance(new Vector3(1, 1, 2 * 0.58f), view.Orbit.Target) < 1e-4f);
        Assert.Equal(8f, view.Orbit.Distance);
        view.Orbit.Zoom(0.5f);
        view.Orbit.LookFrom(new Vector3(1, 0, 0));
        var before = (view.Orbit.Target, view.Orbit.Distance, view.Orbit.Theta, view.Orbit.Phi);

        // A slider held down: the rock grows five times, the camera stays.
        view.SetPointerHeld(true);
        view.ShowRock(Rock(10), frame: false);
        Assert.Equal(before, (view.Orbit.Target, view.Orbit.Distance, view.Orbit.Theta, view.Orbit.Phi));

        view.ShowRock(Rock(10), frame: true);
        Assert.True(Vector3.Distance(new Vector3(5, 5, 5.8f), view.Orbit.Target) < 1e-4f);
        Assert.Equal(27f, view.Orbit.Distance, 4);
        view.SetPointerHeld(false);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ARockThatGrewByMoreThanAFifthIsFramedOnceTheButtonIsLetGo()
    {
        var view = new TileView();
        view.ShowRock(Rock(10), frame: true);
        view.Orbit.Zoom(0.5f);
        float zoomed = view.Orbit.Distance;

        // A small change, with no button down: the camera stays.
        view.ShowRock(Rock(11), frame: false);
        Assert.Equal(zoomed, view.Orbit.Distance);

        // A big change while a slider is held: nothing until it is let go.
        view.SetPointerHeld(true);
        view.ShowRock(Rock(20), frame: false);
        Assert.Equal(zoomed, view.Orbit.Distance);
        view.SetPointerHeld(false);
        Assert.True(Vector3.Distance(new Vector3(10, 10, 20 * 0.58f), view.Orbit.Target) < 1e-4f);
        Assert.Equal(54f, view.Orbit.Distance, 4);

        // Framed at 20: back to 17 is less than a fifth, so it stays.
        view.Orbit.Zoom(0.5f);
        float again = view.Orbit.Distance;
        view.ShowRock(Rock(17), frame: false);
        Assert.Equal(again, view.Orbit.Distance);
    }

    [Fact]
    public void TheGroundIsADiscOfFiftyMetresAtZero()
    {
        ColouredMesh g = GroundGrid.Disc(SceneLook.GroundRadius, SceneLook.GroundSegments);
        int n = g.Positions.Length / 3;
        Assert.Equal(65, n);
        Assert.Equal(64 * 3, g.Indices.Length);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(0f, g.Positions[(3 * i) + 2]);
            Assert.Equal(1f, g.Normals[(3 * i) + 2]);
            float r = MathF.Sqrt((g.Positions[3 * i] * g.Positions[3 * i]) + (g.Positions[(3 * i) + 1] * g.Positions[(3 * i) + 1]));
            Assert.Equal(i == 0 ? 0f : 50f, r, 3);
        }

        // Every triangle faces up (counter-clockwise seen from above).
        for (int t = 0; t < g.Indices.Length; t += 3)
        {
            Vector3 P(int k) => new(g.Positions[3 * g.Indices[t + k]], g.Positions[(3 * g.Indices[t + k]) + 1], 0);
            Assert.True(Vector3.Cross(P(1) - P(0), P(2) - P(0)).Z > 0);
        }
    }

    [Fact]
    public void TheGridHasMetreAndTenMetreLines()
    {
        float[] minor = GroundGrid.Lines(SceneLook.MinorGridSize, SceneLook.MinorGridDivisions, SceneLook.MinorGridHeight);
        Assert.Equal(41 * 2 * 2 * 3, minor.Length);
        var xs = Enumerable.Range(0, minor.Length / 3).Select(i => minor[3 * i]).Distinct().Order().ToList();
        Assert.Equal(Enumerable.Range(-20, 41).Select(i => (float)i), xs);
        Assert.All(Enumerable.Range(0, minor.Length / 3), i => Assert.Equal(0.002f, minor[(3 * i) + 2]));

        float[] major = GroundGrid.Lines(SceneLook.MajorGridSize, SceneLook.MajorGridDivisions, SceneLook.MajorGridHeight);
        Assert.Equal(11 * 2 * 2 * 3, major.Length);
        var ys = Enumerable.Range(0, major.Length / 3).Select(i => major[(3 * i) + 1]).Distinct().Order().ToList();
        Assert.Equal(Enumerable.Range(-5, 11).Select(i => i * 10f), ys);
    }

    [Fact]
    public void TheFarCardBindsBarkAndItsReliefBesideTheAtlas()
    {
        ColouredMesh mesh = FarMesh(bark: true) with
        {
            Texture = Picture(0.5f),
            Bark = Picture(0),
            Relief = Picture(0),
        };

        Assert.Equal(
            [
                (FarGlPainter.MapUnit, "Texture", true, false),
                (FarGlPainter.BarkUnit, "Bark", true, true),
                (FarGlPainter.ReliefUnit, "Relief", false, true),
            ],
            FarGlPainter.Binds(mesh));
        Assert.Equal(3, FarGlPainter.Binds(mesh).Select(b => b.Unit).Distinct().Count());
    }

    [Fact]
    public void TheFarCardOfAMeshWithNoBarkBindsOnlyTheAtlas()
    {
        Assert.Equal([(FarGlPainter.MapUnit, "Texture", true, false)], FarGlPainter.Binds(FarMesh(bark: false) with { Texture = Picture(0.5f) }));

        // Bark pictures with no bark triangles to read them are not bound.
        Assert.Equal(
            [(FarGlPainter.MapUnit, "Texture", true, false)],
            FarGlPainter.Binds(FarMesh(bark: false) with { Texture = Picture(0.5f), Bark = Picture(0), Relief = Picture(0) }));
        Assert.Empty(FarGlPainter.Binds(FarMesh(bark: false)));
    }

    [Fact]
    public void TheShaderTakesRoughnessFromTheMesh()
    {
        // The vertex passes each vertex's roughness on (from its shine, 1 - roughness, so a mesh without it reads 0 and
        // is fully rough), and the lit branch reads it instead of a fixed 1: three.js's roughness, floored at 0.0525
        // and widened by the normal's change across the pixel, its square the GGX alpha.
        Assert.Contains("layout(location = 5) in float aShine;", TileShader.RockVertex, StringComparison.Ordinal);
        Assert.Contains("vRough = 1.0 - clamp(aShine, 0.0, 1.0);", TileShader.RockVertex, StringComparison.Ordinal);
        Assert.Contains("in float vRough;", TileShader.RockFragment, StringComparison.Ordinal);
        Assert.Contains("dfgApprox(dotNV, roughness)", TileShader.RockFragment, StringComparison.Ordinal);
        Assert.Contains("max(vRough, 0.0525)", TileShader.RockFragment, StringComparison.Ordinal);
        Assert.Contains("float alpha = roughness * roughness;", TileShader.RockFragment, StringComparison.Ordinal);
        Assert.DoesNotContain("const float alpha = 1.0;", TileShader.RockFragment, StringComparison.Ordinal);
        Assert.DoesNotContain("dfgApprox(dotNV, 1.0)", TileShader.RockFragment, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUploadCarriesEachVertexsShine()
    {
        ColouredMesh rough = Rock(1);
        (float[] plain, int plainStride, int noShine) = GlMesh.InterleaveRock(rough);
        Assert.Equal((9, -1), (plainStride, noShine));
        Assert.Equal(27, plain.Length);

        // With roughness: position, normal, colour, the map (zeros), the flutter (zero), then 1 - roughness.
        (float[] wet, int stride, int shine) = GlMesh.InterleaveRock(rough with { Roughness = [0.25f, 1, 0.56f] });
        Assert.Equal((14, 13), (stride, shine));
        Assert.Equal([0.75f, 0, 0.44f], [wet[shine], wet[stride + shine], wet[(2 * stride) + shine]], new FloatNear());
        Assert.Equal(rough.Colours[..3], wet[6..9]);

        // Beside a flutter and printed coordinates, each in its own place.
        ColouredMesh all = rough with { Roughness = [0.5f, 0.5f, 0.5f], Flutter = [1, 0, 1], TexCoords = [0, 0, 1, 1, 0, 1, 0, 1, 1] };
        (float[] both, int s2, int sh2) = GlMesh.InterleaveRock(all);
        Assert.Equal((14, 13), (s2, sh2));
        Assert.Equal([0f, 0, 1, 1, 0.5f], both[9..14]);
    }

    private sealed class FloatNear : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => Math.Abs(x - y) < 1e-6f;

        public int GetHashCode(float obj) => 0;
    }

    private static MeshTexture Picture(float cut) => new(2, 2, new byte[2 * 2 * 4], cut);

    // One printed triangle and, when asked, one bark triangle.
    private static ColouredMesh FarMesh(bool bark) =>
        new(new float[18], new float[18], new float[18], [0, 1, 2, 3, 4, 5])
        {
            TexCoords = [0, 0, 1, 1, 0, 1, 0, 1, 1, 0, 0, bark ? 2 : 0, 1, 0, bark ? 2 : 0, 0, 1, bark ? 2 : 0],
        };
}
