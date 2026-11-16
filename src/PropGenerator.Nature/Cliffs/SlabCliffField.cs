using System.Runtime.CompilerServices;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// The cliff's field: its laid blocks joined as the design's joints say, with the chisel's facets cut in after. Blocks
/// of one tier and layer join hard, as the hand-made pieces' blocks do, with a narrow V-crack (Joint crack wide) along
/// the line where their faces meet, and none where they run on flush, so a face two neighbours share stays flat (see
/// <see cref="Crack"/>). Plates and tiers are joined hard too, and show a bedding groove: a V
/// <see cref="GrooveDepth"/> deep, centred on the top plane of the block a block rests on, where the joint meets the
/// surface. The groove is made by pulling both blocks' sides in by the V's depth near that plane, and a filler (the
/// rock both blocks' sides hold, near the plane, pulled in the same way) keeps the rock behind the groove solid, so the
/// field is a fair distance across the joint and the chisel seals no pocket there. Where the lower block's rim is
/// chamfered or bevelled, the upper block's base is cut back to match, so the lower block keeps its own rim. Over the
/// joined blocks the surface detail is pushed in (see <see cref="Detail"/>): the chisel's facets and the
/// <see cref="SlabSurface"/>'s smudge, creases, ledges and lines over all of it, and each level's own chips and wear
/// (those of its block that wins at each point, faded out near a neighbour of its level, so neither runs along a
/// join) cut into that level's rock before the levels are joined, so every plate keeps its own rim.
/// </summary>
internal sealed partial class SlabCliffField : ICliffPart
{
    /// <summary>The joint reach is this share of the smallest block's depth, held to <see cref="LeastBlend"/> to <see cref="MostBlend"/>.</summary>
    private const double BlendShare = 0.15;

    /// <summary>The least joint reach, in metres.</summary>
    private const double LeastBlend = 0.2;

    /// <summary>The most joint reach, in metres.</summary>
    private const double MostBlend = 0.6;

    /// <summary>How deep a bedding groove cuts in, in metres.</summary>
    private const double GrooveDepth = 0.1;

    /// <summary>
    /// The groove's half width over the floor (the narrowest groove the mesh can show): wider than half, so even a
    /// centimetre into the V it is still more than the floor wide.
    /// </summary>
    private const double FloorShare = 0.6;

    /// <summary>
    /// The joint filler reaches this far from the plane past the groove and the chisel's and the creases' steps' depth, in
    /// metres (see <see cref="_reach"/>).
    /// </summary>
    private const double FillMargin = 0.05;

    /// <summary>How many of a block's shell planes make its cheap bound: those facing furthest along ±x, ±y and ±z.</summary>
    internal const int BoundPlanes = 6;

    /// <summary>How many of a block's side planes make the cheap bound of its sides: those facing furthest along ±x and ±y.</summary>
    internal const int SideBoundPlanes = 4;

    /// <summary>
    /// How far behind the nearest face (or block) another still counts toward how far the surface faces sideways, in
    /// metres: across this, the ledges and lines turn from one face's share to the next.
    /// </summary>
    private const double SideReach = 0.05;

    /// <summary>
    /// Chips stay this far from a cut's inner crease, in metres of its planes' values (twice the distance from the
    /// crease they keep clear: a point that near it has both planes and its own block's field within that of 0).
    /// </summary>
    private const double CreaseKeep = 0.2;

    /// <summary>Past <see cref="CreaseKeep"/>, the chips come back in over this much more, in metres.</summary>
    private const double CreaseRamp = 0.1;

    /// <summary>Past where an edge ends (another plane cuts it off), its chips run out over this far, in metres.</summary>
    private const double EdgeEnd = 0.1;

    /// <summary>Inside the rock, the joint's smooth min grows from none at the surface to the whole joint reach over this share of it in depth.</summary>
    private const double InsideShare = 0.25;

    /// <summary>Two faces meeting at a joint within 3° of each other (cos 3°) run on flush: no crack opens between them.</summary>
    private const double FlushCos = 0.99863;

