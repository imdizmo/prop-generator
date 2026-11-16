using System.Runtime.CompilerServices;
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// Chisel and Edge chips: the finished surface cut in by piecewise-flat noise, so it breaks into flat facets that meet at
/// straight creases. Chisel dresses every face into broad facets about Chisel size across, read at the world point so
/// one pattern runs across every block; it is never deeper than <see cref="FaceShare"/> of the facets' size, which keeps
/// the facets broad planes instead of folds (what it still seals or cuts loose, the mesher's clean-up drops). Edge chips
/// knock flat chips off the edges where two block planes meet: each cell of an edge, about Chip size long, either stays
/// whole or loses its corner to one flat cut at a seeded angle and depth, with sharp sides where it ends, so the rim
/// breaks in angular facets (Ruling 35). A chip's cut is the same however far along the edge's planes, so it can never
/// burrow under a face. The ground line's hold is the field's (see <see cref="SlabSurface.Hold"/>).
/// </summary>
internal sealed class CliffChisel
{
    // Salts of the seeded numbers drawn here (SlabSurface draws 200 to 203, 300 up and 700 up).
    private const int Salt = 205;
    private const int WarpSalt = 206;
    private const int ChipSalt = 207;
    private const int ChipWarpSalt = 208;

    /// <summary>The warp's grid is this many times coarser than the facets'.</summary>
    private const double WarpScale = 1.7;

    /// <summary>
    /// How far the warp moves the facets' grid, in its cells: enough that the creases run every which way instead of
    /// repeating the grid's pattern. The warp is piecewise flat too, so the facets stay flat.
    /// </summary>
    private const double WarpReach = 1.2;

    /// <summary>
    /// Chisel is held to this share of Chisel size. Steeper, the facets fold into each other: on a plain box at a share
    /// of 2.5 they sealed 33 pockets and folded 700 edges. Held here they rarely do; on a block bent by other sliders a
    /// few pockets or crumbs can still form, and the parts drop them after meshing.
    /// </summary>
    private const double FaceShare = 0.25;

    /// <summary>Edge chips is held to this many times Chip size.</summary>
    private const double ChipShare = 1.5;

    /// <summary>
    /// Ruling 30's two-cell rule for chips: a chip is cut only where it is this many grid cells long and deep or more,
    /// and its sides fall away over this many cells, so the grid shows its flat cut and sharp sides instead of folding a
    /// sliver of them.
    /// </summary>
    private const double ChipCells = 2;

    /// <summary>The chips' warp moves a chip this many of its cells along its edge, at most (under one, so it never folds).</summary>
    private const double ChipWarpReach = 0.6;

    /// <summary>The chips' warp is this many times coarser than the chips.</summary>
    private const double ChipWarpScale = 1.9;

    /// <summary>This share of an edge's chip cells is left whole.</summary>
    private const double ChipWhole = 0.35;

    /// <summary>
    /// A chipped cell is cut from this share of Edge chips deep (times the edge's sharpness), or two grid cells where
    /// that is deeper, to all of it.
    /// </summary>
    private const double ChipLeast = 0.35;

    /// <summary>The least angle, in degrees, a chip's flat cut is turned from one of the edge's planes toward the other.</summary>
    private const double ChipTurnLeast = 40;

    /// <summary>
    /// How much more, in degrees, the seed may turn a chip's cut: so the cuts lean from 40° to 55°, well past the Sharp
    /// angle the starting points shade with (30°), as the mesh rounds a crease into bends a little shallower than it.
    /// </summary>
    private const double ChipTurnSpread = 15;

    /// <summary>How many angles a chip's cut is picked from, evenly over its spread (so no cell reads a cosine).</summary>
    private const int ChipTurns = 16;

    /// <summary>
    /// With no grid, how far past its cell's ends a chip's sides reach along the edge, in Chip sizes; on a grid they
    /// reach <see cref="ChipCells"/> cells (see <see cref="_sideRun"/>).
    /// </summary>
    private const double ChipSideShare = 0.1;

    /// <summary>
    /// The least a chip falls away from the edge, in metres per metre into the rock along its planes: a bound the flat
    /// cuts keep (turned 40° to 55°, they fall away faster everywhere but at the very bottom of the deepest ones, where
    /// they meet it), so a point that far inside both planes is chipped no more than that.
    /// </summary>
    private const double ChipSlope = 0.4;

