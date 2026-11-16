using System.Runtime.CompilerServices;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// Meshes a solid field by dual contouring on a uniform grid: the field's sign at the grid points, a crossing and a
/// normal on every grid edge whose ends differ in sign, one vertex per cell those edges touch (where the planes of its
/// crossings meet best), and one quad per crossing edge joining the four cells around it. The mesh is always closed and
/// consistently wound, at any field: the signs are first made free of the two configurations whose surface could pinch
/// (a face whose diagonals differ, a cube whose only inside corners are opposite), so every edge of the mesh joins
/// exactly two triangles.
/// </summary>
internal static class DualContour
{
    /// <summary>Cells of padding around the bounds, all outside, so the mesh is closed even if the bounds are tight.</summary>
    private const int Pad = 2;

    /// <summary>
    /// The shares of a cell the grid is moved by along x, y and z (2 − φ, (5 − √5) / 10 and (3 − √5)² / 4), so a flat
    /// face at the edge of the bounds lies well between grid planes, and a plane at 45° through such edges (a bevel, a
    /// chamfer) misses the grid points: their differences and sums are no simple fractions.
    /// </summary>
    private static readonly V3 Offset = new(0.381966011250105, 0.276393202250021, 0.145898033750315);

    /// <summary>How far each vertex is kept inside its cell, as a share of a cell, so no two vertices ever meet.</summary>
    private const double Inset = 1e-3;

    /// <summary>The step of the differences that give a crossing's normal, as a share of a cell.</summary>
    private const double Step = 1e-3;

    /// <summary>A crossing where the field is further than this from zero, as a share of a cell, is found again on the field.</summary>
    private const double Bent = 1e-2;

    /// <summary>A crossing found on the field stops once the field is this near zero, as a share of a cell.</summary>
    private const double Root = 1e-6;

    /// <summary>The most regula falsi steps a crossing takes.</summary>
    private const int RootSteps = 12;

    /// <summary>The most steps a vertex off the surface takes back onto it.</summary>
    private const int ProjectionSteps = 4;

    /// <summary>A vertex whose field is within this share of a cell of zero is on the surface.</summary>
    private const double OnSurface = 1e-2;

    /// <summary>Directions where the crossings' planes meet with less than this share of the strongest are left to the mass point.</summary>
    private const double Truncation = 0.05;

    /// <summary>
    /// Directions fixed with less than this share of the strongest are never solved; between it and <see cref="Truncation"/>
    /// they are solved only when the point found is inside the cell and on the surface (a gentle crease between facets).
    /// </summary>
    private const double FineTruncation = 0.0005;

    /// <summary>A quad whose two halves bend less than this (as the cosine of the bend) either way is flat.</summary>
    private const double Flat = 0.995;

    /// <summary>A triangle with less than half this many square cells of area is a sliver, and the quad is split the other way.</summary>
    private const double Sliver = 2e-6;

    /// <summary>How many edges or cells one thread takes at a time.</summary>
    private const int ChunkSize = 512;

    // Grid point states: an estimate only, the field evaluated, or a value set here that stays (padding, a flip).
    private const byte Estimated = 0;
    private const byte Evaluated = 1;
    private const byte Fixed = 2;

    // The tetrahedron of directions the normals are differenced along: second order, like central differences.
    private static readonly V3[] Tetrahedron = [new(1, -1, -1), new(-1, -1, 1), new(-1, 1, -1), new(1, 1, 1)];

    // The corners of a cube's six faces, each in order around it; corner c sits at (c & 1, c >> 1 & 1, c >> 2 & 1).
    private static readonly int[] Faces = [0, 2, 6, 4, 1, 3, 7, 5, 0, 1, 5, 4, 2, 3, 7, 6, 0, 1, 3, 2, 4, 5, 7, 6];

