using System.Runtime.CompilerServices;
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// The slab cliff's surface detail as a push at each point, read in cliff space so one pattern runs across every block:
/// smudge pushes the surface out and in, edge wear, creases, ledges and lines cut it in. The formulas are the cliff
/// block's; on plane shells, how far a point is from an edge is the second largest of its block's plane values (see
/// <see cref="Wear"/>), and how far it faces sideways is the caller's, read from the faces near the point. Grooves are never
/// narrower than the floor the mesh can show. Near the ground line the whole push is held (see <see cref="Hold"/>).
/// </summary>
internal sealed class SlabSurface
{
    private const int MaxCreases = 4;
    private const int MaxLedges = 3;

    /// <summary>The power that sharpens the line bands into thin lines, as in the reference.</summary>
    private const double LinePower = 60;

    /// <summary>
    /// How narrow a line band of power p is, as a number: its half-depth half-width in metres is about this over
    /// (frequency × √p). A V groove of the floor's width is half as deep 0.293 floors from its middle, which a band
    /// keeps while p ≤ (5.68 / (floor × frequency))², hence 32.3.
    /// </summary>
    private const double LineWidthFactor = 32.3;

    /// <summary>
    /// A groove (crease, ledge or line) cuts in at most this many times its width (from its middle to where it ends):
    /// deeper, its two walls stand within 30° of each other at the bottom, which the mesh can only show as a fold.
    /// </summary>
    internal const double DepthShare = 1.5;

    /// <summary>The floor (the narrowest groove the mesh can show) is this many grid cells.</summary>
    private const double FloorCells = 1.5;

    /// <summary>Lines closer than this many grid cells apart are not cut at all (two cells for each side of a line).</summary>
    private const double LinePeriodCells = 4;

    /// <summary>Past <see cref="LinePeriodCells"/>, the lines come in over this many more cells of spacing.</summary>
    private const double LinePeriodRamp = 2;

    /// <summary>A crease runs through a seeded place within this share of the half width either side of the middle.</summary>
    private const double CreaseSpread = 0.6;

    /// <summary>The ledges run at seeded heights from this share of the height up to one less it.</summary>
    private const double LedgeMargin = 0.1;

    /// <summary>The smudge's noise is this share as fine up and down as across, so the smears stretch upright.</summary>
    private const double SmudgeStretch = 0.35;

    /// <summary>
    /// A groove is read at most this far back along the surface's normal from a point, in metres: as far as any groove
    /// reaches across (see <see cref="Push(V3, double, V3, double, out double)"/>); further out the push is held anyway.
    /// </summary>
    private const double SlideMost = 1;

    /// <summary>Near the ground line the push is held to this share of the height above it (below 1, so the base is never cut).</summary>
    private const double BaseShare = 0.5;

    // Salts of the seeded numbers drawn here (the chisel draws 205 to 208, the layout 100 to 199, 400 to 560 and from
    // 1000 up, the colours 600).
    private const int SmudgeSalt = 200;
    private const int WearSalt = 201;
    private const int LineSalt = 202;
    private const int CreaseSalt = 300;
    private const int LedgeSalt = 700;

    private readonly CliffSettings _s;
    private readonly (double X, double Cos, double Sin, double Wobble)[] _creases;
    private readonly (double Z, double Sign, double Wobble)[] _ledges;
    private readonly double _smudgeW;
    private readonly V3 _smudgeScale;
    private readonly double _wearW;
    private readonly double _grooveWidth;
    private readonly bool _lines;
    private readonly double _lineFrequency;
    private readonly double _lineNoiseScale;
    private readonly double _linePhase;
    private readonly double _lineW;
    private readonly double _linePower;

    /// <summary>How deep the lines cut: Line depth, held to <see cref="DepthShare"/> of a line's width.</summary>
    private readonly double _lineDepth;

