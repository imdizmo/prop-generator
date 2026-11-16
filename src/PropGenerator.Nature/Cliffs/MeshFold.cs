using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// Collapses away the folds <see cref="MeshMend"/> cannot turn or move out of: where two neighbouring triangles fold
/// over each other (their faces bend more than <see cref="MeshMend.FoldBend"/>), a sliver wedge finer than a grid cell
/// (a chip's step, say). Of the two folded triangles' edges, shortest first, then those of the triangles around them,
/// one is collapsed by moving one of its ends onto the other, dropping the two triangles across it (a half-edge
/// collapse, as <see cref="MeshSimplify"/> makes: no point is ever moved anywhere but onto another mesh point). A
/// collapse is kept only when the edge's link is clean (its two ends share only the two points across it, so the mesh
/// stays closed and 2-manifold), no point is left with fewer than three triangles, no point on a held height moves off
/// it, every triangle left at the moved point has area and faces out of the solid (along the field's slope at its
/// middle), and the folds among the triangles it touches fall. Folds are looked at in a fixed order, a few rounds at
/// most, so the same mesh always comes out the same.
/// </summary>
internal static class MeshFold
{
    /// <summary>How many rounds are made at most.</summary>
    private const int Rounds = 4;

    /// <summary>The step of the differences that give the field's slope, as a share of a cell.</summary>
    private const double SlopeStep = 0.01;

    /// <summary>A point within this of a held height is on it, in metres.</summary>
    private const double HeldHair = 1e-9;

    /// <summary>The cosine of <see cref="MeshMend.FoldBend"/>.</summary>
    private static readonly double FoldCos = JsMath.Cos(MeshMend.FoldBend * Math.PI / 180);

