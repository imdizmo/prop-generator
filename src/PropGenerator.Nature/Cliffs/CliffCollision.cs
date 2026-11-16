namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// A cliff's collision chunks: each block's upright boxes (see <see cref="CliffChunk.Of"/>), grown by how far the surface
/// detail pushes the rock out (see <see cref="Reach"/>), and, past a most, merged
/// so that no block is ever left without one. Two chunks may merge when they hold boxes of one block, of plates stacked
/// in one column and tier, or of side by side blocks of one tier and plate; of those pairs, the pair whose merged box
/// (the upright box around both) adds the least volume merges first, until there are few enough.
/// </summary>
internal static class CliffCollision
{
    /// <summary>
    /// How far the surface detail can push a cliff's rock out past its blocks, in metres: the smudge's most (see
    /// <see cref="SlabSurface.Reach"/>), the only push out (the chisel, chips, wear, creases, ledges and lines all cut in;
    /// a block's detail scale only lessens it). Review-final Minor 1: at Smudge 1.5 the Long cliff stood 0.7 m out of
    /// its boxes.
    /// </summary>
    /// <param name="s">The settings.</param>
    public static double Reach(CliffSettings s) => s.Smudge;

    /// <summary>The chunks of laid blocks, merged as need be to at most <paramref name="most"/>.</summary>
    /// <param name="blocks">The cliff's laid blocks.</param>
    /// <param name="most">The most chunks there may be (at least the number of tiers).</param>
    /// <param name="grow">How far each box is grown on its sides and top (see <see cref="Reach"/>), in metres.</param>
    /// <returns>The chunks, in the order of the blocks they first came from.</returns>
    public static List<CliffChunk> Chunks(IReadOnlyList<LaidBlock> blocks, int most, double grow = 0)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var chunks = new List<CliffChunk>();
        var owner = new List<int>();
        for (int b = 0; b < blocks.Count; b++)
        {
            foreach (var chunk in CliffChunk.Of(blocks[b], grow))
            {
                chunks.Add(chunk);
                owner.Add(b);
            }
        }

        if (chunks.Count <= most)
        {
            return chunks;
        }

        // The pairs that may merge, as chunk indices: the boxes of one block in a row, and the first box of each block
        // with the first of each of its neighbours.
        int[] first = new int[blocks.Count];
        for (int c = chunks.Count - 1; c >= 0; c--)
        {
            first[owner[c]] = c;
        }

        var pairs = new List<(int A, int B)>();
        for (int c = 1; c < chunks.Count; c++)
        {
            if (owner[c] == owner[c - 1])
            {
                pairs.Add((c - 1, c));
            }
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            for (int j = i + 1; j < blocks.Count; j++)
            {
                if (Neighbours(blocks[i], blocks[j]))
                {
                    pairs.Add((first[i], first[j]));
                }
            }
        }

        // Each chunk stands for a group of the starting ones, under the lowest index of the group.
        int[] root = new int[chunks.Count];
        for (int c = 0; c < root.Length; c++)
        {
            root[c] = c;
        }

        var merged = chunks.ToArray();
        for (int count = chunks.Count; count > most; count--)
        {
            int best = -1;
            double cost = double.PositiveInfinity;
            for (int k = 0; k < pairs.Count; k++)
            {
                int a = Find(root, pairs[k].A);
                int b = Find(root, pairs[k].B);
                if (a == b)
                {
                    continue;
                }

                double extra = Volume(Around(merged[a], merged[b])) - Volume(merged[a]) - Volume(merged[b]);
                if (extra < cost)
                {
                    cost = extra;
                    best = k;
                }
            }

            if (best < 0)
            {
                break;
            }

            int ra = Find(root, pairs[best].A);
            int rb = Find(root, pairs[best].B);
            int keep = Math.Min(ra, rb);
            merged[keep] = Around(merged[ra], merged[rb]);
            root[Math.Max(ra, rb)] = keep;
        }

        var result = new List<CliffChunk>();
        for (int c = 0; c < root.Length; c++)
        {
            if (root[c] == c)
            {
                result.Add(merged[c]);
            }
        }

        return result;
    }

    /// <summary>Whether two blocks' chunks may merge: plates next to each other in one column and tier, or blocks next to each other in one tier and plate.</summary>
    private static bool Neighbours(LaidBlock a, LaidBlock b) =>
        a.Tier == b.Tier && ((a.Column == b.Column && Math.Abs(a.Layer - b.Layer) == 1) || (a.Layer == b.Layer && Math.Abs(a.Column - b.Column) == 1));

    /// <summary>The box around two: from the lower bottom to the higher top, round both footprints.</summary>
    private static CliffChunk Around(CliffChunk a, CliffChunk b)
    {
        double x0 = Math.Min(a.Base.X - a.HalfX, b.Base.X - b.HalfX);
        double x1 = Math.Max(a.Base.X + a.HalfX, b.Base.X + b.HalfX);
        double y0 = Math.Min(a.Base.Y - a.HalfY, b.Base.Y - b.HalfY);
        double y1 = Math.Max(a.Base.Y + a.HalfY, b.Base.Y + b.HalfY);
        double low = Math.Min(a.Base.Z, b.Base.Z);
        double high = Math.Max(a.Top.Z, b.Top.Z);
        double x = (x0 + x1) / 2;
        double y = (y0 + y1) / 2;
        return new CliffChunk(new V3(x, y, low), new V3(x, y, high), (x1 - x0) / 2, (y1 - y0) / 2);
    }

    /// <summary>A chunk's volume, up to the factor of 4 every chunk shares.</summary>
    private static double Volume(CliffChunk c) => c.HalfX * c.HalfY * (c.Top.Z - c.Base.Z);

    private static int Find(int[] root, int i)
    {
        while (root[i] != i)
        {
            i = root[i] = root[root[i]];
        }

        return i;
    }
}
