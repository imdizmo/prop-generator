using Silk.NET.OpenGL;

namespace PropGenerator.Desktop.View;

/// <summary>
/// A mesh in OpenGL: its vertex array, its vertex buffer and its index buffer. The index buffer holds the triangles
/// and, for a rock, after them each triangle's three edges as lines (the wireframe), so one vertex array draws both.
/// </summary>
/// <param name="Vao">The vertex array.</param>
/// <param name="Vbo">The vertex buffer.</param>
/// <param name="Ebo">The index buffer.</param>
/// <param name="Count">How many triangle corners (or line ends, for lines) there are.</param>
/// <param name="EdgeCount">How many line ends the wireframe has after the triangles (0 when there is none).</param>
/// <param name="Lines">True when the mesh is lines rather than triangles.</param>
internal readonly record struct GlMesh(uint Vao, uint Vbo, uint Ebo, uint Count, uint EdgeCount, bool Lines)
{
    /// <summary>Uploads a mesh with texture coordinates: position at attribute 0, UV at 1.</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="m">The mesh.</param>
    /// <returns>The uploaded mesh.</returns>
    public static unsafe GlMesh Upload(GL g, Mesh m)
    {
        int n = m.Positions.Length / 3;
        float[] interleaved = new float[n * 5];
        for (int i = 0; i < n; i++)
        {
            interleaved[(i * 5) + 0] = m.Positions[(i * 3) + 0];
            interleaved[(i * 5) + 1] = m.Positions[(i * 3) + 1];
            interleaved[(i * 5) + 2] = m.Positions[(i * 3) + 2];
            interleaved[(i * 5) + 3] = m.Uvs[(i * 2) + 0];
            interleaved[(i * 5) + 4] = m.Uvs[(i * 2) + 1];
        }

        uint[] indices = new uint[m.Indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = (uint)m.Indices[i];
        }

        (uint vao, uint vbo, uint ebo) = Buffers(g, interleaved, indices);
        g.EnableVertexAttribArray(0);
        g.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)0);
        g.EnableVertexAttribArray(1);
        g.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)(3 * sizeof(float)));
        return Done(g, vao, vbo, ebo, (uint)indices.Length, 0, lines: false);
    }

    /// <summary>
    /// Uploads a coloured mesh: position at attribute 0, normal at 1, colour at 2, where a printed vertex reads the map at
    /// 3 when the mesh has <see cref="ColouredMesh.TexCoords"/> (left off otherwise, so it reads 0, 0, 0), and how much it
    /// flutters at 4 when it has <see cref="ColouredMesh.Flutter"/> (left off otherwise, so it reads 0), and its shine
    /// (1 - roughness) at 5 when it has <see cref="ColouredMesh.Roughness"/> (left off otherwise, so it reads 0: fully
    /// rough); with its wireframe edges when asked.
    /// </summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="m">The mesh.</param>
    /// <param name="edges">True to add each triangle's edges as lines, for the wireframe.</param>
    /// <returns>The uploaded mesh.</returns>
    public static unsafe GlMesh UploadRock(GL g, ColouredMesh m, bool edges)
    {
        (float[] interleaved, int stride, int shine) = InterleaveRock(m);
        uint attributes = m.TexCoords is null && m.Flutter is null && m.Roughness is null ? 3u : 4u;
        int corners = m.Indices.Length;
        uint[] indices = new uint[edges ? corners * 3 : corners];
        for (int i = 0; i < corners; i++)
        {
            indices[i] = (uint)m.Indices[i];
        }

        if (edges)
        {
            // Each triangle a, b, c gives the lines a-b, b-c, c-a, as three.js's wireframe does.
            for (int t = 0, at = corners; t < corners; t += 3, at += 6)
            {
                uint a = indices[t], b = indices[t + 1], c = indices[t + 2];
                indices[at] = a;
                indices[at + 1] = b;
                indices[at + 2] = b;
                indices[at + 3] = c;
                indices[at + 4] = c;
                indices[at + 5] = a;
            }
        }

        (uint vao, uint vbo, uint ebo) = Buffers(g, interleaved, indices);
        for (uint a = 0; a < attributes; a++)
        {
            g.EnableVertexAttribArray(a);
            g.VertexAttribPointer(a, 3, VertexAttribPointerType.Float, false, (uint)(stride * sizeof(float)), (void*)(a * 3 * sizeof(float)));
        }

        if (m.Flutter is not null)
        {
            g.EnableVertexAttribArray(4);
            g.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, (uint)(stride * sizeof(float)), (void*)(12 * sizeof(float)));
        }

        if (shine >= 0)
        {
            g.EnableVertexAttribArray(5);
            g.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, (uint)(stride * sizeof(float)), (void*)(shine * sizeof(float)));
        }

        return Done(g, vao, vbo, ebo, (uint)corners, edges ? (uint)(corners * 2) : 0, lines: false);
    }

    /// <summary>
    /// A coloured mesh's vertices as <see cref="UploadRock"/> lays them out: position, normal and colour, three numbers
    /// each; then where it reads the map (zeros when it has no <see cref="ColouredMesh.TexCoords"/>) when it has a map, a
    /// flutter or a roughness; then how much it flutters (zero when it has no <see cref="ColouredMesh.Flutter"/>) when it
    /// has a flutter or a roughness; then its shine, 1 - roughness, when it has a <see cref="ColouredMesh.Roughness"/>.
    /// </summary>
    /// <param name="m">The mesh.</param>
    /// <returns>The numbers, how many make one vertex, and where its shine is (-1: none).</returns>
    internal static (float[] Data, int Stride, int Shine) InterleaveRock(ColouredMesh m)
    {
        ArgumentNullException.ThrowIfNull(m);
        int n = m.Positions.Length / 3;
        float[]? tex = m.TexCoords;
        float[]? flutter = m.Flutter;
        float[]? rough = m.Roughness;
        bool mapped = tex is not null || flutter is not null || rough is not null;
        bool fluttered = flutter is not null || rough is not null;
        int stride = (mapped ? 12 : 9) + (fluttered ? 1 : 0) + (rough is null ? 0 : 1);
        int shine = rough is null ? -1 : 13;
        float[] interleaved = new float[n * stride];
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < 3; k++)
            {
                interleaved[(i * stride) + k] = m.Positions[(i * 3) + k];
                interleaved[(i * stride) + 3 + k] = m.Normals[(i * 3) + k];
                interleaved[(i * stride) + 6 + k] = m.Colours[(i * 3) + k];
                if (tex is not null)
                {
                    interleaved[(i * stride) + 9 + k] = tex[(i * 3) + k];
                }
            }

            if (flutter is not null)
            {
                interleaved[(i * stride) + 12] = flutter[i];
            }

            if (rough is not null)
            {
                interleaved[(i * stride) + shine] = 1 - Math.Clamp(rough[i], 0, 1);
            }
        }

        return (interleaved, stride, shine);
    }

    /// <summary>Uploads triangles with positions only (attribute 0), for see-through overlays such as the collision prisms.</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="positions">Three floats per corner.</param>
    /// <param name="triangles">Three corners per triangle.</param>
    /// <returns>The uploaded triangles.</returns>
    public static unsafe GlMesh UploadTriangles(GL g, float[] positions, uint[] triangles)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(triangles);
        (uint vao, uint vbo, uint ebo) = Buffers(g, positions, triangles);
        g.EnableVertexAttribArray(0);
        g.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        return Done(g, vao, vbo, ebo, (uint)triangles.Length, 0, lines: false);
    }

    /// <summary>Uploads line segments: position at attribute 0, two points per line.</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="positions">Three floats per point.</param>
    /// <returns>The uploaded lines.</returns>
    public static unsafe GlMesh UploadLines(GL g, float[] positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        uint[] indices = new uint[positions.Length / 3];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = (uint)i;
        }

        (uint vao, uint vbo, uint ebo) = Buffers(g, positions, indices);
        g.EnableVertexAttribArray(0);
        g.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        return Done(g, vao, vbo, ebo, (uint)indices.Length, 0, lines: true);
    }

    /// <summary>Draws the triangles (or the lines, for lines).</summary>
    /// <param name="g">The OpenGL context.</param>
    public unsafe void Draw(GL g)
    {
        if (Vao == 0)
        {
            return;
        }

        g.BindVertexArray(Vao);
        g.DrawElements(Lines ? PrimitiveType.Lines : PrimitiveType.Triangles, Count, DrawElementsType.UnsignedInt, (void*)0);
    }

    /// <summary>Draws the wireframe edges kept after the triangles (nothing when there are none).</summary>
    /// <param name="g">The OpenGL context.</param>
    public unsafe void DrawEdges(GL g)
    {
        if (Vao == 0 || EdgeCount == 0)
        {
            return;
        }

        g.BindVertexArray(Vao);
        g.DrawElements(PrimitiveType.Lines, EdgeCount, DrawElementsType.UnsignedInt, (void*)(Count * sizeof(uint)));
    }

    /// <summary>Frees the buffers (nothing for an empty mesh).</summary>
    /// <param name="g">The OpenGL context.</param>
    public void Delete(GL g)
    {
        if (Vao == 0)
        {
            return;
        }

        g.DeleteVertexArray(Vao);
        g.DeleteBuffer(Vbo);
        g.DeleteBuffer(Ebo);
    }

    private static (uint Vao, uint Vbo, uint Ebo) Buffers(GL g, float[] vertices, uint[] indices)
    {
        uint vao = g.GenVertexArray();
        g.BindVertexArray(vao);
        uint vbo = g.GenBuffer();
        g.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        g.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);
        uint ebo = g.GenBuffer();
        g.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        g.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, indices, BufferUsageARB.StaticDraw);
        return (vao, vbo, ebo);
    }

    private static GlMesh Done(GL g, uint vao, uint vbo, uint ebo, uint count, uint edges, bool lines)
    {
        g.BindVertexArray(0);
        g.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        return new GlMesh(vao, vbo, ebo, count, edges, lines);
    }
}
