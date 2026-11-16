using System.Runtime.CompilerServices;

namespace PropGenerator.Nature.Cliffs;

/// <summary>Each level's chips and wear: what each block's edges and cuts take off its own rock, faded near a neighbour of its level and handed over between blocks, and the sunk part of a block below the ground line.</summary>
internal sealed partial class SlabCliffField
{
    /// <summary>
    /// A level's own chips and wear at a point: those of the block of it that wins there, faded where it fuses with the
    /// level's next nearest block (see <see cref="Fade"/>), and where that next block is nearly as near, blended with
    /// its own (see <see cref="Handover"/>). Their most (the chips' noise at its top unless
    /// <paramref name="exact"/>) and their least (the wear alone unless exact); 0 for a level with no block read.
    /// Read once per level and point.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void LevelCut(int level, V3 p, bool exact, Levels near, out double most, out double least)
    {
        Wake(level, p, exact, near);
        if (!double.IsNaN(near.Most[level]))
        {
            most = near.Most[level];
            least = near.Least[level];
            return;
        }

        most = 0;
        least = 0;
        double a = near.First[level];
        double b = near.Second[level];
        if (!double.IsInfinity(a))
        {
            (most, least) = BlockCut(near.FirstBlock[level], p, Fade(b), exact);

            // Where the next nearest block is nearly as near, its cut is taken in too, so the cut turns from one block's
            // to the other's as the nearest changes instead of jumping there (the teeth down a join).
            double share = Handover(b - a);
            if (share > 0)
            {
                var (otherMost, otherLeast) = BlockCut(near.SecondBlock[level], p, Fade(a), exact);
                if (exact)
                {
                    most = (most + (share * otherMost)) / (1 + share);
                    least = most;
                }
                else
                {
                    most = Math.Max(most, otherMost);
                    least = Math.Min(least, otherLeast);
                }
            }
        }

        near.Most[level] = most;
        near.Least[level] = least;
    }

    /// <summary>
    /// One block's chips and wear at a point, faded by <paramref name="fade"/>: their most (the chips' noise at its top
    /// unless <paramref name="exact"/>) and their least (the wear alone unless exact).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private (double Most, double Least) BlockCut(int i, V3 p, double fade, bool exact)
    {
        var (gap, chips) = Edges(i, p, fade, exact);
        double wear = fade > 0 ? _surface.Wear(p, gap) * fade : 0;
        return (wear + chips, exact ? wear + chips : wear);
    }

    /// <summary>
    /// How much of the next nearest block's chips and wear a level takes beside its nearest block's, where the next
    /// nearest's field is <paramref name="behind"/> behind: as much (an even mean) where the two are as near, none from
    /// <see cref="HandoverShare"/> of the blend radius behind, so the cut is continuous where the nearest block changes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double Handover(double behind)
    {
        double u = 1 - (behind / (HandoverShare * Blend));
        return u > 0 ? u * u : 0;
    }

    /// <summary>
    /// What a level's chips and wear cut into the rock sunk below its top plane (a plate's or a tier's base, or a
    /// joint's filler, resting in it): all of them a groove's half width below the plane and deeper, none at the plane
    /// and above it, and only while the point is inside the level or within a half width of it, and within a half width
    /// of the sunk rock's own surface (deeper in it, the level's rim is hidden, and cutting there would seal a pocket
    /// under the rock above the plane). So a rim whose neighbour above stands flush is chipped like any other, and the
    /// neighbour's own face above the plane is not.
    /// </summary>
    /// <param name="level">The level below.</param>
    /// <param name="t">How far the point is from that level's top plane (the joint's plane).</param>
    /// <param name="own">The sunk rock's own field at the point (as high as it may be: the share grows with it).</param>
    /// <param name="ownLeast">The sunk rock's own field at the point, as low as it may be (for the least).</param>
    /// <param name="p">The point.</param>
    /// <param name="exact">Whether the chips are read exactly, or bounded.</param>
    /// <param name="near">The levels' fields and cuts read so far.</param>
    /// <param name="least">The least of it (see <see cref="LevelCut"/>).</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Sunk(int level, double t, double own, double ownLeast, V3 p, bool exact, Levels near, out double least)
    {
        Wake(level, p, exact, near);
        double share = Math.Clamp(-t / _half, 0, 1) * Math.Clamp(1 - (Math.Max(near.First[level], 0) / _half), 0, 1);
        double most = share * Math.Clamp(1 + (own / _half), 0, 1);
        least = 0;
        if (most == 0)
        {
            return 0;
        }

        LevelCut(level, p, exact, near, out double high, out double low);
        least = low * share * Math.Clamp(1 + (ownLeast / _half), 0, 1);
        return high * most;
    }

