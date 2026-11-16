using System.Runtime.CompilerServices;

namespace PropGenerator.Nature.Cliffs;

/// <summary>The culled union of the blocks: <see cref="Union"/> and the reads it is made of (the fused join, the waiting blocks woken, the cull grid's picks and the blocks' cheap bounds).</summary>
internal sealed partial class SlabCliffField
{
    /// <summary>
    /// The joined blocks without the push: each level's blocks joined hard, with the crack along the seam of its two
    /// smallest fields (see <see cref="Fuse"/>), the levels and the joint fillers joined hard, and held above the lowest
    /// flat bottom; and, when <paramref name="cut"/>, the same with each level's own chips and wear cut into its own rock
    /// before the levels are joined (see <see cref="LevelCut"/>), so no level's chips cut another's. Not
    /// <paramref name="exact"/>, the cracks are taken between none and their most, the field is the middle of that range
    /// and the slack half of it, and the chips are bounded.
    /// <para>
    /// A block's field is never below the largest of its bounding planes' values (they are some of its shell planes), and
    /// a level's joined field is never below its smallest field, so a block whose
    /// bound is at least 1.25 blend radii (or the most the chips and wear cut in, if more) above the smallest field
    /// found so far can change nothing and is skipped: the result is exact, and only the blocks near the point are read
    /// in full. A filler is skipped the same way.
    /// </para>
    /// <para>
    /// So that the winning block and each level's nearest neighbour are exact where they matter, a block is also read,
    /// unless the point is at least <see cref="_deep"/> inside the rock (where no detail can bring the surface, and
    /// nothing is cut), while its bound is under the smallest block field found so far or under twice the blend radius
    /// (a neighbour further out fades nothing). Each block's field read is put in <paramref name="values"/> (+∞ where
    /// it is not read). The chips and wear are cut only when <paramref name="cut"/>.
    /// </para>
    /// <para>
    /// What is read is the same, in the same order, as had every block and filler been looked at, but less is looked
    /// at: the cull grid's lists name the only blocks and fillers a point can read (see <see cref="SlabCliffCull"/>). A block or
    /// filler that changes nothing but the cut field (no nearer than 1.25 blend radii of the smallest field and a
    /// little past the winner's, and past twice the blend radius when <paramref name="neighbour"/>, for a block; no
    /// nearer than the nearest block, for a filler) waits; and the levels and fillers are cut from the lowest field
    /// up, each only while its field (the least its cut field can be) is under the smallest cut field so far, a
    /// level's waiting blocks read first (see <see cref="Wake"/>). So the many reads that only the chips' reach asks
    /// for are mostly never made, and a union without the cut never makes them.
    /// </para>
    /// </summary>
    /// <param name="p">The point.</param>
    /// <param name="exact">Whether the fused joins and the chips are read exactly, or bounded.</param>
    /// <param name="values">Where each block's field read is put (+∞ where it is not read).</param>
    /// <param name="cut">Whether the chips and wear are cut in.</param>
    /// <param name="neighbour">
    /// Whether the nearest other block's field must be exact under twice the blend radius (see <see cref="Nearest"/>).
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    private Joined Union(V3 p, bool exact, Span<double> values, bool cut, bool neighbour = false)
    {
        int n = _blocks.Length;
        values.Fill(double.PositiveInfinity);

        // The cull grid ranks the blocks and joints by how low their bounds can be in the point's cell (see SlabCliffCull):
        // only a short run of the nearest needs looking at, and is taken in index order as a scan of all would take
        // it. Where a block or joint left off the cell's ranks could matter, every one is looked at.
        var cull = _cull.Value;
        int cell = cull?.CellOf(p) ?? -1;
        int nearest = -1;
        double nearestBound = double.PositiveInfinity;
        if (cell >= 0)
        {
            // A block whose bound cannot be under the smallest so far cannot be the nearest, nor can any ranked after it.
            ReadOnlySpan<int> ranked = cull!.Blocks(cell);
            ReadOnlySpan<double> lows = cull.BlockLows(cell);
            for (int r = 0; r < ranked.Length && lows[r] <= nearestBound; r++)
            {
                int i = ranked[r];
                double b = Bound(i, p);
                if (b < nearestBound || (b == nearestBound && i < nearest))
                {
                    nearest = i;
                    nearestBound = b;
                }
            }

            cell = nearestBound < cull.BlockEdge(cell) ? cell : -1;
        }

        if (cell < 0)
        {
            nearest = -1;
            nearestBound = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                double b = Bound(i, p);
                if (nearest < 0 || b < nearestBound)
                {
                    nearest = i;
                    nearestBound = b;
                }
            }
        }