    /// <summary>
    /// The closed mesh of a field's solid within bounds: triangles counter-clockwise seen from outside, every tag 0.
    /// The field is read from several threads at once; the mesh is the same however they are scheduled.
    /// </summary>
    /// <param name="field">The field; it must be safe to read from several threads.</param>
    /// <param name="min">The lowest corner of a box the solid lies in.</param>
    /// <param name="max">The highest corner of that box.</param>
    /// <param name="cell">The grid's spacing, in metres.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static SolidMesh Mesh(ISolidField field, V3 min, V3 max, double cell)
    {
        ArgumentNullException.ThrowIfNull(field);
        var (origin, nx, ny, nz) = Grid(min, max, cell);
        int[] strides = [1, nx, nx * ny];
        int count = nx * ny * nz;

        // 1. The sign at every grid point: the estimate where it is sure, the field near the surface.
        double[] value = new double[count];
        byte[] state = new byte[count];
        Parallel.For(0, nz, k => Sample(field, value, state, origin, nx, ny, nz, k, cell));

        // 2. No pinches.
        Repair(value, state, nx, ny, nz, cell);

        // 3. Every edge whose ends differ, its ends' field evaluated where it was only estimated, and its crossing.
        var edges = new List<int>();
        var unsettled = new List<int>();
        int[] crossingAt = new int[3 * count];
        Array.Fill(crossingAt, -1);
        for (int axis = 0; axis < 3; axis++)
        {
            int stride = strides[axis];
            for (int k = 0, g = 0; k < nz; k++)
            {
                for (int j = 0; j < ny; j++)
                {
                    for (int i = 0; i < nx; i++, g++)
                    {
                        if ((axis == 0 ? i == nx - 1 : axis == 1 ? j == ny - 1 : k == nz - 1) || (value[g] < 0) == (value[g + stride] < 0))
                        {
                            continue;
                        }

                        crossingAt[(axis * count) + g] = edges.Count;
                        edges.Add((axis * count) + g);
                        foreach (int end in (ReadOnlySpan<int>)[g, g + stride])
                        {
                            if (state[end] == Estimated)
                            {
                                state[end] = Evaluated;
                                unsettled.Add(end);
                            }
                        }
                    }
                }
            }
        }

        Parallel.For(0, unsettled.Count, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (u) =>
        {
            int g = unsettled[u];
            double v = field.Distance(Point(origin, nx, ny, cell, g));

            // Far from zero the estimate's sign is the field's; should the field disagree, the sign the repair saw stays.
            value[g] = (v < 0) == (value[g] < 0) ? v : value[g] < 0 ? -1e-9 : 1e-9;
        });

        var points = new V3[edges.Count];
        var normals = new V3[edges.Count];
        Parallel.For(0, Chunks(edges.Count), [MethodImpl(MethodImplOptions.AggressiveOptimization)] (chunk) =>
        {
            for (int x = chunk * ChunkSize; x < Math.Min(edges.Count, (chunk + 1) * ChunkSize); x++)
            {
                int axis = edges[x] / count;
                int g = edges[x] % count;
                int h = g + strides[axis];
                var direction = axis == 0 ? new V3(1, 0, 0) : axis == 1 ? new V3(0, 1, 0) : new V3(0, 0, 1);
                var (point, normal) = Crossing(field, Point(origin, nx, ny, cell, g), direction, value[g], value[h], state[g] != Fixed && state[h] != Fixed, cell);
                points[x] = point;
                normals[x] = normal.Length > 0.5 ? normal : direction * (value[g] < 0 ? 1 : -1);
            }
        });

        // 4. One vertex per cell with crossings, numbered in grid order.
        int[] vertexOf = new int[count];
        Array.Fill(vertexOf, -1);
        foreach (int e in edges)
        {
            int axis = e / count;
            int g = e % count;
            int b = strides[(axis + 1) % 3];
            int c = strides[(axis + 2) % 3];
            vertexOf[g] = vertexOf[g - b] = vertexOf[g - c] = vertexOf[g - b - c] = -2;
        }

        var cells = new List<int>();
        for (int g = 0; g < count; g++)
        {
            if (vertexOf[g] == -2)
            {
                vertexOf[g] = cells.Count;
                cells.Add(g);
            }
        }

        var positions = new V3[cells.Count];
        Parallel.For(0, Chunks(cells.Count), [MethodImpl(MethodImplOptions.AggressiveOptimization)] (chunk) =>
        {
            Span<V3> cellPoints = stackalloc V3[12];
            Span<V3> cellNormals = stackalloc V3[12];
            for (int v = chunk * ChunkSize; v < Math.Min(cells.Count, (chunk + 1) * ChunkSize); v++)
            {
                int g = cells[v];
                int n = 0;
                for (int axis = 0; axis < 3; axis++)
                {
                    int b = strides[(axis + 1) % 3];
                    int c = strides[(axis + 2) % 3];
                    foreach (int e in (ReadOnlySpan<int>)[g, g + b, g + c, g + b + c])
                    {
                        int x = crossingAt[(axis * count) + e];
                        if (x >= 0)
                        {
                            cellPoints[n] = points[x];
                            cellNormals[n++] = normals[x];
                        }
                    }
                }

                var low = Point(origin, nx, ny, cell, g);
                positions[v] = Place(field, cellPoints[..n], cellNormals[..n], low, low + new V3(cell, cell, cell), cell);
            }
        });

        // 5. One quad per crossing edge, from the four cells around it, in two triangles facing out; a quad that neither
        // diagonal splits into two such triangles is marked, and fanned out from a point of its own afterwards.
        int[] triangles = new int[6 * edges.Count];
        bool[] fan = new bool[edges.Count];
        Parallel.For(0, Chunks(edges.Count), [MethodImpl(MethodImplOptions.AggressiveOptimization)] (chunk) =>
        {
            for (int x = chunk * ChunkSize; x < Math.Min(edges.Count, (chunk + 1) * ChunkSize); x++)
            {
                int axis = edges[x] / count;
                int g = edges[x] % count;
                int b = strides[(axis + 1) % 3];
                int c = strides[(axis + 2) % 3];
                int v00 = vertexOf[g - b - c];
                int v10 = vertexOf[g - c];
                int v11 = vertexOf[g];
                int v01 = vertexOf[g - b];

                // Counter-clockwise seen from the edge's outside end.
                var quad = triangles.AsSpan(6 * x, 6);
                fan[x] = value[g] < 0
                    ? !Split(field, positions, v00, v10, v11, v01, normals[x], cell, quad)
                    : !Split(field, positions, v00, v01, v11, v10, normals[x], cell, quad);
            }
        });

        var mesh = new SolidMesh();
        mesh.Points.AddRange(positions);
        mesh.Tags.AddRange(new double[positions.Length]);
        for (int x = 0; x < edges.Count; x++)
        {
            if (!fan[x])
            {
                mesh.Indices.AddRange(triangles.AsSpan(6 * x, 6));
                continue;
            }

            // The quad's corners in order (the first triangle's, then the second's last), round a point of the
            // surface near their middle.
            ReadOnlySpan<int> q = [triangles[6 * x], triangles[(6 * x) + 1], triangles[(6 * x) + 2], triangles[(6 * x) + 5]];
            int middle = mesh.Points.Count;
            mesh.Points.Add(Settle(field, (positions[q[0]] + positions[q[1]] + positions[q[2]] + positions[q[3]]) / 4, cell));
            mesh.Tags.Add(0);
            for (int k = 0; k < 4; k++)
            {
                mesh.Indices.AddRange([q[k], q[(k + 1) % 4], middle]);
            }
        }

        return mesh;
    }