    /// <summary>The surface detail of a cliff.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="floor">
    /// The narrowest groove the mesh can show, in metres: creases, ledges and the lines are made at least this wide
    /// (each from its middle to where it ends), so a groove too narrow for the grid is a little wider and gentler
    /// instead of shredded.
    /// </param>
    public SlabSurface(CliffSettings s, double floor)
    {
        ArgumentNullException.ThrowIfNull(s);
        _s = s;
        _creases = Creases(s);
        _ledges = Ledges(s);
        _smudgeW = NoiseW(s.Seed, SmudgeSalt);
        _smudgeScale = new V3(1 / s.SmudgeSize, 1 / s.SmudgeSize, SmudgeStretch / s.SmudgeSize);
        _wearW = NoiseW(s.Seed, WearSalt);
        _grooveWidth = Math.Max(s.CreaseWidth, floor);

        // Lines: Blender's sine bands along z, Lines of them over the height, sharpened to thin lines by a power of 60
        // (less when that would make them narrower than the floor).
        _lines = s.Lines > 0 && s.LineDepth != 0;
        _lineFrequency = 2 * Math.PI * s.Lines / s.Height;
        _lineNoiseScale = s.Lines * 0.314159 / s.Height;
        _linePhase = CliffNoise.Hash01(s.Seed, LineSalt) * 2 * Math.PI;
        _lineW = NoiseW(s.Seed, LineSalt + 1);
        double band = floor * _lineFrequency;
        _linePower = band > 0 ? Math.Clamp(LineWidthFactor / (band * band), 1, LinePower) : LinePower;

        // A band of power p is as wide as a V groove of width √(32.3 / p) / frequency (half as deep as far out). Lines
        // closer together than the grid can show (under LinePeriodCells cells from one to the next, the floor being one
        // and a half) fade out instead: so fine a ripple only folds the mesh.
        double cells = floor > 0 ? s.Height / s.Lines / (floor / FloorCells) : double.PositiveInfinity;
        double shown = Math.Clamp((cells - LinePeriodCells) / LinePeriodRamp, 0, 1);
        _lineDepth = _lines ? shown * Math.Min(s.LineDepth, DepthShare * Math.Sqrt(LineWidthFactor / _linePower) / _lineFrequency) : 0;
        Reach = s.Smudge;
        UsesSide = _ledges.Length > 0 || _lines;
        Depth = (_creases.Length * (s.CreaseDepth + s.CreaseStep)) + (_ledges.Length * (s.LedgeDepth + s.LedgeStep)) + (_lines ? s.LineDepth : 0);
        CreaseSteps = _creases.Length * s.CreaseStep;
    }

    /// <summary>Gets how far, at most, the push moves the surface out, in metres: the smudge is the only push out.</summary>
    public double Reach { get; }

    /// <summary>Gets how far, at most, the creases, ledges and lines together cut the surface in, in metres.</summary>
    public double Depth { get; }

    /// <summary>
    /// Gets how far, at most, the creases' steps together set the surface back, in metres: Crease step for each crease
    /// (a step sets back all the rock on one side of its crease, so it reaches every joint there).
    /// </summary>
    public double CreaseSteps { get; }

    /// <summary>Gets a value indicating whether the push reads how far the surface faces sideways (ledges and lines do).</summary>
    public bool UsesSide { get; }

    /// <summary>
    /// How far the push may move the surface at a height, in metres, either way: half the height above the ground line,
    /// and nothing below it, so the cliff stands on its whole base (a cut steeper than that there would come loose).
    /// </summary>
    /// <param name="push">The push, in metres (positive moves the surface in).</param>
    /// <param name="z">The height, in metres, the ground line at 0.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Hold(double push, double z)
    {
        double most = BaseShare * Math.Max(z, 0);
        return Math.Clamp(push, -most, most);
    }

    /// <summary>
    /// The push at a point, before the ground line's hold: how far it is cut in by creases, ledges and lines, less how far
    /// the smudge pushes it out. Positive moves the surface in.
    /// </summary>
    /// <param name="p">A point in cliff space.</param>
    /// <param name="side">
    /// How far the surface faces sideways there, 0 (up or down) to 1 (1 − |z| of the faces' normals): ledges and lines
    /// cut only as far as that, so they run round the sides and leave the tops whole.
    /// </param>
    /// <param name="inward">How far the point is cut in, in metres (0 or more).</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public double Push(V3 p, double side, out double inward) => Push(p, side, default, 0, out inward);

