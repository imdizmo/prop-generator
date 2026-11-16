namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// A part of a cliff that is meshed on its own, in cliff space (Z up, the ground line at z = 0).
/// </summary>
internal interface ICliffPart : ISolidField
{
    /// <summary>How far the surface was cut in at a point on it, in metres, for darkening its colour.</summary>
    /// <param name="p">A point of the part's surface.</param>
    double Tag(V3 p);

    /// <summary>The part's bounds: everything of it lies inside them. Null when nothing is left of it.</summary>
    (V3 Min, V3 Max)? Bounds();
}
