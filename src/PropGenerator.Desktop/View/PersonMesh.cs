// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Numerics;

namespace PropGenerator.Desktop.View;

/// <summary>
/// easy-props' person for scale: a plain grey (<c>#c9ccd1</c>) figure 1.8 m tall, a capsule body (radius 0.21, 1.02
/// between its caps, 6 segments to a cap and 16 round, made 0.72 as deep as wide) and a head (radius 0.135, 20 by 14
/// segments) at 1.665 m. Z up, standing on the origin; the view puts it beside the prop's front-left corner, 0.6 m
/// clear of it.
/// </summary>
internal static class PersonMesh
{
    /// <summary>How tall the person is, in metres.</summary>
    internal const float Height = 1.8f;

    /// <summary>How far from the prop's corner the person stands, along X and along the front.</summary>
    internal const float Clearance = 0.6f;

    private const float BodyRadius = 0.21f;
    private const float BodyLength = 1.02f;
    private const int CapSegments = 6;
    private const int RadialSegments = 16;
    private const float Depth = 0.72f;
    private const float HeadRadius = 0.135f;
    private const float HeadAt = 1.665f;
    private const int HeadAround = 20;
    private const int HeadDown = 14;

    /// <summary>Where the person stands: easy-props' (min x − 0.6, 0, max z + 0.6) in its Y up, so beside the front-left corner.</summary>
    /// <param name="lo">The prop's lowest corner.</param>
    /// <returns>The point under the person's feet.</returns>
    internal static Vector3 Place(Vector3 lo) => new(lo.X - Clearance, lo.Y - Clearance, 0);

    /// <summary>Builds the figure, with outward normals and its grey as linear vertex colours.</summary>
    /// <returns>The mesh, Z up, its feet on the origin.</returns>
    internal static ColouredMesh Build()
    {
        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<int>();

        // The body: a lathe of the capsule's outline (bottom cap, side, top cap) turned round Z, then made shallower.
        var outline = new List<(float R, float Z, float Nr, float Nz)>();
        for (int i = 0; i <= CapSegments; i++)
        {
            float a = (-MathF.PI / 2) + (MathF.PI / 2 * i / CapSegments);
            outline.Add((BodyRadius * MathF.Cos(a), BodyRadius + (BodyRadius * MathF.Sin(a)), MathF.Cos(a), MathF.Sin(a)));
        }

        for (int i = 0; i <= CapSegments; i++)
        {
            float a = MathF.PI / 2 * i / CapSegments;
            outline.Add((BodyRadius * MathF.Cos(a), BodyRadius + BodyLength + (BodyRadius * MathF.Sin(a)), MathF.Cos(a), MathF.Sin(a)));
        }

        Lathe(outline, RadialSegments, Vector3.Zero, Depth, positions, normals, indices);

        // The head.
        var head = new List<(float R, float Z, float Nr, float Nz)>();
        for (int i = 0; i <= HeadDown; i++)
        {
            float a = (-MathF.PI / 2) + (MathF.PI * i / HeadDown);
            head.Add((HeadRadius * MathF.Cos(a), HeadRadius * MathF.Sin(a), MathF.Cos(a), MathF.Sin(a)));
        }

        Lathe(head, HeadAround, new Vector3(0, 0, HeadAt), 1, positions, normals, indices);

        Vector3 grey = SceneLook.ToLinear(SceneLook.Srgb("#c9ccd1"));
        float[] colours = new float[positions.Count];
        for (int i = 0; i < colours.Length; i += 3)
        {
            colours[i] = grey.X;
            colours[i + 1] = grey.Y;
            colours[i + 2] = grey.Z;
        }

        return new ColouredMesh([.. positions], [.. normals], colours, [.. indices]);
    }

    // Turns an outline (radius, height and the outline's normal) round Z about a centre, its Y squeezed by depth; the
    // triangles wind counter-clockwise seen from outside.
    private static void Lathe(
        List<(float R, float Z, float Nr, float Nz)> outline, int around, Vector3 centre, float depth,
        List<float> positions, List<float> normals, List<int> indices)
    {
        int first = positions.Count / 3;
        foreach ((float r, float z, float nr, float nz) in outline)
        {
            for (int j = 0; j <= around; j++)
            {
                float a = 2 * MathF.PI * j / around;
                float c = MathF.Cos(a), s = MathF.Sin(a);
                positions.Add(centre.X + (r * c));
                positions.Add(centre.Y + (r * s * depth));
                positions.Add(centre.Z + z);

                // A surface squeezed along Y has its normals stretched along Y.
                var n = Vector3.Normalize(new Vector3(nr * c, nr * s / depth, nz));
                normals.Add(n.X);
                normals.Add(n.Y);
                normals.Add(n.Z);
            }
        }

        int row = around + 1;
        for (int i = 0; i + 1 < outline.Count; i++)
        {
            for (int j = 0; j < around; j++)
            {
                int a = first + (i * row) + j, b = a + 1, c = a + row, d = c + 1;
                indices.AddRange([a, b, d, a, d, c]);
            }
        }
    }
}
