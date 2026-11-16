// Ported from three.js r186 src/math/Quaternion.js (MIT, © 2010-2026 three.js authors).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Kernel;

/// <summary>
/// three's Quaternion in doubles: a mutable object whose methods change it and return it. Only the methods easy-props
/// calls are here.
/// </summary>
public sealed class Quatd
{
    /// <summary>A quaternion; the default is the identity rotation (0, 0, 0, 1).</summary>
    public Quatd(double x = 0, double y = 0, double z = 0, double w = 1)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>The x part.</summary>
    public double X { get; set; }

    /// <summary>The y part.</summary>
    public double Y { get; set; }

    /// <summary>The z part.</summary>
    public double Z { get; set; }

    /// <summary>The w (scalar) part.</summary>
    public double W { get; set; }

    /// <summary>Becomes the rotation of an Euler angle.</summary>
    public Quatd SetFromEuler(Euler euler)
    {
        double x = euler.X, y = euler.Y, z = euler.Z;
        EulerOrder order = euler.Order;

        double c1 = JsMath.Cos(x / 2);
        double c2 = JsMath.Cos(y / 2);
        double c3 = JsMath.Cos(z / 2);

        double s1 = JsMath.Sin(x / 2);
        double s2 = JsMath.Sin(y / 2);
        double s3 = JsMath.Sin(z / 2);

        switch (order)
        {
            case EulerOrder.XYZ:
                X = (s1 * c2 * c3) + (c1 * s2 * s3);
                Y = (c1 * s2 * c3) - (s1 * c2 * s3);
                Z = (c1 * c2 * s3) + (s1 * s2 * c3);
                W = (c1 * c2 * c3) - (s1 * s2 * s3);
                break;

            case EulerOrder.YXZ:
                X = (s1 * c2 * c3) + (c1 * s2 * s3);
                Y = (c1 * s2 * c3) - (s1 * c2 * s3);
                Z = (c1 * c2 * s3) - (s1 * s2 * c3);
                W = (c1 * c2 * c3) + (s1 * s2 * s3);
                break;

            case EulerOrder.ZYX:
                X = (s1 * c2 * c3) - (c1 * s2 * s3);
                Y = (c1 * s2 * c3) + (s1 * c2 * s3);
                Z = (c1 * c2 * s3) - (s1 * s2 * c3);
                W = (c1 * c2 * c3) + (s1 * s2 * s3);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(euler), order, "Not an order easy-props uses.");
        }

        return this;
    }

    /// <summary>Becomes a rotation of <paramref name="angle"/> radians about a unit <paramref name="axis"/>.</summary>
    public Quatd SetFromAxisAngle(Vec3d axis, double angle)
    {
        double halfAngle = angle / 2, s = JsMath.Sin(halfAngle);

        X = axis.X * s;
        Y = axis.Y * s;
        Z = axis.Z * s;
        W = JsMath.Cos(halfAngle);
        return this;
    }

    /// <summary>Becomes the shortest rotation from one unit vector to another.</summary>
    public Quatd SetFromUnitVectors(Vec3d vFrom, Vec3d vTo)
    {
        double r = vFrom.Dot(vTo) + 1;

        if (r < 1e-8)
        {
            // vFrom and vTo point in opposite directions.
            r = 0;

            if (Math.Abs(vFrom.X) > Math.Abs(vFrom.Z))
            {
                X = -vFrom.Y;
                Y = vFrom.X;
                Z = 0;
                W = r;
            }
            else
            {
                X = 0;
                Y = -vFrom.Z;
                Z = vFrom.Y;
                W = r;
            }
        }
        else
        {
            // crossVectors(vFrom, vTo), inlined as three has it.
            X = (vFrom.Y * vTo.Z) - (vFrom.Z * vTo.Y);
            Y = (vFrom.Z * vTo.X) - (vFrom.X * vTo.Z);
            Z = (vFrom.X * vTo.Y) - (vFrom.Y * vTo.X);
            W = r;
        }

        return Normalize();
    }

    /// <summary>The length.</summary>
    public double Length() => JsMath.Sqrt((X * X) + (Y * Y) + (Z * Z) + (W * W));

    /// <summary>Scales to length 1; a zero quaternion becomes the identity.</summary>
    public Quatd Normalize()
    {
        double l = Length();

        if (l == 0)
        {
            X = 0;
            Y = 0;
            Z = 0;
            W = 1;
        }
        else
        {
            l = 1 / l;

            X *= l;
            Y *= l;
            Z *= l;
            W *= l;
        }

        return this;
    }

    /// <summary>Becomes itself times <paramref name="q"/>.</summary>
    public Quatd Multiply(Quatd q) => MultiplyQuaternions(this, q);

    /// <summary>Becomes <paramref name="a"/> times <paramref name="b"/> (either may be this quaternion).</summary>
    public Quatd MultiplyQuaternions(Quatd a, Quatd b)
    {
        double qax = a.X, qay = a.Y, qaz = a.Z, qaw = a.W;
        double qbx = b.X, qby = b.Y, qbz = b.Z, qbw = b.W;

        X = (qax * qbw) + (qaw * qbx) + (qay * qbz) - (qaz * qby);
        Y = (qay * qbw) + (qaw * qby) + (qaz * qbx) - (qax * qbz);
        Z = (qaz * qbw) + (qaw * qbz) + (qax * qby) - (qay * qbx);
        W = (qaw * qbw) - (qax * qbx) - (qay * qby) - (qaz * qbz);
        return this;
    }
}