    /// <summary>Two faces meeting at a joint 15° apart (cos 15°) or more open the whole crack; between 3° and 15°, a share of it.</summary>
    private const double CrackCos = 0.96593;

    /// <summary>
    /// The joint's crack is this many times its half width deep (<see cref="SlabSurface.DepthShare"/>, as every groove
    /// is), so its walls never stand within about 67° of each other at the bottom.
    /// </summary>
    private const double CrackDepthShare = SlabSurface.DepthShare;

    /// <summary>
    /// The crack fades out this many times its depth into the rock (see <see cref="Crack"/>); so that on its seam it
    /// still cuts its whole depth, it pulls in by up to <see cref="CrackMost"/>.
    /// </summary>
    private const double CrackFadeShare = 3;

    /// <summary>
    /// A crack comes in as each block's face there wins over the block's next term by up to its half width, but never
    /// less than this, in metres (see <see cref="Crack"/>).
    /// </summary>
    private const double CrackSoftLeast = 0.02;

    /// <summary>A crack this many grid cells wide or more is cut whole; under it, less, and none at one cell.</summary>
    private const double CrackCells = 2;

    /// <summary>
    /// Below this 1 − cos² of their angle, two faces turned nearly opposite are taken as a gap to bridge, with no line
    /// to meet on (the check that both faces go on past it fades out toward it).
    /// </summary>
    private const double ParallelAcross = 0.05;

    /// <summary>A plane counts toward a block's soft normal while its value is within this share of the blend radius of the field's.</summary>
    private const double SoftShare = 0.5;

    /// <summary>
    /// Inside a rim cut, its field counts this many times its depth: the field stays continuous across the cut's face
    /// (so no fold forms there), but deeper in the rock it never makes the block shallower than this share of it.
    /// </summary>
    private const double RimInside = 4;

    /// <summary>The share of the height above the ground line a ledge's step may set the sides back by, at most.</summary>
    private const double StepHold = 0.25;

    /// <summary>A rim plane that reaches less than this share of the floor (one and a half cells), two cells, into its block cuts nothing above it.</summary>
    private const double RimShownShare = 4.0 / 3;

    /// <summary>Past <see cref="RimShownShare"/>, a rim plane's cut comes in over this share of the floor: one more cell.</summary>
    private const double RimRampShare = 2.0 / 3;

    /// <summary>
    /// How far from a lower block's top plane its rim cut is read, in metres: past the deepest rim chamfer (about 0.6 m
    /// tall), with room for an overhang of about 0.4 m, under which the cut rises at 45°.
    /// </summary>
    private const double RimReach = 1;

    /// <summary>
    /// Behind the nearest block of a level by less than this share of the blend radius, the next nearest's chips and
    /// wear are blended in (see <see cref="Handover"/>).
    /// </summary>
    private const double HandoverShare = 0.5;

    /// <summary>The most numbers a span read per point takes on the stack; a longer one goes on the heap.</summary>
    private const int StackMost = 1024;

    /// <summary>
    /// A margin against rounding, in metres, where the union proves a block or filler changes nothing (and the cull grid
    /// grows its cells' boxes by, and lowers its bounds' least by).
    /// </summary>
    internal const double Hair = 1e-6;

    /// <summary>With more blocks and joints than this, the field reads them through a cull grid (see <see cref="SlabCliffCull"/>).</summary>
    private const int CullLeast = 12;

    /// <summary>
    /// The most the surface detail may cut into a block, as a share of the block's thinnest width across its own planes
    /// (a plate's thickness): cut in from both sides it still leaves half the block whole, so no detail cuts through.
    /// </summary>
    private const double DetailShare = 0.25;

    private readonly ConvexBlock[] _blocks;
    private readonly Plane[][] _shells;

    /// <summary>The most terms (shell planes and cuts) any block has: the size of the spans that read them.</summary>
    private readonly int _terms;
    private readonly Plane[] _bounds;
    private readonly Plane[] _sideBounds;
    private readonly Plane[][] _sides;

    /// <summary>How many side planes each block's shell starts with (its <see cref="_sides"/>).</summary>
    private readonly int[] _faces;

