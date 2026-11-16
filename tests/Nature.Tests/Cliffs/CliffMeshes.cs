using PropGenerator.Nature.Cliffs;
using PropGenerator.Nature.Parameters;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

/// <summary>What the cliff tests measure on a built block or a piece's mesh, and the settings they build.</summary>
internal static class CliffMeshes
{
    /// <summary>A shipped starting point (<see cref="CliffPresets"/>) by its id, and optionally one slider changed.</summary>
    public static CliffSettings Preset(string id, string? key = null, double value = 0)
    {
        var (_, _, values, seed) = CliffPresets.Values.Single(p => p.Id == id);
        var raw = CliffParameterSet.Instance.Defaults.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        foreach (var (name, number) in values)
        {
            raw[name] = number;
        }

        if (key is not null)
        {
            raw[key] = value;
        }

        var result = CliffParameterSet.Instance.Validate(raw);
        Assert.True(result.Valid);
        return CliffSettings.From(result.Parameters, seed);
    }

    /// <summary>The settings with every Surface and Cracks amount at 0 (their sizes as they are): the joined blocks alone.</summary>
    public static CliffSettings Plain(CliffSettings s) => s with
    {
        Chisel = 0,
        EdgeChips = 0,
        EdgeWear = 0,
        Smudge = 0,
        Creases = 0,
        CreaseDepth = 0,
        CreaseStep = 0,
        Ledges = 0,
        LedgeDepth = 0,
        LedgeStep = 0,
        Lines = 0,
        LineDepth = 0,
    };

    /// <summary>
    /// A box block over x, y and z ranges: four sides, then its top and bottom, as <see cref="BlockMaker"/> orders them.
    /// The front (-y) can be turned about the upright through x = 0, y = <paramref name="y0"/>, by degrees (positive
    /// turns it to face +x a little).
    /// </summary>
    public static ConvexBlock Box(double x0, double x1, double y0, double y1, double z0, double z1, double frontTurn = 0)
    {
        double a = frontTurn * Math.PI / 180;
        var front = new V3(Math.Sin(a), -Math.Cos(a), 0);
        return new ConvexBlock(
        [
            new Plane(new V3(1, 0, 0), x1),
            new Plane(new V3(0, 1, 0), y1),
            new Plane(new V3(-1, 0, 0), -x0),
            new Plane(front, V3.Dot(front, new V3(0, y0, 0))),
            new Plane(new V3(0, 0, 1), z1),
            new Plane(new V3(0, 0, -1), -z0),
        ],
        []);
    }

    /// <summary>
    /// A hand-laid block with the shape that says its 4 sides, its ground line (0.3 m above its bottom when it rests on
    /// a plate) and its depth (its bounds' unless given).
    /// </summary>
    public static LaidBlock Laid(ConvexBlock block, int column, int tier, int layer, double? depth = null)
    {
        var (min, max) = block.Bounds();
        double ground = layer == 0 ? 0 : min.Z + 0.3;
        var shape = new BlockShape(
            new V3((min.X + max.X) / 2, (min.Y + max.Y) / 2, ground), max.X - min.X, depth ?? (max.Y - min.Y), max.Z - ground, ground - min.Z,
            4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.6);
        return new LaidBlock(block, shape, column, tier, layer);
    }

    /// <summary>
    /// Settings with every slider drawn evenly across its range (counts whole), a random palette and seed, from a
    /// fixed seed: the same settings every run. These are the settings as first drawn: the sliders added since (see
    /// <see cref="Added"/>) are drawn last, after the seed, so every other slider and the seed keep what they were first
    /// drawn as; the shape sliders of Task 9b (see <see cref="Shaped"/>) stay at their defaults; and a slider whose range
    /// has grown since is drawn over its first range (see <see cref="DrawMax"/>), so the tests built on these settings
    /// (and the golden field) keep them. The batch draws every slider across its whole range (see <see cref="WideRandom"/>).
    /// </summary>
    public static CliffSettings Random(int seed) => Random(seed, false);

