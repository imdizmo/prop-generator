// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Numerics;

namespace PropGenerator.Desktop.View;

/// <summary>
/// The view's sloping ground, as easy-props' applySlope: the ground disc and its grids turn about the prop's base (the
/// origin) so their up points at <see cref="Up"/>. Only the view: the prop, the floor tile, the collision shapes and
/// part drags stay level, and nothing reaches the scene, the work file or an export.
/// </summary>
internal static class GroundTilt
{
    /// <summary>
    /// The one fixed angle between upstream's downhill 0 and ours, in degrees. Upstream's 0 falls towards three.js's +Z,
    /// where its Front camera stands; turned Z-up as every built part is ((x, y, z) to (x, −z, y)) that is our −Y, where
    /// our Front camera stands. So "0 falls towards the front" holds with no offset.
    /// </summary>
    internal const double FrontOffsetDegrees = 0;

    /// <summary>
    /// The tilted ground's up, Z-up: upstream's (sin f · sin d, cos f, sin f · cos d) in three.js's Y-up, turned to our
    /// axes as (sin f · sin d, −sin f · cos d, cos f), with d measured from our front.
    /// </summary>
    /// <param name="slopeDeg">How steep the ground is, in degrees (0 is level).</param>
    /// <param name="downhillDeg">Which way it falls, in degrees around the prop (0 towards the front, 90 to the right).</param>
    /// <returns>A unit vector.</returns>
    internal static Vector3 Up(double slopeDeg, double downhillDeg)
    {
        double f = slopeDeg * Math.PI / 180;
        double d = (downhillDeg + FrontOffsetDegrees) * Math.PI / 180;
        return Vector3.Normalize(new Vector3((float)(Math.Sin(f) * Math.Sin(d)), (float)(-Math.Sin(f) * Math.Cos(d)), (float)Math.Cos(f)));
    }

    /// <summary>
    /// The turn that takes the level ground's up (Z) onto <see cref="Up"/> by the shortest arc, as three.js's
    /// <c>quaternion.setFromUnitVectors</c>, about the origin. The identity on level ground. Row vectors, as
    /// System.Numerics multiplies (uploaded untransposed, as the view's other matrices are).
    /// </summary>
    /// <param name="slopeDeg">How steep the ground is, in degrees.</param>
    /// <param name="downhillDeg">Which way it falls, in degrees around the prop.</param>
    /// <returns>The model matrix of the ground disc and its grids.</returns>
    internal static Matrix4x4 Matrix(double slopeDeg, double downhillDeg)
    {
        if (slopeDeg == 0)
        {
            return Matrix4x4.Identity;
        }

        Vector3 up = Up(slopeDeg, downhillDeg);
        var axis = Vector3.Cross(Vector3.UnitZ, up);
        float sin = axis.Length();
        if (sin < 1e-7f)
        {
            return Matrix4x4.Identity;
        }

        float angle = MathF.Atan2(sin, Vector3.Dot(Vector3.UnitZ, up));
        return Matrix4x4.CreateFromAxisAngle(axis / sin, angle);
    }
}