    /// <summary>A point stepped onto the surface along the field's slope (at most <see cref="ProjectionSteps"/> steps).</summary>
    private static V3 Settle(ISolidField field, V3 point, double cell)
    {
        for (int step = 0; step < ProjectionSteps; step++)
        {
            var (gradient, value) = Gradient(field, point, cell);
            double slope = V3.Dot(gradient, gradient);
            if (Math.Abs(value) <= Root * cell || slope < 1e-12)
            {
                break;
            }

            point -= gradient * (value / slope);
        }

        return point;
    }

    /// <summary>The place of grid point g.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static V3 Point(V3 origin, int nx, int ny, double cell, int g) =>
        origin + (new V3(g % nx, g / nx % ny, g / (nx * ny)) * cell);

    /// <summary>
    /// The signs of one layer of the grid (z index <paramref name="k"/>): the padding outside, the estimate where it is
    /// sure, and the field where the surface may be.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Sample(ISolidField field, double[] value, byte[] state, V3 origin, int nx, int ny, int nz, int k, double cell)
    {
        for (int j = 0, g = k * nx * ny; j < ny; j++)
        {
            for (int i = 0; i < nx; i++, g++)
            {
                if (i == 0 || j == 0 || k == 0 || i == nx - 1 || j == ny - 1 || k == nz - 1)
                {
                    value[g] = cell;
                    state[g] = Fixed;
                    continue;
                }

                var p = origin + (new V3(i, j, k) * cell);
                double estimate = field.Estimate(p, out double error);
                if (Math.Abs(estimate) <= error)
                {
                    value[g] = field.Distance(p);
                    state[g] = Evaluated;
                }
                else
                {
                    value[g] = estimate;
                }
            }
        }
    }