    /// <summary>
    /// The push at a point (see <see cref="Push(V3, double, out double)"/>), its grooves read at the point's foot on the
    /// surface: the point moved back along the surface's normal by how far it is from the surface (held to
    /// <see cref="SlideMost"/>). A groove is pushed in along the normal; read at the point itself, the groove's lines
    /// would move with the push wherever the face meets the groove's plane at a slant, carrying the V sideways so its
    /// steeper wall overhangs the face. Read at the foot, the groove is the same all along each normal, so its walls
    /// stand on the face as it is cut. The smudge is read at the point.
    /// </summary>
    /// <param name="p">A point in cliff space.</param>
    /// <param name="side">How far the surface faces sideways there (see <see cref="Push(V3, double, out double)"/>).</param>
    /// <param name="normal">The surface's unit normal near the point (zero to read the grooves at the point).</param>
    /// <param name="away">How far the point is outside the surface, in metres (negative inside).</param>
    /// <param name="inward">How far the point is cut in, in metres (0 or more).</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public double Push(V3 p, double side, V3 normal, double away, out double inward)
    {
        inward = 0;
        var foot = p - (normal * Math.Clamp(away, -SlideMost, SlideMost));
        foreach (var (x, cos, sin, wobble) in _creases)
        {
            inward += Groove(Across(foot, x, cos, sin), foot, wobble, _s.CreaseDepth, _s.CreaseStep);
        }

        if (side > 0)
        {
            // A ledge's step is not a push: the field sets the block's sides back by it (see Step).
            foreach (var (z, sign, wobble) in _ledges)
            {
                inward += Groove((foot.Z - z) * sign, foot, wobble, _s.LedgeDepth, 0) * side;
            }

            if (_lines)
            {
                double wave = (_lineFrequency * foot.Z) + _linePhase
                    + (_s.LineWobble * ((CliffNoise.Fbm(foot * _lineNoiseScale, _lineW, 2) * 2) - 1));
                inward += Band(0.5 + (0.5 * JsMath.Sin(wave - (Math.PI / 2)))) * _lineDepth * side;
            }
        }

        double outward = _s.Smudge != 0
            ? ((CliffNoise.Fbm(new V3(p.X * _smudgeScale.X, p.Y * _smudgeScale.Y, p.Z * _smudgeScale.Z), _smudgeW, 4) * 2) - 1) * _s.Smudge
            : 0;
        return inward - outward;
    }

