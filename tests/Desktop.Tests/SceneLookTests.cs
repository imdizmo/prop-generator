using System.Numerics;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.View;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class SceneLookTests
{
    private static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
    {
        Assert.True(
            Vector3.Distance(expected, actual) <= tolerance * 1.8f
            && MathF.Abs(expected.X - actual.X) <= tolerance
            && MathF.Abs(expected.Y - actual.Y) <= tolerance
            && MathF.Abs(expected.Z - actual.Z) <= tolerance,
            $"expected {expected}, got {actual}");
    }

    private static Vector3 Hex(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);

    [Fact]
    public void SceneColoursFollowREF()
    {
        SceneColours sky = SceneLook.Colours("studio-sky");
        Near(Hex(0x3f, 0x97, 0xea), sky.SkyTop);
        Near(Hex(0xcf, 0xe7, 0xf8), sky.SkyHorizon);
        Near(Hex(0xcf, 0xe7, 0xf8), sky.Fog);
        Near(Hex(0xae, 0xb3, 0xa8), sky.Ground);
        Assert.True(sky.Bright);

        SceneColours studio = SceneLook.Colours("studio");
        Near(Hex(0x43, 0x52, 0x5e), studio.SkyTop);
        Near(Hex(0x93, 0xa1, 0x89), studio.SkyHorizon);
        Near(Hex(0x93, 0xa1, 0x89), studio.Fog);
        Near(Hex(0x4c, 0x56, 0x47), studio.Ground);
        Assert.False(studio.Bright);

        SceneColours grey = SceneLook.Colours("grey");
        Near(Hex(0x6f, 0x71, 0x74), grey.SkyTop);
        Near(Hex(0x6f, 0x71, 0x74), grey.SkyHorizon);
        Near(Hex(0x6f, 0x71, 0x74), grey.Fog);
        Near(Hex(0x4c, 0x56, 0x47), grey.Ground);
        Assert.False(grey.Bright);

        Assert.Equal(22f, SceneLook.FogNear);
        Assert.Equal(60f, SceneLook.FogFar);
        Assert.Equal(0.62f, SceneLook.HorizonAt);
        Assert.Equal(50f, SceneLook.GroundRadius);
        Near(Hex(0x5c, 0x66, 0x56), SceneLook.MinorGridColour);
        Near(Hex(0x6d, 0x78, 0x64), SceneLook.MajorGridColour);
        Assert.Equal(0.38f, SceneLook.MinorGridOpacity);
        Assert.Equal(0.55f, SceneLook.MajorGridOpacity);
    }

    [Fact]
    public void SrgbToLinearIsTheExactCurve()
    {
        Near(new Vector3(0, 1, 0.21404114f), SceneLook.ToLinear(new Vector3(0, 1, 0.5f)), 1e-6f);
        Near(new Vector3(0.0030353f), SceneLook.ToLinear(new Vector3(0.0392157f)), 1e-6f);
    }

    // Expected values worked out from easy-props' lighting.ts and create-scene.ts in Python, not from this code.
    [Fact]
    public void LightsFollowREFsFormulas()
    {
        var midday = new Lighting(52, 46, 0.55, 0.8);
        SceneLights studio = SceneLook.Lights("studio", midday);
        Near(new Vector3(0.635750f, 0.643305f, 0.585618f), studio.HemisphereSky);
        Near(new Vector3(0.102242f, 0.068478f, 0.039546f), studio.HemisphereGround);
        Assert.Equal(1.59f, studio.HemisphereIntensity, 4);
        Near(new Vector3(1f, 0.655030f, 0.401099f), studio.SunColour);
        Assert.Equal(2.895f, studio.SunIntensity, 4);
        Near(new Vector3(0.623960f, 0.745404f, 0.871367f), studio.FillColour);
        Assert.Equal(0.55f, studio.FillIntensity, 4);
        Near(new Vector3(-0.682889f, 0.533531f, 0.499007f), studio.FillDirection);
        Assert.Equal(0.8f, studio.Shadow, 4);
        Assert.True(studio.CastsShadow);

        SceneLights bright = SceneLook.Lights("studio-sky", midday);
        Assert.Equal(2.544f, bright.HemisphereIntensity, 4);
        Near(new Vector3(1f, 0.829772f, 0.652697f), bright.SunColour);
        Assert.Equal(2.895f, bright.SunIntensity, 4);
        Assert.Equal(studio.HemisphereSky, bright.HemisphereSky);

        var golden = new Lighting(232, 18, 1, 0.01);
        SceneLights warm = SceneLook.Lights("studio", golden);
        Near(new Vector3(0.693872f, 0.584078f, 0.391572f), warm.HemisphereSky);
        Near(new Vector3(0.147027f, 0.084376f, 0.039546f), warm.HemisphereGround);
        Assert.Equal(1.87f, warm.HemisphereIntensity, 4);
        Near(new Vector3(1f, 0.450786f, 0.107023f), warm.SunColour);
        Assert.Equal(3.3f, warm.SunIntensity, 4);
        Near(new Vector3(0.726380f, -0.567510f, 0.387691f), warm.FillDirection);
        Assert.False(warm.CastsShadow);

        SceneLights warmBright = SceneLook.Lights("studio-sky", golden);
        Assert.Equal(2.992f, warmBright.HemisphereIntensity, 4);
        Near(new Vector3(1f, 0.768499f, 0.564474f), warmBright.SunColour);
        Assert.Equal(SceneLook.Lights("grey", golden), warm);
    }

    [Fact]
    public void SunDirectionFromAzimuthAndElevation()
    {
        Near(new Vector3(0.547398f, -0.427674f, 0.719340f), SceneLook.SunDirection(new Lighting(52, 46, 0.55, 0.8)));
        Near(new Vector3(-0.749443f, 0.585529f, 0.309017f), SceneLook.SunDirection(new Lighting(232, 18, 1, 0.8)));

        // Azimuth 0 is easy-props' +Z (towards the default camera): our −Y. 90 is +X. The elevation is above the XY plane.
        Near(new Vector3(0, -MathF.Cos(0.5f), MathF.Sin(0.5f)), SceneLook.SunDirection(new Lighting(0, 0.5 * 180 / Math.PI, 0, 0)));
        Near(new Vector3(MathF.Cos(0.5f), 0, MathF.Sin(0.5f)), SceneLook.SunDirection(new Lighting(90, 0.5 * 180 / Math.PI, 0, 0)));
    }

    // From three.js's NeutralToneMapping at exposure 1.15, worked out in Python.
    [Fact]
    public void NeutralToneMapMatchesKhronos()
    {
        Near(new Vector3(0.535000f), SceneLook.NeutralToneMap(new Vector3(0.5f)));
        Near(new Vector3(0.929756f, 0.478754f, 0.102919f), SceneLook.NeutralToneMap(new Vector3(1.2f, 0.6f, 0.1f)));
        Near(new Vector3(0.046827f, 0.012327f, 0.000827f), SceneLook.NeutralToneMap(new Vector3(0.05f, 0.02f, 0.01f)));
        Assert.Equal(1.15f, SceneLook.Exposure);
    }

    [Fact]
    public void ShadowBoxFollowsTheBoundingSphere()
    {
        var sun = Vector3.Normalize(new Vector3(1, -1, 1));
        var small = ShadowBox.For(new Vector3(1, 2, 0.5f), 0.8f, sun);
        Assert.Equal(2f, small.HalfSize);
        Assert.Equal(0.1f, small.Near);
        Assert.Equal(-0.0002f, small.Bias);
        Assert.Equal(0.035f, small.NormalBias, 6);
        Assert.Equal(12f, Vector3.Distance(small.Eye, small.Centre), 4);
        Assert.Equal(new Vector3(1, 2, 0.5f), small.Centre);

        // At least the sun's distance plus three half-sizes: the whole sphere is inside, past its centre too.
        Assert.True(small.Far >= 12f + (3 * 2f));

        // Every preset sun, down to Dusk (11°) and the lowest the dial allows (4°): the ground shadow of the top of a
        // small rock still falls inside the map.
        foreach (double elevation in new[] { 46, 30, 18, 11, 4 })
        {
            Vector3 sunAt = SceneLook.SunDirection(new Lighting(232, elevation, 1, 0.8));
            var low = ShadowBox.For(new Vector3(0, 0, 0.5f), 0.8f, sunAt);
            Vector3 away = -Vector3.Normalize(new Vector3(sunAt.X, sunAt.Y, 0));
            float reach = 1.3f / MathF.Tan((float)elevation * MathF.PI / 180f);
            var tip = Vector4.Transform(new Vector4(away * reach, 1), low.Lookup);
            Assert.InRange(tip.Z / tip.W, 0f, 1f);
            Assert.InRange(tip.X / tip.W, 0f, 1f);
            Assert.InRange(tip.Y / tip.W, 0f, 1f);
        }

        var big = ShadowBox.For(Vector3.Zero, 10, sun);
        Assert.Equal(10f, big.HalfSize);
        Assert.Equal(60f, big.Far, 4);
        Assert.Equal(30f, Vector3.Distance(big.Eye, big.Centre), 4);
        Assert.Equal(20f / 2048f * 8f, big.NormalBias, 6);
        Assert.Equal(2048, ShadowBox.MapSize);
        Assert.Equal(3f, ShadowBox.Radius);

        // The centre lands in the middle of the map; a point a half-size to the side lands on its edge.
        var mid = Vector4.Transform(new Vector4(big.Centre, 1), big.Lookup);
        Assert.Equal(0.5f, mid.X / mid.W, 4);
        Assert.Equal(0.5f, mid.Y / mid.W, 4);
        Assert.InRange(mid.Z / mid.W, 0.5f, 1f);
        Vector3 side = Vector3.Normalize(Vector3.Cross(sun, Vector3.UnitZ)) * 10;
        var edge = Vector4.Transform(new Vector4(side, 1), big.Lookup);
        float u = edge.X / edge.W, v = edge.Y / edge.W;
        Assert.True(MathF.Abs(u - 0.5f) > 0.499f || MathF.Abs(v - 0.5f) > 0.499f, $"({u}, {v})");
    }

    [Fact]
    public void AShadowMapForgetsTheHandlesOfALostContext()
    {
        var map = new ShadowMap(texture: 5, fbo: 6, program: 7);
        Assert.Equal(5u, map.Texture);
        Assert.True(map.Ready);
        Assert.False(map.Stale);

        map.Forget();
        Assert.Equal(0u, map.Texture);
        Assert.Equal(0u, map.Framebuffer);
        Assert.False(map.Ready);
        Assert.True(map.Stale);
    }
}