    /// <summary>
    /// Whether the field is negative at any point of the grid <see cref="Mesh"/> would sample: whether the mesh would
    /// have any triangles. The middle of the bounds is tried first, then every point, layers at a time on several
    /// threads, stopping at the first point inside.
    /// </summary>
    /// <param name="field">The field; it must be safe to read from several threads.</param>
    /// <param name="min">The lowest corner of a box the solid lies in.</param>
    /// <param name="max">The highest corner of that box.</param>
    /// <param name="cell">The grid's spacing, in metres.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool HasInside(ISolidField field, V3 min, V3 max, double cell)
    {
        ArgumentNullException.ThrowIfNull(field);
        var (origin, nx, ny, nz) = Grid(min, max, cell);
        if (Inside(field, origin + (new V3(nx / 2, ny / 2, nz / 2) * cell)))
        {
            return true;
        }

        var found = Parallel.For(1, nz - 1, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (k, loop) =>
        {
            for (int j = 1; j < ny - 1 && !loop.ShouldExitCurrentIteration; j++)
            {
                for (int i = 1; i < nx - 1; i++)
                {
                    if (Inside(field, origin + (new V3(i, j, k) * cell)))
                    {
                        loop.Stop();
                        return;
                    }
                }
            }
        });
        return !found.IsCompleted;
    }

    /// <summary>Whether the field is negative at a point, read only when its estimate is not sure.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Inside(ISolidField field, V3 p)
    {
        double estimate = field.Estimate(p, out double error);
        return estimate < -error || (estimate <= error && field.Distance(p) < 0);
    }

    /// <summary>
    /// The grid over bounds: its first point and its points along x, y and z, padded by <see cref="Pad"/> cells (and
    /// moved by <see cref="Offset"/> of one) on every side.
    /// </summary>
    internal static (V3 Origin, int Nx, int Ny, int Nz) Grid(V3 min, V3 max, double cell)
    {
        var pad = new V3(Pad + Offset.X, Pad + Offset.Y, Pad + Offset.Z);
        var origin = min - (pad * cell);
        return (origin, Points(min.X, max.X, pad.X), Points(min.Y, max.Y, pad.Y), Points(min.Z, max.Z, pad.Z));

        int Points(double low, double high, double padding) => (int)Math.Ceiling(((high - low) / cell) + (2 * padding)) + 1;
    }

