namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// A plane of the slab formula: the points where the normal's dot product with the point equals the offset. It is
/// positive on the side the normal points to.
/// </summary>
/// <param name="Normal">The unit normal.</param>
/// <param name="Offset">The plane's distance from the origin along the normal, in metres.</param>
public readonly record struct Plane(V3 Normal, double Offset)
{
    /// <summary>How far a point is from the plane along the normal: positive in front of it, negative behind.</summary>
    /// <param name="p">The point.</param>
    public double At(V3 p) => V3.Dot(Normal, p) - Offset;
}
