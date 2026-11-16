namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// A point or direction of the slab cliff's geometry: three doubles, with the arithmetic its fields and meshes need.
/// Cliff space is Z-up: x across, y front to back, z up. Unlike the ported kernel's vectors it follows no reference's
/// order of operations.
/// </summary>
/// <param name="X">The x component.</param>
/// <param name="Y">The y component.</param>
/// <param name="Z">The z component.</param>
public readonly record struct V3(double X, double Y, double Z)
{
    /// <summary>The vector's length.</summary>
    public double Length => Math.Sqrt(Dot(this, this));

    /// <summary>The sum, component by component.</summary>
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>The difference, component by component.</summary>
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>The vector pointing the other way.</summary>
    public static V3 operator -(V3 a) => new(-a.X, -a.Y, -a.Z);

    /// <summary>The vector scaled by <paramref name="s"/>.</summary>
    public static V3 operator *(V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    /// <summary>The vector scaled by <paramref name="s"/>.</summary>
    public static V3 operator *(double s, V3 a) => a * s;

    /// <summary>The vector divided by <paramref name="s"/>.</summary>
    public static V3 operator /(V3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    /// <summary>The dot product.</summary>
    public static double Dot(V3 a, V3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    /// <summary>The cross product (right-handed: x × y = z).</summary>
    public static V3 Cross(V3 a, V3 b) => new(
        (a.Y * b.Z) - (a.Z * b.Y),
        (a.Z * b.X) - (a.X * b.Z),
        (a.X * b.Y) - (a.Y * b.X));

    /// <summary>The vector at length 1; the zero vector stays zero.</summary>
    public V3 Normalized()
    {
        double length = Length;
        return length == 0 ? this : this / length;
    }
}