    /// <summary>
    /// Makes the signs well composed: while a cell has a face whose diagonals differ in sign, or only two opposite
    /// corners of one sign, the outside corner nearest the surface there is taken inside (its value just below 0).
    /// Only ever adding inside points, it ends; the padding is never touched (no such cell can need it).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Repair(double[] value, byte[] state, int nx, int ny, int nz, double cell)
    {
        int sy = nx;
        int sz = nx * ny;
        int[] offsets = [0, 1, sy, sy + 1, sz, sz + 1, sz + sy, sz + sy + 1];
        var queue = new Queue<int>();
        bool[] queued = new bool[nx * ny * nz];
        for (int k = 0; k < nz - 1; k++)
        {
            for (int j = 0; j < ny - 1; j++)
            {
                for (int i = 0, g = (sy * j) + (sz * k); i < nx - 1; i++, g++)
                {
                    int flip = Pinch(value, state, offsets, g);
                    if (flip >= 0)
                    {
                        Flip(value, state, queue, queued, flip, nx, ny, nz, cell);
                    }
                }
            }
        }

        while (queue.Count > 0)
        {
            int g = queue.Dequeue();
            queued[g] = false;
            int flip = Pinch(value, state, offsets, g);
            if (flip >= 0)
            {
                Flip(value, state, queue, queued, flip, nx, ny, nz, cell);
            }
        }
    }

    /// <summary>Takes a grid point inside, for good, and has the eight cells around it looked at again.</summary>
    private static void Flip(double[] value, byte[] state, Queue<int> queue, bool[] queued, int flip, int nx, int ny, int nz, double cell)
    {
        value[flip] = -1e-6 * cell;
        state[flip] = Fixed;
        int fi = flip % nx;
        int fj = flip / nx % ny;
        int fk = flip / (nx * ny);
        for (int c = 0; c < 8; c++)
        {
            int ci = fi - (c & 1);
            int cj = fj - ((c >> 1) & 1);
            int ck = fk - ((c >> 2) & 1);
            if (ci >= 0 && cj >= 0 && ck >= 0 && ci < nx - 1 && cj < ny - 1 && ck < nz - 1)
            {
                int cg = ci + (nx * cj) + (nx * ny * ck);
                if (!queued[cg])
                {
                    queue.Enqueue(cg);
                    queued[cg] = true;
                }
            }
        }
    }

    /// <summary>The grid point to take inside to undo a pinch in the cell whose lowest corner is g, or −1 when it has none.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int Pinch(double[] value, byte[] state, int[] offsets, int g)
    {
        int mask = 0;
        for (int c = 0; c < 8; c++)
        {
            mask |= value[g + offsets[c]] < 0 ? 1 << c : 0;
        }

        if (mask is 0 or 255)
        {
            return -1;
        }

        for (int f = 0; f < 24; f += 4)
        {
            int a = Faces[f];
            int b = Faces[f + 1];
            int c = Faces[f + 2];
            int d = Faces[f + 3];
            bool ia = (mask >> a & 1) != 0;
            bool ib = (mask >> b & 1) != 0;
            if (ia == ((mask >> c & 1) != 0) && ia != ib && ib == ((mask >> d & 1) != 0))
            {
                return ia ? Nearer(value, state, g + offsets[b], g + offsets[d]) : Nearer(value, state, g + offsets[a], g + offsets[c]);
            }
        }

        int inside = int.PopCount(mask);
        for (int c = 0; c < 4; c++)
        {
            int pair = (1 << c) | (1 << (7 - c));
            if (inside == 2 && mask == pair)
            {
                // Only two opposite corners inside: the outside corner nearest the surface joins them.
                int best = -1;
                for (int o = 0; o < 8; o++)
                {
                    if ((mask >> o & 1) == 0)
                    {
                        best = best < 0 ? g + offsets[o] : Nearer(value, state, best, g + offsets[o]);
                    }
                }

                return best;
            }

            if (inside == 6 && (mask & pair) == 0)
            {
                return Nearer(value, state, g + offsets[c], g + offsets[7 - c]);
            }
        }

        return -1;
    }

    /// <summary>Of two outside grid points, the one nearer the surface (the lower value); never a fixed one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Nearer(double[] value, byte[] state, int a, int b) =>
        state[a] == Fixed ? b : state[b] == Fixed ? a : value[b] < value[a] ? b : a;