        Span<double> raws = n <= StackMost ? stackalloc double[n] : new double[n];
        double near = Value(nearest, p, out raws[nearest]);
        values[nearest] = near;
        double smallest = near;
        int joints = _joints.Length;
        Span<double> fills = joints <= StackMost ? stackalloc double[joints] : new double[joints];
        Span<double> planes = joints <= StackMost ? stackalloc double[joints] : new double[joints];
        Span<double> waitingFill = joints <= StackMost ? stackalloc double[joints] : new double[joints];
        fills.Fill(double.PositiveInfinity);
        waitingFill.Fill(double.PositiveInfinity);
        Span<int> readJoints = joints <= StackMost ? stackalloc int[joints] : new int[joints];
        int jointsRead = 0;
        double fill = double.PositiveInfinity;

        // A joint whose bound is past the nearest block's field by the chips' most is never read.
        Span<int> picked = stackalloc int[SlabCliffCull.Ranks + 1];
        int jointCount = joints;
        bool jointsPicked = cell >= 0 && near + _edgeMost <= cull!.JointEdge(cell);
        if (jointsPicked)
        {
            jointCount = Pick(cull!.Joints(cell), cull.JointLows(cell), near + _edgeMost, -1, picked);
        }

        for (int k = 0; k < jointCount; k++)
        {
            int j = jointsPicked ? picked[k] : k;
            var joint = _joints[j];
            double t = _tops[joint.Lower].At(p);
            double low = Math.Max(Math.Max(Math.Abs(t) - _reach, SideBound(joint.Upper, p)), SideBound(joint.Lower, p));
            if (low < smallest + _edgeMost)
            {
                planes[j] = t;
                readJoints[jointsRead++] = j;
                if (low >= near)
                {
                    // A filler no nearer than the nearest block changes neither the smallest field nor the union, only
                    // (perhaps) the cut one: it waits until the cut asks for it.
                    waitingFill[j] = low;
                    continue;
                }

                fills[j] = Fill(joint, t, p, near + _edgeMost);
                fill = Math.Min(fill, fills[j]);
                smallest = Math.Min(smallest, fills[j]);
            }
        }

        int levels = _levels;
        Span<double> first = levels <= 64 ? stackalloc double[levels] : new double[levels];
        Span<double> second = levels <= 64 ? stackalloc double[levels] : new double[levels];
        Span<int> firstBlock = levels <= 64 ? stackalloc int[levels] : new int[levels];
        Span<int> secondBlock = levels <= 64 ? stackalloc int[levels] : new int[levels];
        firstBlock.Clear();
        secondBlock.Clear();
        first.Fill(double.PositiveInfinity);
        second.Fill(double.PositiveInfinity);
        double reach = Math.Max(1.25 * Blend, _edgeMost);
        double fades = 2 * Blend;
        double best = near;
        int winner = nearest;

        // A block past 1.25 blend radii of the smallest field and a little past the winner's (and, when the nearest
        // other block's field must be exact, past twice the blend radius) changes nothing but its own level's fields,
        // which only the cut reads: it waits, by level, until the cut asks for its level (see Wake).
        double fuses = (1.25 * Blend) + Hair;
        Span<byte> waiting = n <= StackMost ? stackalloc byte[n] : new byte[n];
        waiting.Clear();
        Span<double> waitLow = levels <= 64 ? stackalloc double[levels] : new double[levels];
        waitLow.Fill(double.PositiveInfinity);

        // The smallest field and the winner's only fall as blocks are read, so a block whose bound is at or past what
        // they let in now is never read.
        double letIn = smallest > -_deep ? Math.Max(smallest + reach, Math.Max(best, fades)) : smallest + reach;
        int count = n;
        bool blocksPicked = cell >= 0 && letIn <= cull!.BlockEdge(cell);
        if (blocksPicked)
        {
            count = Pick(cull!.Blocks(cell), cull.BlockLows(cell), letIn, nearest, picked);
        }

