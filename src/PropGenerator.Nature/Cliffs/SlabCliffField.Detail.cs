using System.Runtime.CompilerServices;

namespace PropGenerator.Nature.Cliffs;

/// <summary>The surface push: the chisel's facets and the surface's smudge, creases, ledges and lines over the joined blocks, scaled per block, and the soft normal and facing they read.</summary>
internal sealed partial class SlabCliffField
{
    /// <summary>
    /// The surface detail at a point, held near the ground line (see <see cref="SlabSurface.Hold"/>): the chisel's
    /// facets, the surface's push (ledges and lines read by how far the faces near the point face sideways, see
    /// <see cref="Sideways"/>), and the chips and wear the joined blocks took (see <see cref="Union"/>). Positive cuts in.
    /// </summary>
    /// <param name="p">The point, above the ground line.</param>
    /// <param name="joined">The joined blocks there, with their chips and wear.</param>
    /// <param name="values">The blocks' fields there (+∞ where not read).</param>
    /// <param name="inward">How far the creases, ledges, lines, wear and chips cut in there, before the hold, in metres.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Detail(V3 p, Joined joined, ReadOnlySpan<double> values, out double inward)
    {
        double edges = joined.Cut ? joined.MostCut - joined.Field : 0;
        double push = Push(p, values, joined, out double cut);
        double scale = Scale(values, joined);
        inward = scale * (cut + edges);
        double held = SlabSurface.Hold(scale * (push + edges + _chisel.Face(p)), p.Z);

        // The ledges' steps already set the blocks' sides back (each by no more than the whole held step): the rest goes no
        // further in than the hold leaves, so the two together keep to it.
        return _stepped ? Math.Min(held, StepAllowance(p)) : held;
    }

    /// <summary>
    /// The surface's push at a point (see <see cref="SlabSurface.Push(V3, double, V3, double, out double)"/>), its
    /// grooves read at the point's foot on the joined blocks' surface where any groove reaches: back along the faces'
    /// normal there (see <see cref="Normal"/>) by the joined blocks' plain field (the smallest field read, held above the
    /// flat bottom: the same read whether the union is exact or estimated, so the estimate takes the same push).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Push(V3 p, ReadOnlySpan<double> values, Joined joined, out double inward)
    {
        double side = Sideways(p, values, joined.Best);
        double away = Math.Max(_bottom - p.Z, joined.Smallest);
        return _surface.Reaches(p, away)
            ? _surface.Push(p, side, Normal(p, values, joined.Best), away, out inward)
            : _surface.Push(p, side, out inward);
    }

    /// <summary>
    /// The faces' unit normal near a point: each block's soft normal (see <see cref="Facing"/>) at its own field, the
    /// blocks within <see cref="SideReach"/> of the nearest counting by how near they are (as for <see cref="Sideways"/>),
    /// so the normal turns smoothly from block to block; zero when no block is read.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private V3 Normal(V3 p, ReadOnlySpan<double> values, double best)
    {
        var sum = default(V3);
        for (int i = 0; i < values.Length; i++)
        {
            double w = Soft(values[i] - best);
            if (w > 0)
            {
                sum += Facing(i, p, values[i]) * w;
            }
        }

        return sum.Length > 0 ? sum.Normalized() : default;
    }

    /// <summary>
    /// The share of the surface detail at a point: each block's <see cref="_scale"/>, weighted by how near its field is
    /// to the smallest field there (fully at it, none from <see cref="Blend"/> behind it), so the share turns smoothly
    /// from block to block wherever they meet and no block's detail is cut in further than its own share allows. Every
    /// block that near is read by the union (it keeps all within 1.25 blend radii of the smallest), so the share is
    /// continuous. The winner's, where (deep in a joint's filler) no block is that near.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Scale(ReadOnlySpan<double> values, Joined joined)
    {
        if (!Capped)
        {
            return 1;
        }

        double sum = 0;
        double weight = 0;
        double reach = Blend;
        for (int i = 0; i < values.Length; i++)
        {
            double u = 1 - ((values[i] - joined.Smallest) / reach);
            if (u > 0)
            {
                double w = u * u;
                sum += w * _scale[i];
                weight += w;
            }
        }

        return weight > 0 ? sum / weight : _scale[joined.Winner];
    }

