// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Globalization;
using System.Numerics;
using PropGenerator.Desktop.Commands;

namespace PropGenerator.Desktop.View;

/// <summary>
/// easy-props' scene around the piece as plain numbers: the sky, the fog, the ground and its grid, the lights for a
/// scene and a sun, and the tone mapping. Its directions are easy-props' Y-up ones turned to our Z up:
/// (x, y, z) becomes (x, −z, y).
/// </summary>
internal static class SceneLook
{
    /// <summary>How much brighter the light is made before it is tone mapped.</summary>
    internal const float Exposure = 1.15f;

    /// <summary>Where the fog starts, in metres in front of the camera.</summary>
    internal const float FogNear = 22f;

    /// <summary>Where the fog has hidden everything, in metres in front of the camera.</summary>
    internal const float FogFar = 60f;

    /// <summary>How far down the view the sky reaches its horizon colour (0 is the top).</summary>
    internal const float HorizonAt = 0.62f;

    /// <summary>The ground disc's radius, in metres.</summary>
    internal const float GroundRadius = 50f;

    /// <summary>How many segments the ground disc's rim has.</summary>
    internal const int GroundSegments = 64;

    /// <summary>The minor grid's size in metres (its lines are 1 m apart).</summary>
    internal const float MinorGridSize = 40f;

    /// <summary>How many squares the minor grid has across.</summary>
    internal const int MinorGridDivisions = 40;

    /// <summary>How opaque the minor grid's lines are.</summary>
    internal const float MinorGridOpacity = 0.38f;

    /// <summary>The major grid's size in metres (its lines are 10 m apart).</summary>
    internal const float MajorGridSize = 100f;

    /// <summary>How many squares the major grid has across.</summary>
    internal const int MajorGridDivisions = 10;

    /// <summary>How opaque the major grid's lines are.</summary>
    internal const float MajorGridOpacity = 0.55f;

    /// <summary>How high the minor grid sits above the ground, so it is not lost in it.</summary>
    internal const float MinorGridHeight = 0.002f;

    /// <summary>How high the major grid sits above the ground.</summary>
    internal const float MajorGridHeight = 0.004f;

    /// <summary>Below this shadow strength the sun casts no shadow.</summary>
    internal const float ShadowCutoff = 0.02f;

    /// <summary>A clear midday sun in a bright scene, however warm the time of day is.</summary>
    private const float BrightWarmth = 0.3f;

    /// <summary>A bright scene's big sky: the shade stays coloured and never goes dark.</summary>
    private const float BrightSkyLight = 1.6f;

    /// <summary>Gets the minor grid's line colour (sRGB).</summary>
    internal static Vector3 MinorGridColour { get; } = Srgb("#5c6656");

    /// <summary>Gets the major grid's line colour (sRGB).</summary>
    internal static Vector3 MajorGridColour { get; } = Srgb("#6d7864");

    /// <summary>Gets the colour wireframe lines are drawn in (sRGB).</summary>
    internal static Vector3 WireframeColour { get; } = Srgb("#c3d6bd");

    /// <summary>Reads a colour written as #rrggbb into sRGB components from 0 to 1.</summary>
    /// <param name="hex">The colour.</param>
    /// <returns>Red, green and blue.</returns>
    /// <exception cref="FormatException">Not #rrggbb.</exception>
    internal static Vector3 Srgb(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length != 7 || hex[0] != '#')
        {
            throw new FormatException($"'{hex}' is not a colour written as #rrggbb.");
        }

