using Silk.NET.OpenGL;

namespace PropGenerator.Desktop.View;

/// <summary>Where the rock's program keeps its uniforms, found once when it is linked.</summary>
/// <param name="Mvp">uMvp: world to clip space.</param>
/// <param name="ShadowMatrix">uShadowMatrix: world to the shadow map.</param>
/// <param name="NormalBias">uNormalBias.</param>
/// <param name="Mode">uMode: 0 lit, 1 flat, 2 wireframe.</param>
/// <param name="Base">uBase: the material's colour, times the vertex colours.</param>
/// <param name="Wire">uWire: the wireframe's colour.</param>
/// <param name="HemiSky">uHemiSky.</param>
/// <param name="HemiGround">uHemiGround.</param>
/// <param name="SunDir">uSunDir.</param>
/// <param name="SunColour">uSunColour.</param>
/// <param name="FillDir">uFillDir.</param>
/// <param name="FillColour">uFillColour.</param>
/// <param name="ShadowOn">uShadowOn.</param>
/// <param name="ShadowStrength">uShadowStrength.</param>
/// <param name="ShadowBias">uShadowBias.</param>
/// <param name="ShadowRadius">uShadowRadius.</param>
/// <param name="ShadowSize">uShadowSize.</param>
/// <param name="ShadowMap">uShadowMap.</param>
/// <param name="Finish">The shared finish's uniforms.</param>
/// <param name="Offset">uOffset: where a mesh made about its own origin stands (the person for scale); zero for the rest.</param>
/// <param name="WindStrength">uWindStrength: how strongly the wind moves this mesh; 0 for the ground and the person.</param>
/// <param name="WindTime">uWindTime: the seconds the wind has blown.</param>
/// <param name="WindHeight">uWindHeight: the prop's height over its pivot.</param>
/// <param name="WindReach">uWindReach: how far out the prop reaches from its pivot.</param>
/// <param name="WindFoliage">uWindFoliage: 1 when the prop flutters as well as sways.</param>
/// <param name="Map">uMap: the texture unit of the picture printed points read.</param>
/// <param name="AlphaCut">uAlphaCut: where the printed picture's alpha cuts.</param>
/// <param name="Bark">uBark: the bark's brush strokes.</param>
/// <param name="Relief">uRelief: the bark's relief, a normal map.</param>
/// <param name="Model">uModel: turns the mesh before it is placed (the ground's slope); the identity for the rest.</param>
internal readonly record struct RockUniforms(
    int Mvp,
    int ShadowMatrix,
    int NormalBias,
    int Mode,
    int Base,
    int Wire,
    int HemiSky,
    int HemiGround,
    int SunDir,
    int SunColour,
    int FillDir,
    int FillColour,
    int ShadowOn,
    int ShadowStrength,
    int ShadowBias,
    int ShadowRadius,
    int ShadowSize,
    int ShadowMap,
    FinishUniforms Finish,
    int Offset,
    int WindStrength,
    int WindTime,
    int WindHeight,
    int WindReach,
    int WindFoliage,
    int Map,
    int AlphaCut,
    int Bark,
    int Relief,
    int Model)
{
    /// <summary>Finds the uniforms in the linked rock program.</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="p">The program.</param>
    /// <returns>Their locations.</returns>
    internal static RockUniforms Find(GL g, uint p) => new(
        g.GetUniformLocation(p, "uMvp"),
        g.GetUniformLocation(p, "uShadowMatrix"),
        g.GetUniformLocation(p, "uNormalBias"),
        g.GetUniformLocation(p, "uMode"),
        g.GetUniformLocation(p, "uBase"),
        g.GetUniformLocation(p, "uWire"),
        g.GetUniformLocation(p, "uHemiSky"),
        g.GetUniformLocation(p, "uHemiGround"),
        g.GetUniformLocation(p, "uSunDir"),
        g.GetUniformLocation(p, "uSunColour"),
        g.GetUniformLocation(p, "uFillDir"),
        g.GetUniformLocation(p, "uFillColour"),
        g.GetUniformLocation(p, "uShadowOn"),
        g.GetUniformLocation(p, "uShadowStrength"),
        g.GetUniformLocation(p, "uShadowBias"),
        g.GetUniformLocation(p, "uShadowRadius"),
        g.GetUniformLocation(p, "uShadowSize"),
        g.GetUniformLocation(p, "uShadowMap"),
        FinishUniforms.Find(g, p),
        g.GetUniformLocation(p, "uOffset"),
        g.GetUniformLocation(p, "uWindStrength"),
        g.GetUniformLocation(p, "uWindTime"),
        g.GetUniformLocation(p, "uWindHeight"),
        g.GetUniformLocation(p, "uWindReach"),
        g.GetUniformLocation(p, "uWindFoliage"),
        g.GetUniformLocation(p, "uMap"),
        g.GetUniformLocation(p, "uAlphaCut"),
        g.GetUniformLocation(p, "uBark"),
        g.GetUniformLocation(p, "uRelief"),
        g.GetUniformLocation(p, "uModel"));
}