    // Fixed turns, so no grid lines up with the block's faces (a face cut along a grid plane would show the grid's
    // repeating triangles).
    private static readonly (V3 X, V3 Y, V3 Z) Turn = Rotation(new V3(1, 2, 3), 1.1);
    private static readonly (V3 X, V3 Y, V3 Z) WarpTurn = Rotation(new V3(-3, 1, 2), 2.3);

    /// <summary>The cosine and sine of each angle a chip's cut may be turned by (see <see cref="ChipTurns"/>).</summary>
    private static readonly (double Cos, double Sin)[] ChipTurnTable = [.. Enumerable.Range(0, ChipTurns).Select(k =>
    {
        double turn = (ChipTurnLeast + (ChipTurnSpread * (k + 0.5) / ChipTurns)) * Math.PI / 180;
        return (JsMath.Cos(turn), JsMath.Sin(turn));
    })];

    private readonly double _face;
    private readonly double _faceScale;
    private readonly double _chips;
    private readonly double _chipScale;

    /// <summary>The grid's cell, in metres (0 for no grid): a chip under two grid cells deep is not cut.</summary>
    private readonly double _cell;

    /// <summary>
    /// How far past its cell's ends a chip's sides reach along the edge, in the chips' cells: two grid cells (or a tenth
    /// of a chip with no grid), so a chip's side falls from its depth to none over that, and meets a shallower
    /// neighbour in a slope as long, not a step.
    /// </summary>
    private readonly double _sideRun;
    private readonly uint _seed;
    private readonly uint _warpSeed;
    private readonly uint _chipSeed;
    private readonly uint _chipWarpSeed;

    /// <summary>The chisel of a cliff.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="floor">
    /// The narrowest groove the mesh can show, in metres (one and a half grid cells; 0 for no grid): no chip shorter
    /// than two cells along its edge, nor any cell's chip under two cells deep, is cut (Ruling 30's two-cell rule), and
    /// chips' sides fall away over two cells, as the grid cannot show anything finer, only fold it.
    /// </param>
    public CliffChisel(CliffSettings s, double floor = 0)
    {
        ArgumentNullException.ThrowIfNull(s);
        _face = Math.Clamp(s.Chisel, 0, FaceShare * s.ChiselSize);
        _faceScale = 1 / s.ChiselSize;

        // Held to ChipShare of its length: a chip much deeper than it is long is a slot the mesh cannot show, and it
        // cuts crumbs loose between its neighbours.
        double chips = Math.Clamp(s.EdgeChips, 0, ChipShare * s.ChipSize);
        double cell = floor / 1.5;
        _chips = cell > 0 && s.ChipSize < ChipCells * cell ? 0 : chips;
        _chipScale = 1 / s.ChipSize;
        _cell = cell > 0 ? cell : 0;
        _sideRun = cell > 0 ? ChipCells * cell / s.ChipSize : ChipSideShare;
        _seed = Seed(s.Seed, Salt);
        _warpSeed = Seed(s.Seed, WarpSalt);
        _chipSeed = Seed(s.Seed, ChipSalt);
        _chipWarpSeed = Seed(s.Seed, ChipWarpSalt);
        Depth = _face;
        ChipDepth = _chips;
    }

    /// <summary>Gets the most the chisel's facets cut in anywhere, in metres (0 when Chisel is off).</summary>
    public double Depth { get; }

    /// <summary>Gets the most a chip cuts in anywhere, in metres (0 when Edge chips is off).</summary>
    public double ChipDepth { get; }

    /// <summary>
    /// Gets how far inside an edge's planes a point can be and still be chipped, in metres (0 when Edge chips is off):
    /// the chips' most over how fast they fall away at the least.
    /// </summary>
    public double ChipReach => ChipDepth / ChipSlope;

    /// <summary>How far Chisel cuts the faces in at a point, in metres (0 to Chisel).</summary>
    /// <param name="world">The point in cliff space.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public double Face(V3 world) => _face == 0 ? 0 : Facets(world, _faceScale, Turn, WarpTurn, _seed, _warpSeed) * _face;

