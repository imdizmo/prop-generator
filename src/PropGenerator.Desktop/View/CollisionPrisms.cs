// Adapted from easy-props (MIT, © 2026 AHS12).
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Desktop.View;

/// <summary>
/// The shapes a player bumps into, ready for the view, as easy-props' collision view draws them: each shape the port's
/// prism (<see cref="Collision.Prism"/>, eight sides, or the shape's own polygon ring), turned from Y up to Z up, its faces for the see-through violet
/// fill and its outline (the two rings and the sides, the edges three's EdgesGeometry keeps) for the lines.
/// </summary>
internal sealed class CollisionPrisms
{
    private CollisionPrisms(int count, float[] positions, uint[] triangles, float[] lines)
    {
        Count = count;
        Positions = positions;
        Triangles = triangles;
        Lines = lines;
    }

    /// <summary>Gets how many shapes there are.</summary>
    internal int Count { get; }

    /// <summary>Gets the prisms' corners, three floats each, Z up, in metres.</summary>
    internal float[] Positions { get; }

    /// <summary>Gets the prisms' triangles, as corners of <see cref="Positions"/>, facing out.</summary>
    internal uint[] Triangles { get; }

    /// <summary>Gets the outline: two points (three floats each) per line.</summary>
    internal float[] Lines { get; }

    /// <summary>Makes the prisms of these shapes, where the prop stands (no pivot taken off, as in easy-props' view).</summary>
    /// <param name="shapes">The shapes, in the prop's Y-up space.</param>
    /// <returns>The prisms.</returns>
    internal static CollisionPrisms From(IReadOnlyList<CollisionShape> shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        var positions = new List<float>();
        var triangles = new List<uint>();
        var lines = new List<float>();
        var origin = new Vec3d(0, 0, 0);
        foreach (CollisionShape shape in shapes)
        {
            PropMesh prism = Collision.Prism(shape, origin);
            uint first = (uint)(positions.Count / 3);
            float[] p = prism.Position;
            for (int i = 0; i < p.Length; i += 3)
            {
                // Y up to Z up: (x, y, z) becomes (x, −z, y), a pure turn, so the faces keep facing out.
                positions.Add(p[i]);
                positions.Add(-p[i + 2]);
                positions.Add(p[i + 1]);
            }

            foreach (uint corner in prism.Index ?? [])
            {
                triangles.Add(first + corner);
            }

            // The first ring is the corners 0 to n − 1, the second n to 2n − 1.
            int n = shape.Ring?.Count ?? Collision.Sides;
            for (int side = 0; side < n; side++)
            {
                int next = (side + 1) % n;
                Line(positions, lines, first, side, next);
                Line(positions, lines, first, n + side, n + next);
                Line(positions, lines, first, side, n + side);
            }
        }

        return new CollisionPrisms(shapes.Count, [.. positions], [.. triangles], [.. lines]);
    }

    private static void Line(List<float> positions, List<float> lines, uint first, int a, int b)
    {
        for (int k = 0; k < 3; k++)
        {
            lines.Add(positions[(int)((first + a) * 3) + k]);
        }

        for (int k = 0; k < 3; k++)
        {
            lines.Add(positions[(int)((first + b) * 3) + k]);
        }
    }
}
