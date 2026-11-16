// Ported from easy-props frontend/src/core/scene/turning.ts (turnedAbout, turnOf) (MIT, © 2026 AHS12).
using System.Collections;
using PropGenerator.Nature.Js;
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Desktop.Scenes;

/// <summary>
/// Turning a branch or root of a painted tree: its turn is three angles in degrees, applied by the plan about X, then
/// Y, then Z, each about the world's axes (three.js's ZYX order).
/// </summary>
public static class PartTurning
{
    private const double DegToRad = Math.PI / 180;

    private const double RadToDeg = 180 / Math.PI;

    /// <summary>
    /// A turn, turned further about an axis in the world (easy-props' <c>turnedAbout</c>). Composing as quaternions turns
    /// about the axis as seen, whatever the part was turned by before. Each angle is rounded to a tenth of a degree.
    /// </summary>
    /// <param name="turn">The turn now, in degrees about X, Y and Z.</param>
    /// <param name="axis">The axis, in the tree's Y-up space; need not be of length one.</param>
    /// <param name="radians">How far to turn about it.</param>
    /// <returns>The new turn, in degrees.</returns>
    public static double[] TurnedAbout(IReadOnlyList<double> turn, Vec3d axis, double radians)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(axis);
        double x = turn[0] * DegToRad, y = turn[1] * DegToRad, z = turn[2] * DegToRad;
        Quatd start = new Quatd().SetFromEuler(new Euler(x, y, z, EulerOrder.ZYX));
        Quatd by = new Quatd().SetFromAxisAngle(new Vec3d(axis.X, axis.Y, axis.Z).Normalize(), radians);
        Euler euler = new Euler().SetFromQuaternion(by.Multiply(start), EulerOrder.ZYX);

        // A tenth of a degree is finer than anyone can drag, and keeps Details readable; + 0 turns -0 into 0.
        static double Degrees(double angle) => (JsMath.Round(angle * RadToDeg * 10) / 10) + 0.0;

        return [Degrees(euler.X), Degrees(euler.Y), Degrees(euler.Z)];
    }

    /// <summary>A part's turn, in degrees about X, Y and Z (easy-props' <c>turnOf</c>); 0, 0, 0 when it has none.</summary>
    /// <param name="part">The part, or null.</param>
    /// <returns>Its turn.</returns>
    public static double[] TurnOf(SceneComponent? part)
    {
        if (part is not null && part.Parameters.TryGetValue("turn", out object? value) && value is IList { Count: 3 } turn)
        {
            return [.. turn.Cast<object>().Select(angle => angle is double d ? d : double.NaN)];
        }

        return [0, 0, 0];
    }
}
