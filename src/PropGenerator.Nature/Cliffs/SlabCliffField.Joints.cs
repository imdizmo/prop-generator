using System.Runtime.CompilerServices;

namespace PropGenerator.Nature.Cliffs;

/// <summary>The joints: each block's own field with its bedding grooves, the joint filler, the rim cut, the joint crack between neighbours of a level, and the planes and cuts they are read from.</summary>
internal sealed partial class SlabCliffField
{
    /// <summary>
    /// A block's field with its bedding grooves: near the top plane of the block it rests on, and near its own top
    /// plane when a block rests on it, its sides are pulled in by the groove's V there (by as much of it as the other
    /// block is near, so a set-back tier leaves the rim below it whole). <paramref name="raw"/> is the block's own field,
    /// without them. Once the block's own field is at least <paramref name="past"/>, the rest is not read: the field so
    /// far (never more than the whole) is given, and <paramref name="raw"/> is NaN.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Value(int i, V3 p, out double raw, double past = double.PositiveInfinity)
    {
        // The block's own field, as ConvexBlock reads it: the largest of its shell planes and its cuts, its sides set
        // back by the ledges' steps.
        double f = double.NegativeInfinity;
        double shift = Shift(i, p);
        var shell = _shells[i];
        for (int k = 0; k < shell.Length; k++)
        {
            f = Math.Max(f, shell[k].At(p) + (k < _faces[i] ? shift : 0));
            if (f >= past)
            {
                raw = double.NaN;
                return f;
            }
        }

        foreach (var cut in _cuts[i])
        {
            double m = double.PositiveInfinity;
            foreach (var plane in cut)
            {
                m = Math.Min(m, plane.At(p));
            }

            f = Math.Max(f, m);
            if (f >= past)
            {
                raw = double.NaN;
                return f;
            }
        }

        raw = f;
        int bed = _bed[i];
        if (bed >= 0)
        {
            double t = _tops[bed].At(p);
            double groove = Groove(t);
            if (groove > 0)
            {
                f = Math.Max(f, Side(_sides[i], p) + shift + (Near(Side(_sides[bed], p) + Shift(bed, p)) * groove));
            }

            // Where the bed's rim is chamfered or bevelled, the block's base is cut back to match: below the bed's top
            // by the bed's rim planes themselves, so its sunk part never covers that rim, and above it by their mirror
            // images in the bed's top plane, so no flat lip hangs over it and the two rims meet in a V. A sunk part
            // that reaches through its bed into the blocks under it is cut by their rims too, so it never leaves a
            // skin of rock over a rim the bed was cut back from.
            foreach (int below in _beds[i])
            {
                if (_rims[below].Length > 0)
                {
                    double tb = below == bed ? t : _tops[below].At(p);
                    if (Math.Abs(tb) < RimReach)
                    {
                        f = RimCut(f, below, tb, p);
                    }
                }
            }
        }

        int rider = _rider[i];
        if (rider >= 0)
        {
            double groove = Groove(_tops[i].At(p));
            if (groove > 0)
            {
                f = Math.Max(f, Side(_sides[i], p) + shift + (Near(Side(_sides[rider], p) + Shift(rider, p)) * groove));
            }
        }

        return f;
    }

    /// <summary>
    /// A joint's filler at a point <paramref name="t"/> from the lower block's top plane: the rock inside both blocks'
    /// sides and cuts, within <see cref="_reach"/> of the plane, pulled in from the sides by the groove's V; above the
    /// plane inside the upper block's upward planes and within the reach of its flat bottom, behind the lower block's
    /// downward planes, and inside the mirror images of the lower block's rim planes (as the upper block's base is),
    /// below it inside the lower block's shell less its top (its rim chamfers and bevels too, so the lower block keeps its
    /// own rim). +∞ where it is more than <paramref name="past"/>.
    /// </summary>
    /// <param name="joint">The joint.</param>
    /// <param name="t">How far the point is from the lower block's top plane.</param>
    /// <param name="p">The point.</param>
    /// <param name="past">
    /// Past this the filler's field is not read but taken as +∞: the union reads it with the nearest block's field plus
    /// the chips' most, and a filler further out than that changes nothing (the union is never above the nearest
    /// block's field, and no cut is deeper than the chips' most).
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Fill(Joint joint, double t, V3 p, double past)
    {
        double f = Math.Max(Math.Abs(t) - _reach, Side(t >= 0 ? joint.Above : joint.Below, p));
        if (t > 0 && _rims[joint.Lower].Length > 0)
        {
            f = RimCut(f, joint.Lower, t, p);
        }

        double groove = Groove(t);
        double side = Side(joint.Sides, p) + Math.Min(Shift(joint.Upper, p), Shift(joint.Lower, p));
        if (f > past || side + groove > past)
        {
            return double.PositiveInfinity;
        }

        side = Math.Max(side, Cut(joint.Cuts, p));
        return Math.Max(f, side + groove);
    }