    /// <summary>
    /// Whether a groove may reach a point <paramref name="away"/> from the surface, read at its foot (see
    /// <see cref="Push(V3, double, V3, double, out double)"/>): where none can, the foot need not be found, as the push is
    /// the same read at the point.
    /// </summary>
    /// <param name="p">A point in cliff space.</param>
    /// <param name="away">How far the point is outside the surface, in metres (negative inside).</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool Reaches(V3 p, double away)
    {
        if (_lines)
        {
            return true;
        }

        double reach = _grooveWidth + _s.CreaseWobble + Math.Min(Math.Abs(away), SlideMost);
        foreach (var (x, cos, sin, _) in _creases)
        {
            if (Math.Abs(Across(p, x, cos, sin)) < reach)
            {
                return true;
            }
        }

        foreach (var (z, _, _) in _ledges)
        {
            if (Math.Abs(p.Z - z) < reach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets a value indicating whether any ledge has a step (see <see cref="Step"/>).</summary>
    public bool Steps => _ledges.Length > 0 && _s.LedgeStep != 0;

    /// <summary>
    /// How far the ledges' steps set the rock back at a point, before the ground line's hold: Ledge step on the set-back
    /// side of each ledge line (wobbling as its groove does), easing across the groove's width. The field moves each
    /// block's side planes in by this (its sides, not its top or bottom), so a block above a ledge is a smaller block
    /// whose top meets its sides square, with no lip.
    /// </summary>
    /// <param name="p">A point in cliff space.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public double Step(V3 p)
    {
        double step = 0;
        foreach (var (z, sign, wobble) in _ledges)
        {
            step += Groove((p.Z - z) * sign, p, wobble, 0, _s.LedgeStep);
        }

        return step;
    }

    /// <summary>
    /// Edge wear: noise dents that only reach in near an edge, 0 to Edge wear. On a plane shell a point's distance to its
    /// nearest edge is how far it is inside the second nearest of its block's planes (the first is the face it is on).
    /// </summary>
    /// <param name="p">A point in cliff space.</param>
    /// <param name="gap">How far the point is inside its block's second nearest plane, in metres.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public double Wear(V3 p, double gap)
    {
        double near = WearMost(gap);
        if (near == 0)
        {
            return 0;
        }

        double noise = CliffNoise.Fbm(p / _s.WearSize, _wearW, 4);
        return near * Math.Clamp((noise * 2) - 0.6, 0, 1);
    }

    /// <summary>The most <see cref="Wear"/> can be at a gap from the edge, whatever the noise.</summary>
    /// <param name="gap">How far the point is inside its block's second nearest plane, in metres.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public double WearMost(double gap)
    {
        if (_s.EdgeWear == 0)
        {
            return 0;
        }

        double near = Math.Clamp(1 - (Math.Max(gap, 0) / _s.WearWidth), 0, 1);
        return near * near * _s.EdgeWear;
    }

    /// <summary>A seeded noise field's offset for one feature.</summary>
    private static double NoiseW(uint seed, int salt) => CliffNoise.Hash01(seed, salt) * 1000;

    /// <summary>
    /// The creases in use: upright planes across the width, each through a seeded place along x, its normal ±x turned by
    /// up to Crease angle either way about the upright (cos and sin of its direction), and its wobble's noise.
    /// </summary>
    private static (double X, double Cos, double Sin, double Wobble)[] Creases(CliffSettings s)
    {
        int count = s.CreaseDepth != 0 || s.CreaseStep != 0 ? Math.Clamp(s.Creases, 0, MaxCreases) : 0;
        var creases = new (double X, double Cos, double Sin, double Wobble)[count];

        // The crease seed picks the layout of the cracks only.
        uint seed = unchecked(s.Seed + ((uint)s.CreaseSeed * 101u));
        for (int k = 0; k < count; k++)
        {
            int salt = CreaseSalt + (k * 8);
            double angle = (CliffNoise.Hash01(seed, salt + 2) < 0.5 ? 0 : Math.PI)
                + (((CliffNoise.Hash01(seed, salt + 3) * 2) - 1) * s.CreaseAngle * Math.PI / 180);
            double x = ((CliffNoise.Hash01(seed, salt) * 2) - 1) * CreaseSpread * s.Width / 2;
            creases[k] = (x, JsMath.Cos(angle), JsMath.Sin(angle), NoiseW(s.Seed, salt + 4));
        }

        return creases;
    }

    /// <summary>The ledges in use: the height each runs at, which side is set back, and its wobble's noise.</summary>
    private static (double Z, double Sign, double Wobble)[] Ledges(CliffSettings s)
    {
        int count = s.LedgeDepth != 0 || s.LedgeStep != 0 ? Math.Clamp(s.Ledges, 0, MaxLedges) : 0;
        var ledges = new (double Z, double Sign, double Wobble)[count];
        for (int k = 0; k < count; k++)
        {
            int salt = LedgeSalt + (k * 8);
            double z = (LedgeMargin + ((1 - (2 * LedgeMargin)) * CliffNoise.Hash01(s.Seed, salt))) * s.Height;
            double sign = CliffNoise.Hash01(s.Seed, salt + 1) > 0.5 ? 1 : -1;
            ledges[k] = (z, sign, NoiseW(s.Seed, salt + 2));
        }

        return ledges;
    }

    /// <summary>How far a point is across a crease's straight plane, through x on the cliff's middle line (y = 0).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Across(V3 p, double x, double cos, double sin) => ((p.X - x) * cos) + (p.Y * sin);

    /// <summary>
    /// A wobbling groove across a line: a V of <paramref name="depth"/> (held to <see cref="DepthShare"/> of the groove
    /// width) reaching the groove width either side, plus a
    /// smooth <paramref name="step"/> that sets the positive side back. The line wanders by Crease wobble × a noise on
    /// the point; beyond the reach of the V and the wobble the groove is the step or nothing, so no noise is read.
    /// </summary>
    /// <param name="across">How far the point is across the straight line, in metres.</param>
    /// <param name="p">The point, for the wobble's noise.</param>
    /// <param name="wobble">The wobble noise's offset.</param>
    /// <param name="depth">The V's depth.</param>
    /// <param name="step">How far the positive side is set back.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Groove(double across, V3 p, double wobble, double depth, double step)
    {
        double reach = _grooveWidth + _s.CreaseWobble;
        if (Math.Abs(across) >= reach)
        {
            return across > 0 ? step : 0;
        }

        across += ((CliffNoise.Fbm(p * 0.4, wobble, 3) * 2) - 1) * _s.CreaseWobble;
        double g = Math.Clamp(1 - (Math.Abs(across) / _grooveWidth), 0, 1);
        double t = Math.Clamp((across / _grooveWidth * 0.5) + 0.5, 0, 1);
        return (g * g * Math.Min(depth, DepthShare * _grooveWidth)) + (t * t * (3 - (2 * t)) * step);
    }

    /// <summary>A line band's profile: the sine band sharpened by the line power (60, the reference's, unless the floor lowers it).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Band(double f)
    {
        if (_linePower < LinePower)
        {
            return JsMath.Pow(f, _linePower);
        }

        double f4 = f * f * f * f;
        double f16 = f4 * f4 * f4 * f4;
        return f16 * f16 * f16 * f4 * f4 * f4;
    }
}
