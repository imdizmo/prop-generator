using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// Mends a dual-contoured mesh where two neighbouring triangles fold over each other (their faces bend more than
/// <see cref="FoldBend"/>) or one is empty (under <see cref="EmptyArea"/>: its corners in a line). Each fold is first
/// tried by turning the shared edge the other way round its quad; then, where one is left, by moving one of the quad's
/// four corners toward the middle of the points around it and stepping it back onto the surface. A change is kept only
/// when every triangle it touches has area and faces out of the solid (along the field's slope at its middle) and the
/// worst bend there gets better; no point moves more than <see cref="Reach"/> cells from where the mesher put it, and
/// no triangle is added or dropped, so the mesh stays closed and 2-manifold. Triangles are looked at in index order, a
/// few rounds at most, so the same mesh always comes out the same.
/// </summary>
internal static class MeshMend
{
    /// <summary>Two neighbouring triangles whose faces bend more than this, in degrees, fold over each other.</summary>
    internal const double FoldBend = 150;

    /// <summary>A triangle with less area than this, in square metres, is empty.</summary>
    internal const double EmptyArea = 1e-10;

    /// <summary>How many rounds of edge turns are made at most.</summary>
    private const int TurnRounds = 4;

    /// <summary>How many rounds of point moves are made at most.</summary>
    private const int MoveRounds = 10;

    /// <summary>How far a point may move from where the mesher put it, as a share of a cell.</summary>
    private const double Reach = 1;

    /// <summary>The step of the differences that give the field's slope, as a share of a cell.</summary>
    private const double SlopeStep = 0.01;

    /// <summary>How many steps a moved point takes back onto the surface, at most.</summary>
    private const int SettleSteps = 4;

    /// <summary>The cosine of <see cref="FoldBend"/>.</summary>
    private static readonly double FoldCos = JsMath.Cos(FoldBend * Math.PI / 180);