    /// <summary>Whether a ledge sets the blocks' sides back (see <see cref="Shift"/>).</summary>
    private readonly bool _stepped;
    private readonly Plane[] _tops;
    private readonly Plane[][] _belowTops;
    private readonly Plane[][][] _cuts;
    private readonly Plane[][] _rims;

    /// <summary>
    /// How much of each rim plane's cut a block's base takes (see <see cref="RimCut"/>), 0 to 1, by how far the plane
    /// reaches into its own block's plain shell: none under two grid cells, all from three.
    /// </summary>
    private readonly double[][] _rimShares;

    /// <summary>How far each rim plane reaches into its own block's plain shell, in metres (its chamfer's depth).</summary>
    private readonly double[][] _rimDepths;
    private readonly int[][] _beds;

    /// <summary>
    /// Each block's share of the surface detail, 0 to 1: 1 unless the detail's most (<see cref="_inward"/>) would cut it
    /// in further than <see cref="DetailShare"/> of its thinnest width; then all of its detail is scaled down together.
    /// </summary>
    private readonly double[] _scale;
    private readonly int[] _level;

    /// <summary>Each level's blocks, in index order.</summary>
    private readonly int[][] _levelBlocks;
    private readonly int[] _bed;
    private readonly int[] _rider;
    private readonly Joint[] _joints;
    private readonly int _levels;
    private readonly CliffChisel _chisel;
    private readonly SlabSurface _surface;

    /// <summary>The cull grid, made on the first read of the field (none with few blocks and joints).</summary>
    private readonly Lazy<SlabCliffCull?> _cull;

    /// <summary>Whether any of the surface detail is on: when none is, the field is the joined blocks alone.</summary>
    private readonly bool _detail;

    /// <summary>
    /// The most the surface detail cuts in anywhere, before the ground line's hold and the blocks' scales, in metres: the
    /// chisel's depth, the chips' and wear's most, the creases', ledges' and lines' depth, and the smudge's reach (it
    /// pushes in as far as out).
    /// </summary>
    private readonly double _inward;

    /// <summary>
    /// A point at least this far inside the joined blocks stays inside whatever the detail does, in metres: there the
    /// winning block need not be found exactly (see <see cref="Union"/>).
    /// </summary>
    private readonly double _deep;

    /// <summary>The most the chips and wear together cut in anywhere, in metres.</summary>
    private readonly double _edgeMost;

    /// <summary>
    /// The cliff's flat bottom, in metres: the lowest of the ground level's blocks (its first tier and layer), Sink under
    /// the ground line for every layout <see cref="CliffLayout"/> makes. The field is held above it, so no block resting
    /// on others ever pokes out under the cliff (review-final I2: the lowest block's bottom let a block sunk through the
    /// ones under it out of the underside).
    /// </summary>
    private readonly double _bottom;

    /// <summary>The groove's half width at the face, in metres.</summary>
    private readonly double _half;

    /// <summary>The joint crack's half width at the surface, in metres (half of Joint crack).</summary>
    private readonly double _crackHalf;

    /// <summary>
    /// How deep the joint crack cuts on its seam, in metres: <see cref="CrackDepthShare"/> of its half width, held to
    /// none where it is under <see cref="CrackCells"/> grid cells wide (0 when there is no crack).
    /// </summary>
    private readonly double _crackDepth;

    /// <summary>
    /// How far from a joint's plane its filler reaches, in metres: past the groove's half width and one and a half times
    /// the chisel's depth and the creases' steps (a hard union is shallower than that near the plane: the block above is
    /// cut back at 45° there, so a crease's step pushed in deeper than the filler opened the joint's inside as a thin slot,
    /// random 56). Not the rest of the detail: the filler holds both blocks' sides, so within its reach it fills a crack
    /// between neighbours of a level, and a reach of the whole detail (metres) closed the cracks all along every joint.
    /// </summary>
    private readonly double _reach;