    /// <summary>
    /// Where the surface crosses a grid edge, and the field's unit normal there by differences over a small
    /// tetrahedron. The crossing is first found by linear interpolation between the ends; where the field bends
    /// between them (two flat faces meeting near the edge, say) and is not near zero there, it is found on the field
    /// itself by regula falsi (the Illinois kind), keeping the two ends' signs either side. Ends whose values were set
    /// here, not read from the field, keep the interpolated crossing.
    /// </summary>
    private static (V3 Point, V3 Normal) Crossing(ISolidField field, V3 start, V3 direction, double from, double to, bool refine, double cell)
    {
        double t = from / (from - to);
        var point = start + (direction * (t * cell));
        var (gradient, value) = Gradient(field, point, cell);
        if (refine && Math.Abs(value) > Bent * cell)
        {
            double a = 0;
            double fa = from;
            double b = 1;
            double fb = to;
            int kept = 0;
            for (int step = 0; step < RootSteps && Math.Abs(value) > Root * cell; step++)
            {
                // Keep the end on the other side of the new point; halve a kept end's value if it is kept twice running.
                if ((value < 0) == (fa < 0))
                {
                    a = t;
                    fa = value;
                    fb = kept == 1 ? fb / 2 : fb;
                    kept = 1;
                }
                else
                {
                    b = t;
                    fb = value;
                    fa = kept == -1 ? fa / 2 : fa;
                    kept = -1;
                }

                t = ((a * fb) - (b * fa)) / (fb - fa);
                value = field.Distance(start + (direction * (t * cell)));
            }

            point = start + (direction * (t * cell));
            (gradient, _) = Gradient(field, point, cell);
        }

        return (point, gradient.Normalized());
    }

    /// <summary>The field's gradient at a point by differences over a small tetrahedron, and its mean value there.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (V3 Gradient, double Value) Gradient(ISolidField field, V3 point, double cell)
    {
        double h = Step * cell;
        var gradient = default(V3);
        double mean = 0;
        foreach (var k in Tetrahedron)
        {
            double f = field.Distance(point + (k * h));
            gradient += k * f;
            mean += f;
        }

        return (gradient / (4 * h), mean / 4);
    }