    /// <summary>
    /// How far the surface faces sideways at a point, 0 (up or down) to 1, for the ledges and lines: 1 − |z| of the
    /// normals of the faces near it, blended so the share turns smoothly from face to face and from block to block. Each
    /// block whose field is within <see cref="SideReach"/> of the smallest counts by how near it is, and within a block
    /// each of its terms within that of its largest (see <see cref="Up"/>). 0 when nothing reads it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Sideways(V3 p, ReadOnlySpan<double> values, double best)
    {
        if (!_surface.UsesSide)
        {
            return 0;
        }

        double up = 0;
        double weight = 0;
        for (int i = 0; i < values.Length; i++)
        {
            double w = Soft(values[i] - best);
            if (w > 0)
            {
                up += w * Up(i, p);
                weight += w;
            }
        }

        return weight > 0 ? 1 - (up / weight) : 0;
    }

    /// <summary>
    /// How far a block faces up or down at a point, 0 to 1: the |z| of the normals of its terms (each shell plane, and
    /// each cut's face) within <see cref="SideReach"/> of its largest, weighted by how near they are to it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    private double Up(int i, V3 p)
    {
        int count = _terms;
        Span<double> value = count <= 256 ? stackalloc double[count] : new double[count];
        Span<double> up = count <= 256 ? stackalloc double[count] : new double[count];
        int at = 0;
        double top = double.NegativeInfinity;
        foreach (var plane in _shells[i])
        {
            value[at] = plane.At(p);
            up[at] = Math.Abs(plane.Normal.Z);
            top = Math.Max(top, value[at++]);
        }

        foreach (var cut in _cuts[i])
        {
            double m = double.PositiveInfinity;
            double z = 0;
            foreach (var plane in cut)
            {
                double v = plane.At(p);
                if (v < m)
                {
                    m = v;
                    z = Math.Abs(plane.Normal.Z);
                }
            }

            value[at] = m;
            up[at] = z;
            top = Math.Max(top, value[at++]);
        }

        double sum = 0;
        double weight = 0;
        for (int k = 0; k < at; k++)
        {
            double w = Soft(top - value[k]);
            sum += w * up[k];
            weight += w;
        }

        return sum / weight;
    }

    /// <summary>How much a face or block counts toward <see cref="Sideways"/> a gap behind the nearest: 1 at it, none from <see cref="SideReach"/> on.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Soft(double gap)
    {
        double u = Math.Max(1 - (gap / SideReach), 0);
        return u * u;
    }

    /// <summary>
    /// The most the ledges' steps may set the sides back, near the ground line, before the rest of the detail is held
    /// (see <see cref="Detail"/>): the step alone, held to <see cref="StepHold"/> of the height. Fused neighbours blend
    /// by their faces, so a set-back join can move up to twice that; the rest of the detail is held to what is left.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double StepAllowance(V3 p) => Math.Max((0.5 * p.Z) - (2 * Math.Min(_surface.Step(p), StepHold * p.Z)), 0);

    /// <summary>
    /// A block's soft normal at a point where its field is <paramref name="f"/>: its planes' normals (and, for a cut, its
    /// own soft normal over its planes) weighted by how near their values are to the field, fading to nothing a quarter
    /// blend radius below it. On a face it is the face's normal; across an edge it turns smoothly from one face's to
    /// the other's.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private V3 Facing(int i, V3 p, double f)
    {
        double soft = Blend * SoftShare;
        var sum = default(V3);
        foreach (var plane in _shells[i])
        {
            sum += plane.Normal * Weight(f - plane.At(p), soft);
        }

        foreach (var cut in _cuts[i])
        {
            double m = double.PositiveInfinity;
            foreach (var plane in cut)
            {
                m = Math.Min(m, plane.At(p));
            }

            double w = Weight(f - m, soft);
            if (w > 0)
            {
                var inner = default(V3);
                foreach (var plane in cut)
                {
                    inner += plane.Normal * Weight(plane.At(p) - m, soft);
                }

                sum += inner.Normalized() * w;
            }
        }

        return sum.Normalized();
    }

    /// <summary>How much a plane counts toward a soft normal when its value is <paramref name="gap"/> from the field's: 1 at the field, 0 from <paramref name="soft"/> on.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double Weight(double gap, double soft)
    {
        double u = Math.Max(1 - (gap / soft), 0);
        return u * u;
    }
}