        for (int k = 0; k < count; k++)
        {
            int i = blocksPicked ? picked[k] : k;
            double v;
            if (i == nearest)
            {
                v = near;
            }
            else
            {
                double b = Bound(i, p);
                if (b < smallest + reach || (smallest > -_deep && (b < best || b < fades)))
                {
                    // A block waits when its bound shows it can, or when its field, read as far as it takes to show
                    // the same, does.
                    double wait = Math.Max(smallest + fuses, best + SideReach + Hair);
                    wait = neighbour ? Math.Max(wait, fades) : wait;
                    v = b >= wait ? b : Value(i, p, out raws[i], wait);
                    if (v >= wait && (b >= wait || double.IsNaN(raws[i])))
                    {
                        waiting[i] = 1;
                        waitLow[_level[i]] = Math.Min(waitLow[_level[i]], v);
                        continue;
                    }

                    values[i] = v;
                    smallest = Math.Min(smallest, v);
                    if (v < best || (v == best && i < winner))
                    {
                        best = v;
                        winner = i;
                    }
                }
                else
                {
                    continue;
                }
            }

            int level = _level[i];
            if (v < first[level])
            {
                second[level] = first[level];
                secondBlock[level] = firstBlock[level];
                first[level] = v;
                firstBlock[level] = i;
            }
            else if (v < second[level])
            {
                second[level] = v;
                secondBlock[level] = i;
            }
        }

        // Each level's fused field (exact, or the hard and the whole blend's ends of its range).
        Span<double> fused = levels <= 64 ? stackalloc double[levels] : new double[levels];
        Span<double> fullest = levels <= 64 ? stackalloc double[levels] : new double[levels];
        double hard = fill;
        double lowest = fill;
        for (int level = 0; level < levels; level++)
        {
            var (f, full) = Fuse(first[level], second[level], firstBlock[level], secondBlock[level], p, exact, raws);
            fused[level] = f;
            fullest[level] = full;
            hard = Math.Min(hard, f);
            lowest = Math.Min(lowest, full);
        }

        // Where neighbours' flat bottoms meet, the fused join would bulge below them.
        double floor = _bottom - p.Z;
        hard = Math.Max(hard, floor);
        lowest = Math.Max(lowest, floor);
        double field = (hard + lowest) / 2;
        double slack = (hard - lowest) / 2;
        var joined = new Joined(field, slack, winner, second[_level[winner]], best, smallest, false, field, field);
        if (!cut || _edgeMost == 0 || smallest <= -_deep)
        {
            return joined;
        }

        // Each level within reach takes its own chips and wear (a level further out than they cut is never the nearest
        // rock); the fillers and the parts sunk below a joint's plane take their beds' too.
        Span<double> most = levels <= 64 ? stackalloc double[levels] : new double[levels];
        Span<double> least = levels <= 64 ? stackalloc double[levels] : new double[levels];
        most.Fill(double.NaN);
        least.Fill(double.NaN);
        var read = new Levels(first, second, firstBlock, secondBlock, most, least, fused, fullest, values, raws, waiting, waitLow);

        // A level or filler takes its cut only while its field (the cut's least, the cuts going only in) is under the
        // smallest cut field so far; read from the lowest field up, the rock that wins is cut first, and the rest
        // mostly need no cut at all.
        int items = levels + jointsRead;
        Span<int> order = items <= StackMost ? stackalloc int[items] : new int[items];
        Span<double> key = items <= StackMost ? stackalloc double[items] : new double[items];
        int cuts = 0;
        for (int item = 0; item < items; item++)
        {
            double f = item >= levels ? Math.Min(fills[readJoints[item - levels]], waitingFill[readJoints[item - levels]])
                : waitLow[item] < double.PositiveInfinity ? Math.Min(first[item], waitLow[item]) - (Blend / 4) : fullest[item];
            if (f > hard + _edgeMost)
            {
                continue;
            }

            int m = cuts++;
            for (; m > 0 && key[m - 1] > f; m--)
            {
                order[m] = order[m - 1];
                key[m] = key[m - 1];
            }

            order[m] = item;
            key[m] = f;
        }

