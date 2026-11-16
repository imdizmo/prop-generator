using System.Numerics;
using Silk.NET.OpenGL;

namespace PropGenerator.Desktop.View;

/// <summary>
/// Where a program keeps the uniforms of the shaders' shared finish (TileShader.Finish): the exposure, the fog and the
/// camera. Found once when the program is linked, so a frame looks nothing up by name.
/// </summary>
/// <param name="Exposure">uExposure.</param>
/// <param name="FogColour">uFogColour.</param>
/// <param name="Fog">uFog: where the fog starts and ends.</param>
/// <param name="Eye">uEye: the camera's position.</param>
/// <param name="Forward">uForward: the way the camera looks.</param>
internal readonly record struct FinishUniforms(int Exposure, int FogColour, int Fog, int Eye, int Forward)
{
    /// <summary>Finds the uniforms in a linked program.</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="program">The program.</param>
    /// <returns>Their locations.</returns>
    internal static FinishUniforms Find(GL g, uint program) => new(
        g.GetUniformLocation(program, "uExposure"),
        g.GetUniformLocation(program, "uFogColour"),
        g.GetUniformLocation(program, "uFog"),
        g.GetUniformLocation(program, "uEye"),
        g.GetUniformLocation(program, "uForward"));

    /// <summary>Sets them, for the program in use: easy-props' exposure and fog, and the camera.</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="eye">Where the camera is.</param>
    /// <param name="forward">The unit vector the camera looks along.</param>
    /// <param name="fogColour">The fog's colour, sRGB-encoded.</param>
    internal void Set(GL g, Vector3 eye, Vector3 forward, Vector3 fogColour)
    {
        g.Uniform1(Exposure, SceneLook.Exposure);
        g.Uniform3(FogColour, fogColour.X, fogColour.Y, fogColour.Z);
        g.Uniform2(Fog, SceneLook.FogNear, SceneLook.FogFar);
        g.Uniform3(Eye, eye.X, eye.Y, eye.Z);
        g.Uniform3(Forward, forward.X, forward.Y, forward.Z);
    }
}