    /// <summary>
    /// A field <paramref name="f"/> cut to match a block's base to the rim of a block under it, at a point
    /// <paramref name="t"/> from that block's top plane: each rim plane (its chamfers and bevels) cuts by how far the
    /// point (mirrored in that plane when above it) is past it, and <see cref="RimInside"/> times that inside it, by as
    /// much of the cut as the plane's share (<see cref="_rimShares"/>): all of it for a chamfer the grid shows, none for
    /// one under two cells (which the grid cannot show, and whose cut only folds the mesh), in between from two to three
    /// cells. Each cut is held to <see cref="RimInside"/> times how far the point is inside the bed's sides plus the
    /// plane's own depth (<see cref="_rimDepths"/>) and the groove's half width, so past the bed's footprint by more than
    /// that nothing is cut (review 8-8c,
    /// Important 2: the sunk part of a block hanging past its bed was trimmed along the bed's chamfer planes). With every
    /// share 1 the field is the larger of f and the cut.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double RimCut(double f, int bed, double t, V3 p)
    {
        var q = t > 0 ? p - (_tops[bed].Normal * (2 * t)) : p;
        Plane[] rims = _rims[bed];
        double[] shares = _rimShares[bed];
        double cut = f;

        // Only over the bed: past its sides by more than a rim plane's own depth and the groove's half width, that
        // plane cuts nothing, so a block hanging out past its bed is never trimmed along a chamfer's plane carried on
        // past the bed's end.
        double side = Side(_sides[bed], p);
        double[] depths = _rimDepths[bed];
        for (int j = 0; j < rims.Length; j++)
        {
            double v = rims[j].At(q);
            double c = Math.Min(v > 0 ? v : v * RimInside, f + (RimInside * (depths[j] + _half - side)));
            if (c > f)
            {
                cut = Math.Max(cut, f + (shares[j] * (c - f)));
            }
        }

        return cut;
    }

    /// <summary>
    /// How far the ledges' steps set a block's sides back at a point (see <see cref="SlabSurface.Step"/>): held near the
    /// ground line to <see cref="StepHold"/> of the height, and scaled with the rest of the detail by the block's share
    /// (so it never thins a plate through); none at or below the ground line, or with no step.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Shift(int i, V3 p) => _stepped && p.Z > 0 ? Math.Min(_scale[i] * _surface.Step(p), StepHold * p.Z) : 0;

    /// <summary>
    /// How far the groove's V pulls the sides in at a point <paramref name="t"/> from its plane: its depth on the plane
    /// (never more than <see cref="SlabSurface.DepthShare"/> of its half width, as for every groove), nothing a half
    /// width away.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Groove(double t) => Math.Min(GrooveDepth, SlabSurface.DepthShare * _half) * Math.Max(0, 1 - (Math.Abs(t) / _half));

    /// <summary>
    /// How much of the groove a block gets from another block whose sides are <paramref name="side"/> away: all of it
    /// where the two faces are flush (the other's sides within the groove's depth inside the point, as they are on the
    /// groove's own surface), none a half width further out or further in. So where the other block stands back (a set-back
    /// plate above, or one under an overhang) the face meets its ledge whole, with no half of a V undercutting it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Near(double side) => Math.Clamp(1 - (Math.Max(side, -side - GrooveDepth) / _half), 0, 1);

