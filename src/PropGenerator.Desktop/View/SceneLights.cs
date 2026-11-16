// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Numerics;

namespace PropGenerator.Desktop.View;

/// <summary>
/// The lights of the scene, as easy-props sets them from the scene and the sun: a hemisphere light (sky above, ground
/// below), the sun and a fill from the side opposite the sun. Colours are linear; directions point from the piece
/// towards the light, Z up.
/// </summary>
/// <param name="HemisphereSky">The hemisphere light's colour from above.</param>
/// <param name="HemisphereGround">The hemisphere light's colour from below.</param>
/// <param name="HemisphereIntensity">The hemisphere light's strength.</param>
/// <param name="SunColour">The sunlight's colour.</param>
/// <param name="SunIntensity">The sun's strength.</param>
/// <param name="SunDirection">Towards the sun, of length one.</param>
/// <param name="FillColour">The fill light's colour.</param>
/// <param name="FillIntensity">The fill light's strength.</param>
/// <param name="FillDirection">Towards the fill light, of length one.</param>
/// <param name="Shadow">How dark the sun's shadow is, from 0 to 1.</param>
/// <param name="CastsShadow">True when the sun casts a shadow at all (the shadow is above 0.02).</param>
internal readonly record struct SceneLights(
    Vector3 HemisphereSky,
    Vector3 HemisphereGround,
    float HemisphereIntensity,
    Vector3 SunColour,
    float SunIntensity,
    Vector3 SunDirection,
    Vector3 FillColour,
    float FillIntensity,
    Vector3 FillDirection,
    float Shadow,
    bool CastsShadow);