    /// <summary>Joins the laid blocks.</summary>
    /// <param name="blocks">The laid blocks (see <see cref="CliffLayout.Make"/>); their shells list their sides first, then the top, as <see cref="BlockMaker"/> makes them.</param>
    /// <param name="s">The settings (for the chisel).</param>
    /// <param name="floor">The narrowest groove the mesh can show, in metres: the bedding groove is made wider than this.</param>
    public SlabCliffField(IReadOnlyList<LaidBlock> blocks, CliffSettings s, double floor)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(s);
        if (blocks.Count == 0)
        {
            throw new ArgumentException("A cliff field needs at least one block.", nameof(blocks));
        }

        int n = blocks.Count;
        _blocks = [.. blocks.Select(b => b.Block)];
        _shells = [.. _blocks.Select(b => b.Shell.ToArray())];
        _terms = _blocks.Max(b => b.Shell.Count + b.Cuts.Count);
        Blend = BlendOf(blocks);
        _chisel = new CliffChisel(s, floor);
        _surface = new SlabSurface(s, floor);
        _edgeMost = _chisel.ChipDepth + _surface.WearMost(0);
        _inward = _chisel.Depth + _edgeMost + _surface.Depth + _surface.Reach;
        _detail = _inward > 0;
        _deep = _inward + 0.01;
        _scale = [.. _blocks.Select(b => _inward > DetailShare * b.MinWidth() ? DetailShare * b.MinWidth() / _inward : 1)];
        Capped = _scale.Any(k => k < 1);
        _half = Math.Max(GrooveDepth, FloorShare * floor);
        _crackHalf = s.JointCrack / 2;

        // The grid's cell from the floor (which is 1.5 cells), and how much of the crack it can show.
        double cell = floor / 1.5;
        double shown = cell <= 0 ? 1 : Math.Clamp((s.JointCrack - cell) / ((CrackCells - 1) * cell), 0, 1);
        _crackDepth = CrackDepthShare * _crackHalf * shown;
        _stepped = _detail && _surface.Steps;

        // Past the chisel's depth and the creases' steps (scaled by the largest block share): a crack's step pushed in
        // deeper than the filler reached opened the joint's inside as a slot (random 56). The rest of the detail is left
        // out, as the filler fills the joint cracks within its reach.
        _reach = _half + (1.5 * (_chisel.Depth + _surface.CreaseSteps) * _scale.DefaultIfEmpty(1).Max()) + FillMargin;
        var ground = blocks.Min(b => (b.Tier, b.Layer));
        _bottom = blocks.Where(b => (b.Tier, b.Layer) == ground).Min(b => b.Block.Bounds().Min.Z);

        _bounds = new Plane[n * BoundPlanes];
        _sideBounds = new Plane[n * SideBoundPlanes];
        _sides = new Plane[n][];
        _faces = new int[n];
        _tops = new Plane[n];
        _belowTops = new Plane[n][];
        _rims = new Plane[n][];
        _rimShares = new double[n][];
        _rimDepths = new double[n][];
        _cuts = new Plane[n][][];
        var ups = new Plane[n][];
        for (int i = 0; i < n; i++)
        {
            var shell = _blocks[i].Shell;
            int faces = Math.Max(blocks[i].Shape.Faces, 4);
            _sides[i] = [.. shell.Take(faces)];
            _faces[i] = faces;
            _tops[i] = shell[faces];
            ups[i] = [.. shell.Skip(faces).Where(p => p.Normal.Z > 0)];
            _belowTops[i] = [.. shell.Skip(faces + 1)];
            _rims[i] = [.. _belowTops[i].Where(p => p.Normal.Z > 0)];

            // How far each rim plane reaches into the block's plain shell (its sides, top and bottom), against the grid.
            var plain = new ConvexBlock([.. shell.Take(faces + 2)], []).Corners();
            _rimDepths[i] = [.. _rims[i].Select(r => Math.Max(plain.Max(c => r.At(c)), 0))];
            _rimShares[i] = [.. _rimDepths[i].Select(d => floor <= 0 ? 1 : Math.Clamp((d - (RimShownShare * floor)) / (RimRampShare * floor), 0, 1))];
            _cuts[i] = [.. _blocks[i].Cuts.Select(c => c.ToArray())];
            _bounds[(i * BoundPlanes) + 0] = shell.MaxBy(p => p.Normal.X);
            _bounds[(i * BoundPlanes) + 1] = shell.MinBy(p => p.Normal.X);
            _bounds[(i * BoundPlanes) + 2] = shell.MaxBy(p => p.Normal.Y);
            _bounds[(i * BoundPlanes) + 3] = shell.MinBy(p => p.Normal.Y);
            _bounds[(i * BoundPlanes) + 4] = shell.MaxBy(p => p.Normal.Z);
            _bounds[(i * BoundPlanes) + 5] = shell.MinBy(p => p.Normal.Z);
            _sideBounds[(i * SideBoundPlanes) + 0] = _sides[i].MaxBy(p => p.Normal.X);
            _sideBounds[(i * SideBoundPlanes) + 1] = _sides[i].MinBy(p => p.Normal.X);
            _sideBounds[(i * SideBoundPlanes) + 2] = _sides[i].MaxBy(p => p.Normal.Y);
            _sideBounds[(i * SideBoundPlanes) + 3] = _sides[i].MinBy(p => p.Normal.Y);
        }

