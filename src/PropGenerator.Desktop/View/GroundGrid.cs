// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace PropGenerator.Desktop.View;

/// <summary>
/// easy-props' ground: a disc of 50 m at Z = 0 (lit and shadowed with the rock's program), and over it two grids of
/// lines, 1 m apart across 40 m and 10 m apart across 100 m, drawn see-through, tone mapped and fogged. The grid stops
/// at the disc's rim (the 100 m grid's corners would otherwise lie over the sky). The disc and both grids turn together
/// through a model matrix, the view's slope (<see cref="GroundTilt"/>).
/// </summary>
internal sealed class GroundGrid
{
    private uint _program;
    private int _mvp;
    private int _model;
    private int _colour;
    private int _opacity;
    private int _radius;
    private FinishUniforms _finish;
    private GlMesh _minor;
    private GlMesh _major;

    /// <summary>Gets the ground disc, uploaded (empty before <see cref="Init"/>).</summary>
    internal GlMesh DiscMesh { get; private set; }

    /// <summary>Gets a value indicating whether the grid's program is linked.</summary>
    internal bool Ready => _program != 0;

    /// <summary>
    /// The ground disc as three.js's CircleGeometry: a centre and a rim of points, flat at Z = 0, facing up, in white
    /// (its colour comes from the scene).
    /// </summary>
    /// <param name="radius">The radius, in metres.</param>
    /// <param name="segments">How many segments the rim has.</param>
    /// <returns>The disc.</returns>
    internal static ColouredMesh Disc(float radius, int segments)
    {
        int n = segments + 1;
        float[] positions = new float[n * 3];
        float[] normals = new float[n * 3];
        float[] colours = new float[n * 3];
        int[] indices = new int[segments * 3];
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                float a = 2 * MathF.PI * (i - 1) / segments;
                positions[3 * i] = radius * MathF.Cos(a);
                positions[(3 * i) + 1] = radius * MathF.Sin(a);
            }