    /// <summary>Mends a mesh's folds and empty triangles (see <see cref="MeshMend"/>).</summary>
    /// <param name="mesh">The mesh, closed and consistently wound; its points and triangles are changed in place.</param>
    /// <param name="field">The field the mesh was made from.</param>
    /// <param name="cell">The grid's spacing the mesh was made at, in metres.</param>
    /// <returns>How many changes were made.</returns>
    public static int Mend(SolidMesh mesh, ISolidField field, double cell)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(field);
        return new Mender(mesh, field, cell).Run();
    }

    /// <summary>One mesh's mending: its triangles, the triangles around each point, and the points moved so far.</summary>
    private sealed class Mender
    {
        private readonly SolidMesh _mesh;
        private readonly ISolidField _field;
        private readonly double _cell;
        private readonly double _step;
        private readonly int[] _tri;
        private readonly int[] _start;
        private readonly int[] _around;

        /// <summary>The triangles around the points whose triangles changed since the mesh was read (else see <see cref="_around"/>).</summary>
        private readonly Dictionary<int, List<int>> _changed = [];

        /// <summary>Where each moved point was first.</summary>
        private readonly Dictionary<int, V3> _home = [];

        public Mender(SolidMesh mesh, ISolidField field, double cell)
        {
            _mesh = mesh;
            _field = field;
            _cell = cell;
            _step = SlopeStep * cell;
            _tri = [.. mesh.Indices];
            int count = mesh.Points.Count;
            _start = new int[count + 1];
            foreach (int v in _tri)
            {
                _start[v + 1]++;
            }

            for (int v = 0; v < count; v++)
            {
                _start[v + 1] += _start[v];
            }

            _around = new int[_tri.Length];
            int[] fill = _start[..^1];
            for (int i = 0; i < _tri.Length; i++)
            {
                _around[fill[_tri[i]]++] = i / 3;
            }
        }

        public int Run()
        {
            int triangles = _tri.Length / 3;

            // Only triangles with a bad edge are looked at, and after a change only those near it.
            bool[] bad = new bool[triangles];
            Parallel.For(0, triangles, t =>
            {
                for (int k = 0; k < 3 && !bad[t]; k++)
                {
                    int u = Across(t, k);
                    bad[t] = u >= 0 && Bad(t, u);
                }
            });

            var watch = new SortedSet<int>(Enumerable.Range(0, triangles).Where(t => bad[t]));
            var next = new SortedSet<int>();
            int changes = 0;
            for (int round = 0; round < TurnRounds && watch.Count > 0; round++)
            {
                next.Clear();
                foreach (int t in watch)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int u = Across(t, k);
                        if (u < 0 || !Bad(t, u))
                        {
                            continue;
                        }

                        if (TryTurn(t, k, u))
                        {
                            changes++;
                            Near(t, next);
                            Near(u, next);
                            break;
                        }

                        next.Add(t);
                    }
                }

                (watch, next) = (next, watch);
            }

            Span<int> corners = stackalloc int[4];
            for (int round = 0; round < MoveRounds && watch.Count > 0; round++)
            {
                next.Clear();
                foreach (int t in watch)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int u = Across(t, k);
                        if (u < 0 || !Bad(t, u))
                        {
                            continue;
                        }

                        Quad(t, k, u, corners);
                        if (TryMove(corners) is int moved)
                        {
                            changes++;
                            foreach (int around in Around(moved))
                            {
                                Near(around, next);
                            }
                        }
                        else
                        {
                            next.Add(t);
                        }
                    }
                }

                (watch, next) = (next, watch);
            }

            _mesh.Indices.Clear();
            _mesh.Indices.AddRange(_tri);
            return changes;
        }

        /// <summary>The triangles around a point.</summary>
        private IEnumerable<int> Around(int v) =>
            _changed.TryGetValue(v, out var list) ? list : new ArraySegment<int>(_around, _start[v], _start[v + 1] - _start[v]);

        /// <summary>The triangle across a triangle's k-th edge (from its corner k to the next), or −1.</summary>
        private int Across(int t, int k) => Across(_tri[(3 * t) + k], _tri[(3 * t) + ((k + 1) % 3)], t);

        /// <summary>The triangle other than t that has both points, or −1.</summary>
        private int Across(int a, int b, int t)
        {
            if (_changed.Count > 0 && _changed.TryGetValue(a, out var list))
            {
                foreach (int u in list)
                {
                    if (u != t && Has(u, b))
                    {
                        return u;
                    }
                }

                return -1;
            }

            for (int i = _start[a]; i < _start[a + 1]; i++)
            {
                int u = _around[i];
                if (u != t && Has(u, b))
                {
                    return u;
                }
            }

            return -1;
        }

        private bool Has(int t, int v) => _tri[3 * t] == v || _tri[(3 * t) + 1] == v || _tri[(3 * t) + 2] == v;

        /// <summary>Puts a triangle and its three neighbours in a set.</summary>
        private void Near(int t, SortedSet<int> into)
        {
            into.Add(t);
            for (int k = 0; k < 3; k++)
            {
                int u = Across(t, k);
                if (u >= 0)
                {
                    into.Add(u);
                }
            }
        }

        /// <summary>The quad of t and u round their shared edge, t's k-th: that edge's ends, t's third corner, u's third corner.</summary>
        private void Quad(int t, int k, int u, Span<int> corners)
        {
            int a = _tri[(3 * t) + k];
            int b = _tri[(3 * t) + ((k + 1) % 3)];
            corners[0] = a;
            corners[1] = b;
            corners[2] = _tri[(3 * t) + ((k + 2) % 3)];
            corners[3] = -1;
            for (int j = 0; j < 3; j++)
            {
                int v = _tri[(3 * u) + j];
                if (v != a && v != b)
                {
                    corners[3] = v;
                }
            }
        }

        private V3 Normal(int t, out double area)
        {
            var points = _mesh.Points;
            var p = points[_tri[3 * t]];
            var cross = V3.Cross(points[_tri[(3 * t) + 1]] - p, points[_tri[(3 * t) + 2]] - p);
            area = cross.Length / 2;
            return area > 0 ? cross / (2 * area) : default;
        }

        /// <summary>How well two neighbours meet: the cosine of their bend, −2 if either is empty.</summary>
        private double Meet(int t, int u)
        {
            var n = Normal(t, out double at);
            var m = Normal(u, out double au);
            return at < EmptyArea || au < EmptyArea ? -2 : V3.Dot(n, m);
        }

        private bool Bad(int t, int u) => Meet(t, u) < FoldCos;

        /// <summary>Whether a triangle has area and faces the way the field grows at its middle.</summary>
        private bool Out(int t)
        {
            var n = Normal(t, out double area);
            var points = _mesh.Points;
            return area >= EmptyArea
                && V3.Dot(n, Slope((points[_tri[3 * t]] + points[_tri[(3 * t) + 1]] + points[_tri[(3 * t) + 2]]) / 3)) > 0;
        }

        private V3 Slope(V3 m) => new V3(
            _field.Distance(m + new V3(_step, 0, 0)) - _field.Distance(m - new V3(_step, 0, 0)),
            _field.Distance(m + new V3(0, _step, 0)) - _field.Distance(m - new V3(0, _step, 0)),
            _field.Distance(m + new V3(0, 0, _step)) - _field.Distance(m - new V3(0, 0, _step))) / (2 * _step);

        /// <summary>
        /// Turns the edge between t (whose corner k starts it) and u: t (a, b, c) and u (b, a, d) become (a, d, c) and
        /// (d, b, c), each running its edges the way the old pair did; kept if both face out and the worst bend across
        /// the quad's five edges gets better.
        /// </summary>
        private bool TryTurn(int t, int k, int u)
        {
            Span<int> q = stackalloc int[4];
            Quad(t, k, u, q);
            int a = q[0];
            int b = q[1];
            int c = q[2];
            int d = q[3];
            if (d < 0 || d == c || Across(c, d, -1) >= 0)
            {
                return false;
            }

            int ad = Across(a, d, u);
            int db = Across(d, b, u);
            int bc = Across(b, c, t);
            int ca = Across(c, a, t);
            double worst = Math.Min(Math.Min(Meet(t, u), Math.Min(Meet(u, ad), Meet(u, db))), Math.Min(Meet(t, bc), Meet(t, ca)));
            Span<int> keep = [_tri[3 * t], _tri[(3 * t) + 1], _tri[(3 * t) + 2], _tri[3 * u], _tri[(3 * u) + 1], _tri[(3 * u) + 2]];
            (_tri[3 * t], _tri[(3 * t) + 1], _tri[(3 * t) + 2]) = (a, d, c);
            (_tri[3 * u], _tri[(3 * u) + 1], _tri[(3 * u) + 2]) = (d, b, c);
            double better = Math.Min(Math.Min(Meet(t, u), Math.Min(Meet(t, ad), Meet(u, db))), Math.Min(Meet(u, bc), Meet(t, ca)));
            if (better > worst && Out(t) && Out(u))
            {
                // a and b each lose one of the pair, c and d each gain one.
                Replace(a, u, -1);
                Replace(b, t, -1);
                Replace(c, -1, u);
                Replace(d, -1, t);
                return true;
            }

            keep[..3].CopyTo(_tri.AsSpan(3 * t, 3));
            keep[3..].CopyTo(_tri.AsSpan(3 * u, 3));
            return false;
        }

        /// <summary>Changes the triangles around a point: drops <paramref name="from"/> (unless −1) and adds <paramref name="to"/> (unless −1).</summary>
        private void Replace(int v, int from, int to)
        {
            if (!_changed.TryGetValue(v, out var list))
            {
                _changed[v] = list = [.. Around(v)];
            }

            if (from >= 0)
            {
                list.Remove(from);
            }

            if (to >= 0)
            {
                list.Add(to);
                list.Sort();
            }
        }

        /// <summary>
        /// Moves one of a quad's corners (to the middle of the points around it, or half way there, stepped back onto
        /// the surface), whichever makes the worst of the triangles around the four corners best, if any is better than
        /// now and every one of them faces out.
        /// </summary>
        /// <returns>The point moved, or null.</returns>
        private int? TryMove(ReadOnlySpan<int> corners)
        {
            if (corners[3] < 0)
            {
                return null;
            }

            var points = _mesh.Points;
            double best = Quality(corners);
            int chosen = -1;
            var moveTo = default(V3);
            foreach (int v in corners)
            {
                var was = points[v];
                var home = _home.TryGetValue(v, out var first) ? first : was;
                var ring = Ring(v);
                for (int part = 0; part < 2; part++)
                {
                    var to = Settle(part == 0 ? ring : (ring + was) / 2);
                    if ((to - home).Length > Reach * _cell)
                    {
                        continue;
                    }

                    points[v] = to;
                    double quality = Quality(corners);
                    points[v] = was;
                    if (quality > best)
                    {
                        best = quality;
                        chosen = v;
                        moveTo = to;
                    }
                }
            }

            if (chosen < 0)
            {
                return null;
            }

            _home.TryAdd(chosen, points[chosen]);
            points[chosen] = moveTo;
            return chosen;
        }

        /// <summary>The middle of the points that share a triangle with a point.</summary>
        private V3 Ring(int v)
        {
            var sum = default(V3);
            int n = 0;
            foreach (int t in Around(v))
            {
                for (int k = 0; k < 3; k++)
                {
                    if (_tri[(3 * t) + k] != v)
                    {
                        sum += _mesh.Points[_tri[(3 * t) + k]];
                        n++;
                    }
                }
            }

            return sum / n;
        }

        /// <summary>A point stepped onto the surface along the field's slope.</summary>
        private V3 Settle(V3 p)
        {
            for (int i = 0; i < SettleSteps; i++)
            {
                double f = _field.Distance(p);
                if (Math.Abs(f) <= 1e-4 * _cell)
                {
                    break;
                }

                var g = Slope(p);
                double g2 = V3.Dot(g, g);
                if (g2 < 1e-12)
                {
                    break;
                }

                p -= g * (f / g2);
            }

            return p;
        }

        /// <summary>
        /// The worst of the triangles around some points: −3 where one faces in or has no area, else the cosine of the
        /// sharpest bend between any of them and a neighbour.
        /// </summary>
        private double Quality(ReadOnlySpan<int> corners)
        {
            double worst = 1;
            foreach (int v in corners)
            {
                foreach (int t in Around(v))
                {
                    if (!Out(t))
                    {
                        return -3;
                    }

                    for (int k = 0; k < 3; k++)
                    {
                        int u = Across(t, k);
                        worst = u < 0 ? worst : Math.Min(worst, Meet(t, u));
                    }
                }
            }

            return worst;
        }
    }
}
