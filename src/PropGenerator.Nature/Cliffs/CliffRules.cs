using System.Numerics;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// The backstop behind the layout's own limits: a cliff whose blocks still break a rule is laid out again from the next
/// sub-seed, so the user never has to skip past a bad seed. The rules: every block is at least Min thickness across its
/// own plane normals, and a probe mesh of the whole cliff has no open edge and no more shells above
/// <see cref="ShellShare"/> of its volume than the layout has connected groups of blocks (see <see cref="Groups"/>). The
/// probe's grid is <see cref="ProbeCells"/> times the start cell a full level would have at <see cref="ProbeDetail"/>
/// points per metre: it depends on the cliff's size alone, not on the level being built or on the Detail slider, so
/// every level, at any Detail, picks the same sub-seed. At most <see cref="Attempts"/> sub-seeds are tried, in order; past
/// them the cliff is laid out from its own seed with no cuts, and is not checked again (its blocks and joints alone hold).
/// </summary>
internal static class CliffRules
{
    /// <summary>How many sub-seeds are tried before the fallback.</summary>
    internal const int Attempts = 8;

    /// <summary>A probe shell with more than this share of the probe's volume counts as a piece of the cliff.</summary>
    private const double ShellShare = 0.01;

    /// <summary>How many times the full level's start cell (at <see cref="ProbeDetail"/>) the probe's grid is.</summary>
    private const double ProbeCells = 2;

    /// <summary>The points per metre the probe's cell is taken from, whatever Detail says: the Detail slider's default.</summary>
    private const double ProbeDetail = 8;

    /// <summary>Two blocks whose shells come this close, in metres, touch: they are one connected group.</summary>
    private const double TouchGap = 1e-3;

    /// <summary>A block may be this much under Min thickness, in metres, for rounding.</summary>
    private const double ThickTolerance = 1e-9;

    /// <summary>The salt of the sub-seeds' draws from the cliff's seed.</summary>
    private const int SubSeedSalt = 7000;

    /// <summary>
    /// A test hook: an extra rule that rejects an attempt by its number (0 to <see cref="Attempts"/> − 1) before it is
    /// laid out. Null, as it always is outside the tests, rejects nothing.
    /// </summary>
    internal static Func<int, bool>? Reject { get; set; }

    /// <summary>
    /// The sub-seed the cliff is laid out from: the first of <see cref="SubSeed"/> for attempts 0 to
    /// <see cref="Attempts"/> − 1 whose layout holds the rules; the cliff's own seed when none does (the fallback then lays
    /// it out with no cuts; see <see cref="Choose"/>).
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <returns>The sub-seed; the same settings always give the same one.</returns>
    public static uint Pick(CliffSettings s) => Choose(s).Seed;

    /// <summary>
    /// The attempt that holds the rules (see <see cref="Pick"/>), with its sub-seed and its layout, or attempt
    /// <see cref="Attempts"/>, the fallback: the cliff's own seed with every cut count and Cut depth at 0. The attempts
    /// stop at the first that holds; each probe mesh is let go before the next attempt is laid out.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <returns>The attempt's number, its seed and its layout.</returns>
    internal static (int Attempt, uint Seed, IReadOnlyList<LaidBlock> Layout) Choose(CliffSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        double thin = CliffLayout.MinThickness(s);
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (Reject?.Invoke(attempt) == true)
            {
                continue;
            }

            uint seed = SubSeed(s.Seed, attempt);
            var layout = CliffLayout.Make(s, seed);

            // The thickness is read off the layout, so a thin block costs no mesh.
            if (Thick(layout, thin) && Sound(Probe(s, layout), Groups(layout)))
            {
                return (attempt, seed, layout);
            }
        }