        float Part(int at) => int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
        return new Vector3(Part(1), Part(3), Part(5));
    }

    /// <summary>Turns sRGB-encoded components into linear light, with the exact sRGB curve (as three.js does).</summary>
    /// <param name="srgb">The encoded colour.</param>
    /// <returns>The linear colour.</returns>
    internal static Vector3 ToLinear(Vector3 srgb)
    {
        static float One(float c) => c < 0.04045f ? c * 0.0773993808f : MathF.Pow((c * 0.9478672986f) + 0.0521327014f, 2.4f);
        return new Vector3(One(srgb.X), One(srgb.Y), One(srgb.Z));
    }

    /// <summary>The colours of a scene: Studio, Studio with sky or Neutral grey (any other name is Studio).</summary>
    /// <param name="scene">One of <see cref="ViewState.Scenes"/>.</param>
    /// <returns>The scene's sky, fog and ground.</returns>
    internal static SceneColours Colours(string scene)
    {
        Vector3 studioGround = Srgb("#4c5647");
        return scene switch
        {
            "studio-sky" => new SceneColours(Srgb("#3f97ea"), Srgb("#cfe7f8"), Srgb("#cfe7f8"), Srgb("#aeb3a8"), Bright: true),
            "grey" => new SceneColours(Srgb("#6f7174"), Srgb("#6f7174"), Srgb("#6f7174"), studioGround, Bright: false),
            _ => new SceneColours(Srgb("#43525e"), Srgb("#93a189"), Srgb("#93a189"), studioGround, Bright: false),
        };
    }

    /// <summary>
    /// The direction towards the sun: easy-props' (cos e sin a, sin e, cos e cos a) for azimuth a and elevation e, turned
    /// to Z up, so the elevation is the height above the ground (the XY plane).
    /// </summary>
    /// <param name="lighting">The sun.</param>
    /// <returns>A unit vector from the piece towards the sun.</returns>
    internal static Vector3 SunDirection(Lighting lighting)
    {
        float a = (float)(lighting.Azimuth * Math.PI / 180), e = (float)(lighting.Elevation * Math.PI / 180);
        return Vector3.Normalize(new Vector3(MathF.Cos(e) * MathF.Sin(a), -MathF.Cos(e) * MathF.Cos(a), MathF.Sin(e)));
    }

    /// <summary>
    /// The lights for a scene and a sun, by easy-props' formulas: the hemisphere's sky colour runs from #c6dcea to
    /// #d9c9a8 with the warmth, its ground is #5a4a38 (#6b5238 above warmth 0.6), its strength
    /// (1.15 + (1 − elevation/90) × 0.9), times 1.6 in Studio with sky; the sun runs from #fff4e2 to #ffb35c with the
    /// warmth (times 0.3 in Studio with sky) at 2.4 + warmth × 0.9; the fill is #cfe0f0 at 0.55 from the side opposite
    /// the sun, a little above. Colours are mixed in linear light.
    /// </summary>
    /// <param name="scene">One of <see cref="ViewState.Scenes"/>.</param>
    /// <param name="lighting">The sun and the shadow.</param>
    /// <returns>The lights.</returns>
    internal static SceneLights Lights(string scene, Lighting lighting)
    {
        bool bright = Colours(scene).Bright;
        float warmth = Math.Clamp((float)lighting.Warmth, 0, 1);
        float height = (float)lighting.Elevation / 90f;
        var sky = Vector3.Lerp(ToLinear(Srgb("#c6dcea")), ToLinear(Srgb("#d9c9a8")), warmth);
        Vector3 ground = ToLinear(Srgb(warmth > 0.6f ? "#6b5238" : "#5a4a38"));
        float hemisphere = (1.15f + ((1 - height) * 0.9f)) * (bright ? BrightSkyLight : 1);
        float sunWarmth = Math.Clamp(bright ? warmth * BrightWarmth : warmth, 0, 1);
        var sun = Vector3.Lerp(ToLinear(Srgb("#fff4e2")), ToLinear(Srgb("#ffb35c")), sunWarmth);
        Vector3 toSun = SunDirection(lighting);

        // easy-props puts the fill at (−x, 0.4, −z) of the sun's Y-up direction: in Z up, (−x, −y, 0.4).
        var fill = Vector3.Normalize(new Vector3(-toSun.X, -toSun.Y, 0.4f));
        float shadow = (float)lighting.Shadow;
        return new SceneLights(
            sky, ground, hemisphere, sun, 2.4f + (warmth * 0.9f), toSun, ToLinear(Srgb("#cfe0f0")), 0.55f, fill, shadow,
            shadow > ShadowCutoff);
    }

    /// <summary>
    /// Khronos PBR Neutral tone mapping, exactly as three.js's NeutralToneMapping (the C# twin of the shader's): the
    /// light is first multiplied by the exposure.
    /// Adapted from three.js (MIT, © 2010–2026 three.js authors), after the Khronos PBR Neutral specification
    /// (https://github.com/KhronosGroup/ToneMapping/tree/main/PBR_Neutral).
    /// </summary>
    /// <param name="colour">Linear light.</param>
    /// <param name="exposure">The exposure.</param>
    /// <returns>The tone-mapped linear colour, from 0 to 1.</returns>
    internal static Vector3 NeutralToneMap(Vector3 colour, float exposure = Exposure)
    {
        const float startCompression = 0.8f - 0.04f;
        const float desaturation = 0.15f;
        colour *= exposure;
        float x = MathF.Min(colour.X, MathF.Min(colour.Y, colour.Z));
        float offset = x < 0.08f ? x - (6.25f * x * x) : 0.04f;
        colour -= new Vector3(offset);
        float peak = MathF.Max(colour.X, MathF.Max(colour.Y, colour.Z));
        if (peak < startCompression)
        {
            return colour;
        }

        const float d = 1f - startCompression;
        float newPeak = 1f - (d * d / (peak + d - startCompression));
        colour *= newPeak / peak;
        float g = 1f - (1f / ((desaturation * (peak - newPeak)) + 1f));
        return Vector3.Lerp(colour, new Vector3(newPeak), g);
    }
}
