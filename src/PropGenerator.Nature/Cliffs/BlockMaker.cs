using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Cliffs;

/// <summary>Builds a <see cref="ConvexBlock"/> from a <see cref="BlockShape"/>: the slab-rock formula standing up.</summary>
internal static class BlockMaker
{
    private const int ShellSalt = 2000;
    private const int CutSalt = 3000;
    private const int CutSaltStride = 20;
    private const double Deg = Math.PI / 180;
    private const double AngleJitter = 8 * Deg;
    private const double TiltJitter = 3 * Deg;
    private const double FrontNormalY = -0.3;
    private const double MaxCutShare = 0.4;
    private const double HeartShare = 0.8;
    private const double MinCut = 0.05;
    private const double MaxEndShift = 0.4;
    private const double MaxOverhang = 45 * Deg;
    private const double FacingWalls = 0.7;

    /// <summary>How many walls a step draws, at most, before it is left out for leaving a thin fin (2 draws each, within its cut's salts).</summary>
    private const int StepTries = 4;

    /// <summary>How many times a bite or a groove is drawn, at most, before it is left out for leaving a thin fin (7 draws each at most, within its cut's salts).</summary>
    private const int CutTries = 2;

    /// <summary>
    /// The rungs that ease a block towards a plain rounded slab when its first try is too thin: the scale of
    /// roughness, bevel and top tilt; of lean; and of the end turns.
    /// </summary>
    private static readonly (double Fine, double Lean, double Turn)[] Rungs =
        [(1, 1, 1), (0.5, 0.5, 1), (0, 0.5, 1), (0, 0, 1), (0, 0, 0)];

    /// <summary>Makes the block. The same shape and seed always give the same block.</summary>
    /// <param name="shape">What the block is like.</param>
    /// <param name="seed">The seed of the block's jitter and cuts.</param>
    /// <returns>A closed convex shell with wedge cuts, thick enough everywhere it can be.</returns>
    public static ConvexBlock Make(BlockShape shape, uint seed)
    {
        V3 heart = shape.Base + new V3(0, 0, shape.Height / 2);
        for (int rung = 0; ; rung++)
        {
            var (fine, lean, turn) = Rungs[rung];
            List<Side> sides = Sides(shape, seed, fine, lean, turn);
            List<Plane> shell = Shell(shape, seed, fine, sides);
            var shellBlock = new ConvexBlock(shell, []);
            if (rung == Rungs.Length - 1 || (shellBlock.MinWidth() >= shape.MinThickness && shellBlock.Distance(heart) < 0))
            {
                return new ConvexBlock(shell, Cuts(shape, seed, shellBlock, sides, shell[sides.Count], heart));
            }
        }
    }

    private static List<Side> Sides(BlockShape s, uint seed, double fine, double leanScale, double turnScale)
    {
        var draw = new Draws(seed, ShellSalt);
        int faces = Math.Max(s.Faces, 4);
        double a = s.Width / 2;
        double b = s.Depth / 2;
        double r = Math.Min(s.Width, s.Depth) / 4;
        double step = 2 * Math.PI / faces;
        double jitter = Math.Min(0.25 * step, AngleJitter);
        double maxLean = JsMath.Atan(Math.Max(s.Depth - s.MinThickness, 0) / s.Height);
        double lean = Math.Clamp(s.Lean * Deg * leanScale, -MaxOverhang, maxLean);

        var sides = new List<Side>();
        for (int i = 0; i < faces; i++)
        {
            double theta = (step * i) + (((draw.Next() * 2) - 1) * jitter);
            double cx = JsMath.Cos(theta);
            double cy = JsMath.Sin(theta);

            // The tangent point on the rounded rectangle whose support function is the rectangle's plus the radius.
            var tangent = new V3(
                s.Base.X + (((cx >= 0 ? 1 : -1) * (a - r)) + (r * cx)),
                s.Base.Y + (((cy >= 0 ? 1 : -1) * (b - r)) + (r * cy)),
                s.Base.Z);
            double tilt = TiltJitter * ((draw.Next() * 2) - 1);
            double inset = s.Roughness * fine * draw.Next();
            if (cy < FrontNormalY)
            {
                tilt = Math.Min(lean + tilt, maxLean);
            }

            var normal = new V3(cx * JsMath.Cos(tilt), cy * JsMath.Cos(tilt), JsMath.Sin(tilt));
            sides.Add(new Side(new Plane(normal, V3.Dot(normal, tangent) - inset), tangent, inset));
        }

        double limit = JsMath.Atan(MaxEndShift * s.Width / s.Depth);
        Turn(s, sides, -1, Math.Min(s.TurnLeft * Deg * turnScale, limit));
        Turn(s, sides, 1, Math.Min(s.TurnRight * Deg * turnScale, limit));
        return sides;
    }

