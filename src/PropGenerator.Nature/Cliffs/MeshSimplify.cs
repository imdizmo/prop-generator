using System.Runtime.CompilerServices;
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// Thins a closed mesh where it is flat, by quadric-error edge collapse (Garland and Heckbert): each point carries the
/// planes of the triangles it has gathered, and an edge is collapsed by moving one of its ends onto the other, the
/// cheapest first, while the moved point's error (the square root of its summed squared distances to those planes, so
/// never less than its distance to any one of them) stays under a distance. Flat faces collapse into large triangles;
/// creases, chips and edges, whose planes disagree, stay. Every point left is one the mesh already had, where it was.
/// A collapse is refused where it would break the mesh's link (so it stays closed and 2-manifold, one shell per shell),
/// turn a touched triangle more than <see cref="FlipCos"/> allows from how it faced just before that collapse (turns
/// over several collapses are not bounded), leave a triangle under <see cref="MeshMend.EmptyArea"/> or with an angle
/// under <see cref="MinAngle"/>, leave more folds over <see cref="MeshMend.FoldBend"/> among the triangles it touches
/// than there were (a net count: it may move a fold, never add one there), or move a point off a held height (the
/// ground) it lies on.
/// </summary>
/// <remarks>
/// The edges wait in a binary heap, stale entries skipped as they come up; a few passes pick up edges a neighbour's
/// collapse made possible. A large mesh is cut into <see cref="Regions"/> slices along its longest side, each thinned
/// on its own thread with its own heap where every point within two edges of a collapse lies inside the slice (so no
/// two threads touch the same points or triangles); then again in slices half a slice along, so the first seams lie
/// inside them; then the whole mesh in one heap. Each point a collapse thins away is kept as a home point of the
/// triangle nearest it, and no collapse may leave one the stop or more from every triangle (so the first mesh's points
/// lie within the stop of the thinned one; the other way round, the thinned surface against the first, is not checked
/// here, only sampled in the tests). The arrays are sized to the mesh, and nothing is kept after it. The same mesh
/// always comes out the same, however the threads run.
/// </remarks>
internal static class MeshSimplify
{
    /// <summary>The least cosine between a touched triangle's normal before and after a collapse.</summary>
    internal const double FlipCos = 0.2;

    /// <summary>The smallest angle, in degrees, a triangle a collapse makes may have.</summary>
    internal const double MinAngle = 3;

    /// <summary>How many passes over the edges are made at most.</summary>
    private const int MaxPasses = 6;

    /// <summary>How close to a held height a point must be to lie on it, as a share of the error stop.</summary>
    private const double HeldShare = 1e-3;

    /// <summary>How many slices a large mesh is thinned in at once (fixed, so the result does not depend on the machine).</summary>
    private const int Regions = 8;

    /// <summary>The fewest points a slice is worth: a mesh with fewer than this many per slice is thinned in one heap.</summary>
    private const int RegionPoints = 2000;

    /// <summary>The cosine of <see cref="MinAngle"/>.</summary>
    private static readonly double MinAngleCos = JsMath.Cos(MinAngle * Math.PI / 180);

    /// <summary>The cosine of <see cref="MeshMend.FoldBend"/>.</summary>
    private static readonly double FoldCos = JsMath.Cos(MeshMend.FoldBend * Math.PI / 180);

