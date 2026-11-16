// Ported from three.js r186 src/math/Euler.js (MIT, © 2010-2026 three.js authors).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Kernel;

/// <summary>
/// three's Euler: three angles in radians and the order they apply in. A mutable object. Only the methods easy-props
/// calls are here.
/// </summary>
public sealed class Euler
{
    /// <summary>The order a new Euler gets when none is given, as three's Euler.DEFAULT_ORDER.</summary>
    public const EulerOrder DefaultOrder = EulerOrder.XYZ;

    /// <summary>An Euler angle; the order defaults to XYZ.</summary>
    public Euler(double x = 0, double y = 0, double z = 0, EulerOrder order = DefaultOrder)
    {
        X = x;
        Y = y;
        Z = z;
        Order = order;
    }

    /// <summary>The angle about x, in radians.</summary>
    public double X { get; set; }

    /// <summary>The angle about y, in radians.</summary>
    public double Y { get; set; }

    /// <summary>The angle about z, in radians.</summary>
    public double Z { get; set; }

    /// <summary>The order the rotations apply in.</summary>
    public EulerOrder Order { get; set; }

    /// <summary>
    /// Becomes the angles of a pure rotation matrix, in <paramref name="order"/> (its own order when null), which it
    /// then keeps.
    /// </summary>
    public Euler SetFromRotationMatrix(Mat4d m, EulerOrder? order = null)
    {
        EulerOrder use = order ?? Order;
        double[] te = m.Elements;
        double m11 = te[0], m12 = te[4], m13 = te[8];
        double m21 = te[1], m22 = te[5], m23 = te[9];
        double m31 = te[2], m32 = te[6], m33 = te[10];

        switch (use)
        {
            case EulerOrder.XYZ:
                Y = JsMath.Asin(Clamp(m13, -1, 1));

                if (Math.Abs(m13) < 0.9999999)
                {
                    X = JsMath.Atan2(-m23, m33);
                    Z = JsMath.Atan2(-m12, m11);
                }
                else
                {
                    X = JsMath.Atan2(m32, m22);
                    Z = 0;
                }

                break;

            case EulerOrder.YXZ:
                X = JsMath.Asin(-Clamp(m23, -1, 1));

                if (Math.Abs(m23) < 0.9999999)
                {
                    Y = JsMath.Atan2(m13, m33);
                    Z = JsMath.Atan2(m21, m22);
                }
                else
                {
                    Y = JsMath.Atan2(-m31, m11);
                    Z = 0;
                }

                break;

            case EulerOrder.ZYX:
                Y = JsMath.Asin(-Clamp(m31, -1, 1));

                if (Math.Abs(m31) < 0.9999999)
                {
                    X = JsMath.Atan2(m32, m33);
                    Z = JsMath.Atan2(m21, m11);
                }
                else
                {
                    X = 0;
                    Z = JsMath.Atan2(-m12, m22);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(order), use, "Not an order easy-props uses.");
        }

        Order = use;
        return this;
    }

    /// <summary>Becomes the angles of a unit quaternion, in <paramref name="order"/> (its own order when null).</summary>
    public Euler SetFromQuaternion(Quatd q, EulerOrder? order = null) =>
        SetFromRotationMatrix(new Mat4d().MakeRotationFromQuaternion(q), order);

    /// <summary>three's MathUtils.clamp: Math.max(min, Math.min(max, value)), NaN and signed zeros as JavaScript has them.</summary>
    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
}