        // Levels: one per tier and layer, from the bottom up.
        var levels = blocks.Select(b => (b.Tier, b.Layer)).Distinct().Order().ToList();
        _levels = levels.Count;
        _level = [.. blocks.Select(b => levels.IndexOf((b.Tier, b.Layer)))];
        _levelBlocks = [.. Enumerable.Range(0, _levels).Select(level => Enumerable.Range(0, n).Where(i => _level[i] == level).ToArray())];

        // Each block rests on the block of its column one level down (the top plate of the tier below, for a tier's
        // first plate), and that block carries it.
        _bed = new int[n];
        _rider = new int[n];
        Array.Fill(_rider, -1);
        var joints = new List<Joint>();
        for (int i = 0; i < n; i++)
        {
            var b = blocks[i];
            _bed[i] = Enumerable.Range(0, n)
                .Where(j => blocks[j].Column == b.Column && (blocks[j].Tier, blocks[j].Layer).CompareTo((b.Tier, b.Layer)) < 0)
                .DefaultIfEmpty(-1)
                .MaxBy(j => j < 0 ? (-1, -1) : (blocks[j].Tier, blocks[j].Layer));
            if (_bed[i] >= 0)
            {
                _rider[_bed[i]] = i;
                int bed = _bed[i];
                // Above the plane the filler stays inside the upper block's upward planes, within the joint reach of its
                // flat bottom, and behind the bed's own downward planes (its bottom, bottom chamfers and foot), at every
                // Foot: a leaning or footed bed's sides fall away under its rim, so bounded by the sides alone the filler
                // ran on past both blocks as a bridge or a lip, at Foot 0 too (review-final I1). Not behind the upper
                // block's own foot: bounded there, it put random 56's stepped crack and batch random 0 over the fold
                // budget (a crease's step pushed in past the foot opens the joint unless the filler reaches behind it).
                var bottom = _blocks[i].Shell[_faces[i] + 1];
                Plane[] above =
                [
                    .. ups[i],
                    new Plane(bottom.Normal, bottom.Offset + _reach),
                    .. _belowTops[bed].Where(q => q.Normal.Z < 0),
                ];
                joints.Add(new Joint(i, bed, [.. _sides[i], .. _sides[bed]], above, _belowTops[bed], [.. _cuts[i], .. _cuts[bed]]));
            }
        }

        _joints = [.. joints];

        // The blocks under each block in its column whose rims it reaches: its bed, and, while its sunk part reaches
        // below the ground line of the last one (into the one that rests under it), the next one down.
        _beds = new int[n][];
        for (int i = 0; i < n; i++)
        {
            double lowest = _blocks[i].Bounds().Min.Z;
            var chain = new List<int>();
            for (int b = _bed[i]; b >= 0; b = lowest < blocks[b].Shape.Base.Z ? _bed[b] : -1)
            {
                chain.Add(b);
            }

            _beds[i] = [.. chain];
        }

