// Adapted from easy-props (MIT, © 2026 AHS12).
using Silk.NET.OpenGL;

namespace PropGenerator.Desktop.View;

/// <summary>
/// The sky behind everything: easy-props' vertical gradient across the view (a flat colour for Neutral grey), drawn
/// first, with one triangle that covers the view, so it needs no clearing colour.
/// </summary>
internal sealed class Sky
{
    private uint _program;
    private uint _vao;
    private int _top;
    private int _horizon;
    private int _height;
    private int _horizonAt;

    /// <summary>Gets a value indicating whether the sky's program is linked.</summary>
    internal bool Ready => _program != 0;

    /// <summary>Links the sky's program.</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="head">The shader version line(s).</param>
    /// <exception cref="InvalidOperationException">The shaders did not compile or link.</exception>
    internal void Init(GL g, string head)
    {
        Forget();
        _program = TileView.Link(g, head + TileShader.ComposeVertex, head + TileShader.SkyFragment);
        _vao = g.GenVertexArray();
        _top = g.GetUniformLocation(_program, "uTop");
        _horizon = g.GetUniformLocation(_program, "uHorizon");
        _height = g.GetUniformLocation(_program, "uHeight");
        _horizonAt = g.GetUniformLocation(_program, "uHorizonAt");
    }

    /// <summary>Draws the sky over the whole view, writing no depth.</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="colours">The scene's colours.</param>
    /// <param name="height">The view's height in pixels.</param>
    internal void Draw(GL g, SceneColours colours, int height)
    {
        if (_program == 0)
        {
            return;
        }

        g.Disable(EnableCap.DepthTest);
        g.DepthMask(false);
        g.UseProgram(_program);
        g.Uniform3(_top, colours.SkyTop.X, colours.SkyTop.Y, colours.SkyTop.Z);
        g.Uniform3(_horizon, colours.SkyHorizon.X, colours.SkyHorizon.Y, colours.SkyHorizon.Z);
        g.Uniform1(_height, (float)height);
        g.Uniform1(_horizonAt, SceneLook.HorizonAt);
        g.BindVertexArray(_vao);
        g.DrawArrays(PrimitiveType.Triangles, 0, 3);
        g.BindVertexArray(0);
        g.DepthMask(true);
        g.Enable(EnableCap.DepthTest);
    }

    /// <summary>Forgets the OpenGL handles without freeing them (their context is gone).</summary>
    internal void Forget()
    {
        _program = 0;
        _vao = 0;
    }

    /// <summary>Frees the program.</summary>
    /// <param name="g">The OpenGL context.</param>
    internal void Delete(GL g)
    {
        if (_program != 0)
        {
            g.DeleteProgram(_program);
            g.DeleteVertexArray(_vao);
        }

        _program = 0;
        _vao = 0;
    }
}
