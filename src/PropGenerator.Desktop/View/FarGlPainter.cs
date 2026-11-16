// Adapted from easy-props frontend/src/core/export/far-painter.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Export;
using Silk.NET.OpenGL;

namespace PropGenerator.Desktop.View;

/// <summary>
/// Draws the far-away card's pictures with the view's own OpenGL, as easy-props' WebGL far painter does: the prop at
/// full detail seen straight on from the front, right, back and left, framed by the card's box, twice as large and
/// shrunk by averaging; once in its colours (sRGB, unlit, cut out where its leaves are, its bark printed with its brush
/// strokes) and once as its relief (each normal in the view's own axes, the bark's bent by its own relief as the main
/// view bends it). The four views go into the cells of one picture, which the card's corners read. Runs on the view's
/// OpenGL thread only, except <see cref="Binds"/>.
/// </summary>
internal sealed class FarGlPainter
{
    /// <summary>The texture unit the printed picture (the atlas) is read from.</summary>
    internal const int MapUnit = 0;

    /// <summary>The texture unit the bark's brush strokes are read from.</summary>
    internal const int BarkUnit = 1;

    /// <summary>The texture unit the bark's relief is read from.</summary>
    internal const int ReliefUnit = 2;

    private const int Supersample = 2;

    private const string Vertex = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec3 aColour;
        layout(location = 3) in vec3 aTex;
        uniform vec3 uRight;
        uniform vec3 uFacing;
        uniform vec3 uFrame;
        out vec3 vPos;
        out vec3 vNormal;
        out vec3 vColour;
        out vec3 vTex;
        void main()
        {
            vPos = aPos;
            vNormal = aNormal;
            vColour = aColour;
            vTex = aTex;
            // Orthographic and Z-up: across is along the view's right, up is z, depth is toward the viewer.
            float across = dot(aPos, uRight) / uFrame.x;
            float up = (aPos.z - uFrame.y) / (uFrame.z - uFrame.y) * 2.0 - 1.0;
            float depth = -dot(aPos, uFacing) / (uFrame.x * 2.0 + 2.0);
            gl_Position = vec4(across, up, depth, 1.0);
        }
        """;

    private const string Colour = """
        uniform sampler2D uMap;
        uniform sampler2D uBark;
        uniform int uHasBark;
        uniform float uCutoff;
        in vec3 vPos;
        in vec3 vNormal;
        in vec3 vColour;
        in vec3 vTex;
        out vec4 fragColour;
        vec3 toSrgb(vec3 linear)
        {
            return mix(linear * 12.92, 1.055 * pow(linear, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, linear));
        }
        void main()
        {
            vec3 colour = vColour;
            // Both read outside the branches, so the bark's mipmaps see whole quads.
            vec4 texel = texture(uMap, vTex.xy);
            vec3 strokes = texture(uBark, vTex.xy).rgb;
            if (vTex.z > 1.5)
            {
                // Bark, as the main view prints it: its strokes over its colour, never cut out.
                if (uHasBark == 1)
                {
                    colour *= strokes;
                }
            }
            else if (vTex.z > 0.5)
            {
                if (texel.a < uCutoff)
                {
                    discard;
                }

                colour *= texel.rgb;
            }

            fragColour = vec4(toSrgb(clamp(colour, 0.0, 1.0)), 1.0);
        }
        """;

    private const string Relief = """
        uniform sampler2D uMap;
        uniform sampler2D uRelief;
        uniform int uHasRelief;
        uniform float uCutoff;
        uniform vec3 uRight;
        uniform vec3 uFacing;
        in vec3 vPos;
        in vec3 vNormal;
        in vec3 vColour;
        in vec3 vTex;
        out vec4 fragColour;
        // three.js's getTangentFrame, as the main view's: the surface's frame from screen derivatives.
        mat3 tangentFrame(vec3 pos, vec3 n, vec2 uv)
        {
            vec3 q0 = dFdx(pos);
            vec3 q1 = dFdy(pos);
            vec2 st0 = dFdx(uv);
            vec2 st1 = dFdy(uv);
            vec3 q1perp = cross(q1, n);
            vec3 q0perp = cross(n, q0);
            vec3 t = q1perp * st0.x + q0perp * st1.x;
            vec3 b = q1perp * st0.y + q0perp * st1.y;
            float det = max(dot(t, t), dot(b, b));
            float scale = det == 0.0 ? 0.0 : inversesqrt(det);
            return mat3(t * scale, b * scale, n);
        }
        void main()
        {
            // Read outside the branches, so the derivatives and the relief's mipmaps see whole quads.
            float alpha = texture(uMap, vTex.xy).a;
            vec3 bent = texture(uRelief, vTex.xy).xyz * 2.0 - 1.0;
            vec3 normal = normalize(vNormal);
            mat3 tbn = tangentFrame(vPos, normal, vTex.xy);
            if (vTex.z > 0.5 && vTex.z < 1.5 && alpha < uCutoff)
            {
                discard;
            }

            if (vTex.z > 1.5 && uHasRelief == 1)
            {
                // The bark's relief bends it as the main view's does (wood shows only its outside, so no flip).
                normal = normalize(tbn * bent);
            }

            vec3 local = vec3(dot(normal, uRight), normal.z, dot(normal, uFacing));
            local.z = max(local.z, 0.1);
            fragColour = vec4(normalize(local) * 0.5 + 0.5, 1.0);
        }
        """;

    private uint _colour;
    private uint _relief;

    /// <summary>Compiles the two programs; false when this OpenGL cannot (the card is then left out).</summary>
    /// <param name="g">The view's OpenGL.</param>
    /// <param name="head">The shader version line the view uses.</param>
    /// <returns>True when ready.</returns>
    internal bool Init(GL g, string head)
    {
        try
        {
            _colour = TileView.Link(g, head + Vertex, head + Colour);
            _relief = TileView.Link(g, head + Vertex, head + Relief);
            return true;
        }
        catch (InvalidOperationException e)
        {
            TraceLog.Write($"The far-away card's painter could not start: {e.Message}");
            Delete(g);
            return false;
        }
    }

    /// <summary>Frees the programs.</summary>
    /// <param name="g">The view's OpenGL.</param>
    internal void Delete(GL g)
    {
        if (_colour != 0)
        {
            g.DeleteProgram(_colour);
        }

        if (_relief != 0)
        {
            g.DeleteProgram(_relief);
        }

        _colour = _relief = 0;
    }

    /// <summary>
    /// Says which of a mesh's pictures the card's painter reads, on which texture unit and how each is uploaded: the
    /// printed picture (sRGB, not repeating) on <see cref="MapUnit"/>, and, when some triangles are bark (texCoord z 2),
    /// the bark's strokes (sRGB, repeating) on <see cref="BarkUnit"/> and its relief (linear, repeating) on
    /// <see cref="ReliefUnit"/>, as the main view reads them. A slot the mesh has no picture for is left out.
    /// </summary>
    /// <param name="mesh">The prop at full detail.</param>
    /// <returns>The pictures to bind, by unit; the slot is the name of the mesh's property that holds it.</returns>
    internal static IReadOnlyList<(int Unit, string Slot, bool Srgb, bool Repeat)> Binds(ColouredMesh mesh)
    {
        List<(int Unit, string Slot, bool Srgb, bool Repeat)> binds = [];
        if (mesh.Texture is not null)
        {
            binds.Add((MapUnit, nameof(ColouredMesh.Texture), true, false));
        }

        if (HasBark(mesh.TexCoords))
        {
            if (mesh.Bark is not null)
            {
                binds.Add((BarkUnit, nameof(ColouredMesh.Bark), true, true));
            }

            if (mesh.Relief is not null)
            {
                binds.Add((ReliefUnit, nameof(ColouredMesh.Relief), false, true));
            }
        }

        return binds;
    }

    /// <summary>
    /// Draws the card's pictures of a mesh already on the GPU. The caller binds its own framebuffer and viewport back
    /// afterwards, and frees the pictures it uploaded.
    /// </summary>
    /// <param name="g">The view's OpenGL.</param>
    /// <param name="mesh">The prop at full detail, uploaded as the view uploads a rock (Z-up).</param>
    /// <param name="pictures">Its pictures on the GPU, by unit and slot as <see cref="Binds"/> lists them.</param>
    /// <param name="cutoff">Where the printed picture's alpha cuts.</param>
    /// <param name="frame">The card's box (Y-up, as the export's).</param>
    /// <param name="viewSize">Pixels across each view.</param>
    /// <returns>The pictures, or null when the programs are not ready or the framebuffer cannot be made.</returns>
    internal unsafe FarPictures? Paint(GL g, GlMesh mesh, IReadOnlyList<(int Unit, string Slot, uint Texture)> pictures, float cutoff, FarFrame frame, int viewSize)
    {
        if (_colour == 0 || _relief == 0)
        {
            return null;
        }

        int drawn = viewSize * Supersample;
        int size = viewSize * 2;
        byte[] colour = new byte[size * size * 4];
        byte[] relief = new byte[size * size * 4];
        byte[] samples = new byte[drawn * drawn * 4];
        uint fbo = g.GenFramebuffer();
        uint texture = g.GenTexture();
        uint depth = g.GenRenderbuffer();
        try
        {
            g.BindTexture(TextureTarget.Texture2D, texture);
            g.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)drawn, (uint)drawn, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
            g.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
            g.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)drawn, (uint)drawn);
            g.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            g.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
            g.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depth);
            if (g.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            {
                return null;
            }

            g.Viewport(0, 0, (uint)drawn, (uint)drawn);
            g.Enable(EnableCap.DepthTest);
            g.DepthFunc(DepthFunction.Less);
            g.Disable(EnableCap.CullFace);
            g.Disable(EnableCap.Blend);
            g.ClearColor(0, 0, 0, 0);
            Bind(g, pictures);
            bool hasBark = pictures.Any(p => p.Slot == nameof(ColouredMesh.Bark));
            bool hasRelief = pictures.Any(p => p.Slot == nameof(ColouredMesh.Relief));
            foreach ((string _, double[] facing, double[] right, int[] cell) in FarCard.Views)
            {
                // The export's Y-up axes in the view's Z-up ones: (x, y, z) becomes (x, -z, y).
                float[] f = [(float)facing[0], (float)-facing[2], (float)facing[1]];
                float[] r = [(float)right[0], (float)-right[2], (float)right[1]];
                foreach ((uint program, byte[] picture, bool keepAlpha) in new[] { (_colour, colour, true), (_relief, relief, false) })
                {
                    g.UseProgram(program);
                    g.Uniform3(g.GetUniformLocation(program, "uRight"), r[0], r[1], r[2]);
                    g.Uniform3(g.GetUniformLocation(program, "uFacing"), f[0], f[1], f[2]);
                    g.Uniform3(g.GetUniformLocation(program, "uFrame"), (float)frame.Reach, (float)frame.Bottom, (float)frame.Top);
                    g.Uniform1(g.GetUniformLocation(program, "uCutoff"), Math.Max(cutoff, 1e-3f));
                    g.Uniform1(g.GetUniformLocation(program, "uMap"), MapUnit);
                    g.Uniform1(g.GetUniformLocation(program, "uBark"), BarkUnit);
                    g.Uniform1(g.GetUniformLocation(program, "uRelief"), ReliefUnit);
                    g.Uniform1(g.GetUniformLocation(program, "uHasBark"), hasBark ? 1 : 0);
                    g.Uniform1(g.GetUniformLocation(program, "uHasRelief"), hasRelief ? 1 : 0);
                    g.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    mesh.Draw(g);
                    g.PixelStore(PixelStoreParameter.PackAlignment, 1);
                    fixed (byte* at = samples)
                    {
                        g.ReadPixels(0, 0, (uint)drawn, (uint)drawn, PixelFormat.Rgba, PixelType.UnsignedByte, at);
                    }

                    ShrinkInto(samples, drawn, picture, size, cell[0] * viewSize, cell[1] * viewSize, keepAlpha);
                }
            }
        }
        finally
        {
            Bind(g, []);
            g.BindTexture(TextureTarget.Texture2D, 0);
            g.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            g.DeleteFramebuffer(fbo);
            g.DeleteTexture(texture);
            g.DeleteRenderbuffer(depth);
            g.UseProgram(0);
        }

        FarCard.FinishPictures(colour, relief, size);
        return new FarPictures(size, colour, relief);
    }

    // Binds each picture on its unit and clears the units it has none for (the view's own textures stay bound
    // elsewhere otherwise); unit 0 is left active.
    private static void Bind(GL g, IReadOnlyList<(int Unit, string Slot, uint Texture)> pictures)
    {
        foreach (int unit in (int[])[ReliefUnit, BarkUnit, MapUnit])
        {
            g.ActiveTexture(TextureUnit.Texture0 + unit);
            g.BindTexture(TextureTarget.Texture2D, pictures.FirstOrDefault(p => p.Unit == unit).Texture);
        }
    }

    // True when some vertex reads the bark (texCoord z 2).
    private static bool HasBark(float[]? texCoords)
    {
        if (texCoords is null)
        {
            return false;
        }

        for (int i = 2; i < texCoords.Length; i += 3)
        {
            if (texCoords[i] > 1.5f)
            {
                return true;
            }
        }

        return false;
    }

    // Averages each square of samples into one pixel of the picture's cell, weighted by how much of it is covered
    // (easy-props' shrinkInto); the colour keeps the coverage as its alpha, the relief none.
    private static void ShrinkInto(byte[] source, int sourceSize, byte[] target, int targetSize, int left, int bottom, bool keepAlpha)
    {
        int cell = sourceSize / Supersample;
        Span<double> sum = stackalloc double[3];
        for (int y = 0; y < cell; y++)
        {
            for (int x = 0; x < cell; x++)
            {
                sum.Clear();
                double weight = 0;
                for (int dy = 0; dy < Supersample; dy++)
                {
                    for (int dx = 0; dx < Supersample; dx++)
                    {
                        int from = ((((y * Supersample) + dy) * sourceSize) + (x * Supersample) + dx) * 4;
                        double alpha = source[from + 3] / 255.0;
                        sum[0] += source[from] * alpha;
                        sum[1] += source[from + 1] * alpha;
                        sum[2] += source[from + 2] * alpha;
                        weight += alpha;
                    }
                }

                int at = ((((bottom + y) * targetSize) + left + x) * 4);
                for (int channel = 0; channel < 3; channel++)
                {
                    target[at + channel] = weight > 0 ? (byte)Math.Round(sum[channel] / weight, MidpointRounding.AwayFromZero) : (byte)0;
                }

                target[at + 3] = keepAlpha ? (byte)Math.Round(weight / (Supersample * Supersample) * 255, MidpointRounding.AwayFromZero) : (byte)0;
            }
        }
    }
}