    /// <summary>
    /// A cell's vertex: where the planes of its crossings meet best (the normal equations of their squared distances,
    /// solved about the crossings' mean, with directions the planes barely fix left at the mean), kept inside the cell
    /// along the way from the mean, and stepped back onto the surface if it is not on it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static V3 Place(ISolidField field, ReadOnlySpan<V3> points, ReadOnlySpan<V3> normals, V3 low, V3 high, double cell)
    {
        double inset = Inset * cell;
        var mean = default(V3);
        foreach (var p in points)
        {
            mean += p;
        }

        mean /= points.Length;
        Span<double> a = stackalloc double[9];
        var rhs = default(V3);
        for (int i = 0; i < points.Length; i++)
        {
            var n = normals[i];
            double d = V3.Dot(n, points[i] - mean);
            a[0] += n.X * n.X;
            a[1] += n.X * n.Y;
            a[2] += n.X * n.Z;
            a[4] += n.Y * n.Y;
            a[5] += n.Y * n.Z;
            a[8] += n.Z * n.Z;
            rhs += n * d;
        }

        a[3] = a[1];
        a[6] = a[2];
        a[7] = a[5];
        Span<double> v = stackalloc double[9];
        Eigen(a, v);
        double strongest = Math.Max(Math.Max(a[0], a[4]), a[8]);
        var offset = default(V3);
        var fine = default(V3);
        for (int e = 0; e < 3; e++)
        {
            double lambda = a[(e * 3) + e];
            if (lambda > FineTruncation * strongest && lambda > 1e-12)
            {
                var axis = new V3(v[e], v[3 + e], v[6 + e]);
                var along = axis * (V3.Dot(axis, rhs) / lambda);
                fine += along;
                if (lambda > Truncation * strongest)
                {
                    offset += along;
                }
            }
        }

        var lo = low + new V3(inset, inset, inset);
        var hi = high - new V3(inset, inset, inset);

        // A gentle crease (flat facets meeting at a few degrees) is solved too, so it runs straight instead of in steps,
        // but only when its point is inside the cell: where it is not, the planes barely fix it, and the coarse solution
        // below is kept.
        var exact = mean + fine;
        if (exact.X >= lo.X && exact.X <= hi.X && exact.Y >= lo.Y && exact.Y <= hi.Y && exact.Z >= lo.Z && exact.Z <= hi.Z
            && Math.Abs(field.Distance(exact)) <= OnSurface * cell)
        {
            return exact;
        }

        var from = new V3(Math.Clamp(mean.X, lo.X, hi.X), Math.Clamp(mean.Y, lo.Y, hi.Y), Math.Clamp(mean.Z, lo.Z, hi.Z));
        var to = mean + offset;
        double reach = 1;
        reach = Math.Min(reach, Reach(from.X, to.X, lo.X, hi.X));
        reach = Math.Min(reach, Reach(from.Y, to.Y, lo.Y, hi.Y));
        reach = Math.Min(reach, Reach(from.Z, to.Z, lo.Z, hi.Z));
        var vertex = from + ((to - from) * reach);
        if (Math.Abs(field.Distance(vertex)) <= OnSurface * cell)
        {
            return vertex;
        }

        // Off the surface: the planes meet outside the cell (faces meeting at a point beyond it) and the vertex was held
        // in, or they barely fix a direction and the mean was kept there. It is stepped back onto the surface along the
        // field's slope, still inside the cell.
        for (int step = 0; step < ProjectionSteps; step++)
        {
            var (gradient, value) = Gradient(field, vertex, cell);
            double slope = V3.Dot(gradient, gradient);
            if (Math.Abs(value) <= Root * cell || slope < 1e-12)
            {
                break;
            }

            var next = vertex - (gradient * (value / slope));
            vertex = new V3(Math.Clamp(next.X, lo.X, hi.X), Math.Clamp(next.Y, lo.Y, hi.Y), Math.Clamp(next.Z, lo.Z, hi.Z));
        }

        return vertex;
    }

    /// <summary>How far along the way from <paramref name="from"/> to <paramref name="to"/> stays within lo..hi (1 for all of it).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Reach(double from, double to, double lo, double hi) =>
        to > hi ? (hi - from) / (to - from) : to < lo ? (lo - from) / (to - from) : 1;

    /// <summary>
    /// The eigenvalues and eigenvectors of a symmetric 3 × 3 matrix by Jacobi rotations: the matrix (row by row) ends
    /// diagonal, its eigenvalues on the diagonal, and <paramref name="v"/> holds the eigenvectors as its columns.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Eigen(Span<double> a, Span<double> v)
    {
        v.Clear();
        v[0] = v[4] = v[8] = 1;
        for (int sweep = 0; sweep < 12; sweep++)
        {
            double off = (a[1] * a[1]) + (a[2] * a[2]) + (a[5] * a[5]);
            double scale = (a[0] * a[0]) + (a[4] * a[4]) + (a[8] * a[8]);
            if (off <= 1e-30 * scale || off == 0)
            {
                return;
            }

            Rotate(a, v, 0, 1);
            Rotate(a, v, 0, 2);
            Rotate(a, v, 1, 2);
        }
    }