    /// <summary>The most <see cref="ChipCut"/> can be at a point of an edge, times a fade, whatever the cells and the block's field.</summary>
    /// <param name="p">The point in cliff space.</param>
    /// <param name="a">One of the edge's planes.</param>
    /// <param name="b">The other.</param>
    /// <param name="fade">The share of the chip to cut, 0 to 1.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public double ChipMost(V3 p, Plane a, Plane b, double fade) =>
        _chips == 0 || fade <= 0 ? 0 : Cut(p, a, b, false, double.NegativeInfinity) * Math.Min(fade, 1);

    /// <summary>
    /// The chip at a point of one edge, where two of a block's planes meet, before any fade (the field takes this times
    /// the fade, held to 1): the push that brings the block's surface in to the flat cut of the edge's chip cell there
    /// (see <see cref="Cut"/>), times how sharp the edge is (the sine of the angle between the planes' normals, so a
    /// gentle bend is chipped less; 1 past a right angle). The cut runs along the edge (the direction a × b), so on each
    /// face beside the edge it is the same however deep into the rock: it cannot seal a pocket under the face. 0 to
    /// <see cref="ChipDepth"/>.
    /// </summary>
    /// <param name="p">The point in cliff space.</param>
    /// <param name="a">One of the edge's planes.</param>
    /// <param name="b">The other.</param>
    /// <param name="field">The block's own field at the point: the largest of its planes' (and cuts') values.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public double ChipCut(V3 p, Plane a, Plane b, double field) => _chips == 0 ? 0 : Cut(p, a, b, true, field);

    /// <summary>
    /// The most <see cref="ChipMost"/> can be, with no fade, for any edge whose planes the point is at least
    /// <paramref name="inside"/> into (the larger of how far it is inside each, negative outside): the chips' most less
    /// the run out, a hair over, as no edge is sharper than a right angle.
    /// </summary>
    /// <param name="inside">The smaller of the edge's two planes' values at the point, in metres.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public double ChipBound(double inside) => Math.Max((_chips * (1 + 1e-9)) - (ChipSlope * Math.Max(-inside, 0)), 0);

    /// <summary>An edge's unit direction, turned so its largest part is positive: the same whichever plane comes first.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static V3 Along(V3 edge)
    {
        double x = Math.Abs(edge.X);
        double y = Math.Abs(edge.Y);
        double z = Math.Abs(edge.Z);
        double largest = x >= y && x >= z ? edge.X : y >= z ? edge.Y : edge.Z;
        return edge / (largest < 0 ? -edge.Length : edge.Length);
    }

    /// <summary>The number of an edge's chip pattern: a hash of its two planes, the same whichever comes first.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int Line(Plane a, Plane b) => unchecked((int)(Key(a) ^ Key(b)));

    /// <summary>A hash of a plane's normal and offset.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static uint Key(Plane plane)
    {
        unchecked
        {
            ulong h = ((ulong)BitConverter.DoubleToInt64Bits(plane.Normal.X) * 0x9E3779B97F4A7C15UL)
                ^ ((ulong)BitConverter.DoubleToInt64Bits(plane.Normal.Y) * 0xC2B2AE3D27D4EB4FUL)
                ^ ((ulong)BitConverter.DoubleToInt64Bits(plane.Normal.Z) * 0x165667B19E3779F9UL)
                ^ ((ulong)BitConverter.DoubleToInt64Bits(plane.Offset) * 0x27D4EB2F165667C5UL);
            return (uint)(h ^ (h >> 29) ^ (h >> 47));
        }
    }

    /// <summary>Warped piecewise-flat noise at a scale (see <see cref="CliffNoise.Facets"/>): 0 to 1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double Facets(V3 m, double scale, (V3 X, V3 Y, V3 Z) turn, (V3 X, V3 Y, V3 Z) warpTurn, uint seed, uint warpSeed)
    {
        var q = Turned(turn, m) * scale;
        var warp = CliffNoise.Facets3(Turned(warpTurn, m) * (scale / WarpScale), warpSeed);
        return CliffNoise.Facets(q + ((warp - new V3(0.5, 0.5, 0.5)) * WarpReach), seed);
    }

    private static uint Seed(uint seed, int salt) => (uint)(CliffNoise.Hash01(seed, salt) * 4294967295.0);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static V3 Turned((V3 X, V3 Y, V3 Z) turn, V3 p) => new(V3.Dot(turn.X, p), V3.Dot(turn.Y, p), V3.Dot(turn.Z, p));

