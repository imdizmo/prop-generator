using System.Numerics;
using System.Runtime.CompilerServices;
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// A finished cliff, ready to draw or export: Z up, in metres, its pivot on the ground line z = 0 (its flat bottom lies
/// Sink below it). Vertices are split along sharp edges, so each has the normal of the faces it shades; three numbers
/// per vertex in each array.
/// </summary>
/// <param name="Positions">x, y, z of each vertex.</param>
/// <param name="Normals">The unit normal of each vertex.</param>
/// <param name="Colours">The linear red, green and blue of each vertex.</param>
/// <param name="Indices">Three vertex indices per triangle, counter-clockwise seen from outside.</param>
/// <param name="Chunks">The cliff's collision chunks: each block's upright boxes (see <see cref="CliffChunk.Of"/>).</param>
public sealed record CliffBlock(float[] Positions, float[] Normals, float[] Colours, int[] Indices, IReadOnlyList<CliffChunk> Chunks)
{
    /// <summary>
    /// The cliff made of its meshed parts: each part's vertices split along every edge whose faces bend more than Sharp
    /// angle and coloured from the palette by facing, by the colour patch where the vertex lies and by cut depth (the
    /// point tags), where the parts lie (the pivot is the ground line z = 0, nothing is lifted).
    /// </summary>
    /// <param name="parts">The parts, each a closed solid; none is empty.</param>
    /// <param name="s">The settings.</param>
    /// <param name="chunks">The cliff's collision chunks: each block's upright boxes (see <see cref="CliffChunk.Of"/>).</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static CliffBlock From(IReadOnlyList<SolidMesh> parts, CliffSettings s, IReadOnlyList<CliffChunk> chunks)
    {
        var ramp = CliffColours.RampFor(s.Palette);
        double smoothCos = JsMath.Cos(s.SharpAngle * Math.PI / 180);
        var shaded = new (int[] Corners, V3[] Normals, int[] Points)[parts.Count];
        Parallel.For(0, parts.Count, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (index) => shaded[index] = Shade(parts[index], smoothCos));

        int vertices = shaded.Sum(piece => piece.Normals.Length);
        float[] positions = new float[3 * vertices];
        float[] normals = new float[3 * vertices];
        float[] colours = new float[3 * vertices];
        int[] indices = new int[shaded.Sum(piece => piece.Corners.Length)];
        int offset = 0;
        int corner = 0;
        for (int index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            var (corners, normal, point) = shaded[index];
            for (int v = 0; v < normal.Length; v++)
            {
                var p = part.Points[point[v]];
                var n = normal[v];
                var colour = ramp.Colour(CliffColours.Value(n.Z, CliffColours.Shift(s.Seed, p, s.Variation), part.Tags[point[v]]));
                int at = 3 * (offset + v);
                positions[at] = (float)p.X;
                positions[at + 1] = (float)p.Y;
                positions[at + 2] = (float)p.Z;
                normals[at] = (float)n.X;
                normals[at + 1] = (float)n.Y;
                normals[at + 2] = (float)n.Z;
                colours[at] = (float)colour.R;
                colours[at + 1] = (float)colour.G;
                colours[at + 2] = (float)colour.B;
            }

            foreach (int c in corners)
            {
                indices[corner++] = c + offset;
            }

            offset += normal.Length;
        }

        return new CliffBlock(positions, normals, colours, indices, chunks);
    }