    /// <summary>
    /// How far the joint's crack pulls the surface in at a point where two blocks of a level meet, their own fields
    /// <paramref name="va"/> and <paramref name="vb"/> and the second <paramref name="apart"/> behind the first: a V
    /// along the seam (where the two fields are equal), <see cref="_crackDepth"/> deep on it and none
    /// <see cref="_crackHalf"/> across it, how far across read as how far apart the fields are over how fast they part
    /// (the difference of the two faces' soft normals, see <see cref="Facing"/>). Only as much as the two faces really
    /// meet in a crease: none where they run on flush (within <see cref="FlushCos"/> of each other in angle, all from
    /// <see cref="CrackCos"/>), and none where one block's face there is hidden in the other (its end inside a
    /// neighbour, its edge on the neighbour's face): each face must go on past the line where the two meet, out of the
    /// other block, as a face of its own block.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private double Crack(int i, int j, V3 p, double va, double vb, double apart)
    {
        if (double.IsNaN(va) || double.IsNaN(vb))
        {
            return 0;
        }

        // Each block's face at the point: the plane of it that wins there, and by how much. Near an edge of a block
        // (where another of its planes nearly wins) the crack fades out, so it never jumps where the winner changes,
        // and a flush neighbour's hidden end, a hair behind the shared face, never reads as a crease.
        var na = Face(i, p, out double clearA);
        var nb = Face(j, p, out double clearB);
        double soft = Math.Max(_crackHalf, CrackSoftLeast);
        double dot = V3.Dot(na, nb);
        double angle = Math.Clamp((FlushCos - dot) / (FlushCos - CrackCos), 0, 1)
            * Math.Clamp(clearA / soft, 0, 1) * Math.Clamp(clearB / soft, 0, 1);

        // Along either face, away from the seam, the other block's field grows by the sine of the angle between them
        // per metre (faces turned further than a right angle part at least as fast).
        double rate = dot > 0 ? Math.Sqrt(1 - (dot * dot)) : 1;
        double across = apart / Math.Max(rate, 1e-9);
        double v = 1 - (across / _crackHalf);

        // Deeper in the rock than the crack reaches, it fades out, so no seam hidden inside the rock is opened (where
        // the chisel's facets come near it, that would seal a pocket).
        double depth = Math.Clamp(1 + (Math.Min(va, vb) / (CrackFadeShare * _crackDepth)), 0, 1);
        if (angle == 0 || v <= 0 || depth == 0)
        {
            return 0;
        }

        double share = angle;
        double sideways = 1 - (dot * dot);
        double sure = Math.Clamp(sideways / ParallelAcross, 0, 1);
        if (sure > 0)
        {
            // The point of the line where the two faces meet nearest p, and a step from it along each face, out of the
            // other block.
            double alpha = (va - (dot * vb)) / sideways;
            double beta = (vb - (dot * va)) / sideways;
            var meet = p - (na * alpha) - (nb * beta);
            double step = Blend / 2;
            var alongA = (nb - (na * dot)).Normalized();
            var alongB = (na - (nb * dot)).Normalized();
            double faceA = Math.Clamp(1 - (2 * _blocks[i].Distance(meet + (alongA * step)) / step), 0, 1);
            double faceB = Math.Clamp(1 - (2 * _blocks[j].Distance(meet + (alongB * step)) / step), 0, 1);
            share *= 1 - sure + (sure * faceA * faceB);
        }

        return share * v * depth * CrackMost;
    }

    /// <summary>
    /// The normal of a block's face at a point: of the term (a shell plane, or a cut's face: its smallest plane) whose
    /// value is largest there, the block's sides set back by the ledges' steps as its field is (see <see cref="Value"/>).
    /// <paramref name="clear"/> is by how much it wins: how far the next largest term is below it, and for a cut's face,
    /// how far the cut's next smallest plane is above it, in metres.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private V3 Face(int i, V3 p, out double clear)
    {
        double shift = Shift(i, p);
        double top = double.NegativeInfinity;
        double second = double.NegativeInfinity;
        double inner = double.PositiveInfinity;
        V3 normal = default;
        var shell = _shells[i];
        for (int k = 0; k < shell.Length; k++)
        {
            double v = shell[k].At(p) + (k < _faces[i] ? shift : 0);
            if (v > top)
            {
                second = top;
                top = v;
                normal = shell[k].Normal;
                inner = double.PositiveInfinity;
            }
            else if (v > second)
            {
                second = v;
            }
        }

        foreach (var cut in _cuts[i])
        {
            double m1 = double.PositiveInfinity;
            double m2 = double.PositiveInfinity;
            V3 face = default;
            foreach (var plane in cut)
            {
                double v = plane.At(p);
                if (v < m1)
                {
                    m2 = m1;
                    m1 = v;
                    face = plane.Normal;
                }
                else if (v < m2)
                {
                    m2 = v;
                }
            }

            if (m1 > top)
            {
                second = top;
                top = m1;
                normal = face;
                inner = m2 - m1;
            }
            else if (m1 > second)
            {
                second = m1;
            }
        }

        clear = Math.Min(top - second, inner);
        return normal;
    }

    /// <summary>The largest of some cuts' fields at a point (each the smallest of its planes' values); -∞ with no cuts.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double Cut(Plane[][] cuts, V3 p)
    {
        double f = double.NegativeInfinity;
        foreach (var cut in cuts)
        {
            double m = double.PositiveInfinity;
            foreach (var plane in cut)
            {
                m = Math.Min(m, plane.At(p));
            }

            f = Math.Max(f, m);
        }

        return f;
    }

    /// <summary>The largest of some planes' values at a point.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double Side(Plane[] planes, V3 p)
    {
        double f = double.NegativeInfinity;
        foreach (var plane in planes)
        {
            f = Math.Max(f, plane.At(p));
        }

        return f;
    }

    /// <summary>
    /// A joint between a block and the block it rests on: their indices, both blocks' side planes, the planes that bound
    /// the filler above the plane (the upper block's upward-facing planes past its sides, its flat bottom let down by
    /// the joint's reach, and the lower block's downward planes) and below it (the lower block's planes past its sides and top), and both blocks' cuts.
    /// </summary>
    private sealed record Joint(int Upper, int Lower, Plane[] Sides, Plane[] Above, Plane[] Below, Plane[][] Cuts);
}