    /// <summary>The rows of the turn by an angle about an axis (Rodrigues' formula).</summary>
    private static (V3 X, V3 Y, V3 Z) Rotation(V3 axis, double angle)
    {
        var k = axis.Normalized();
        double c = JsMath.Cos(angle);
        double s = JsMath.Sin(angle);
        double t = 1 - c;
        return (
            new V3((t * k.X * k.X) + c, (t * k.X * k.Y) - (s * k.Z), (t * k.X * k.Z) + (s * k.Y)),
            new V3((t * k.X * k.Y) + (s * k.Z), (t * k.Y * k.Y) + c, (t * k.Y * k.Z) - (s * k.X)),
            new V3((t * k.X * k.Z) - (s * k.Y), (t * k.Y * k.Z) + (s * k.X), (t * k.Z * k.Z) + c));
    }

    /// <summary>
    /// The chip at a point of an edge before its fade (see <see cref="ChipCut"/>); its most unless
    /// <paramref name="noise"/>. The edge is cut in cells along it, Chip size long (the cells' ends wander a little, by
    /// the chips' warp): each cell is whole, or loses its corner to one flat plane, its own depth and angle seeded.
    /// The cut is the push that brings the block's surface to the plane (how far the plane is from the point, less the
    /// block's own <paramref name="field"/> there), held to the cell's stretch of the edge, its sides falling away over
    /// two grid cells past the cell's ends (see <see cref="_sideRun"/>); the deepest chip of the cells about the point
    /// wins. Never more than the most less <see cref="ChipSlope"/> times how far the point is inside the further plane.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Cut(V3 p, Plane a, Plane b, bool noise, double field)
    {
        // How far the point is inside each plane (0 outside it).
        double alpha = Math.Max(-a.At(p), 0);
        double beta = Math.Max(-b.At(p), 0);
        // A bend of more than a right angle (a knife edge, a block's end turned back) is as sharp as a chip can want.
        double dot = V3.Dot(a.Normal, b.Normal);
        var edge = V3.Cross(a.Normal, b.Normal);
        double most = _chips * (dot < 0 ? 1 : edge.Length);
        double bound = most - (ChipSlope * Math.Max(alpha, beta));
        if (bound <= 0)
        {
            return 0;
        }

        if (!noise)
        {
            return bound;
        }

        int line = Line(a, b);
        double u = V3.Dot(p, Along(edge)) * _chipScale;
        u += (CliffNoise.Facets1(u / ChipWarpScale, _chipWarpSeed, line) - 0.5) * ChipWarpReach;
        int at = (int)Math.Floor(u);
        double place = u - at;
        double chip = 0;

        // The point's own cell, and a neighbour only where the point is within its sides' reach past the cell's end.
        int first = place < _sideRun ? at - 1 : at;
        int last = place > 1 - _sideRun ? at + 1 : at;
        for (int cell = first; cell <= last; cell++)
        {
            double whole = CliffNoise.Cell01(cell, _chipSeed, line, 0x1u);
            if (whole < ChipWhole)
            {
                continue;
            }

            // No chip is cut under two grid cells deep (Ruling 30's two-cell rule): its flat cut and sharp sides would be
            // finer than the grid can show, and only fold the mesh. The drawn depth is spread from two cells (or the
            // least share of the most, if deeper) to the most (Ruling 40), so the chips keep their spread of depths
            // and none passes the slider; where the edge allows no chip two cells deep, none is cut.
            double least = Math.Max(ChipLeast * most, ChipCells * _cell);
            if (most < least)
            {
                break;
            }

            double depth = least + ((most - least) * CliffNoise.Cell01(cell, _chipSeed, line, 0x2u));

            var (l, m) = ChipTurnTable[(int)(CliffNoise.Cell01(cell, _chipSeed, line, 0x3u) * ChipTurns)];

            // The plane's normal is l a + m b turned out of the rock; its length over the two unit normals.
            double g = Math.Sqrt((l * l) + (m * m) + (2 * l * m * dot));
            double facet = ((depth - (l * alpha) - (m * beta)) / g) - field;
            // Full depth across the cell's own stretch, falling to none over the sides' run past its ends.
            double side = depth * (0.5 + _sideRun - Math.Abs(u - (cell + 0.5))) / _sideRun;
            chip = Math.Max(chip, Math.Min(facet, side));
        }

        return Math.Min(chip, bound);
    }
}
