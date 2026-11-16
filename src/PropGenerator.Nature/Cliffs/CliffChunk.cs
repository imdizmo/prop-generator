namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// One piece of a cliff as an upright box for collision, in cliff space (Z up, metres, the ground line at z = 0), its
/// sides along x and y.
/// </summary>
/// <param name="Base">The middle of the box's bottom face.</param>
/// <param name="Top">The middle of the box's top face.</param>
/// <param name="HalfX">Half the box's size along x.</param>
/// <param name="HalfY">Half the box's size along y.</param>
public sealed record CliffChunk(V3 Base, V3 Top, double HalfX, double HalfY)
{
    /// <summary>
    /// A block's boxes: its footprint (the box around its shell, seen from above) split along its long side into
    /// ⌈long / short⌉ equal segments, each an upright box that fills its segment and reaches from the block's flat bottom
    /// to its top, so together they hold the whole block; each grown on its sides and top by how far the surface detail
    /// can push the rock out past the block (its bottom stays: no detail moves the rock below the ground line).
    /// </summary>
    /// <param name="b">The laid block.</param>
    /// <param name="grow">How far the surface detail pushes the rock out at most, in metres (see <see cref="CliffCollision.Reach"/>).</param>
    /// <returns>The block's boxes, from its -x (or -y) end.</returns>
    internal static IReadOnlyList<CliffChunk> Of(LaidBlock b, double grow = 0)
    {
        ArgumentNullException.ThrowIfNull(b);
        var (min, max) = b.Block.Bounds();
        double dx = max.X - min.X;
        double dy = max.Y - min.Y;
        bool alongX = dx >= dy;
        double length = Math.Max(dx, dy);
        double side = Math.Min(dx, dy);
        int count = side > 0 ? (int)Math.Ceiling((length / side) - 1e-9) : 1;
        double segment = length / count;
        double x = (min.X + max.X) / 2;
        double y = (min.Y + max.Y) / 2;
        var chunks = new CliffChunk[count];
        for (int i = 0; i < count; i++)
        {
            double along = (i + 0.5) * segment;
            double cx = alongX ? min.X + along : x;
            double cy = alongX ? y : min.Y + along;
            chunks[i] = alongX
                ? new CliffChunk(new V3(cx, cy, min.Z), new V3(cx, cy, max.Z + grow), (segment / 2) + grow, (dy / 2) + grow)
                : new CliffChunk(new V3(cx, cy, min.Z), new V3(cx, cy, max.Z + grow), (dx / 2) + grow, (segment / 2) + grow);
        }

        return chunks;
    }
}
