namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// The slab-rock formula as a solid: a convex shell (the intersection of the shell planes' back sides) minus convex
/// wedge cuts. A cut removes the points in front of all its planes at once. The field is the largest of the shell
/// planes' values and of each cut's smallest plane value, so it is negative inside.
/// </summary>
internal sealed class ConvexBlock : ISolidField
{
    private const double CornerTolerance = 1e-9;
    private const double MergeDistance = 1e-7;
    private const double SingularDeterminant = 1e-12;

    /// <summary>A corner this close to a plane, in metres, lies on it (for finding the shell's edges).</summary>
    private const double OnPlane = 1e-6;

    /// <summary>Two edge directions whose cross product is shorter than this are parallel: they give no axis to test.</summary>
    private const double ParallelEdges = 1e-9;

    private readonly Plane[] _shell;
    private readonly Plane[][] _cuts;
    private readonly V3[] _corners;

    /// <summary>Builds the block and finds the shell's corners, once.</summary>
    /// <param name="shell">The planes of the convex shell.</param>
    /// <param name="cuts">The wedge cuts, each a list of planes whose fronts intersect.</param>
    public ConvexBlock(IReadOnlyList<Plane> shell, IReadOnlyList<IReadOnlyList<Plane>> cuts)
    {
        _shell = [.. shell];
        _cuts = [.. cuts.Select(c => c.ToArray())];
        _corners = FindCorners(_shell);
    }

    /// <summary>The planes of the convex shell.</summary>
    public IReadOnlyList<Plane> Shell => _shell;

    /// <summary>The wedge cuts, each a list of planes.</summary>
    public IReadOnlyList<IReadOnlyList<Plane>> Cuts => _cuts;

    /// <inheritdoc/>
    public double Distance(V3 p)
    {
        double f = double.NegativeInfinity;
        foreach (Plane plane in _shell)
        {
            f = Math.Max(f, plane.At(p));
        }

        foreach (Plane[] cut in _cuts)
        {
            double m = double.PositiveInfinity;
            foreach (Plane plane in cut)
            {
                m = Math.Min(m, plane.At(p));
            }

            f = Math.Max(f, m);
        }

        return f;
    }

    /// <inheritdoc/>
    public double Estimate(V3 p, out double error)
    {
        error = 0;
        return Distance(p);
    }

    /// <summary>The shell's vertices: every point where three shell planes meet that no shell plane puts outside.</summary>
    public IReadOnlyList<V3> Corners() => _corners;

    /// <summary>
    /// The vertices of the shell cut down by more planes (the region behind all of them and inside the shell): the
    /// shell's own corners behind every extra plane, and every point where three planes, at least one of them extra,
    /// meet inside all of them. The same corners a block of all those planes would find, at a fraction of the work,
    /// since the shell's corners are found once.
    /// </summary>
    /// <param name="extra">The planes the shell is cut down by.</param>
    public List<V3> CornersWithin(IReadOnlyList<Plane> extra)
    {
        ArgumentNullException.ThrowIfNull(extra);
        Plane[] all = [.. _shell, .. extra];
        var corners = new List<V3>();
        foreach (V3 c in _corners)
        {
            if (Inside(all, c))
            {
                corners.Add(c);
            }
        }

        for (int i = 0; i < all.Length; i++)
        {
            for (int j = i + 1; j < all.Length; j++)
            {
                for (int k = Math.Max(j + 1, _shell.Length); k < all.Length; k++)
                {
                    var c = Meet(all[i], all[j], all[k], out bool ok);
                    if (ok && Inside(all, c) && !corners.Any(o => (o - c).Length < MergeDistance))
                    {
                        corners.Add(c);
                    }
                }
            }
        }

        return corners;
    }

    /// <summary>The shell's thickness along a direction: its extent along it plus its extent along the opposite way.</summary>
    /// <param name="n">The unit direction.</param>
    public double WidthAlong(V3 n)
    {
        double hi = double.NegativeInfinity;
        double lo = double.PositiveInfinity;
        foreach (V3 c in _corners)
        {
            double d = V3.Dot(n, c);
            hi = Math.Max(hi, d);
            lo = Math.Min(lo, d);
        }

        return hi - lo;
    }

    /// <summary>The shell's smallest thickness over its own plane normals.</summary>
    public double MinWidth()
    {
        double min = double.PositiveInfinity;
        foreach (Plane plane in _shell)
        {
            min = Math.Min(min, WidthAlong(plane.Normal));
        }

        return min;
    }

    /// <summary>The box around the shell's corners.</summary>
    public (V3 Min, V3 Max) Bounds()
    {
        var min = new V3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var max = -min;
        foreach (V3 c in _corners)
        {
            min = new V3(Math.Min(min.X, c.X), Math.Min(min.Y, c.Y), Math.Min(min.Z, c.Z));
            max = new V3(Math.Max(max.X, c.X), Math.Max(max.Y, c.Y), Math.Max(max.Z, c.Z));
        }

        return (min, max);
    }