        _cull = new Lazy<SlabCliffCull?>(() => n + _joints.Length > CullLeast
            ? new SlabCliffCull(Bounds()!.Value, _bounds, _sideBounds, [.. _joints.Select(j => (j.Upper, j.Lower, _tops[j.Lower]))], _reach)
            : null);
    }

    /// <summary>
    /// Gets the most the joint crack pulls the field in anywhere, in metres: its depth over (1 − 1 /
    /// <see cref="CrackFadeShare"/>), as it fades with depth (see <see cref="Crack"/>).
    /// </summary>
    private double CrackMost => _crackDepth * CrackFadeShare / (CrackFadeShare - 1);

    /// <summary>
    /// Gets a value indicating whether the surface detail is scaled down on any block, so that it cuts none of them
    /// further than a quarter of its thinnest width (else the scales are not read).
    /// </summary>
    internal bool Capped { get; }

    /// <summary>
    /// Gets the joint reach (the blend radius of the fused joins that came before the hard ones), in metres: 0.15 × the
    /// smallest block's depth, held to 0.2 to 0.6 m. Within twice it of a neighbour of its level, a block's chips and
    /// wear fade out (see <see cref="Fade"/>), and the detail's share and the faces' normals turn from block to block.
    /// </summary>
    public double Blend { get; }

    /// <summary>
    /// Builds the cull grid now, if this many blocks and joints use one, so the first samples (which may come from
    /// several threads at once) don't wait for it.
    /// </summary>
    internal void Prepare() => _ = _cull.Value;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public double Distance(V3 p)
    {
        int n = _blocks.Length;
        Span<double> values = n <= StackMost ? stackalloc double[n] : new double[n];
        bool detail = _detail && p.Z > 0;
        var joined = Union(p, true, values, detail);
        return detail ? joined.Field + Detail(p, joined, values, out _) : joined.Field;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The joined blocks are read exactly but for the fused joins, whose field lies between the hard union's and the
    /// whole blend's: the estimate takes both ends, so the crease's share is never read. The detail is bounded by its
    /// most either way where the point is far from the surface; nearer, it is read as it is (see <see cref="Detail"/>)
    /// but for the chips, which are bounded by the most each level's edges allow there, the noise at its top; and the
    /// chips and wear are read only where the sign is still open without them. The field grows with the union and with
    /// the union cut by the chips and wear, so its least and most come from their least and most.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    [SkipLocalsInit]
    public double Estimate(V3 p, out double error)
    {
        int n = _blocks.Length;
        Span<double> values = n <= StackMost ? stackalloc double[n] : new double[n];
        var joined = Union(p, false, values, false);
        double leastUnion = joined.Field - joined.Slack;
        double mostUnion = joined.Field + joined.Slack;
        if (!_detail || p.Z <= 0)
        {
            error = joined.Slack;
            return joined.Field;
        }

        // Anywhere, the detail is from the smudge's most out to the most everything cuts in.
        double least = leastUnion + SlabSurface.Hold(-_surface.Reach, p.Z);
        double most = mostUnion + SlabSurface.Hold(_inward, p.Z);
        if (least <= 0 && most >= 0)
        {
            // Nearer, the push and the chisel are read as they are (noise is cheap beside the joints), the chips and
            // wear taken as anything from none to their most; and where that still leaves the sign open, the levels'
            // own chips and wear are cut in, the chips bounded.
            double scale = Scale(values, joined);
            double rest = Push(p, values, joined, out _) + _chisel.Face(p);

            // Where the ledges' steps set the blocks back, the rest is held further (see Detail).
            double allow = _stepped ? StepAllowance(p) : double.PositiveInfinity;
            least = leastUnion + Math.Min(SlabSurface.Hold(scale * rest, p.Z), allow);
            most = mostUnion + SlabSurface.Hold(scale * (_edgeMost + rest), p.Z);
            if (least <= 0 && most >= 0)
            {
                joined = Union(p, false, values, true);
                if (joined.Cut)
                {
                    least = leastUnion + Math.Min(SlabSurface.Hold(scale * (joined.LeastCut - leastUnion + rest), p.Z), allow);
                    most = mostUnion + SlabSurface.Hold(scale * (joined.MostCut - mostUnion + rest), p.Z);
                }
            }
        }

        error = (most - least) / 2;
        return (most + least) / 2;
    }

    /// <inheritdoc/>
    /// <remarks>What the creases, ledges, lines, edge wear and chips cut in there, held as the push is near the ground line.</remarks>
    [SkipLocalsInit]
    public double Tag(V3 p)
    {
        if (!_detail || p.Z <= 0)
        {
            return 0;
        }

        int n = _blocks.Length;
        Span<double> values = n <= StackMost ? stackalloc double[n] : new double[n];
        var joined = Union(p, true, values, true);
        Detail(p, joined, values, out double inward);
        return SlabSurface.Hold(inward, p.Z);
    }

    /// <inheritdoc/>
    /// <remarks>The blocks' bounds, grown by half the joint reach (a margin round them: the joins only take rock away) and by how far the smudge pushes out (see <see cref="BoundsOf"/>).</remarks>
    public (V3 Min, V3 Max)? Bounds() => Grown(_blocks, Blend, _surface.Reach);

    /// <summary>
    /// The bounds of the field laid blocks make (see <see cref="Bounds"/>), without making it: the blocks' bounds, grown
    /// by half the joint reach and by how far the smudge pushes out (review-final Minor 10: the generator made a whole
    /// field to read them).
    /// </summary>
    /// <param name="blocks">The laid blocks.</param>
    /// <param name="s">The settings.</param>
    internal static (V3 Min, V3 Max) BoundsOf(IReadOnlyList<LaidBlock> blocks, CliffSettings s) =>
        Grown([.. blocks.Select(b => b.Block)], BlendOf(blocks), s.Smudge);

    /// <summary>The joint reach of laid blocks (see <see cref="Blend"/>).</summary>
    private static double BlendOf(IReadOnlyList<LaidBlock> blocks) => Math.Clamp(BlendShare * blocks.Min(b => b.Shape.Depth), LeastBlend, MostBlend);

    /// <summary>Blocks' bounds grown by half a joint reach and by how far the surface pushes out.</summary>
    private static (V3 Min, V3 Max) Grown(ConvexBlock[] blocks, double blend, double reach)
    {
        var (min, max) = blocks[0].Bounds();
        foreach (var block in blocks)
        {
            var (lo, hi) = block.Bounds();
            min = new V3(Math.Min(min.X, lo.X), Math.Min(min.Y, lo.Y), Math.Min(min.Z, lo.Z));
            max = new V3(Math.Max(max.X, hi.X), Math.Max(max.Y, hi.Y), Math.Max(max.Z, hi.Z));
        }

        double grown = (blend / 2) + reach;
        var grow = new V3(grown, grown, grown);
        return (min - grow, max + grow);
    }

    /// <summary>
    /// The block that wins at a point (whose field, with its groove, is smallest there; the first of them on a tie), and
    /// the field of the nearest other block of its tier and layer, which it fuses with. Found as the field is, reading
    /// only the blocks near the point and nothing on the heap; exact wherever the point is within reach of the surface
    /// (deeper in the rock it does not matter which block wins, and see <see cref="Union"/>).
    /// </summary>
    /// <param name="p">The point.</param>
    /// <param name="otherDistance">
    /// The nearest other block's field at the point, exact while it is under twice the blend radius (from there on it
    /// fades nothing, and may be read as +∞); +∞ when the winner's tier and layer has no other block.
    /// </param>
    /// <returns>The winner's index in the blocks the field was made from.</returns>
    [SkipLocalsInit]
    public int Nearest(V3 p, out double otherDistance)
    {
        int n = _blocks.Length;
        Span<double> values = n <= StackMost ? stackalloc double[n] : new double[n];
        var joined = Union(p, false, values, false, true);
        otherDistance = joined.Other;
        return joined.Winner;
    }
}