    /// <summary>Thins a mesh (see <see cref="MeshSimplify"/>); a mesh with nothing to collapse is left as it is.</summary>
    /// <param name="mesh">A closed, consistently wound mesh; its points, tags (when it has one per point) and triangles are changed in place.</param>
    /// <param name="tau">The error stop, in metres: no collapse moves a point this far or more from its planes.</param>
    /// <param name="held">Heights (z) a point lying on stays on: it is moved only onto a point at the same height.</param>
    /// <returns>How many edges were collapsed.</returns>
    public static int Simplify(SolidMesh mesh, double tau, IReadOnlyList<double> held)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tau);
        var shared = new Shared(mesh, tau, held);
        int regions = mesh.Points.Count >= Regions * RegionPoints ? Regions : 1;
        int collapses = 0;
        int token = 0;
        if (regions > 1)
        {
            // The slices, then slices half a slice along, so each seam of the first lies inside one of the second.
            foreach (double shift in new[] { 0, 0.5 })
            {
                int slices = shared.Slice(regions, shift);
                var workers = new Worker[slices];
                int start = token;
                Parallel.For(0, slices, r => (workers[r] = new Worker(shared, r, start)).Run());
                collapses += workers.Sum(w => w.Collapses);
                token = workers.Max(w => w.Token);
            }
        }

        var seams = new Worker(shared, -1, token + 1);
        seams.Run();
        collapses += seams.Collapses;
        if (collapses > 0)
        {
            shared.Write();
        }

        return collapses;
    }

    /// <summary>
    /// A candidate collapse: moving <see cref="From"/> onto <see cref="To"/>, at its cost and the edge's squared length,
    /// with both points' stamps when it was costed.
    /// </summary>
    private readonly record struct Candidate(double Cost, double Length2, int From, int To, int FromStamp, int ToStamp)
    {
        /// <summary>
        /// Whether this candidate comes before another: cheaper, then shorter (so a flat face, where every collapse costs
        /// nothing, thins evenly rather than into one fan), then by its points, so the order never depends on the heap.
        /// </summary>
        public bool Before(in Candidate other) =>
            Cost < other.Cost
            || (Cost == other.Cost && (Length2 < other.Length2
                || (Length2 == other.Length2 && (From < other.From || (From == other.From && To < other.To)))));
    }

    /// <summary>Whether each of a triangle's three angles is at least <see cref="MinAngle"/>.</summary>
    internal static bool Opens(V3 p0, V3 p1, V3 p2) =>
        Open(p1 - p0, p2 - p0) && Open(p2 - p1, p0 - p1) && Open(p0 - p2, p1 - p2);

    /// <summary>Whether the angle between two edges from a corner is at least <see cref="MinAngle"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Open(V3 a, V3 b) => V3.Dot(a, b) <= MinAngleCos * a.Length * b.Length;

    /// <summary>The point of a triangle closest to p (Ericson, Real-Time Collision Detection, 5.1.5).</summary>
    internal static V3 Closest(V3 p, V3 a, V3 b, V3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        double d1 = V3.Dot(ab, ap);
        double d2 = V3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0)
        {
            return a;
        }

        var bp = p - b;
        double d3 = V3.Dot(ab, bp);
        double d4 = V3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3)
        {
            return b;
        }

        double vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            return a + (ab * (d1 / (d1 - d3)));
        }

        var cp = p - c;
        double d5 = V3.Dot(ab, cp);
        double d6 = V3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6)
        {
            return c;
        }

        double vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            return a + (ac * (d2 / (d2 - d6)));
        }

        double va = (d3 * d6) - (d5 * d4);
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            return b + ((c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6))));
        }

        double scale = 1 / (va + vb + vc);
        return a + (ab * (vb * scale)) + (ac * (vc * scale));
    }

    /// <summary>
    /// One mesh as it is thinned: its points, triangles, the corners at each point, each point's planes, held height
    /// and slice, and marks the workers set (each only on points and triangles of its own slice).
    /// </summary>
    private sealed class Shared
    {
        private readonly SolidMesh _mesh;

        public Shared(SolidMesh mesh, double tau, IReadOnlyList<double> held)
        {
            _mesh = mesh;
            Tau2 = tau * tau;
            P = [.. mesh.Points];
            Tri = [.. mesh.Indices];
            int points = P.Length;
            int triangles = Tri.Length / 3;
            Head = new int[points];
            Next = new int[Tri.Length];
            Dead = new bool[triangles];
            Stamp = new int[points];
            Q = new double[10 * points];
            Held = new int[points];
            Region = new int[points];
            NearFrom = new int[points];
            NearTo = new int[points];
            InFan = new int[triangles];
            Home = new int[triangles];
            NextHomed = new int[points];
            Array.Fill(Head, -1);
            Array.Fill(Home, -1);
            for (int c = Tri.Length - 1; c >= 0; c--)
            {
                Next[c] = Head[Tri[c]];
                Head[Tri[c]] = c;
            }

            for (int t = 0; t < triangles; t++)
            {
                var a = P[Tri[3 * t]];
                var n = V3.Cross(P[Tri[(3 * t) + 1]] - a, P[Tri[(3 * t) + 2]] - a);
                double length = n.Length;
                if (length == 0)
                {
                    continue;
                }

                n /= length;
                double d = -V3.Dot(n, a);
                for (int k = 0; k < 3; k++)
                {
                    AddPlane(Tri[(3 * t) + k], n, d);
                }
            }

            double near = HeldShare * tau;
            for (int v = 0; v < points; v++)
            {
                Held[v] = -1;
                for (int h = 0; h < held.Count && Held[v] < 0; h++)
                {
                    Held[v] = Math.Abs(P[v].Z - held[h]) <= near ? h : -1;
                }
            }
        }

        /// <summary>The error stop, squared.</summary>
        public double Tau2 { get; }

        public V3[] P { get; }

        public int[] Tri { get; }

        /// <summary>Each point's first corner (−1 for none); the corners at a point are chained by <see cref="Next"/>.</summary>
        public int[] Head { get; }

        /// <summary>Each corner's next corner at the same point, or −1.</summary>
        public int[] Next { get; }

        public bool[] Dead { get; }

        /// <summary>Each point's stamp, raised whenever its planes or its triangles change; −1 once it is moved away.</summary>
        public int[] Stamp { get; }

        /// <summary>Each point's quadric: the symmetric 4 × 4 sum of its planes' outer products, 10 numbers a point.</summary>
        public double[] Q { get; }

        /// <summary>Each point's held height (an index into the heights), or −1.</summary>
        public int[] Held { get; }

        /// <summary>
        /// Each point's slice when it and every point it shares a triangle with lie in that slice, else −1 (see
        /// <see cref="Slice"/>).
        /// </summary>
        public int[] Region { get; }

        /// <summary>Marks for the neighbours of the two ends of an edge.</summary>
        public int[] NearFrom { get; }

        /// <summary>Marks for the neighbours of a point, so each is seen once.</summary>
        public int[] NearTo { get; }

        /// <summary>Marks for the triangles whose folds are being counted.</summary>
        public int[] InFan { get; }

        /// <summary>
        /// Each triangle's first home point (−1 for none): a point thinned away that lies nearest it, within the stop;
        /// the points a triangle is home to are chained by <see cref="NextHomed"/>.
        /// </summary>
        public int[] Home { get; }

        /// <summary>Each thinned point's next point with the same home triangle, or −1.</summary>
        public int[] NextHomed { get; }

        /// <summary>
        /// Cuts the points of the live triangles into equal slices along the mesh's longest side, their bounds moved
        /// along by a share of a slice (so there is one slice more when it is not 0); a point whose triangles reach into
        /// another slice belongs to none. No collapse changes which points a point shares triangles with, other than
        /// inside one slice, so this holds while the slices are thinned.
        /// </summary>
        /// <returns>How many slices there are.</returns>
        public int Slice(int regions, double shift)
        {
            var min = P[0];
            var max = P[0];
            foreach (var p in P)
            {
                min = new V3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new V3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }

            int slices = shift == 0 ? regions : regions + 1;
            var size = max - min;
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            double low = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            double span = Math.Max(axis == 0 ? size.X : axis == 1 ? size.Y : size.Z, 1e-12);
            for (int v = 0; v < P.Length; v++)
            {
                double at = axis == 0 ? P[v].X : axis == 1 ? P[v].Y : P[v].Z;
                Region[v] = Math.Clamp((int)(((at - low) / span * regions) + shift), 0, slices - 1);
            }

            int[] own = [.. Region];
            for (int t = 0; t < Dead.Length; t++)
            {
                if (Dead[t])
                {
                    continue;
                }

                int a = Tri[3 * t];
                int b = Tri[(3 * t) + 1];
                int c = Tri[(3 * t) + 2];
                if (own[a] != own[b] || own[b] != own[c])
                {
                    Region[a] = Region[b] = Region[c] = -1;
                }
            }

            return slices;
        }

        /// <summary>The summed squared distance of <paramref name="to"/> to both points' planes.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Error(int from, int to)
        {
            int a = 10 * from;
            int b = 10 * to;
            var p = P[to];
            double x = p.X, y = p.Y, z = p.Z;
            double[] q = Q;
            double e =
                ((q[a] + q[b]) * x * x) + (2 * (q[a + 1] + q[b + 1]) * x * y) + (2 * (q[a + 2] + q[b + 2]) * x * z) + (2 * (q[a + 3] + q[b + 3]) * x)
                + ((q[a + 4] + q[b + 4]) * y * y) + (2 * (q[a + 5] + q[b + 5]) * y * z) + (2 * (q[a + 6] + q[b + 6]) * y)
                + ((q[a + 7] + q[b + 7]) * z * z) + (2 * (q[a + 8] + q[b + 8]) * z)
                + (q[a + 9] + q[b + 9]);
            return Math.Max(e, 0);
        }

        /// <summary>Writes the live triangles and the points they use back into the mesh, both in their first order.</summary>
        public void Write()
        {
            int[] moved = new int[P.Length];
            Array.Fill(moved, -1);
            for (int t = 0; t < Dead.Length; t++)
            {
                if (!Dead[t])
                {
                    for (int k = 0; k < 3; k++)
                    {
                        moved[Tri[(3 * t) + k]] = 0;
                    }
                }
            }

            bool tagged = _mesh.Tags.Count == P.Length;
            double[] tags = tagged ? [.. _mesh.Tags] : [];
            _mesh.Points.Clear();
            _mesh.Tags.Clear();
            for (int v = 0; v < P.Length; v++)
            {
                if (moved[v] == 0)
                {
                    moved[v] = _mesh.Points.Count;
                    _mesh.Points.Add(P[v]);
                    if (tagged)
                    {
                        _mesh.Tags.Add(tags[v]);
                    }
                }
            }

            _mesh.Indices.Clear();
            for (int t = 0; t < Dead.Length; t++)
            {
                if (!Dead[t])
                {
                    for (int k = 0; k < 3; k++)
                    {
                        _mesh.Indices.Add(moved[Tri[(3 * t) + k]]);
                    }
                }
            }
        }

        private void AddPlane(int v, V3 n, double d)
        {
            int i = 10 * v;
            Q[i] += n.X * n.X;
            Q[i + 1] += n.X * n.Y;
            Q[i + 2] += n.X * n.Z;
            Q[i + 3] += n.X * d;
            Q[i + 4] += n.Y * n.Y;
            Q[i + 5] += n.Y * n.Z;
            Q[i + 6] += n.Y * d;
            Q[i + 7] += n.Z * n.Z;
            Q[i + 8] += n.Z * d;
            Q[i + 9] += d * d;
        }
    }

    /// <summary>
    /// One heap's thinning: of one slice (every point within two edges of a collapse in it), or, with region −1, of
    /// the whole mesh.
    /// </summary>
    private sealed class Worker(Shared mesh, int region, int token)
    {
        private readonly V3[] _p = mesh.P;
        private readonly int[] _tri = mesh.Tri;
        private readonly int[] _head = mesh.Head;
        private readonly int[] _next = mesh.Next;
        private readonly bool[] _dead = mesh.Dead;
        private readonly int[] _stamp = mesh.Stamp;
        private readonly List<int> _fanFrom = [];
        private readonly List<int> _fanTo = [];
        private readonly List<int> _around = [];
        private readonly List<int> _homeless = [];
        private readonly List<int> _homes = [];
        private Candidate[] _heap = [];
        private int _count;

        /// <summary>How many edges this worker collapsed.</summary>
        public int Collapses { get; private set; }

        /// <summary>The last mark this worker used: the next worker on the same points starts above it.</summary>
        public int Token { get; private set; } = token;

        public void Run()
        {
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                _count = 0;
                for (int t = 0; t < _dead.Length; t++)
                {
                    if (_dead[t])
                    {
                        continue;
                    }

                    for (int k = 0; k < 3; k++)
                    {
                        int a = _tri[(3 * t) + k];
                        int b = _tri[(3 * t) + ((k + 1) % 3)];
                        if (a < b && Mine(a) && Mine(b))
                        {
                            Offer(a, b, heapify: true);
                        }
                    }
                }

                for (int i = (_count / 2) - 1; i >= 0; i--)
                {
                    Down(i, _heap[i]);
                }

                int made = 0;
                while (_count > 0)
                {
                    var c = Pop();
                    if (_stamp[c.From] != c.FromStamp || _stamp[c.To] != c.ToStamp)
                    {
                        continue;
                    }

                    // The cheaper way round first; where its triangles will not have it, the other, if it is allowed.
                    int kept = TryCollapse(c.From, c.To) ? c.To
                        : Movable(c.To, c.From) && mesh.Error(c.To, c.From) < mesh.Tau2 && TryCollapse(c.To, c.From) ? c.From
                        : -1;
                    if (kept >= 0)
                    {
                        made++;
                        OfferAround(kept);
                    }
                }

                Collapses += made;
                if (made == 0)
                {
                    break;
                }
            }

            _heap = [];
        }

        /// <summary>Whether this worker may touch a point: any, for the whole mesh, else one of its slice's.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Mine(int v) => region < 0 || mesh.Region[v] == region;

        /// <summary>
        /// Puts an edge in the heap the cheaper way round it may collapse (moving the first point onto the second on a
        /// tie), if that costs under the stop; with <paramref name="heapify"/>, only appended, for the heap to be made
        /// in one go.
        /// </summary>
        private void Offer(int a, int b, bool heapify = false)
        {
            double ab = Movable(a, b) ? mesh.Error(a, b) : double.PositiveInfinity;
            double ba = Movable(b, a) ? mesh.Error(b, a) : double.PositiveInfinity;
            var (from, to, cost) = ba < ab ? (b, a, ba) : (a, b, ab);
            if (cost >= mesh.Tau2)
            {
                return;
            }

            var d = _p[to] - _p[from];
            var c = new Candidate(cost, V3.Dot(d, d), from, to, _stamp[from], _stamp[to]);
            if (heapify)
            {
                Grow();
                _heap[_count++] = c;
            }
            else
            {
                Push(c);
            }
        }

        /// <summary>Whether a point may move onto another: unless it lies on a held height, the other must lie on it too.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Movable(int from, int to) => mesh.Held[from] < 0 || mesh.Held[from] == mesh.Held[to];

        /// <summary>Offers every edge at a point.</summary>
        private void OfferAround(int v)
        {
            Fan(v, _fanTo);
            int mark = ++Token;
            foreach (int t in _fanTo)
            {
                for (int k = 0; k < 3; k++)
                {
                    int w = _tri[(3 * t) + k];
                    if (w != v && mesh.NearTo[w] != mark && Mine(w))
                    {
                        mesh.NearTo[w] = mark;
                        Offer(v, w);
                    }
                }
            }
        }

        /// <summary>The live triangles at a point, in its chain's order; the chain loses its dead corners on the way.</summary>
        private void Fan(int v, List<int> into)
        {
            into.Clear();
            int previous = -1;
            for (int c = _head[v]; c >= 0; c = _next[c])
            {
                if (_dead[c / 3])
                {
                    if (previous < 0)
                    {
                        _head[v] = _next[c];
                    }
                    else
                    {
                        _next[previous] = _next[c];
                    }

                    continue;
                }

                into.Add(c / 3);
                previous = c;
            }
        }

        /// <summary>
        /// Moves <paramref name="u"/> onto <paramref name="v"/> if every rule allows (see <see cref="MeshSimplify"/>):
        /// the two triangles on the edge go, and the others at u take v instead.
        /// </summary>
        private bool TryCollapse(int u, int v)
        {
            Fan(u, _fanFrom);
            int t1 = -1;
            int t2 = -1;
            foreach (int t in _fanFrom)
            {
                if (!MineAll(t))
                {
                    return false;
                }

                if (Has(t, v))
                {
                    if (t1 < 0)
                    {
                        t1 = t;
                    }
                    else if (t2 < 0)
                    {
                        t2 = t;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            if (t2 < 0)
            {
                return false;
            }

            int a = Third(t1, u, v);
            int b = Third(t2, u, v);
            if (a == b)
            {
                return false;
            }

            // Every triangle left at u, moved, must have area, open angles and face about the way it did.
            foreach (int t in _fanFrom)
            {
                if (t != t1 && t != t2 && !Fits(t, u, v))
                {
                    return false;
                }
            }

            // The link: u's and v's only shared neighbours are the two across the edge, and no point is left with
            // fewer than three triangles.
            int mark = ++Token;
            foreach (int t in _fanFrom)
            {
                for (int k = 0; k < 3; k++)
                {
                    mesh.NearFrom[_tri[(3 * t) + k]] = mark;
                }
            }

            Fan(v, _fanTo);
            int shared = 0;
            foreach (int t in _fanTo)
            {
                if (!MineAll(t))
                {
                    return false;
                }

                for (int k = 0; k < 3; k++)
                {
                    int w = _tri[(3 * t) + k];
                    if (w != u && w != v && mesh.NearFrom[w] == mark && mesh.NearTo[w] != mark)
                    {
                        mesh.NearTo[w] = mark;
                        shared++;
                    }
                }
            }

            if (shared != 2 || _fanFrom.Count + _fanTo.Count - 4 < 3 || Live(a) < 4 || Live(b) < 4)
            {
                return false;
            }

            // Then made, and put back if it folds more edges than there were: those of the triangles at u.
            _fanTo.Clear();
            foreach (int t in _fanFrom)
            {
                if (t != t1 && t != t2)
                {
                    _fanTo.Add(t);
                }
            }

            int last = Apply(u, v, t1, t2);
            int after = Folds(_fanTo);
            if (after > 0)
            {
                Revert(u, v, t1, t2, last);
                if (after > Folds(_fanFrom))
                {
                    return false;
                }

                Apply(u, v, t1, t2);
            }

            // Every point thinned away so far near u, and u itself, must lie within the stop of the triangles at v.
            if (!Rehome(u, v))
            {
                Revert(u, v, t1, t2, last);
                return false;
            }

            _head[u] = -1;
            int qu = 10 * u;
            int qv = 10 * v;
            for (int i = 0; i < 10; i++)
            {
                mesh.Q[qv + i] += mesh.Q[qu + i];
            }

            _stamp[u] = -1;
            _stamp[v]++;
            return true;
        }

        /// <summary>
        /// With u moved onto v: gives u and every point the triangles at u were home to the triangle at v nearest it,
        /// unless one of them lies the stop or more from all of them (then nothing changes).
        /// </summary>
        private bool Rehome(int u, int v)
        {
            _homeless.Clear();
            _homeless.Add(u);
            foreach (int t in _fanFrom)
            {
                for (int w = mesh.Home[t]; w >= 0; w = mesh.NextHomed[w])
                {
                    _homeless.Add(w);
                }
            }

            _around.Clear();
            for (int c = _head[v]; c >= 0; c = _next[c])
            {
                if (!_dead[c / 3])
                {
                    _around.Add(c / 3);
                }
            }

            _homes.Clear();
            foreach (int w in _homeless)
            {
                var p = _p[w];
                double best = mesh.Tau2;
                int home = -1;
                foreach (int t in _around)
                {
                    var gap = Closest(p, _p[_tri[3 * t]], _p[_tri[(3 * t) + 1]], _p[_tri[(3 * t) + 2]]) - p;
                    double d = V3.Dot(gap, gap);
                    if (d < best)
                    {
                        best = d;
                        home = t;
                    }
                }

                if (home < 0)
                {
                    return false;
                }

                _homes.Add(home);
            }

            foreach (int t in _fanFrom)
            {
                mesh.Home[t] = -1;
            }

            for (int i = 0; i < _homeless.Count; i++)
            {
                int w = _homeless[i];
                mesh.NextHomed[w] = mesh.Home[_homes[i]];
                mesh.Home[_homes[i]] = w;
            }

            return true;
        }

        /// <summary>Whether every corner of a triangle is one this worker may touch.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool MineAll(int t) => region < 0 || (Mine(_tri[3 * t]) && Mine(_tri[(3 * t) + 1]) && Mine(_tri[(3 * t) + 2]));

        /// <summary>
        /// Moves u onto v: the triangles at u take v, the two on the edge die, and u's chain of corners joins v's.
        /// </summary>
        /// <returns>u's last corner, to put the chains back with.</returns>
        private int Apply(int u, int v, int t1, int t2)
        {
            int last = -1;
            for (int c = _head[u]; c >= 0; c = _next[c])
            {
                last = c;
                _tri[c] = v;
            }

            _dead[t1] = true;
            _dead[t2] = true;
            _next[last] = _head[v];
            _head[v] = _head[u];
            return last;
        }

        /// <summary>Undoes <see cref="Apply"/>: u's corners (the head of v's chain up to its last one, all live before) go back to u.</summary>
        private void Revert(int u, int v, int t1, int t2, int last)
        {
            _head[v] = _next[last];
            _next[last] = -1;
            for (int c = _head[u]; c >= 0; c = _next[c])
            {
                _tri[c] = u;
            }

            _dead[t1] = false;
            _dead[t2] = false;
        }

        /// <summary>
        /// Whether a triangle at u, with u moved onto v, has area, no angle under the least, and faces no further from
        /// the way it did than allowed.
        /// </summary>
        private bool Fits(int t, int u, int v)
        {
            var was = Cross(t);
            var p0 = Moved(_tri[3 * t], u, v);
            var p1 = Moved(_tri[(3 * t) + 1], u, v);
            var p2 = Moved(_tri[(3 * t) + 2], u, v);
            var cross = V3.Cross(p1 - p0, p2 - p0);
            double length = cross.Length;
            return length / 2 >= MeshMend.EmptyArea
                && V3.Dot(cross, was) >= FlipCos * length * was.Length
                && Opens(p0, p1, p2);
        }

        /// <summary>A point's position with u moved onto v.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private V3 Moved(int w, int u, int v) => _p[w == u ? v : w];

        /// <summary>How many edges of some live triangles (each counted once) fold over <see cref="MeshMend.FoldBend"/>.</summary>
        private int Folds(List<int> triangles)
        {
            int mark = ++Token;
            foreach (int t in triangles)
            {
                mesh.InFan[t] = mark;
            }

            int folds = 0;
            foreach (int t in triangles)
            {
                var n = Cross(t).Normalized();
                for (int k = 0; k < 3; k++)
                {
                    int w = Across(t, _tri[(3 * t) + k], _tri[(3 * t) + ((k + 1) % 3)]);
                    if (w < 0 || (mesh.InFan[w] == mark && w < t))
                    {
                        continue;
                    }

                    if (V3.Dot(n, Cross(w).Normalized()) < FoldCos)
                    {
                        folds++;
                    }
                }
            }

            return folds;
        }

        /// <summary>The live triangle other than t on the edge from a to b, or −1.</summary>
        private int Across(int t, int a, int b)
        {
            for (int c = _head[a]; c >= 0; c = _next[c])
            {
                int s = c / 3;
                if (s != t && !_dead[s] && Has(s, b))
                {
                    return s;
                }
            }

            return -1;
        }

        /// <summary>How many live triangles a point has.</summary>
        private int Live(int v)
        {
            int n = 0;
            for (int c = _head[v]; c >= 0; c = _next[c])
            {
                n += _dead[c / 3] ? 0 : 1;
            }

            return n;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Has(int t, int v) => _tri[3 * t] == v || _tri[(3 * t) + 1] == v || _tri[(3 * t) + 2] == v;

        /// <summary>The corner of a triangle that is neither of two points.</summary>
        private int Third(int t, int u, int v)
        {
            for (int k = 0; k < 3; k++)
            {
                int w = _tri[(3 * t) + k];
                if (w != u && w != v)
                {
                    return w;
                }
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private V3 Cross(int t)
        {
            var a = _p[_tri[3 * t]];
            return V3.Cross(_p[_tri[(3 * t) + 1]] - a, _p[_tri[(3 * t) + 2]] - a);
        }

        private void Grow()
        {
            if (_count == _heap.Length)
            {
                Array.Resize(ref _heap, Math.Max(1024, 2 * _heap.Length));
            }
        }

        private void Push(in Candidate c)
        {
            Grow();
            int i = _count++;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!c.Before(_heap[parent]))
                {
                    break;
                }

                _heap[i] = _heap[parent];
                i = parent;
            }

            _heap[i] = c;
        }

        private Candidate Pop()
        {
            var top = _heap[0];
            if (--_count > 0)
            {
                Down(0, _heap[_count]);
            }

            return top;
        }

        /// <summary>Puts a candidate at a place in the heap and sifts it down to where it belongs.</summary>
        private void Down(int i, Candidate last)
        {
            while (true)
            {
                int child = (2 * i) + 1;
                if (child >= _count)
                {
                    break;
                }

                if (child + 1 < _count && _heap[child + 1].Before(_heap[child]))
                {
                    child++;
                }

                if (!_heap[child].Before(last))
                {
                    break;
                }

                _heap[i] = _heap[child];
                i = child;
            }

            _heap[i] = last;
        }
    }
}