    /// <summary>
    /// The random batch's settings by their index: <see cref="WideDraw"/>'s from a seed hashed from the index (see
    /// <see cref="Dealt"/>), so that the batch's settings are independent draws.
    /// </summary>
    public static CliffSettings WideRandom(int index) => Random(Dealt(index), true);

    /// <summary>
    /// <see cref="Random(int)"/>'s settings, but with every slider across its whole range as it is now: a grown range is
    /// drawn whole from the same draw, and the shape sliders of Task 9b are drawn last, after the others. Consecutive
    /// seeds give settings that are shifts of each other (review-final Minor 7), so the batch deals its seeds through
    /// <see cref="Dealt"/>; the tests that name a setting found with consecutive seeds name it by its seed here.
    /// </summary>
    public static CliffSettings WideDraw(int seed) => Random(seed, true);

    /// <summary>
    /// The seed the random batch draws its setting of an index from: the index hashed. System.Random seeded with
    /// consecutive integers steps each draw by a fixed amount from one seed to the next, so the batch's first 200
    /// settings were a lattice, leaving 17% of Layer variety's range undrawn (review-final Minor 7).
    /// </summary>
    public static int Dealt(int index) => (int)(CliffNoise.Hash01(unchecked((uint)index), DealSalt) * int.MaxValue);

    /// <summary>The most a random draw takes for a slider: its top, or with <paramref name="first"/>, its top when the random settings were first drawn (Top tilt's 25°, before Task 9b).</summary>
    public static double DrawMax(NumberSpec n, bool first) => first && FirstMaxes.TryGetValue(n.Key, out double max) ? max : n.Max;

    /// <summary>The parameter set's sliders that random settings draw before their seed: all but <see cref="Added"/> and <see cref="Shaped"/>, in panel order.</summary>
    public static IEnumerable<ParameterSpec> FirstDrawn => CliffParameterSet.Instance.Schema.Where(spec => spec.Key != "joint-crack" && !ShapeKeys.Contains(spec.Key));

    /// <summary>
    /// The sliders added since the random settings were first drawn (Joint crack, Task 8c), which they draw after their
    /// seed, so the random settings drawn before keep every other value and their seed.
    /// </summary>
    public static IEnumerable<ParameterSpec> Added => CliffParameterSet.Instance.Schema.Where(spec => spec.Key == "joint-crack");

    /// <summary>The shape sliders of Task 9b, which only <see cref="WideRandom"/> draws (after every other), in panel order.</summary>
    public static IEnumerable<ParameterSpec> Shaped => CliffParameterSet.Instance.Schema.Where(spec => ShapeKeys.Contains(spec.Key));

    /// <summary>The salt <see cref="Dealt"/> hashes the batch's indices with.</summary>
    private const int DealSalt = 48;

    /// <summary>The keys of the shape sliders of Task 9b.</summary>
    private static readonly string[] ShapeKeys = ["taper", "end-turn", "foot", "underside-tilt"];

    /// <summary>The sliders whose range has grown since the random settings were first drawn, by key, with their first top.</summary>
    private static readonly Dictionary<string, double> FirstMaxes = new(StringComparer.Ordinal) { ["top-tilt"] = 25 };

    private static CliffSettings Random(int seed, bool wide)
    {
        var random = new System.Random(seed);
        var raw = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var spec in FirstDrawn)
        {
            Draw(spec);
        }

        uint cliffSeed = (uint)random.Next();
        foreach (var spec in wide ? Added.Concat(Shaped) : Added)
        {
            Draw(spec);
        }

        var result = CliffParameterSet.Instance.Validate(raw);
        Assert.True(result.Valid);
        return CliffSettings.From(result.Parameters, cliffSeed);