        double edged = double.PositiveInfinity;
        double edgedLeast = double.PositiveInfinity;
        for (int k = 0; k < cuts; k++)
        {
            int item = order[k];
            if (item < levels)
            {
                int level = item;
                if (waitLow[level] < double.PositiveInfinity)
                {
                    // A level with blocks waiting: its field is never under the key it was sorted by.
                    if (key[k] >= edged && key[k] >= edgedLeast)
                    {
                        continue;
                    }

                    Wake(level, p, exact, read);
                }

                if (fullest[level] > hard + _edgeMost || (fused[level] >= edged && fullest[level] >= edgedLeast))
                {
                    continue;
                }

                LevelCut(level, p, exact, read, out double hi, out double lo);
                int bed = _bed[firstBlock[level]];
                if (bed >= 0)
                {
                    double sunk = Sunk(_level[bed], _tops[bed].At(p), fused[level], fullest[level], p, exact, read, out double sunkLow);
                    hi = Math.Max(hi, sunk);
                    lo = Math.Max(lo, sunkLow);
                }

                edged = Math.Min(edged, fused[level] + hi);
                edgedLeast = Math.Min(edgedLeast, fullest[level] + lo);
            }
            else
            {
                int j = readJoints[item - levels];
                if (waitingFill[j] < double.PositiveInfinity)
                {
                    // A waiting filler's field is never under its cheap bound.
                    if (waitingFill[j] >= edged && waitingFill[j] >= edgedLeast)
                    {
                        continue;
                    }

                    fills[j] = Fill(_joints[j], planes[j], p, near + _edgeMost);
                    if (fills[j] > hard + _edgeMost)
                    {
                        continue;
                    }
                }

                if (fills[j] >= edged && fills[j] >= edgedLeast)
                {
                    continue;
                }

                var joint = _joints[j];
                LevelCut(_level[joint.Upper], p, exact, read, out double hi, out double lo);
                double sunk = Sunk(_level[joint.Lower], planes[j], fills[j], fills[j], p, exact, read, out double sunkLow);
                edged = Math.Min(edged, fills[j] + Math.Max(hi, sunk));
                edgedLeast = Math.Min(edgedLeast, fills[j] + Math.Max(lo, sunkLow));
            }
        }

