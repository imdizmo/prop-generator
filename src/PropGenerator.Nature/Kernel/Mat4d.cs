// Ported from three.js r186 src/math/Matrix4.js (MIT, © 2010-2026 three.js authors).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Kernel;

/// <summary>
/// three's Matrix4 in doubles: 16 elements in column-major order (element 12, 13, 14 is the translation). A mutable
/// object whose methods change it and return it. Only the methods easy-props calls are here.
/// </summary>
public sealed class Mat4d
{
    /// <summary>The identity matrix.</summary>
    public Mat4d()
    {
    }

    /// <summary>The 16 elements, column by column. Starts as the identity.</summary>
    public double[] Elements { get; } = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    /// <summary>Sets the elements from row-major arguments (n12 is row 1, column 2).</summary>
    public Mat4d Set(
        double n11, double n12, double n13, double n14,
        double n21, double n22, double n23, double n24,
        double n31, double n32, double n33, double n34,
        double n41, double n42, double n43, double n44)
    {
        double[] te = Elements;

        te[0] = n11; te[4] = n12; te[8] = n13; te[12] = n14;
        te[1] = n21; te[5] = n22; te[9] = n23; te[13] = n24;
        te[2] = n31; te[6] = n32; te[10] = n33; te[14] = n34;
        te[3] = n41; te[7] = n42; te[11] = n43; te[15] = n44;
        return this;
    }

    /// <summary>Becomes a turn of <paramref name="angle"/> radians about a unit <paramref name="axis"/> (three's makeRotationAxis).</summary>
    public Mat4d MakeRotationAxis(Vec3d axis, double angle)
    {
        ArgumentNullException.ThrowIfNull(axis);
        double c = JsMath.Cos(angle);
        double s = JsMath.Sin(angle);
        double t = 1 - c;
        double x = axis.X, y = axis.Y, z = axis.Z;
        double tx = t * x, ty = t * y;

        return Set(
            (tx * x) + c, (tx * y) - (s * z), (tx * z) + (s * y), 0,
            (tx * y) + (s * z), (ty * y) + c, (ty * z) - (s * x), 0,
            (tx * z) - (s * y), (ty * z) + (s * x), (t * z * z) + c, 0,
            0, 0, 0, 1);
    }

    /// <summary>Becomes a translation.</summary>
    public Mat4d MakeTranslation(double x, double y, double z) => Set(
        1, 0, 0, x,
        0, 1, 0, y,
        0, 0, 1, z,
        0, 0, 0, 1);

    /// <summary>Becomes a scale along the axes.</summary>
    public Mat4d MakeScale(double x, double y, double z) => Set(
        x, 0, 0, 0,
        0, y, 0, 0,
        0, 0, z, 0,
        0, 0, 0, 1);

    /// <summary>Becomes the rotation of a quaternion (compose with no translation and a scale of 1).</summary>
    public Mat4d MakeRotationFromQuaternion(Quatd q) => Compose(new Vec3d(0, 0, 0), q, new Vec3d(1, 1, 1));

    /// <summary>Becomes scale, then rotation, then translation.</summary>
    public Mat4d Compose(Vec3d position, Quatd quaternion, Vec3d scale)
    {
        double[] te = Elements;

        double x = quaternion.X, y = quaternion.Y, z = quaternion.Z, w = quaternion.W;
        double x2 = x + x, y2 = y + y, z2 = z + z;
        double xx = x * x2, xy = x * y2, xz = x * z2;
        double yy = y * y2, yz = y * z2, zz = z * z2;
        double wx = w * x2, wy = w * y2, wz = w * z2;

        double sx = scale.X, sy = scale.Y, sz = scale.Z;

        te[0] = (1 - (yy + zz)) * sx;
        te[1] = (xy + wz) * sx;
        te[2] = (xz - wy) * sx;
        te[3] = 0;

        te[4] = (xy - wz) * sy;
        te[5] = (1 - (xx + zz)) * sy;
        te[6] = (yz + wx) * sy;
        te[7] = 0;

        te[8] = (xz + wy) * sz;
        te[9] = (yz - wx) * sz;
        te[10] = (1 - (xx + yy)) * sz;
        te[11] = 0;

        te[12] = position.X;
        te[13] = position.Y;
        te[14] = position.Z;
        te[15] = 1;
        return this;
    }

    /// <summary>Reads 16 elements, column-major, from <paramref name="offset"/> on.</summary>
    public Mat4d FromArray(IReadOnlyList<double> array, int offset = 0)
    {
        for (int i = 0; i < 16; i++)
        {
            Elements[i] = array[i + offset];
        }

        return this;
    }

    /// <summary>The 16 elements, column-major, as a new array.</summary>
    public double[] ToArray() => (double[])Elements.Clone();
}