        void Draw(ParameterSpec spec)
        {
            if (spec is NumberSpec n)
            {
                double value = n.Min + (random.NextDouble() * (DrawMax(n, !wide) - n.Min));
                raw[n.Key] = n.Whole ? Math.Floor(value + 0.5) : value;
            }
            else if (spec is ChoiceSpec c)
            {
                raw[c.Key] = c.Options[random.Next(c.Options.Count)].Value;
            }
        }
    }

    /// <summary>A block's shell cut at a height: the corners of its section there, in no order (none where it does not reach the height).</summary>
    public static List<V3> Section(ConvexBlock block, double z) =>
        block.CornersWithin([new Plane(new V3(0, 0, 1), z + 1e-7), new Plane(new V3(0, 0, -1), -z + 1e-7)]);

    /// <summary>How far a block's section at a height spans along a direction (its extent that way plus the other way); 0 where it does not reach the height.</summary>
    public static double SectionWidth(ConvexBlock block, double z, V3 along)
    {
        var section = Section(block, z);
        return section.Count == 0 ? 0 : section.Max(c => V3.Dot(along, c)) - section.Min(c => V3.Dot(along, c));
    }

    /// <summary>How far a block's section at a height reaches along a direction (its extent that way); -∞ where it does not reach the height.</summary>
    public static double SectionReach(ConvexBlock block, double z, V3 along) =>
        Section(block, z).Select(c => V3.Dot(along, c)).DefaultIfEmpty(double.NegativeInfinity).Max();

    /// <summary>
    /// How deep a cut goes into its block's shell along its first plane's normal (from the shell's farthest corner that
    /// way to the deepest point the cut takes), and its limit: 40% of the shell's thickness that way.
    /// </summary>
    public static (double Depth, double Limit) CutDepth(ConvexBlock block, IReadOnlyList<Plane> cut)
    {
        var n = cut[0].Normal;
        double hi = block.Corners().Max(c => V3.Dot(n, c));

        // What the cut takes: inside the shell and in front of every cut plane.
        var taken = new ConvexBlock([.. block.Shell, .. cut.Select(p => new Plane(-p.Normal, -p.Offset))], []);
        double depth = taken.Corners().Count == 0 ? 0 : hi - taken.Corners().Min(c => V3.Dot(n, c));
        return (depth, 0.4 * block.WidthAlong(n));
    }

    /// <summary>The signed volume of each separate shell of a mesh (inside-out shells, pockets sealed in a solid, are negative).</summary>
    public static List<double> ShellVolumes(SolidMesh mesh)
    {
        int[] parent = [.. Enumerable.Range(0, mesh.Points.Count)];
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            parent[Root(mesh.Indices[t + 1])] = Root(mesh.Indices[t]);
            parent[Root(mesh.Indices[t + 2])] = Root(mesh.Indices[t]);
        }

        var volume = new Dictionary<int, double>();
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            var a = mesh.Points[mesh.Indices[t]];
            var b = mesh.Points[mesh.Indices[t + 1]];
            var c = mesh.Points[mesh.Indices[t + 2]];
            int shell = Root(mesh.Indices[t]);
            volume[shell] = volume.GetValueOrDefault(shell) + (V3.Dot(a, V3.Cross(b, c)) / 6);
        }

        return [.. volume.Values];

        int Root(int i)
        {
            while (parent[i] != i)
            {
                i = parent[i] = parent[parent[i]];
            }

            return i;
        }
    }

    /// <summary>
    /// How far a point is outside every collision box (the least, over the boxes, of how far it is past the box's
    /// furthest side): 0 or less when inside one.
    /// </summary>
    public static double Outside(IReadOnlyList<CliffChunk> chunks, V3 p) =>
        chunks.Min(c => Math.Max(Math.Max(Math.Abs(p.X - c.Base.X) - c.HalfX, Math.Abs(p.Y - c.Base.Y) - c.HalfY), Math.Max(c.Base.Z - p.Z, p.Z - c.Top.Z)));

    /// <summary>The block's vertex positions.</summary>
    public static V3[] Points(CliffBlock block) =>
        [.. Enumerable.Range(0, block.Positions.Length / 3)
            .Select(i => new V3(block.Positions[3 * i], block.Positions[(3 * i) + 1], block.Positions[(3 * i) + 2]))];

    /// <summary>
    /// Why the block is not a closed, consistently wound surface once its positions are welded at 1e-6 (shading
    /// splits vertices along sharp edges, and positions are floats), or null when it is: no triangle may weld into a
    /// line or a point, every edge must belong to exactly two triangles that run it opposite ways, and the triangles
    /// around each point must make one fan (no two sheets touching at a point).
    /// </summary>
    public static string? Open(CliffBlock block) => Open(Points(block), block.Indices);

    /// <summary>The same check on a piece's mesh.</summary>
    public static string? Open(SolidMesh mesh) => Open([.. mesh.Points], [.. mesh.Indices]);

    /// <summary>How many separate pieces of surface the block has once welded.</summary>
    public static int Shells(CliffBlock block)
    {
        var points = Points(block);
        int[] weld = Weld(points, 1e-6);
        int[] parent = [.. Enumerable.Range(0, points.Length)];
        for (int t = 0; t < block.Indices.Length; t += 3)
        {
            int a = Root(weld[block.Indices[t]]);
            parent[Root(weld[block.Indices[t + 1]])] = a;
            parent[Root(weld[block.Indices[t + 2]])] = a;
        }

        return block.Indices.Select(i => Root(weld[i])).Distinct().Count();

        int Root(int i)
        {
            while (parent[i] != i)
            {
                i = parent[i] = parent[parent[i]];
            }

            return i;
        }
    }

    /// <summary>The area of the block's triangles whose three corners all pass <paramref name="on"/>.</summary>
    public static double FlatArea(CliffBlock block, Func<V3, bool> on)
    {
        var points = Points(block);
        double area = 0;
        for (int t = 0; t < block.Indices.Length; t += 3)
        {
            if (on(points[block.Indices[t]]) && on(points[block.Indices[t + 1]]) && on(points[block.Indices[t + 2]]))
            {
                area += Face(points, block.Indices, t).Area;
            }
        }

        return area;
    }

    /// <summary>
    /// The block's front outline: its triangles projected onto x–z (seen from the front, along +y), filled into a
    /// square grid of <paramref name="size"/> cells a side that spans the mesh's own x and z bounds. A cell is set when
    /// its centre lies in any triangle's projection.
    /// </summary>
    public static bool[,] FrontOutline(CliffBlock b, int size = 128)
    {
        var points = Points(b);
        double minX = points.Min(p => p.X);
        double maxX = points.Max(p => p.X);
        double minZ = points.Min(p => p.Z);
        double maxZ = points.Max(p => p.Z);
        double sx = size / Math.Max(maxX - minX, 1e-9);
        double sz = size / Math.Max(maxZ - minZ, 1e-9);
        bool[,] outline = new bool[size, size];
        for (int t = 0; t < b.Indices.Length; t += 3)
        {
            var (ax, az) = Cell(points[b.Indices[t]]);
            var (bx, bz) = Cell(points[b.Indices[t + 1]]);
            var (cx, cz) = Cell(points[b.Indices[t + 2]]);
            double area = ((bx - ax) * (cz - az)) - ((cx - ax) * (bz - az));
            if (Math.Abs(area) < 1e-12)
            {
                continue;
            }

            int i0 = Math.Max((int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))), 0);
            int i1 = Math.Min((int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))), size - 1);
            int j0 = Math.Max((int)Math.Floor(Math.Min(az, Math.Min(bz, cz))), 0);
            int j1 = Math.Min((int)Math.Ceiling(Math.Max(az, Math.Max(bz, cz))), size - 1);
            for (int i = i0; i <= i1; i++)
            {
                for (int j = j0; j <= j1; j++)
                {
                    double x = i + 0.5;
                    double z = j + 0.5;
                    double u = (((bx - x) * (cz - z)) - ((cx - x) * (bz - z))) / area;
                    double v = (((cx - x) * (az - z)) - ((ax - x) * (cz - z))) / area;
                    if (u >= 0 && v >= 0 && u + v <= 1)
                    {
                        outline[i, j] = true;
                    }
                }
            }
        }

        return outline;

        (double X, double Z) Cell(V3 p) => ((p.X - minX) * sx, (p.Z - minZ) * sz);
    }

    /// <summary>How much two outlines of the same size overlap: the cells set in both over the cells set in either.</summary>
    public static double Iou(bool[,] a, bool[,] b)
    {
        int both = 0;
        int either = 0;
        for (int i = 0; i < a.GetLength(0); i++)
        {
            for (int j = 0; j < a.GetLength(1); j++)
            {
                both += a[i, j] && b[i, j] ? 1 : 0;
                either += a[i, j] || b[i, j] ? 1 : 0;
            }
        }

        return either == 0 ? 1 : (double)both / either;
    }

    /// <summary>Each component's absolute value.</summary>
    public static V3 Abs(V3 v) => new(Math.Abs(v.X), Math.Abs(v.Y), Math.Abs(v.Z));

    /// <summary>
    /// For each point, the index of the point it welds to: an earlier point closer than <paramref name="distance"/>
    /// that welds to itself, or itself.
    /// </summary>
    public static int[] Weld(IReadOnlyList<V3> points, double distance)
    {
        // Points go in cubes twice the weld distance across, so a point closer than it is in the same cube or, along
        // each axis, in the neighbour on the side of the cube's middle the point is on: 8 cubes to look in.
        int[] result = new int[points.Count];
        var firstInCell = new Dictionary<(long, long, long), int>();
        int[] nextInCell = new int[points.Count];
        double size = 2 * distance;
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var (x, nearX) = Cell(p.X);
            var (y, nearY) = Cell(p.Y);
            var (z, nearZ) = Cell(p.Z);
            result[i] = i;
            for (int corner = 0; corner < 8 && result[i] == i; corner++)
            {
                var key = ((corner & 1) == 0 ? x : nearX, (corner & 2) == 0 ? y : nearY, (corner & 4) == 0 ? z : nearZ);
                for (int other = firstInCell.GetValueOrDefault(key, -1); other >= 0; other = nextInCell[other])
                {
                    var d = points[other] - p;
                    if (V3.Dot(d, d) < distance * distance)
                    {
                        result[i] = other;
                        break;
                    }
                }
            }

            if (result[i] == i)
            {
                nextInCell[i] = firstInCell.GetValueOrDefault((x, y, z), -1);
                firstInCell[(x, y, z)] = i;
            }
        }

        return result;

        (long Cell, long Near) Cell(double coordinate)
        {
            double scaled = coordinate / size;
            long cell = (long)Math.Floor(scaled);
            return (cell, scaled - cell < 0.5 ? cell - 1 : cell + 1);
        }
    }

    /// <summary>A triangle's unit normal and area.</summary>
    public static (V3 Normal, double Area) Face(IReadOnlyList<V3> points, IReadOnlyList<int> indices, int t)
    {
        var a = points[indices[t]];
        var cross = V3.Cross(points[indices[t + 1]] - a, points[indices[t + 2]] - a);
        return (cross.Normalized(), cross.Length / 2);
    }

    /// <summary>The volume a closed mesh encloses: positive when it winds counter-clockwise seen from outside.</summary>
    public static double Volume(SolidMesh mesh)
    {
        double sum = 0;
        var origin = mesh.Points.Count > 0 ? mesh.Points[0] : default;
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            var a = mesh.Points[mesh.Indices[t]] - origin;
            var b = mesh.Points[mesh.Indices[t + 1]] - origin;
            var c = mesh.Points[mesh.Indices[t + 2]] - origin;
            sum += V3.Dot(a, V3.Cross(b, c));
        }

        return sum / 6;
    }

    /// <summary>
    /// The width of each fold of a mesh (an edge whose two triangles bend more than the angle): how far apart the two
    /// triangles' far corners are (the points of each not on the shared edge), so a thin fin, its two faces back to back,
    /// is as wide as its tip is thick.
    /// </summary>
    public static List<double> FoldWidths(SolidMesh mesh, double degrees)
    {
        var faces = new Dictionary<(int, int), int>();
        double cos = Math.Cos(degrees * Math.PI / 180);
        var widths = new List<double>();
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = mesh.Indices[t + k];
                int b = mesh.Indices[t + ((k + 1) % 3)];
                var edge = a < b ? (a, b) : (b, a);
                if (faces.Remove(edge, out int other))
                {
                    if (V3.Dot(Face(mesh.Points, mesh.Indices, t).Normal, Face(mesh.Points, mesh.Indices, other).Normal) < cos)
                    {
                        int c = mesh.Indices[t + ((k + 2) % 3)];
                        int d = Enumerable.Range(0, 3).Select(q => mesh.Indices[other + q]).Single(v => v != a && v != b);
                        widths.Add((mesh.Points[c] - mesh.Points[d]).Length);
                    }
                }
                else
                {
                    faces[edge] = t;
                }
            }
        }

        return widths;
    }

    /// <summary>The share of a mesh's edges between two triangles whose faces bend more than the angle (folds).</summary>
    public static double Folds(SolidMesh mesh, double degrees)
    {
        var faces = new Dictionary<(int, int), int>();
        double cos = Math.Cos(degrees * Math.PI / 180);
        int folds = 0;
        int edges = 0;
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = mesh.Indices[t + k];
                int b = mesh.Indices[t + ((k + 1) % 3)];
                var edge = a < b ? (a, b) : (b, a);
                if (faces.Remove(edge, out int other))
                {
                    edges++;
                    if (V3.Dot(Face(mesh.Points, mesh.Indices, t).Normal, Face(mesh.Points, mesh.Indices, other).Normal) < cos)
                    {
                        folds++;
                    }
                }
                else
                {
                    faces[edge] = t;
                }
            }
        }

        return edges == 0 ? 0 : (double)folds / edges;
    }

    private static string? Open(V3[] points, int[] indices)
    {
        if (indices.Length == 0 || indices.Length % 3 != 0)
        {
            return $"{indices.Length} indices";
        }

        int[] weld = Weld(points, 1e-6);
        var uses = new Dictionary<(int, int), (int Forward, int Backward)>();
        for (int t = 0; t < indices.Length; t += 3)
        {
            int a = weld[indices[t]];
            int b = weld[indices[t + 1]];
            int c = weld[indices[t + 2]];
            if (a == b || b == c || c == a)
            {
                return $"triangle {t / 3} welds into a line at {points[a]}";
            }

            foreach (var (from, to) in new[] { (a, b), (b, c), (c, a) })
            {
                var edge = from < to ? (from, to) : (to, from);
                var (forward, backward) = uses.GetValueOrDefault(edge);
                uses[edge] = from < to ? (forward + 1, backward) : (forward, backward + 1);
            }
        }

        foreach (var ((a, b), (forward, backward)) in uses)
        {
            if (forward != 1 || backward != 1)
            {
                return $"edge {points[a]} – {points[b]} is used {forward} times one way and {backward} the other";
            }
        }

        // No pinch either: around each point its triangles make one fan, the far edges of its triangles one loop.
        var around = new Dictionary<int, Dictionary<int, int>>();
        for (int t = 0; t < indices.Length; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int v = weld[indices[t + k]];
                int a = weld[indices[t + ((k + 1) % 3)]];
                int b = weld[indices[t + ((k + 2) % 3)]];
                if (!around.TryGetValue(v, out var next))
                {
                    around[v] = next = [];
                }

                next[a] = b;
            }
        }

        foreach (var (v, next) in around)
        {
            int start = next.Keys.First();
            int steps = 0;
            for (int at = next[start]; at != start && steps <= next.Count; at = next[at])
            {
                steps++;
            }

            if (steps + 1 != next.Count)
            {
                return $"the triangles around {points[v]} make more than one fan";
            }
        }

        return null;
    }
}