        return joined with { Cut = true, MostCut = Math.Max(floor, edged), LeastCut = Math.Max(floor, edgedLeast) };
    }

    /// <summary>
    /// A level's joined field from its smallest and next smallest block fields <paramref name="a"/> and
    /// <paramref name="b"/> (of blocks <paramref name="i"/> and <paramref name="j"/>, whose own fields, without their
    /// grooves, are in <paramref name="raws"/>) when <paramref name="exact"/>: the hard union <paramref name="a"/> at and
    /// outside the surface, with the joint's crack pulled in along the seam where the two meet (see <see cref="Crack"/>);
    /// inside the rock, a polynomial smooth min over a radius that grows from none at the surface to the joint reach
    /// <see cref="Blend"/> a quarter of it in, so the rock deep in a joint reads as deep as a fused joint's did (which
    /// keeps the chisel from sealing pockets along the faces hidden in it) while the surface is the hard one. And the
    /// least it can be: the same when exact; else the whole smooth min's, the field then the most the crack can make it.
    /// Neither is ever more than a quarter of the joint reach below <paramref name="a"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private (double Fused, double Fullest) Fuse(double a, double b, int i, int j, V3 p, bool exact, ReadOnlySpan<double> raws)
    {
        // A level with no block read has both fields +∞ (and so no difference): its field is +∞.
        double apart = b - a;
        if (!(apart < Blend))
        {
            return (a, a);
        }

        if (!exact)
        {
            double hFull = (Blend - apart) / Blend;
            return (_crackDepth > 0 && apart < _crackHalf ? a + CrackMost : a, a - (hFull * hFull * Blend / 4));
        }

        double f = a;
        double k = Blend * Math.Clamp(-a / (Blend * InsideShare), 0, 1);
        if (apart < k)
        {
            double h = (k - apart) / k;
            f -= h * h * k / 4;
        }

        if (_crackDepth > 0 && apart < _crackHalf)
        {
            f += Crack(i, j, p, raws[i], raws[j], apart);
        }

        return (f, f);
    }

    /// <summary>
    /// Reads a level's waiting blocks (see <see cref="Union"/>), if it has any, and takes its smallest and next smallest
    /// fields and its fused field again over all its blocks read, in index order as the union takes them: the same as
    /// had they been read with the rest. Every use of a level's fields in the cut wakes it first.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void Wake(int level, V3 p, bool exact, Levels near)
    {
        if (near.WaitLow[level] == double.PositiveInfinity)
        {
            return;
        }

        near.WaitLow[level] = double.PositiveInfinity;
        double a = double.PositiveInfinity;
        double b = double.PositiveInfinity;
        int ia = 0;
        int ib = 0;
        foreach (int i in _levelBlocks[level])
        {
            if (near.Waiting[i] != 0)
            {
                near.Waiting[i] = 0;
                near.Values[i] = Value(i, p, out near.Raws[i]);
            }

            double v = near.Values[i];
            if (v < a)
            {
                b = a;
                ib = ia;
                a = v;
                ia = i;
            }
            else if (v < b)
            {
                b = v;
                ib = i;
            }
        }

        near.First[level] = a;
        near.Second[level] = b;
        near.FirstBlock[level] = ia;
        near.SecondBlock[level] = ib;
        (near.Fused[level], near.Fullest[level]) = Fuse(a, b, ia, ib, p, exact, near.Raws);
    }

    /// <summary>
    /// The run of a cell's ranks (see <see cref="SlabCliffCull"/>) whose bounds can be under <paramref name="under"/>, and
    /// <paramref name="also"/> (unless −1), put in <paramref name="into"/> in index order.
    /// </summary>
    /// <returns>How many were picked.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int Pick(ReadOnlySpan<int> ranked, ReadOnlySpan<double> lows, double under, int also, Span<int> into)
    {
        int count = 0;
        bool met = also < 0;
        for (int r = 0; r < ranked.Length && lows[r] < under; r++)
        {
            met |= ranked[r] == also;
            into[count++] = ranked[r];
        }

        if (!met)
        {
            into[count++] = also;
        }

        for (int k = 1; k < count; k++)
        {
            int v = into[k];
            int m = k;
            for (; m > 0 && into[m - 1] > v; m--)
            {
                into[m] = into[m - 1];
            }

            into[m] = v;
        }

        return count;
    }

    /// <summary>A block's cheap bound: the largest of its six shell planes facing furthest along ±x, ±y and ±z (never above its field).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Bound(int i, V3 p)
    {
        double b = double.NegativeInfinity;
        for (int k = i * BoundPlanes; k < (i + 1) * BoundPlanes; k++)
        {
            b = Math.Max(b, _bounds[k].At(p));
        }

        return b;
    }

    /// <summary>The cheap bound of a block's sides: the largest of the four side planes facing furthest along ±x and ±y.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double SideBound(int i, V3 p)
    {
        double b = double.NegativeInfinity;
        for (int k = i * SideBoundPlanes; k < (i + 1) * SideBoundPlanes; k++)
        {
            b = Math.Max(b, _sideBounds[k].At(p));
        }

        return b;
    }

    /// <summary>
    /// The joined blocks at a point (see <see cref="Union"/>): their field (the middle of its range when estimated) and
    /// half that range, the block that wins and the field of the nearest other block of its level, the winner's field,
    /// whether the chips and wear were cut in, and the joined field with them cut in, at its most and its least (both
    /// the same when exact; the field itself when not cut).
    /// </summary>
    private readonly record struct Joined(double Field, double Slack, int Winner, double Other, double Best, double Smallest, bool Cut, double MostCut, double LeastCut);

    /// <summary>
    /// The levels' fields at a point as <see cref="Union"/> read them (the smallest and next smallest of each level,
    /// which blocks they are, and its fused field at its most and least), each level's chips and wear once read (NaN
    /// until then), for <see cref="LevelCut"/>; and the blocks' fields read (with their grooves and without), which blocks wait, and the least bound of
    /// each level's waiting blocks (+∞ for a level with none), for <see cref="Wake"/>.
    /// </summary>
    private readonly ref struct Levels(
        Span<double> first,
        Span<double> second,
        Span<int> firstBlock,
        Span<int> secondBlock,
        Span<double> most,
        Span<double> least,
        Span<double> fused,
        Span<double> fullest,
        Span<double> values,
        Span<double> raws,
        Span<byte> waiting,
        Span<double> waitLow)
    {
        /// <summary>Gets each level's smallest block field.</summary>
        public Span<double> First { get; } = first;

        /// <summary>Gets each level's next smallest block field.</summary>
        public Span<double> Second { get; } = second;

        /// <summary>Gets each level's smallest block.</summary>
        public Span<int> FirstBlock { get; } = firstBlock;

        /// <summary>Gets each level's next smallest block.</summary>
        public Span<int> SecondBlock { get; } = secondBlock;

        /// <summary>Gets each level's chips and wear at their most, once read.</summary>
        public Span<double> Most { get; } = most;

        /// <summary>Gets each level's chips and wear at their least, once read.</summary>
        public Span<double> Least { get; } = least;

        /// <summary>Gets each level's fused field.</summary>
        public Span<double> Fused { get; } = fused;

        /// <summary>Gets each level's fused field at its least.</summary>
        public Span<double> Fullest { get; } = fullest;

        /// <summary>Gets each block's field, +∞ where not read.</summary>
        public Span<double> Values { get; } = values;

        /// <summary>Gets each read block's own field, without its grooves.</summary>
        public Span<double> Raws { get; } = raws;

        /// <summary>Gets whether each block waits to be read.</summary>
        public Span<byte> Waiting { get; } = waiting;

        /// <summary>Gets the least bound of each level's waiting blocks, +∞ for a level with none.</summary>
        public Span<double> WaitLow { get; } = waitLow;
    }
}