    /// <summary>
    /// How much of a block's chips and wear are cut where the nearest other block of its level has field
    /// <paramref name="other"/>: none inside it, all from twice the blend radius out, so neither runs along the edges a
    /// fused neighbour hides.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Fade(double other) => Math.Clamp(other / (2 * Blend), 0, 1);

    /// <summary>
    /// A block's edges at a point, read from its terms: each shell plane, and for each cut the plane of it that is the
    /// cut's face there (the cut's smallest plane). Gives how far the point is inside the second largest term (how far
    /// it is from its nearest edge; see <see cref="SlabSurface.Wear"/>), and the deepest chip of every pair of the terms
    /// within the chips' reach (see <see cref="CliffChisel.ChipCut"/>), or with
    /// <paramref name="exact"/> false the most it can be. Every edge within reach is chipped, not only the nearest, so
    /// the chips do not jump where the nearest edge changes (toward a corner); but only as much as the edge is really
    /// there (see <see cref="OnBlock"/>). An edge between shell planes, between a shell plane and a cut's face, or
    /// between two cuts' faces is convex; one cut's own planes meet in its inner crease, which is never chipped, and the
    /// chips fade out near it (see <see cref="CreaseKeep"/>). Every term is read: the spans hold the most any block has.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    private (double Gap, double Chips) Edges(int i, V3 p, double fade, bool exact)
    {
        double reach = _chisel.ChipReach;
        int size = _terms;
        Span<Plane> near = size <= 128 ? stackalloc Plane[size] : new Plane[size];
        Span<int> terms = size <= 128 ? stackalloc int[size] : new int[size];
        Span<double> at = size <= 128 ? stackalloc double[size] : new double[size];
        int count = 0;
        double top = double.NegativeInfinity;
        double second = double.NegativeInfinity;
        Plane[] shell = _shells[i];
        for (int j = 0; j < shell.Length; j++)
        {
            double v = shell[j].At(p);
            Rank(v, ref top, ref second);
            if (v > -reach)
            {
                near[count] = shell[j];
                at[count] = v;
                terms[count++] = j;
            }
        }

        // A cut's face is its smallest plane; how near its inner crease the point is, the gap to its second smallest.
        Plane[][] cuts = _cuts[i];
        Span<double> smallest = cuts.Length <= 64 ? stackalloc double[cuts.Length] : new double[cuts.Length];
        Span<double> crease = cuts.Length <= 64 ? stackalloc double[cuts.Length] : new double[cuts.Length];
        for (int k = 0; k < cuts.Length; k++)
        {
            double m1 = double.PositiveInfinity;
            double m2 = double.PositiveInfinity;
            var face = cuts[k][0];
            foreach (var plane in cuts[k])
            {
                double v = plane.At(p);
                if (v < m1)
                {
                    m2 = m1;
                    m1 = v;
                    face = plane;
                }
                else if (v < m2)
                {
                    m2 = v;
                }
            }

            smallest[k] = m1;
            crease[k] = m2 - m1;
            Rank(m1, ref top, ref second);
            if (m1 > -reach)
            {
                near[count] = face;
                at[count] = m1;
                terms[count++] = -1 - k;
            }
        }

        double chips = 0;
        if (reach > 0 && fade > 0)
        {
            // Near a cut's inner crease (its two smallest planes and the block's field all near each other) no chip.
            double keep = fade;
            for (int k = 0; k < cuts.Length && keep > 0; k++)
            {
                double apart = Math.Max(crease[k], top - smallest[k]);
                keep *= Math.Clamp((apart - CreaseKeep) / CreaseRamp, 0, 1);
            }

            // The terms from the one the point is least inside: a pair's chip is never more than the chips' most less
            // how far the point is inside its inner term, so once that cannot beat the deepest so far, neither can
            // any pair further down. Each pair is still read in the order its terms were found.
            Span<int> order = size <= 128 ? stackalloc int[size] : new int[size];
            for (int k = 0; k < count; k++)
            {
                int m = k;
                for (; m > 0 && at[order[m - 1]] < at[k]; m--)
                {
                    order[m] = order[m - 1];
                }

                order[m] = k;
            }

            double share = Math.Min(keep, 1);
            for (int x = 0; x + 1 < count && keep > 0 && _chisel.ChipBound(at[order[x + 1]]) * share > chips; x++)
            {
                for (int y = x + 1; y < count && _chisel.ChipBound(at[order[y]]) * share > chips; y++)
                {
                    int a = Math.Min(order[x], order[y]);
                    int b = Math.Max(order[x], order[y]);

                    // A bound need not ask whether the edge is there: it is never more than all of it.
                    double most = _chisel.ChipMost(p, near[a], near[b], keep);
                    if (!exact)
                    {
                        chips = Math.Max(chips, most);
                    }
                    else if (most > chips)
                    {
                        // The chip is its cut times the share of it kept, which is never more than keep: where that
                        // cannot beat the deepest so far, whether the edge is there need not be asked.
                        double chip = _chisel.ChipCut(p, near[a], near[b], top);
                        if (chip * share > chips)
                        {
                            double kept = keep * OnBlock(i, p, near[a], terms[a], near[b], terms[b]);
                            chips = Math.Max(chips, kept <= 0 ? 0 : chip * Math.Min(kept, 1));
                        }
                    }
                }
            }
        }

        return (-second, chips);
    }

