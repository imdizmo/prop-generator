// Ported from easy-props frontend/src/assets/rocks/rock-plan.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Rocks;

/// <summary>
/// A point or direction of the rock plan: rock-plan.ts' <c>Vec3</c>, a fixed triple of doubles, with its helpers
/// <c>dot</c>, <c>normalize</c> and <c>cross</c> computed in the same order as easy-props.
/// </summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    /// <summary>One axis: 0 is X, 1 is Y, 2 is Z.</summary>
    public double this[int axis] => axis switch
    {
        0 => X,
        1 => Y,
        2 => Z,
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "An axis is 0, 1 or 2"),
    };

    /// <summary>The dot product, added left to right.</summary>
    public static double Dot(Vec3 a, Vec3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    /// <summary>The cross product.</summary>
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        (a.Y * b.Z) - (a.Z * b.Y),
        (a.Z * b.X) - (a.X * b.Z),
        (a.X * b.Y) - (a.Y * b.X));

    /// <summary>The vector at length 1. A zero (or NaN) length divides by 1, so a zero vector stays zero.</summary>
    public static Vec3 Normalize(Vec3 a)
    {
        double length = JsMath.Hypot(a.X, a.Y, a.Z);

        // JavaScript's `|| 1`: 0 and NaN are both falsy.
        if (length == 0 || double.IsNaN(length))
        {
            length = 1;
        }

        return new Vec3(a.X / length, a.Y / length, a.Z / length);
    }
}