    /// <summary>Replaces the side nearest an end by a plane turned back toward the front (-y), pivoting on the end's back corner, so it cuts the front end corner away.</summary>
    private static void Turn(BlockShape s, List<Side> sides, int end, double angle)
    {
        if (angle <= 0)
        {
            return;
        }

        int index = 0;
        for (int i = 1; i < sides.Count; i++)
        {
            if (end * sides[i].Plane.Normal.X > end * sides[index].Plane.Normal.X)
            {
                index = i;
            }
        }

        double sin = JsMath.Sin(angle);
        double cos = JsMath.Cos(angle);
        var normal = new V3(end * cos, -sin, 0);
        var pivot = new V3(s.Base.X + (end * s.Width / 2), s.Base.Y + (s.Depth / 2), s.Base.Z);
        double inset = sides[index].Inset;
        var middle = pivot + new V3(-end * sin / cos * (s.Depth / 2), -s.Depth / 2, 0);
        sides[index] = new Side(new Plane(normal, V3.Dot(normal, pivot) - inset), middle, inset);
    }

    /// <summary>The sides, then the top and the bottom, then the chamfers.</summary>
    private static List<Plane> Shell(BlockShape s, uint seed, double fine, List<Side> sides)
    {
        var draw = new Draws(seed, ShellSalt + 500);
        var shell = sides.ConvertAll(side => side.Plane);

        double tilt = s.TopTilt * Deg * fine;
        double aim = draw.Next() * 2 * Math.PI;
        var nt = new V3(JsMath.Tan(tilt) * JsMath.Cos(aim), JsMath.Tan(tilt) * JsMath.Sin(aim), 1).Normalized();
        var top = new Plane(nt, V3.Dot(nt, s.Base + new V3(0, 0, s.Height)));
        shell.Add(top);
        shell.Add(new Plane(new V3(0, 0, -1), s.Sink - s.Base.Z));

        double bevel = s.Bevel * fine;
        foreach (Side side in sides)
        {
            if (bevel <= 0)
            {
                break;
            }

            var n = side.Plane.Normal;
            var h = new V3(n.X, n.Y, 0).Normalized();
            if (draw.Next() < 0.85)
            {
                double w = 0.7 + (0.6 * draw.Next());
                double z = (top.Offset - (nt.X * side.Tangent.X) - (nt.Y * side.Tangent.Y)) / nt.Z;
                shell.Add(Chamfer(side, h, w, z, bevel * (0.6 + (0.8 * draw.Next()))));
            }

            if (draw.Next() < 0.6)
            {
                double w = 0.7 + (0.6 * draw.Next());
                shell.Add(Chamfer(side, h, -w, s.Base.Z - s.Sink, bevel * 0.5 * (0.6 + (0.8 * draw.Next()))));
            }
        }

        return shell;
    }

    /// <summary>A plane through the side's edge at height z, tilted up (w &gt; 0) or down, and pushed in by cut.</summary>
    private static Plane Chamfer(Side side, V3 h, double w, double z, double cut)
    {
        var n = side.Plane.Normal;
        double reach = (side.Plane.Offset - V3.Dot(n, new V3(side.Tangent.X, side.Tangent.Y, z))) / ((n.X * h.X) + (n.Y * h.Y));
        var edge = new V3(side.Tangent.X + (h.X * reach), side.Tangent.Y + (h.Y * reach), z);
        var normal = new V3(h.X, h.Y, w).Normalized();
        return new Plane(normal, V3.Dot(normal, edge) - cut);
    }

    /// <summary>
    /// The block's steps, bites and grooves, in that order. A cut that would leave a fin of rock thinner than Min
    /// thickness against an earlier cut or the shell (see <see cref="LeavesAnyFin"/>) is drawn again, and left out if no
    /// draw fits: two cuts whose faces cross at a knife edge leave a sliver the mesh cannot hold.
    /// </summary>
    private static List<IReadOnlyList<Plane>> Cuts(BlockShape s, uint seed, ConvexBlock block, List<Side> sides, Plane top, V3 heart)
    {
        var cuts = new List<IReadOnlyList<Plane>>();
        var (min, max) = block.Bounds();
        var middle = new V3((min.X + max.X) / 2, (min.Y + max.Y) / 2, 0);
        int total = s.Steps + s.Bites + s.Grooves;
        for (int j = 0; j < total; j++)
        {
            var draw = new Draws(seed, CutSalt + (j * CutSaltStride));
            Plane[]? cut = null;
            if (j < s.Steps)
            {
                cut = Step(s, block, top, middle, heart, draw, cuts);
            }
            else
            {
                for (int attempt = 0; attempt < CutTries && cut == null; attempt++)
                {
                    var drawn = j < s.Steps + s.Bites ? Bite(s, block, sides, heart, draw) : Groove(s, block, sides, heart, draw);
                    if (drawn == null)
                    {
                        break;
                    }

                    cut = LeavesAnyFin(block, drawn, cuts, s.MinThickness) ? null : drawn;
                }
            }

            if (cut != null)
            {
                cuts.Add(cut);
            }
        }

        return cuts;
    }