    /// <summary>
    /// How much of an edge between two of a block's terms is really there near a point, 0 to 1: at the point of the
    /// line where their planes meet nearest the point, every other term of the block must be at or below 0 (no other
    /// plane cuts the edge off), and a cut's plane must be that cut's face there (its other planes at or above 0, so the
    /// point is in front of them, where the cut takes the rock away). All of it while that holds, none from
    /// <see cref="EdgeEnd"/> past it: so no chip runs along where two planes would meet behind another face.
    /// </summary>
    /// <param name="i">The block.</param>
    /// <param name="p">The point.</param>
    /// <param name="a">The first plane.</param>
    /// <param name="termA">Its term: its index in the block's shell, or −1 − the index of its cut.</param>
    /// <param name="b">The second plane.</param>
    /// <param name="termB">Its term, the same way.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double OnBlock(int i, V3 p, Plane a, int termA, Plane b, int termB)
    {
        double d = V3.Dot(a.Normal, b.Normal);
        double across = 1 - (d * d);
        if (across < 1e-9)
        {
            return 0;
        }

        double va = a.At(p);
        double vb = b.At(p);
        var q = p - (a.Normal * ((va - (d * vb)) / across)) - (b.Normal * ((vb - (d * va)) / across));
        double past = double.NegativeInfinity;
        Plane[] shell = _shells[i];
        for (int j = 0; j < shell.Length; j++)
        {
            if (j != termA && j != termB)
            {
                past = Math.Max(past, shell[j].At(q));
                if (past >= EdgeEnd)
                {
                    return 0;
                }
            }
        }

        Plane[][] cuts = _cuts[i];
        for (int k = 0; k < cuts.Length; k++)
        {
            int term = -1 - k;
            if (term == termA || term == termB)
            {
                // The pair's plane of this cut must be its face there: in front of every other plane of the cut.
                var own = term == termA ? a : b;
                foreach (var plane in cuts[k])
                {
                    if (plane != own)
                    {
                        past = Math.Max(past, -plane.At(q));
                    }

                    if (past >= EdgeEnd)
                    {
                        return 0;
                    }
                }
            }
            else
            {
                double m = double.PositiveInfinity;
                foreach (var plane in cuts[k])
                {
                    m = Math.Min(m, plane.At(q));
                }

                past = Math.Max(past, m);
                if (past >= EdgeEnd)
                {
                    return 0;
                }
            }
        }

        return Math.Clamp(1 - (past / EdgeEnd), 0, 1);
    }

    /// <summary>Takes a term's value into the largest and second largest so far.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Rank(double v, ref double top, ref double second)
    {
        if (v > top)
        {
            second = top;
            top = v;
        }
        else if (v > second)
        {
            second = v;
        }
    }
}