        return (Attempts, s.Seed, CliffLayout.Make(Uncut(s), s.Seed));
    }

    /// <summary>The sub-seed of an attempt: a hash of the cliff's seed and the attempt's number.</summary>
    /// <param name="seed">The cliff's seed.</param>
    /// <param name="attempt">The attempt's number, from 0.</param>
    internal static uint SubSeed(uint seed, int attempt) => (uint)(CliffNoise.Hash01(seed, SubSeedSalt + attempt) * 4294967296.0);

    /// <summary>The settings the fallback lays the cliff out with: no steps, bites or grooves, and Cut depth 0, so the end bites turn to bevels.</summary>
    /// <param name="s">The settings.</param>
    internal static CliffSettings Uncut(CliffSettings s) => s with { Steps = 0, Bites = 0, Grooves = 0, CutDepth = 0 };

    /// <summary>
    /// Whether a layout and its probe mesh hold the rules: every block's <see cref="ConvexBlock.MinWidth"/> is at least
    /// Min thickness, and the probe has no open edge and no more shells above <see cref="ShellShare"/> of its volume than
    /// the layout has connected groups of blocks.
    /// </summary>
    /// <param name="layout">The laid blocks.</param>
    /// <param name="probe">The cliff's probe mesh, built from the layout.</param>
    /// <param name="minThickness">The least thickness any block may have, in metres.</param>
    public static bool Holds(IReadOnlyList<LaidBlock> layout, SolidMesh probe, double minThickness)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(probe);
        return Thick(layout, minThickness) && Sound(probe, Groups(layout));
    }

    /// <summary>
    /// How many connected groups the layout's blocks make: two blocks are linked when their shells overlap or touch
    /// (come within <see cref="TouchGap"/>), and a group is every block linked to another of it, directly or through others.
    /// </summary>
    /// <param name="layout">The laid blocks.</param>
    internal static int Groups(IReadOnlyList<LaidBlock> layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        int[] root = new int[layout.Count];
        for (int i = 0; i < root.Length; i++)
        {
            root[i] = i;
        }

        var edges = layout.Select(b => b.Block.EdgeDirections()).ToArray();
        for (int i = 0; i < layout.Count; i++)
        {
            for (int j = i + 1; j < layout.Count; j++)
            {
                if (Find(root, i) != Find(root, j) && layout[i].Block.Meets(layout[j].Block, TouchGap, edges[i], edges[j]))
                {
                    Join(root, i, j);
                }
            }
        }

        int groups = 0;
        for (int i = 0; i < root.Length; i++)
        {
            groups += Find(root, i) == i ? 1 : 0;
        }

        return groups;
    }

    /// <summary>Whether every block is at least Min thickness across its own plane normals.</summary>
    private static bool Thick(IReadOnlyList<LaidBlock> layout, double minThickness) =>
        layout.All(b => b.Block.MinWidth() >= minThickness - ThickTolerance);

    /// <summary>
    /// The cliff's probe mesh, at <see cref="ProbeCells"/> times the start cell of a full level at
    /// <see cref="ProbeDetail"/> points per metre; at that start cell itself when the coarser grid catches no rock.
    /// </summary>
    private static SolidMesh Probe(CliffSettings s, IReadOnlyList<LaidBlock> layout)
    {
        var probe = s with { Detail = ProbeDetail };
        double start = CliffGenerator.StartCell(probe, 1, layout);
        var mesh = CliffGenerator.Mesh(probe, layout, ProbeCells * start);
        return mesh.TriangleCount > 0 ? mesh : CliffGenerator.Mesh(probe, layout, start);
    }

    /// <summary>
    /// Whether a mesh has rock, no more shells above <see cref="ShellShare"/> of its volume than the layout's groups, and
    /// every edge in two triangles that run it opposite ways.
    /// </summary>
    private static bool Sound(SolidMesh mesh, int groups) => mesh.TriangleCount > 0 && Shells(mesh) <= groups && Closed(mesh);

    /// <summary>How many of the mesh's shells enclose more than <see cref="ShellShare"/> of its volume (inside-out shells enclose less than none).</summary>
    private static int Shells(SolidMesh mesh)
    {
        var points = mesh.Points;
        var indices = mesh.Indices;
        int[] root = new int[points.Count];
        for (int i = 0; i < root.Length; i++)
        {
            root[i] = i;
        }

        for (int t = 0; t < indices.Count; t += 3)
        {
            Join(root, indices[t], indices[t + 1]);
            Join(root, indices[t], indices[t + 2]);
        }

        var origin = points[indices[0]];
        double[] volume = new double[points.Count];
        double total = 0;
        for (int t = 0; t < indices.Count; t += 3)
        {
            var a = points[indices[t]] - origin;
            var b = points[indices[t + 1]] - origin;
            var c = points[indices[t + 2]] - origin;
            double v = V3.Dot(a, V3.Cross(b, c)) / 6;
            volume[Find(root, indices[t])] += v;
            total += v;
        }

        return volume.Count(v => v > ShellShare * total);
    }

    /// <summary>Whether every edge of the mesh is in exactly two triangles, which run it opposite ways (no open edge, no fin).</summary>
    private static bool Closed(SolidMesh mesh)
    {
        // Each edge one way, by its two point indices, in a table of twice the edges, found by the edge's key: a closed
        // surface runs each of them exactly once, and each of them exactly once the other way.
        var indices = mesh.Indices;
        int corners = indices.Count;
        int size = (int)Math.Max(16, BitOperations.RoundUpToPowerOf2((uint)(2 * corners)));
        long[] keys = new long[size];
        int[] uses = new int[size];
        Array.Fill(keys, -1);
        long points = mesh.Points.Count;
        for (int c = 0; c < corners; c++)
        {
            int from = indices[c];
            int to = indices[c % 3 == 2 ? c - 2 : c + 1];
            if (from == to)
            {
                return false;
            }

            long key = (Math.Min(from, to) * points) + Math.Max(from, to);
            int slot = (int)((ulong)(key * unchecked((long)0x9E3779B97F4A7C15)) >> 32) & (size - 1);
            while (keys[slot] != -1 && keys[slot] != key)
            {
                slot = (slot + 1) & (size - 1);
            }

            keys[slot] = key;

            // The low half counts the runs from the lower index, the high half the runs the other way.
            uses[slot] += from < to ? 1 : 1 << 16;
        }

        for (int slot = 0; slot < size; slot++)
        {
            if (keys[slot] != -1 && uses[slot] != 1 + (1 << 16))
            {
                return false;
            }
        }

        return true;
    }

    private static int Find(int[] root, int i)
    {
        while (root[i] != i)
        {
            i = root[i] = root[root[i]];
        }

        return i;
    }

    private static void Join(int[] root, int a, int b)
    {
        int ra = Find(root, a);
        int rb = Find(root, b);
        if (ra != rb)
        {
            root[rb] = ra;
        }
    }
}