    /// <summary>
    /// Below a lowered top plane and past an upright plane: a layer off one part of the top. A step whose wall would
    /// leave a thin fin against an earlier cut or the shell (see <see cref="LeavesAnyFin"/>) is drawn again, up to
    /// <see cref="StepTries"/> times in all, and the step is left out if none fits.
    /// </summary>
    private static Plane[]? Step(BlockShape s, ConvexBlock block, Plane top, V3 middle, V3 heart, Draws draw, List<IReadOnlyList<Plane>> earlier)
    {
        double depth = Depth(s, block, top.Normal, heart, 0.35 + (0.65 * draw.Next()), out double hi);
        if (depth < MinCut)
        {
            return null;
        }

        var floor = new Plane(top.Normal, hi - depth);
        for (int attempt = 0; attempt < StepTries; attempt++)
        {
            double phase = draw.Next() * 2 * Math.PI;
            var normal = new V3(JsMath.Cos(phase), JsMath.Sin(phase), 0);
            double position = (draw.Next() - 0.5) * 0.8 * block.WidthAlong(normal) / 2;
            Plane[] step = [floor, new Plane(normal, V3.Dot(normal, middle) + position)];
            if (!LeavesAnyFin(block, step, earlier, s.MinThickness))
            {
                return step;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a cut would leave a fin of rock thinner than a thickness against any earlier cut or against the shell
    /// itself: see <see cref="LeavesFin(ConvexBlock, IReadOnlyList{Plane}, IReadOnlyList{Plane}, double)"/>, and the
    /// same between each of the cut's faces and each shell face facing it.
    /// </summary>
    /// <param name="shell">The block's shell (its cuts are not read).</param>
    /// <param name="cut">The cut's planes: it removes the rock in front of all of them.</param>
    /// <param name="earlier">The cuts already made.</param>
    /// <param name="thickness">The least thickness a fin may have, in metres.</param>
    internal static bool LeavesAnyFin(ConvexBlock shell, IReadOnlyList<Plane> cut, IEnumerable<IReadOnlyList<Plane>> earlier, double thickness)
    {
        if (earlier.Any(other => LeavesFin(shell, other, cut, thickness)))
        {
            return true;
        }

        for (int a = 0; a < cut.Count; a++)
        {
            foreach (var face in shell.Shell)
            {
                if (-V3.Dot(cut[a].Normal, face.Normal) >= FacingWalls
                    && Thin(shell, cut[a], face, [.. Others(cut, a)], thickness))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether two cuts leave a fin of rock thinner than a thickness between a face of one and a face of the other that
    /// face each other: the rock between the two faces, inside the shell and in front of both cuts' other planes (where
    /// both cut, on either side of it), is a convex region, and its thickness across the faces is least at one of its
    /// corners, so every corner is measured. Faces that cross inside the shell leave a fin that thins to nothing (a
    /// knife edge). Faces that do not face each other leave a corner, not a fin, and cuts with nothing between them
    /// leave no fin.
    /// </summary>
    /// <param name="shell">The block's shell (its cuts are not read).</param>
    /// <param name="cutA">The first cut's planes: it removes the rock in front of all of them.</param>
    /// <param name="cutB">The second cut's planes.</param>
    /// <param name="thickness">The least thickness the fin may have, in metres.</param>
    internal static bool LeavesFin(ConvexBlock shell, IReadOnlyList<Plane> cutA, IReadOnlyList<Plane> cutB, double thickness)
    {
        for (int a = 0; a < cutA.Count; a++)
        {
            for (int b = 0; b < cutB.Count; b++)
            {
                if (-V3.Dot(cutA[a].Normal, cutB[b].Normal) >= FacingWalls
                    && Thin(shell, cutA[a], cutB[b], [.. Others(cutA, a), .. Others(cutB, b)], thickness))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the rock between a face and a face facing it (behind both), inside the shell and behind the other planes
    /// given, is anywhere thinner than a thickness across the faces: the thickness is linear, so it is least at one of
    /// the region's corners.
    /// </summary>
    private static bool Thin(ConvexBlock shell, Plane face, Plane facing, IEnumerable<Plane> within, double thickness)
    {
        double cos = -V3.Dot(face.Normal, facing.Normal);
        Plane[] region = [face, facing, .. within];

        // First inside only the shell's planes facing furthest along ±x, ±y and ±z, which hold the region: where even
        // that is empty or thick enough everywhere, so is the region, and the other shell planes need not be read.
        var outer = shell.Shell;
        var loose = new ConvexBlock(
            [
                outer.MaxBy(p => p.Normal.X), outer.MinBy(p => p.Normal.X), outer.MaxBy(p => p.Normal.Y),
                outer.MinBy(p => p.Normal.Y), outer.MaxBy(p => p.Normal.Z), outer.MinBy(p => p.Normal.Z), .. region,
            ],
            []);
        var thin = loose.Corners().Where(c => -face.At(c) - (facing.At(c) / cos) < thickness).ToList();
        if (thin.Count == 0)
        {
            return false;
        }

        // A thin corner of that inside the whole shell is a point of the region.
        if (thin.Any(c => shell.Distance(c) <= 1e-9))
        {
            return true;
        }

        return shell.CornersWithin(region).Any(c => -face.At(c) - (facing.At(c) / cos) < thickness);
    }

    /// <summary>A cut's planes but one, each flipped: their fronts, where the cut takes rock, become their backs.</summary>
    private static IEnumerable<Plane> Others(IReadOnlyList<Plane> cut, int skip) =>
        cut.Where((_, k) => k != skip).Select(Flipped);

    /// <summary>The plane facing the other way: its back is the original's front.</summary>
    private static Plane Flipped(Plane plane) => new(-plane.Normal, -plane.Offset);

    /// <summary>Inside a pushed-in side and past a cross plane: a bite out of a corner.</summary>
    private static Plane[]? Bite(BlockShape s, ConvexBlock block, List<Side> sides, V3 heart, Draws draw)
    {
        Side side = sides[(int)(draw.Next() * sides.Count) % sides.Count];
        double depth = Depth(s, block, side.Plane.Normal, heart, 0.35 + (0.65 * draw.Next()), out double hi);
        var h = new V3(side.Plane.Normal.X, side.Plane.Normal.Y, 0).Normalized();
        int sign = draw.Next() < 0.5 ? 1 : -1;
        var cross = new V3(
            (-h.Y * sign) + ((draw.Next() - 0.5) * 0.3),
            (h.X * sign) + ((draw.Next() - 0.5) * 0.3),
            (draw.Next() - 0.5) * 0.6).Normalized();
        double offset = V3.Dot(cross, side.Tangent) + 0.3 + (0.9 * draw.Next());
        return depth < MinCut ? null : [new Plane(side.Plane.Normal, hi - depth), new Plane(cross, offset)];
    }

    /// <summary>Inside a pushed-in side and past an upright step plane: part of one face lowered by 0.15 to 0.4 m.</summary>
    private static Plane[]? Groove(BlockShape s, ConvexBlock block, List<Side> sides, V3 heart, Draws draw)
    {
        Side side = sides[(int)(draw.Next() * sides.Count) % sides.Count];
        double depth = Depth(s, block, side.Plane.Normal, heart, 1, out double hi);
        double drop = Math.Min(depth, 0.15 + (0.25 * draw.Next()));
        var h = new V3(side.Plane.Normal.X, side.Plane.Normal.Y, 0).Normalized();
        int sign = draw.Next() < 0.5 ? 1 : -1;
        var wall = new V3(-h.Y * sign, h.X * sign, 0);
        double offset = V3.Dot(wall, side.Tangent) + ((draw.Next() - 0.5) * 0.3 * block.WidthAlong(wall));
        return drop < MinCut ? null : [new Plane(side.Plane.Normal, hi - drop), new Plane(wall, offset)];
    }

    /// <summary>
    /// How deep a cut from direction n may go: its share of 40% of the block's width along n, and never past
    /// four fifths of the way to the block's heart.
    /// </summary>
    private static double Depth(BlockShape s, ConvexBlock block, V3 n, V3 heart, double share, out double hi)
    {
        hi = block.Corners().Max(c => V3.Dot(n, c));
        double depth = s.CutDepth * MaxCutShare * block.WidthAlong(n) * share;
        return Math.Min(depth, HeartShare * (hi - V3.Dot(n, heart)));
    }

    /// <summary>A side plane, the point on the footprint it touches, and how far roughness set it in.</summary>
    private readonly record struct Side(Plane Plane, V3 Tangent, double Inset);

    /// <summary>A run of seeded numbers in 0 to 1, from consecutive salts.</summary>
    private sealed class Draws(uint seed, int salt)
    {
        private int _salt = salt;

        public double Next() => CliffNoise.Hash01(seed, _salt++);
    }
}