    /// <summary>
    /// The piece's vertices split along sharp edges. Corners of triangles meeting at a point are joined across each
    /// edge the two triangles share smoothly (their normals less than the sharp angle apart, or one of them without
    /// area); each joined group becomes one vertex, its normal the area-weighted mean of its triangles'.
    /// </summary>
    /// <param name="piece">A closed mesh from <see cref="DualContour"/>: every edge in exactly two triangles, and no
    /// triangle touching a point twice.</param>
    /// <param name="smoothCos">The cosine of the sharp angle: triangles whose normals' dot product is below it meet at
    /// a crisp edge.</param>
    /// <returns>Each triangle corner's vertex, and each vertex's normal and the piece's point it sits on.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static (int[] Corners, V3[] Normals, int[] Points) Shade(SolidMesh piece, double smoothCos)
    {
        var indices = piece.Indices;
        int cornerCount = indices.Count;
        var area = new V3[cornerCount / 3];
        var unit = new V3[cornerCount / 3];
        for (int t = 0; t < area.Length; t++)
        {
            var a = piece.Points[indices[3 * t]];
            area[t] = V3.Cross(piece.Points[indices[(3 * t) + 1]] - a, piece.Points[indices[(3 * t) + 2]] - a);
            unit[t] = area[t].Normalized();
        }

        // Each edge, with the corners its two triangles start it from, in a table of twice the corners, found by the
        // edge's key.
        int size = (int)Math.Max(16, BitOperations.RoundUpToPowerOf2((uint)(2 * cornerCount)));
        long[] keys = new long[size];
        int[] first = new int[size];
        int[] second = new int[size];
        Array.Fill(keys, -1);
        long points = piece.Points.Count;
        for (int c = 0; c < cornerCount; c++)
        {
            int from = indices[c];
            int to = indices[Next(c)];
            long key = from < to ? (from * points) + to : (to * points) + from;
            int slot = (int)((ulong)(key * unchecked((long)0x9E3779B97F4A7C15)) >> 32) & (size - 1);
            while (keys[slot] != -1 && keys[slot] != key)
            {
                slot = (slot + 1) & (size - 1);
            }

            if (keys[slot] == -1)
            {
                keys[slot] = key;
                first[slot] = c;
            }
            else
            {
                second[slot] = c;
            }
        }

        int[] group = new int[cornerCount];
        for (int c = 0; c < cornerCount; c++)
        {
            group[c] = c;
        }

        for (int slot = 0; slot < size; slot++)
        {
            if (keys[slot] == -1)
            {
                continue;
            }

            int t1 = first[slot] / 3;
            int t2 = second[slot] / 3;
            if (V3.Dot(unit[t1], unit[t2]) < smoothCos && unit[t1] != default && unit[t2] != default)
            {
                continue;
            }

            // Join the corners at each end of the edge: the end a corner starts in one triangle may end it in the other.
            int c1 = first[slot];
            Join(group, c1, CornerAt(indices, t2, indices[c1]));
            Join(group, Next(c1), CornerAt(indices, t2, indices[Next(c1)]));
        }

        int[] vertexOf = new int[cornerCount];
        Array.Fill(vertexOf, -1);
        var sums = new List<V3>();
        var pointOf = new List<int>();
        int[] corners = new int[cornerCount];
        for (int c = 0; c < cornerCount; c++)
        {
            int root = Find(group, c);
            if (vertexOf[root] < 0)
            {
                vertexOf[root] = sums.Count;
                sums.Add(default);
                pointOf.Add(indices[root]);
            }

            corners[c] = vertexOf[root];
            sums[corners[c]] += area[c / 3];
        }

        // A vertex whose triangles have no area between them faces up rather than nowhere.
        var normals = new V3[sums.Count];
        for (int v = 0; v < normals.Length; v++)
        {
            var n = sums[v].Normalized();
            normals[v] = n.Length > 0.5 ? n : new V3(0, 0, 1);
        }

        return (corners, normals, [.. pointOf]);
    }

    /// <summary>The corner of triangle t on the point, or its last corner.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CornerAt(List<int> indices, int t, int point)
    {
        for (int k = 0; k < 2; k++)
        {
            if (indices[(3 * t) + k] == point)
            {
                return (3 * t) + k;
            }
        }

        return (3 * t) + 2;
    }

    /// <summary>The group a corner is in: the corner its chain of joins leads to, the chain shortened on the way.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Find(int[] group, int c)
    {
        while (group[c] != c)
        {
            group[c] = group[group[c]];
            c = group[c];
        }

        return c;
    }

    /// <summary>Puts two corners' groups together under the lower of their roots.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Join(int[] group, int a, int b)
    {
        int ra = Find(group, a);
        int rb = Find(group, b);
        if (ra != rb)
        {
            group[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }
    }

    /// <summary>The next corner of the same triangle.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Next(int c) => c % 3 == 2 ? c - 2 : c + 1;
}
