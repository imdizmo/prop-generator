// Ported from three.js r186 src/math/Matrix3.js (MIT, © 2010-2026 three.js authors).
namespace PropGenerator.Nature.Kernel;

/// <summary>
/// three's Matrix3 in doubles: 9 elements in column-major order. Here for the normal matrix that transforms normals
/// when a mesh is transformed. Only the methods easy-props reaches are here.
/// </summary>
public sealed class Mat3d
{
    /// <summary>The identity matrix.</summary>
    public Mat3d()
    {
    }

    /// <summary>The 9 elements, column by column. Starts as the identity.</summary>
    public double[] Elements { get; } = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    /// <summary>Sets the elements from row-major arguments (n12 is row 1, column 2).</summary>
    public Mat3d Set(double n11, double n12, double n13, double n21, double n22, double n23, double n31, double n32, double n33)
    {
        double[] te = Elements;

        te[0] = n11; te[1] = n21; te[2] = n31;
        te[3] = n12; te[4] = n22; te[5] = n32;
        te[6] = n13; te[7] = n23; te[8] = n33;
        return this;
    }

    /// <summary>Becomes the upper-left 3×3 of <paramref name="m"/>.</summary>
    public Mat3d SetFromMatrix4(Mat4d m)
    {
        double[] me = m.Elements;
        return Set(
            me[0], me[4], me[8],
            me[1], me[5], me[9],
            me[2], me[6], me[10]);
    }

    /// <summary>Becomes its inverse; a singular matrix becomes all zeros.</summary>
    public Mat3d Invert()
    {
        double[] te = Elements;
        double n11 = te[0], n21 = te[1], n31 = te[2];
        double n12 = te[3], n22 = te[4], n32 = te[5];
        double n13 = te[6], n23 = te[7], n33 = te[8];

        double t11 = (n33 * n22) - (n32 * n23);
        double t12 = (n32 * n13) - (n33 * n12);
        double t13 = (n23 * n12) - (n22 * n13);

        double det = (n11 * t11) + (n21 * t12) + (n31 * t13);

        if (det == 0)
        {
            return Set(0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        double detInv = 1 / det;

        te[0] = t11 * detInv;
        te[1] = ((n31 * n23) - (n33 * n21)) * detInv;
        te[2] = ((n32 * n21) - (n31 * n22)) * detInv;

        te[3] = t12 * detInv;
        te[4] = ((n33 * n11) - (n31 * n13)) * detInv;
        te[5] = ((n31 * n12) - (n32 * n11)) * detInv;

        te[6] = t13 * detInv;
        te[7] = ((n21 * n13) - (n23 * n11)) * detInv;
        te[8] = ((n22 * n11) - (n21 * n12)) * detInv;
        return this;
    }

    /// <summary>Swaps rows and columns.</summary>
    public Mat3d Transpose()
    {
        double[] m = Elements;
        (m[1], m[3]) = (m[3], m[1]);
        (m[2], m[6]) = (m[6], m[2]);
        (m[5], m[7]) = (m[7], m[5]);
        return this;
    }

    /// <summary>Becomes the normal matrix of <paramref name="matrix4"/>: the inverse transpose of its upper-left 3×3.</summary>
    public Mat3d GetNormalMatrix(Mat4d matrix4) => SetFromMatrix4(matrix4).Invert().Transpose();
}