    /// <summary>
    /// Whether this block's shell and another's overlap or come within <paramref name="gap"/> of each other (their cuts
    /// left aside): no axis separates their corners by more than the gap, of the axes that can separate two convex
    /// shells (each shell's plane normals and the cross product of each edge of one with each edge of the other).
    /// </summary>
    /// <param name="other">The other block.</param>
    /// <param name="gap">How far apart, in metres, two shells may be and still count as touching.</param>
    /// <param name="edges">This shell's <see cref="EdgeDirections"/>.</param>
    /// <param name="otherEdges">The other shell's <see cref="EdgeDirections"/>.</param>
    public bool Meets(ConvexBlock other, double gap, IReadOnlyList<V3> edges, IReadOnlyList<V3> otherEdges)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(otherEdges);
        var (aMin, aMax) = Bounds();
        var (bMin, bMax) = other.Bounds();
        if (aMin.X > bMax.X + gap || bMin.X > aMax.X + gap || aMin.Y > bMax.Y + gap || bMin.Y > aMax.Y + gap
            || aMin.Z > bMax.Z + gap || bMin.Z > aMax.Z + gap)
        {
            return false;
        }

        foreach (Plane plane in _shell)
        {
            if (Apart(other, plane.Normal, gap))
            {
                return false;
            }
        }

        foreach (Plane plane in other._shell)
        {
            if (Apart(other, plane.Normal, gap))
            {
                return false;
            }
        }

        foreach (V3 a in edges)
        {
            foreach (V3 b in otherEdges)
            {
                var axis = V3.Cross(a, b);
                double length = axis.Length;
                if (length > ParallelEdges && Apart(other, axis / length, gap))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>The two shell planes a point is nearest to, which meet at the nearest edge.</summary>
    /// <param name="p">The point, inside the shell.</param>
    /// <returns>The two plane indices (into <see cref="Shell"/>), and how far inside each plane the point is.</returns>
    public (int A, int B, double GapA, double GapB) NearestEdge(V3 p)
    {
        int a = -1;
        int b = -1;
        double va = double.NegativeInfinity;
        double vb = double.NegativeInfinity;
        for (int i = 0; i < _shell.Length; i++)
        {
            double v = _shell[i].At(p);
            if (v > va)
            {
                b = a;
                vb = va;
                a = i;
                va = v;
            }
            else if (v > vb)
            {
                b = i;
                vb = v;
            }
        }

        return (a, b, -va, -vb);
    }

    /// <summary>Whether the two shells' corners lie more than the gap apart along a unit axis.</summary>
    private bool Apart(ConvexBlock other, V3 axis, double gap)
    {
        var (aLow, aHigh) = Extent(_corners, axis);
        var (bLow, bHigh) = Extent(other._corners, axis);
        return aHigh + gap < bLow || bHigh + gap < aLow;
    }

    /// <summary>
    /// The directions of the shell's edges (each pair of shell planes that two corners lie on, once), for
    /// <see cref="Meets"/>: worked out on each call, so a caller testing many pairs works them out once per block.
    /// </summary>
    public V3[] EdgeDirections()
    {
        var edges = new List<V3>();
        for (int i = 0; i < _shell.Length; i++)
        {
            for (int j = i + 1; j < _shell.Length; j++)
            {
                int on = 0;
                foreach (V3 c in _corners)
                {
                    on += Math.Abs(_shell[i].At(c)) < OnPlane && Math.Abs(_shell[j].At(c)) < OnPlane ? 1 : 0;
                }

                var direction = V3.Cross(_shell[i].Normal, _shell[j].Normal);
                if (on >= 2 && direction.Length > ParallelEdges)
                {
                    edges.Add(direction.Normalized());
                }
            }
        }

        return [.. edges];
    }

    /// <summary>The least and most of the points' dot products with an axis.</summary>
    private static (double Low, double High) Extent(V3[] points, V3 axis)
    {
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (V3 p in points)
        {
            double d = V3.Dot(axis, p);
            low = Math.Min(low, d);
            high = Math.Max(high, d);
        }

        return (low, high);
    }

    private static V3[] FindCorners(Plane[] shell)
    {
        var corners = new List<V3>();
        for (int i = 0; i < shell.Length; i++)
        {
            for (int j = i + 1; j < shell.Length; j++)
            {
                for (int k = j + 1; k < shell.Length; k++)
                {
                    var c = Meet(shell[i], shell[j], shell[k], out bool ok);
                    if (ok && Inside(shell, c) && !corners.Any(o => (o - c).Length < MergeDistance))
                    {
                        corners.Add(c);
                    }
                }
            }
        }

        return [.. corners];
    }

    private static V3 Meet(Plane a, Plane b, Plane c, out bool ok)
    {
        var bc = V3.Cross(b.Normal, c.Normal);
        double det = V3.Dot(a.Normal, bc);
        ok = Math.Abs(det) > SingularDeterminant;
        if (!ok)
        {
            return default;
        }

        return ((a.Offset * bc) + (b.Offset * V3.Cross(c.Normal, a.Normal)) + (c.Offset * V3.Cross(a.Normal, b.Normal))) / det;
    }

    private static bool Inside(Plane[] shell, V3 p) => shell.All(plane => plane.At(p) <= CornerTolerance);
}
