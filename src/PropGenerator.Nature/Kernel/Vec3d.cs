// Ported from three.js r186 src/math/Vector3.js (MIT, © 2010-2026 three.js authors).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Kernel;

/// <summary>
/// three's Vector3 in doubles: a mutable object whose methods change it and return it, so calls chain and aliasing
/// behaves as in three (<c>a.Cross(b)</c> reads <c>a</c> before writing it). Each method does three's arithmetic in
/// three's order. Only the methods easy-props calls are here.
/// </summary>
public sealed class Vec3d
{
    /// <summary>The zero vector.</summary>
    public Vec3d()
    {
    }

    /// <summary>A vector with these components.</summary>
    public Vec3d(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>The x component.</summary>
    public double X { get; set; }

    /// <summary>The y component (up).</summary>
    public double Y { get; set; }

    /// <summary>The z component.</summary>
    public double Z { get; set; }

    /// <summary>Sets all three components.</summary>
    public Vec3d Set(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
        return this;
    }

    /// <summary>A new vector with the same components.</summary>
    public Vec3d Clone() => new(X, Y, Z);

    /// <summary>Takes the components of <paramref name="v"/>.</summary>
    public Vec3d Copy(Vec3d v)
    {
        X = v.X;
        Y = v.Y;
        Z = v.Z;
        return this;
    }

    /// <summary>Adds <paramref name="v"/>.</summary>
    public Vec3d Add(Vec3d v)
    {
        X += v.X;
        Y += v.Y;
        Z += v.Z;
        return this;
    }

    /// <summary>Adds <paramref name="v"/> times <paramref name="s"/>.</summary>
    public Vec3d AddScaledVector(Vec3d v, double s)
    {
        X += v.X * s;
        Y += v.Y * s;
        Z += v.Z * s;
        return this;
    }

    /// <summary>Subtracts <paramref name="v"/>.</summary>
    public Vec3d Sub(Vec3d v)
    {
        X -= v.X;
        Y -= v.Y;
        Z -= v.Z;
        return this;
    }

    /// <summary>Becomes <paramref name="a"/> minus <paramref name="b"/>.</summary>
    public Vec3d SubVectors(Vec3d a, Vec3d b)
    {
        X = a.X - b.X;
        Y = a.Y - b.Y;
        Z = a.Z - b.Z;
        return this;
    }

    /// <summary>Multiplies every component by <paramref name="scalar"/>.</summary>
    public Vec3d MultiplyScalar(double scalar)
    {
        X *= scalar;
        Y *= scalar;
        Z *= scalar;
        return this;
    }

    /// <summary>Divides by <paramref name="scalar"/> as three does: multiplies by its reciprocal.</summary>
    public Vec3d DivideScalar(double scalar) => MultiplyScalar(1 / scalar);

    /// <summary>Rotates by an Euler angle, through a quaternion.</summary>
    public Vec3d ApplyEuler(Euler euler) => ApplyQuaternion(new Quatd().SetFromEuler(euler));

    /// <summary>Rotates by <paramref name="angle"/> radians about a unit <paramref name="axis"/>, through a quaternion.</summary>
    public Vec3d ApplyAxisAngle(Vec3d axis, double angle) => ApplyQuaternion(new Quatd().SetFromAxisAngle(axis, angle));

    /// <summary>Multiplies by a 3×3 matrix.</summary>
    public Vec3d ApplyMatrix3(Mat3d m)
    {
        double x = X, y = Y, z = Z;
        double[] e = m.Elements;

        X = (e[0] * x) + (e[3] * y) + (e[6] * z);
        Y = (e[1] * x) + (e[4] * y) + (e[7] * z);
        Z = (e[2] * x) + (e[5] * y) + (e[8] * z);
        return this;
    }

    /// <summary>Multiplies by a normal matrix, then normalises.</summary>
    public Vec3d ApplyNormalMatrix(Mat3d m) => ApplyMatrix3(m).Normalize();

    /// <summary>Multiplies by a 4×4 matrix as a point (w = 1), dividing by the resulting w.</summary>
    public Vec3d ApplyMatrix4(Mat4d m)
    {
        double x = X, y = Y, z = Z;
        double[] e = m.Elements;

        double w = 1 / ((e[3] * x) + (e[7] * y) + (e[11] * z) + e[15]);

        X = ((e[0] * x) + (e[4] * y) + (e[8] * z) + e[12]) * w;
        Y = ((e[1] * x) + (e[5] * y) + (e[9] * z) + e[13]) * w;
        Z = ((e[2] * x) + (e[6] * y) + (e[10] * z) + e[14]) * w;
        return this;
    }

    /// <summary>Rotates by a unit quaternion.</summary>
    public Vec3d ApplyQuaternion(Quatd q)
    {
        double vx = X, vy = Y, vz = Z;
        double qx = q.X, qy = q.Y, qz = q.Z, qw = q.W;

        // t = 2 * cross(q.xyz, v)
        double tx = 2 * ((qy * vz) - (qz * vy));
        double ty = 2 * ((qz * vx) - (qx * vz));
        double tz = 2 * ((qx * vy) - (qy * vx));

        // v + q.w * t + cross(q.xyz, t)
        X = vx + (qw * tx) + (qy * tz) - (qz * ty);
        Y = vy + (qw * ty) + (qz * tx) - (qx * tz);
        Z = vz + (qw * tz) + (qx * ty) - (qy * tx);
        return this;
    }

    /// <summary>Takes the smaller of each component and <paramref name="v"/>'s (NaN wins, -0 is below +0).</summary>
    public Vec3d Min(Vec3d v)
    {
        X = Math.Min(X, v.X);
        Y = Math.Min(Y, v.Y);
        Z = Math.Min(Z, v.Z);
        return this;
    }

    /// <summary>Takes the larger of each component and <paramref name="v"/>'s (NaN wins, +0 is above -0).</summary>
    public Vec3d Max(Vec3d v)
    {
        X = Math.Max(X, v.X);
        Y = Math.Max(Y, v.Y);
        Z = Math.Max(Z, v.Z);
        return this;
    }

    /// <summary>Flips the sign of every component.</summary>
    public Vec3d Negate()
    {
        X = -X;
        Y = -Y;
        Z = -Z;
        return this;
    }

    /// <summary>The dot product with <paramref name="v"/>.</summary>
    public double Dot(Vec3d v) => (X * v.X) + (Y * v.Y) + (Z * v.Z);

    /// <summary>The squared length.</summary>
    public double LengthSq() => (X * X) + (Y * Y) + (Z * Z);

    /// <summary>The length.</summary>
    public double Length() => JsMath.Sqrt((X * X) + (Y * Y) + (Z * Z));

    /// <summary>Scales to length 1. A zero (or NaN-length) vector is divided by 1, so zero stays zero.</summary>
    public Vec3d Normalize()
    {
        double length = Length();

        // JavaScript's `length || 1`: 0 and NaN are falsy.
        return DivideScalar(length == 0 || double.IsNaN(length) ? 1 : length);
    }

    /// <summary>Moves toward <paramref name="v"/> by <paramref name="alpha"/>.</summary>
    public Vec3d Lerp(Vec3d v, double alpha)
    {
        X += (v.X - X) * alpha;
        Y += (v.Y - Y) * alpha;
        Z += (v.Z - Z) * alpha;
        return this;
    }

    /// <summary>Becomes the cross product of itself and <paramref name="v"/>.</summary>
    public Vec3d Cross(Vec3d v) => CrossVectors(this, v);

    /// <summary>Becomes the cross product of <paramref name="a"/> and <paramref name="b"/> (either may be this vector).</summary>
    public Vec3d CrossVectors(Vec3d a, Vec3d b)
    {
        double ax = a.X, ay = a.Y, az = a.Z;
        double bx = b.X, by = b.Y, bz = b.Z;

        X = (ay * bz) - (az * by);
        Y = (az * bx) - (ax * bz);
        Z = (ax * by) - (ay * bx);
        return this;
    }

    /// <summary>The distance to <paramref name="v"/>.</summary>
    public double DistanceTo(Vec3d v) => JsMath.Sqrt(DistanceToSquared(v));

    /// <summary>The squared distance to <paramref name="v"/>.</summary>
    public double DistanceToSquared(Vec3d v)
    {
        double dx = X - v.X, dy = Y - v.Y, dz = Z - v.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    /// <summary>Reads vertex <paramref name="index"/> of a three-component float attribute.</summary>
    public Vec3d FromBufferAttribute(float[] attribute, int index)
    {
        X = attribute[index * 3];
        Y = attribute[(index * 3) + 1];
        Z = attribute[(index * 3) + 2];
        return this;
    }

    /// <summary>The components as a new array [x, y, z].</summary>
    public double[] ToArray() => [X, Y, Z];
}
