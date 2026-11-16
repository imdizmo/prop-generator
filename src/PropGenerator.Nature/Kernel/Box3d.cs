// Ported from three.js r186 src/math/Box3.js (MIT, © 2010-2026 three.js authors).
namespace PropGenerator.Nature.Kernel;

/// <summary>
/// three's Box3: an axis-aligned box in doubles. Built from a mesh's float32 positions, read as doubles, as three's
/// computeBoundingBox does. Only the methods easy-props calls are here.
/// </summary>
public sealed class Box3d
{
    /// <summary>The empty box: min at +infinity, max at -infinity.</summary>
    public Box3d()
    {
    }

    /// <summary>The low corner.</summary>
    public Vec3d Min { get; } = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);

    /// <summary>The high corner.</summary>
    public Vec3d Max { get; } = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);

    /// <summary>Becomes the box around every vertex of a three-component float attribute.</summary>
    public Box3d SetFromBufferAttribute(float[] attribute)
    {
        MakeEmpty();

        var vector = new Vec3d();
        for (int i = 0, il = attribute.Length / 3; i < il; i++)
        {
            ExpandByPoint(vector.FromBufferAttribute(attribute, i));
        }

        return this;
    }

    /// <summary>Becomes empty.</summary>
    public Box3d MakeEmpty()
    {
        Min.Set(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        Max.Set(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        return this;
    }

    /// <summary>True when max is below min on any axis.</summary>
    public bool IsEmpty() => Max.X < Min.X || Max.Y < Min.Y || Max.Z < Min.Z;

    /// <summary>Writes the size into <paramref name="target"/> (zero for an empty box) and returns it.</summary>
    public Vec3d GetSize(Vec3d target) => IsEmpty() ? target.Set(0, 0, 0) : target.SubVectors(Max, Min);

    /// <summary>Grows to hold <paramref name="point"/>.</summary>
    public Box3d ExpandByPoint(Vec3d point)
    {
        Min.Min(point);
        Max.Max(point);
        return this;
    }
}
