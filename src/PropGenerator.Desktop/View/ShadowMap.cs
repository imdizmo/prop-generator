// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace PropGenerator.Desktop.View;

/// <summary>
/// The sun's shadow map: a 2048 × 2048 depth texture, read with hardware depth comparison and linear filtering (so each
/// tap is already four), made only when a lit rock with a shadow is first shown and kept until the view's OpenGL goes.
/// It is drawn again only when the rock or the sun changed. As three.js does, the caster's back faces are drawn into
/// it, which keeps the lit side free of acne.
/// </summary>
internal sealed class ShadowMap
{
    private const GLEnum CompareRefToTexture = (GLEnum)0x884E;

    private uint _program;
    private int _mvp;
    private int _map;
    private int _alphaCut;

    /// <summary>Starts with nothing made.</summary>
    internal ShadowMap()
    {
    }

    /// <summary>Starts holding handles made earlier: for the tests, a map whose OpenGL context was lost.</summary>
    /// <param name="texture">The depth texture.</param>
    /// <param name="fbo">Its framebuffer.</param>
    /// <param name="program">The depth program.</param>
    internal ShadowMap(uint texture, uint fbo, uint program)
    {
        Texture = texture;
        Framebuffer = fbo;
        _program = program;
        Stale = false;
    }

    /// <summary>Gets the depth texture (0 until the first shadow is drawn).</summary>
    internal uint Texture { get; private set; }

    /// <summary>Gets or sets a value indicating whether the map must be drawn again before it is used.</summary>
    internal bool Stale { get; set; } = true;

    /// <summary>Gets a value indicating whether the depth program is linked.</summary>
    internal bool Ready => _program != 0;

    /// <summary>Gets the framebuffer the map is drawn through (0 until the first shadow is drawn).</summary>
    internal uint Framebuffer { get; private set; }

    /// <summary>
    /// Forgets every OpenGL handle without freeing it: the context they belonged to is gone (Avalonia then sets the view
    /// up again without a deinit), so the next <see cref="Init"/> and <see cref="Ensure"/> make them afresh.
    /// </summary>
    internal void Forget()
    {
        _program = 0;
        _mvp = 0;
        Framebuffer = 0;
        Texture = 0;
        Stale = true;
    }

    /// <summary>Links the depth-only program (the texture waits until a shadow is wanted).</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="head">The shader version line(s).</param>
    /// <exception cref="InvalidOperationException">The shaders did not compile or link.</exception>
    internal void Init(GL g, string head)
    {
        Forget();
        _program = TileView.Link(g, head + TileShader.DepthVertex, head + TileShader.DepthFragment);
        _mvp = g.GetUniformLocation(_program, "uMvp");
        _map = g.GetUniformLocation(_program, "uMap");
        _alphaCut = g.GetUniformLocation(_program, "uAlphaCut");
        Stale = true;
    }

    /// <summary>Makes the depth texture and its framebuffer, once.</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <returns>Null when it is ready; otherwise why it cannot be made.</returns>
    internal unsafe string? Ensure(GL g)
    {
        if (Texture != 0)
        {
            return null;
        }

        Texture = g.GenTexture();
        g.BindTexture(TextureTarget.Texture2D, Texture);
        g.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24, ShadowBox.MapSize, ShadowBox.MapSize, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, null);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)CompareRefToTexture);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareFunc, (int)DepthFunction.Lequal);
        g.BindTexture(TextureTarget.Texture2D, 0);
        Framebuffer = g.GenFramebuffer();
        g.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
        g.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, Texture, 0);
        GLEnum none = GLEnum.None;
        g.DrawBuffers(1, &none);
        g.ReadBuffer(ReadBufferMode.None);
        GLEnum status = g.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        g.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        Stale = true;
        return status == GLEnum.FramebufferComplete ? null : $"The sun's shadow cannot be drawn: its depth buffer is not usable ({status}).";
    }

    /// <summary>Draws the caster's depth as the sun sees it; the caller binds its own framebuffer again after.</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="box">Where the sun looks from and what it covers.</param>
    /// <param name="caster">The mesh that casts the shadow (its triangles).</param>
    /// <param name="extra">A second caster made about its own origin (the person for scale), or an empty mesh for none.</param>
    /// <param name="extraAt">Where the second caster stands.</param>
    /// <param name="map">The picture the caster's printed points read (0 for none), so a cut-out casts no shadow.</param>
    /// <param name="alphaCut">Where that picture's alpha cuts.</param>
    internal void Render(GL g, ShadowBox box, GlMesh caster, GlMesh extra, Vector3 extraAt, uint map = 0, float alphaCut = 0)
    {
        if (_program == 0 || Texture == 0)
        {
            return;
        }

        Matrix4x4 mvp = box.ViewProjection;
        g.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
        g.Viewport(0, 0, ShadowBox.MapSize, ShadowBox.MapSize);
        g.Enable(EnableCap.DepthTest);
        g.DepthFunc(DepthFunction.Lequal);
        g.DepthMask(true);
        g.ClearDepth(1);
        g.Clear(ClearBufferMask.DepthBufferBit);
        g.Enable(EnableCap.CullFace);
        g.CullFace(TriangleFace.Front);
        g.UseProgram(_program);
        g.UniformMatrix4(_mvp, 1, false, MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
        g.ActiveTexture(TextureUnit.Texture2);
        g.BindTexture(TextureTarget.Texture2D, map);
        g.Uniform1(_map, 2);
        g.Uniform1(_alphaCut, alphaCut);
        g.ActiveTexture(TextureUnit.Texture0);
        caster.Draw(g);
        if (extra.Vao != 0)
        {
            Matrix4x4 moved = Matrix4x4.CreateTranslation(extraAt) * mvp;
            g.UniformMatrix4(_mvp, 1, false, MemoryMarshal.CreateReadOnlySpan(ref moved.M11, 16));
            extra.Draw(g);
        }

        g.BindVertexArray(0);
        g.Disable(EnableCap.CullFace);
        g.CullFace(TriangleFace.Back);
        Stale = false;
    }

    /// <summary>Frees the program, the texture and the framebuffer.</summary>
    /// <param name="g">The OpenGL context.</param>
    internal void Delete(GL g)
    {
        if (_program != 0)
        {
            g.DeleteProgram(_program);
        }

        if (Framebuffer != 0)
        {
            g.DeleteFramebuffer(Framebuffer);
        }

        if (Texture != 0)
        {
            g.DeleteTexture(Texture);
        }

        _program = 0;
        Framebuffer = 0;
        Texture = 0;
        Stale = true;
    }
}