            normals[(3 * i) + 2] = 1;
            colours[3 * i] = colours[(3 * i) + 1] = colours[(3 * i) + 2] = 1;
        }

        for (int s = 0; s < segments; s++)
        {
            indices[3 * s] = 0;
            indices[(3 * s) + 1] = 1 + s;
            indices[(3 * s) + 2] = 1 + ((s + 1) % segments);
        }

        return new ColouredMesh(positions, normals, colours, indices);
    }

    /// <summary>
    /// A grid as three.js's GridHelper: divisions + 1 lines each way across a square of this size, centred on the
    /// origin, at a height.
    /// </summary>
    /// <param name="size">The square's side, in metres.</param>
    /// <param name="divisions">How many squares across.</param>
    /// <param name="z">The height above the ground.</param>
    /// <returns>Two points (three floats each) per line.</returns>
    internal static float[] Lines(float size, int divisions, float z)
    {
        float half = size / 2, step = size / divisions;
        float[] p = new float[(divisions + 1) * 12];
        for (int i = 0; i <= divisions; i++)
        {
            float k = -half + (i * step);
            float[] line = [-half, k, z, half, k, z, k, -half, z, k, half, z];
            line.CopyTo(p, i * 12);
        }

        return p;
    }

    /// <summary>Links the grid's program and uploads the disc and the grids.</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="head">The shader version line(s).</param>
    /// <exception cref="InvalidOperationException">The shaders did not compile or link.</exception>
    internal void Init(GL g, string head)
    {
        Forget();
        _program = TileView.Link(g, head + TileShader.GridVertex, head + TileShader.Finish + TileShader.GridFragment);
        _mvp = g.GetUniformLocation(_program, "uMvp");
        _model = g.GetUniformLocation(_program, "uModel");
        _colour = g.GetUniformLocation(_program, "uColour");
        _opacity = g.GetUniformLocation(_program, "uOpacity");
        _radius = g.GetUniformLocation(_program, "uRadius");
        _finish = FinishUniforms.Find(g, _program);
        DiscMesh = GlMesh.UploadRock(g, Disc(SceneLook.GroundRadius, SceneLook.GroundSegments), edges: false);
        _minor = GlMesh.UploadLines(g, Lines(SceneLook.MinorGridSize, SceneLook.MinorGridDivisions, SceneLook.MinorGridHeight));
        _major = GlMesh.UploadLines(g, Lines(SceneLook.MajorGridSize, SceneLook.MajorGridDivisions, SceneLook.MajorGridHeight));
    }

    /// <summary>Draws both grids, see-through, over what is drawn (they hide behind it where it is nearer).</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="viewProjection">The camera's world to clip space.</param>
    /// <param name="eye">The camera's position.</param>
    /// <param name="forward">The way the camera looks.</param>
    /// <param name="fogColour">The fog's colour, sRGB-encoded.</param>
    /// <param name="model">The ground's turn (the view's slope; the identity on level ground).</param>
    internal void Draw(GL g, Matrix4x4 viewProjection, Vector3 eye, Vector3 forward, Vector3 fogColour, Matrix4x4 model)
    {
        if (_program == 0)
        {
            return;
        }

        g.UseProgram(_program);
        g.UniformMatrix4(_mvp, 1, false, MemoryMarshal.CreateReadOnlySpan(ref viewProjection.M11, 16));
        g.UniformMatrix4(_model, 1, false, MemoryMarshal.CreateReadOnlySpan(ref model.M11, 16));
        _finish.Set(g, eye, forward, fogColour);
        g.Uniform1(_radius, SceneLook.GroundRadius);
        g.Enable(EnableCap.Blend);
        g.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        g.DepthMask(false);
        DrawOne(g, _minor, SceneLook.MinorGridColour, SceneLook.MinorGridOpacity);
        DrawOne(g, _major, SceneLook.MajorGridColour, SceneLook.MajorGridOpacity);
        g.DepthMask(true);
        g.Disable(EnableCap.Blend);
    }

    /// <summary>
    /// Draws see-through faces and their outline in one colour over everything, hidden by nothing (the collision
    /// prisms, as easy-props draws them with depth testing off), with the grid's program: tone mapped, encoded and
    /// fogged as three.js's basic materials are. Only the faces turned to the camera are filled.
    /// </summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="viewProjection">The camera's world to clip space.</param>
    /// <param name="eye">The camera's position.</param>
    /// <param name="forward">The way the camera looks.</param>
    /// <param name="fogColour">The fog's colour, sRGB-encoded.</param>
    /// <param name="faces">The faces (triangles, positions only).</param>
    /// <param name="outline">The outline (lines).</param>
    /// <param name="srgb">The colour, sRGB-encoded.</param>
    /// <param name="faceOpacity">How opaque the faces are.</param>
    /// <param name="lineOpacity">How opaque the outline is.</param>
    internal void DrawOver(
        GL g, Matrix4x4 viewProjection, Vector3 eye, Vector3 forward, Vector3 fogColour, GlMesh faces, GlMesh outline,
        Vector3 srgb, float faceOpacity, float lineOpacity)
    {
        if (_program == 0)
        {
            return;
        }

        g.UseProgram(_program);
        g.UniformMatrix4(_mvp, 1, false, MemoryMarshal.CreateReadOnlySpan(ref viewProjection.M11, 16));
        Matrix4x4 level = Matrix4x4.Identity;
        g.UniformMatrix4(_model, 1, false, MemoryMarshal.CreateReadOnlySpan(ref level.M11, 16));
        _finish.Set(g, eye, forward, fogColour);
        g.Uniform1(_radius, SceneLook.GroundRadius * 4);
        g.Disable(EnableCap.DepthTest);
        g.DepthMask(false);
        g.Enable(EnableCap.Blend);
        g.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        g.Enable(EnableCap.CullFace);
        g.CullFace(TriangleFace.Back);
        DrawOne(g, faces, srgb, faceOpacity);
        g.Disable(EnableCap.CullFace);
        DrawOne(g, outline, srgb, lineOpacity);
        g.Disable(EnableCap.Blend);
        g.DepthMask(true);
        g.Enable(EnableCap.DepthTest);
    }

    /// <summary>Forgets the OpenGL handles without freeing them (their context is gone).</summary>
    internal void Forget()
    {
        _program = 0;
        DiscMesh = default;
        _minor = default;
        _major = default;
    }

    /// <summary>Frees the program, the disc and the grids.</summary>
    /// <param name="g">The OpenGL context.</param>
    internal void Delete(GL g)
    {
        if (_program != 0)
        {
            g.DeleteProgram(_program);
        }

        _program = 0;
        DiscMesh.Delete(g);
        DiscMesh = default;
        _minor.Delete(g);
        _minor = default;
        _major.Delete(g);
        _major = default;
    }

    private void DrawOne(GL g, GlMesh lines, Vector3 srgb, float opacity)
    {
        Vector3 linear = SceneLook.ToLinear(srgb);
        g.Uniform3(_colour, linear.X, linear.Y, linear.Z);
        g.Uniform1(_opacity, opacity);
        lines.Draw(g);
    }
}