    /// <summary>One Jacobi rotation of the plane (p, q): zeroes the matrix's p, q entry and turns the eigenvectors with it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Rotate(Span<double> a, Span<double> v, int p, int q)
    {
        double apq = a[(p * 3) + q];
        if (apq == 0)
        {
            return;
        }

        double theta = (a[(q * 3) + q] - a[(p * 3) + p]) / (2 * apq);
        double t = (theta >= 0 ? 1 : -1) / (Math.Abs(theta) + Math.Sqrt((theta * theta) + 1));
        double c = 1 / Math.Sqrt((t * t) + 1);
        double s = t * c;
        for (int r = 0; r < 3; r++)
        {
            // Columns p and q, then rows p and q: A ← Jᵀ A J.
            double arp = a[(r * 3) + p];
            double arq = a[(r * 3) + q];
            a[(r * 3) + p] = (c * arp) - (s * arq);
            a[(r * 3) + q] = (s * arp) + (c * arq);
        }

        for (int r = 0; r < 3; r++)
        {
            double apr = a[(p * 3) + r];
            double aqr = a[(q * 3) + r];
            a[(p * 3) + r] = (c * apr) - (s * aqr);
            a[(q * 3) + r] = (s * apr) + (c * aqr);
        }

        a[(p * 3) + q] = a[(q * 3) + p] = 0;
        for (int r = 0; r < 3; r++)
        {
            double vrp = v[(r * 3) + p];
            double vrq = v[(r * 3) + q];
            v[(r * 3) + p] = (c * vrp) - (s * vrq);
            v[(r * 3) + q] = (s * vrp) + (c * vrq);
        }
    }

    /// <summary>
    /// A quad as two triangles, along the diagonal that keeps both facing out (their normals with the crossing's) and
    /// neither a sliver: if both diagonals do and the quad is bent, the one whose middle lies nearer the surface; if
    /// neither does, the one that comes closer. The six corners are written to <paramref name="corners"/>, the
    /// first triangle's corners going round the quad from its first corner and the second's ending at its last.
    /// </summary>
    /// <returns>Whether both triangles have area (else the quad is fanned round a point of its own).</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool Split(ISolidField field, V3[] positions, int q0, int q1, int q2, int q3, V3 normal, double cell, Span<int> corners)
    {
        var p0 = positions[q0];
        var p1 = positions[q1];
        var p2 = positions[q2];
        var p3 = positions[q3];
        double sliver = Sliver * cell * cell;
        var a1 = Facing(V3.Cross(p1 - p0, p2 - p0), sliver);
        var a2 = Facing(V3.Cross(p2 - p0, p3 - p0), sliver);
        var b1 = Facing(V3.Cross(p2 - p1, p3 - p1), sliver);
        var b2 = Facing(V3.Cross(p3 - p1, p0 - p1), sliver);
        double facingA = Math.Min(V3.Dot(a1, normal), V3.Dot(a2, normal));
        double facingB = Math.Min(V3.Dot(b1, normal), V3.Dot(b2, normal));
        bool useA;
        if ((facingA > 0) != (facingB > 0) || (facingA <= 0 && facingB <= 0))
        {
            useA = facingA >= facingB;
        }
        else
        {
            double bendA = V3.Dot(a1, a2);
            double bendB = V3.Dot(b1, b2);
            useA = bendA >= Flat && bendB >= Flat
                ? bendA >= bendB
                : Math.Abs(field.Distance((p0 + p2) / 2)) <= Math.Abs(field.Distance((p1 + p3) / 2));
        }

        if (useA)
        {
            ReadOnlySpan<int> a = [q0, q1, q2, q0, q2, q3];
            a.CopyTo(corners);
        }
        else
        {
            ReadOnlySpan<int> b = [q1, q2, q3, q1, q3, q0];
            b.CopyTo(corners);
        }

        // An empty triangle (its corners in a line) is no triangle: such a quad is fanned instead.
        double empty = 2 * MeshMend.EmptyArea;
        return useA
            ? V3.Cross(p1 - p0, p2 - p0).Length >= empty && V3.Cross(p2 - p0, p3 - p0).Length >= empty
            : V3.Cross(p2 - p1, p3 - p1).Length >= empty && V3.Cross(p3 - p1, p0 - p1).Length >= empty;
    }

    /// <summary>A triangle's unit normal from its corners' cross product, or none for a sliver (which faces nowhere).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static V3 Facing(V3 cross, double sliver)
    {
        double length = cross.Length;
        return length > sliver ? cross / length : default;
    }

    /// <summary>How many runs of <see cref="ChunkSize"/> cover a count.</summary>
    private static int Chunks(int count) => (count + ChunkSize - 1) / ChunkSize;
}