    /// <summary>Collapses a mesh's folds where it can (see <see cref="MeshFold"/>), and drops the points left unused.</summary>
    /// <param name="mesh">The mesh, closed and consistently wound, with no tags yet; changed in place.</param>
    /// <param name="field">The field the mesh was made from.</param>
    /// <param name="cell">The grid's spacing the mesh was made at, in metres.</param>
    /// <param name="tau">The error stop, in metres, as <see cref="MeshSimplify"/>'s: no collapse leaves the point it moves this far or more from the surface it makes.</param>
    /// <param name="held">Heights (z) whose points may only move onto points at the same height.</param>
    /// <returns>How many edges were collapsed.</returns>
    public static int Collapse(SolidMesh mesh, ISolidField field, double cell, double tau, IReadOnlyList<double> held)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(held);
        return new Folder(mesh, field, cell, tau, held).Run();
    }

    /// <summary>One mesh's collapses: its triangles, which are dropped, and the triangles around each point.</summary>
    private sealed class Folder
    {
        private readonly SolidMesh _mesh;
        private readonly ISolidField _field;
        private readonly double _step;

        /// <summary>The error stop, in metres (see <see cref="MeshSimplify"/>).</summary>
        private readonly double _tau;
        private readonly IReadOnlyList<double> _held;
        private readonly int[] _tri;
        private readonly bool[] _dead;
        private readonly List<int>[] _around;

        public Folder(SolidMesh mesh, ISolidField field, double cell, double tau, IReadOnlyList<double> held)
        {
            _mesh = mesh;
            _field = field;
            _step = SlopeStep * cell;
            _tau = tau;
            _held = held;
            _tri = [.. mesh.Indices];
            _dead = new bool[_tri.Length / 3];
            _around = new List<int>[mesh.Points.Count];
            for (int v = 0; v < _around.Length; v++)
            {
                _around[v] = [];
            }

            for (int t = 0; t < _dead.Length; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    _around[_tri[(3 * t) + k]].Add(t);
                }
            }
        }

        public int Run()
        {
            int collapses = 0;
            for (int round = 0; round < Rounds; round++)
            {
                int made = 0;
                foreach (var (t, u) in FoldedPairs())
                {
                    if (!_dead[t] && !_dead[u] && TryPair(t, u))
                    {
                        made++;
                    }
                }

                collapses += made;
                if (made == 0)
                {
                    break;
                }
            }

            if (collapses > 0)
            {
                Write();
            }

            return collapses;
        }

        /// <summary>Every pair of live neighbouring triangles that fold over each other, in index order.</summary>
        private List<(int T, int U)> FoldedPairs()
        {
            var pairs = new List<(int, int)>();
            for (int t = 0; t < _dead.Length; t++)
            {
                if (_dead[t])
                {
                    continue;
                }

                var n = Normal(t);
                for (int k = 0; k < 3; k++)
                {
                    int u = Across(t, _tri[(3 * t) + k], _tri[(3 * t) + ((k + 1) % 3)]);
                    if (u > t && V3.Dot(n, Normal(u)) < FoldCos)
                    {
                        pairs.Add((t, u));
                    }
                }
            }

            return pairs;
        }

        /// <summary>
        /// Tries the folded pair's edges, shortest first, each either way round; then, where none would do, the edges of
        /// the triangles around the pair's corners the same way. True when one collapsed.
        /// </summary>
        private bool TryPair(int t, int u)
        {
            return TryEdges([t, u], false) || TryEdges([.. Ring(t, u)], true);
        }

        /// <summary>The live triangles around the corners of two triangles, the two left out, in index order.</summary>
        private SortedSet<int> Ring(int t, int u)
        {
            var ring = new SortedSet<int>();
            foreach (int s in (ReadOnlySpan<int>)[t, u])
            {
                for (int k = 0; k < 3; k++)
                {
                    foreach (int r in _around[_tri[(3 * s) + k]])
                    {
                        if (!_dead[r] && r != t && r != u)
                        {
                            ring.Add(r);
                        }
                    }
                }
            }

            return ring;
        }

        /// <summary>
        /// Tries the edges of some triangles, shortest first (each once), each either way round: on the
        /// <paramref name="ring"/> around a fold, only edges no longer than the stop, each held to it (see
        /// <see cref="Merge"/>).
        /// </summary>
        private bool TryEdges(List<int> triangles, bool ring)
        {
            var edges = new SortedSet<(double Length, int A, int B)>();
            foreach (int s in triangles)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = _tri[(3 * s) + k];
                    int b = _tri[(3 * s) + ((k + 1) % 3)];
                    edges.Add(((Point(a) - Point(b)).Length, Math.Min(a, b), Math.Max(a, b)));
                }
            }

            foreach (var (length, a, b) in edges)
            {
                if (ring && length > _tau)
                {
                    break;
                }

                if (TryCollapse(a, b, ring) || TryCollapse(b, a, ring))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Collapses the edge from <paramref name="from"/> onto <paramref name="to"/> if every check holds (see <see cref="Merge"/>).</summary>
        private bool TryCollapse(int from, int to, bool ring)
        {
            // The edge's two triangles, and the points across it.
            var across = new List<int>(2);
            foreach (int s in _around[from])
            {
                if (!_dead[s] && Has(s, to))
                {
                    across.Add(s);
                }
            }

            if (across.Count != 2 || !Holds(from, to))
            {
                return false;
            }

            // The link: the two ends share only the two points across the edge, each left with three triangles or more.
            var opposite = new HashSet<int>();
            foreach (int s in across)
            {
                for (int k = 0; k < 3; k++)
                {
                    int w = _tri[(3 * s) + k];
                    if (w != from && w != to)
                    {
                        opposite.Add(w);
                    }
                }
            }

            var nearFrom = Neighbours(from);
            var nearTo = Neighbours(to);
            nearFrom.IntersectWith(nearTo);
            if (opposite.Count != 2 || !nearFrom.SetEquals(opposite) || opposite.Any(w => Live(w) < 4)
                || Live(from) + Live(to) - 4 < 3)
            {
                return false;
            }

            return Merge(from, to, across, ring);
        }

        /// <summary>
        /// Drops some triangles and moves <paramref name="from"/> onto <paramref name="to"/> in the rest of its, keeping
        /// it only if every moved triangle has area and faces out and the folds among the triangles touched fall. On the
        /// <paramref name="ring"/> around a fold the moved point must also stay within the stop of the surface it makes;
        /// an edge of the folded pair itself need not, as the fold it takes away is no surface to keep.
        /// </summary>
        private bool Merge(int from, int to, List<int> across, bool ring)
        {
            // The triangles touched: all at either end. Their folds before, and which of them fold.
            var touched = new List<int>();
            foreach (int s in _around[from].Concat(_around[to]))
            {
                if (!_dead[s] && !touched.Contains(s))
                {
                    touched.Add(s);
                }
            }

            int before = Folds(touched);
            var moved = _around[from].Where(s => !_dead[s] && !across.Contains(s)).ToList();
            var was = moved.Select(s => (Normal(s), Folded(s))).ToList();
            var from0 = Point(from);
            foreach (int s in moved)
            {
                Replace(s, from, to);
                _around[to].Add(s);
            }

            foreach (int s in across)
            {
                _dead[s] = true;
            }

            // Each moved triangle has area, open angles and faces out, and (unless it was in a fold, which it may turn out
            // of) turns no further than MeshSimplify lets a collapse turn one; on the ring, the moved point stays within
            // the stop of the surface made; and the folds among the touched triangles (the moved ones read from every
            // side) fall.
            bool fits = true;
            for (int k = 0; k < moved.Count && fits; k++)
            {
                var n = Normal(moved[k]);
                fits = Out(moved[k]) && Opens(moved[k]) && (was[k].Item2 || V3.Dot(n, was[k].Item1) >= MeshSimplify.FlipCos);
            }

            fits = fits && (!ring || Near(from0, to));
            if (!fits || Folds([.. touched.Where(s => !_dead[s])]) >= before)
            {
                foreach (int s in across)
                {
                    _dead[s] = false;
                }

                foreach (int s in moved)
                {
                    Replace(s, to, from);
                }

                _around[to].RemoveRange(_around[to].Count - moved.Count, moved.Count);
                return false;
            }

            _around[from].Clear();
            return true;
        }

        /// <summary>Whether a live triangle has an edge that folds.</summary>
        private bool Folded(int t)
        {
            var n = Normal(t);
            for (int k = 0; k < 3; k++)
            {
                int u = Across(t, _tri[(3 * t) + k], _tri[(3 * t) + ((k + 1) % 3)]);
                if (u >= 0 && V3.Dot(n, Normal(u)) < FoldCos)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether a triangle's three angles are each at least <see cref="MeshSimplify.MinAngle"/>.</summary>
        private bool Opens(int t) => MeshSimplify.Opens(Point(_tri[3 * t]), Point(_tri[(3 * t) + 1]), Point(_tri[(3 * t) + 2]));

        /// <summary>Whether a point lies within the error stop of one of the live triangles at <paramref name="v"/>.</summary>
        private bool Near(V3 p, int v)
        {
            foreach (int s in _around[v])
            {
                if (!_dead[s] && (MeshSimplify.Closest(p, Point(_tri[3 * s]), Point(_tri[(3 * s) + 1]), Point(_tri[(3 * s) + 2])) - p).Length < _tau)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether a point may move onto another: off no held height it is on.</summary>
        private bool Holds(int from, int to)
        {
            double z = Point(from).Z;
            foreach (double h in _held)
            {
                if (Math.Abs(z - h) <= HeldHair && Math.Abs(Point(to).Z - h) > HeldHair)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The points that share a live triangle with a point.</summary>
        private HashSet<int> Neighbours(int v)
        {
            var near = new HashSet<int>();
            foreach (int s in _around[v])
            {
                if (_dead[s])
                {
                    continue;
                }

                for (int k = 0; k < 3; k++)
                {
                    int w = _tri[(3 * s) + k];
                    if (w != v)
                    {
                        near.Add(w);
                    }
                }
            }

            return near;
        }

        /// <summary>How many live triangles a point is in.</summary>
        private int Live(int v) => _around[v].Count(s => !_dead[s]);

        /// <summary>How many edges among some live triangles (each counted once) fold.</summary>
        private int Folds(List<int> triangles)
        {
            int folds = 0;
            foreach (int t in triangles)
            {
                var n = Normal(t);
                for (int k = 0; k < 3; k++)
                {
                    int u = Across(t, _tri[(3 * t) + k], _tri[(3 * t) + ((k + 1) % 3)]);
                    if (u >= 0 && (u > t || !triangles.Contains(u)) && V3.Dot(n, Normal(u)) < FoldCos)
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
            foreach (int s in _around[a])
            {
                if (s != t && !_dead[s] && Has(s, b))
                {
                    return s;
                }
            }

            return -1;
        }

        /// <summary>Whether a triangle has area and faces out of the solid, along the field's slope at its middle.</summary>
        private bool Out(int t)
        {
            var a = Point(_tri[3 * t]);
            var b = Point(_tri[(3 * t) + 1]);
            var c = Point(_tri[(3 * t) + 2]);
            var cross = V3.Cross(b - a, c - a);
            if (cross.Length / 2 < MeshMend.EmptyArea)
            {
                return false;
            }

            var m = (a + b + c) / 3;
            var slope = new V3(
                _field.Distance(m + new V3(_step, 0, 0)) - _field.Distance(m - new V3(_step, 0, 0)),
                _field.Distance(m + new V3(0, _step, 0)) - _field.Distance(m - new V3(0, _step, 0)),
                _field.Distance(m + new V3(0, 0, _step)) - _field.Distance(m - new V3(0, 0, _step)));
            return V3.Dot(cross, slope) > 0;
        }

        private bool Has(int t, int v) => _tri[3 * t] == v || _tri[(3 * t) + 1] == v || _tri[(3 * t) + 2] == v;

        private void Replace(int t, int from, int to)
        {
            for (int k = 0; k < 3; k++)
            {
                if (_tri[(3 * t) + k] == from)
                {
                    _tri[(3 * t) + k] = to;
                }
            }
        }

        private V3 Point(int v) => _mesh.Points[v];

        private V3 Normal(int t)
        {
            var a = Point(_tri[3 * t]);
            return V3.Cross(Point(_tri[(3 * t) + 1]) - a, Point(_tri[(3 * t) + 2]) - a).Normalized();
        }

        /// <summary>Writes the live triangles back, and only the points they use, in their first use's order.</summary>
        private void Write()
        {
            int[] map = new int[_mesh.Points.Count];
            Array.Fill(map, -1);
            var points = new List<V3>();
            var indices = new List<int>();
            for (int t = 0; t < _dead.Length; t++)
            {
                if (_dead[t])
                {
                    continue;
                }

                for (int k = 0; k < 3; k++)
                {
                    int v = _tri[(3 * t) + k];
                    if (map[v] < 0)
                    {
                        map[v] = points.Count;
                        points.Add(_mesh.Points[v]);
                    }

                    indices.Add(map[v]);
                }
            }

            _mesh.Points.Clear();
            _mesh.Points.AddRange(points);
            _mesh.Indices.Clear();
            _mesh.Indices.AddRange(indices);
            _mesh.Tags.Clear();
        }
    }
}
