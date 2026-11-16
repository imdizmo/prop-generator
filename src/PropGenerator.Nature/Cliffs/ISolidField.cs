namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// A solid given by a signed distance: negative inside, positive outside, in metres. Only where it is zero must be
/// exact; elsewhere it need only keep its sign and grow roughly as a distance does.
/// </summary>
internal interface ISolidField
{
    /// <summary>The field at a point.</summary>
    /// <param name="p">The point.</param>
    double Distance(V3 p);

    /// <summary>
    /// A cheap estimate of <see cref="Distance"/> at a point, and how far the field can be from it there: where the
    /// estimate is further from zero than that, the field has the estimate's sign without being evaluated.
    /// </summary>
    /// <param name="p">The point.</param>
    /// <param name="error">How far <see cref="Distance"/> can be from the estimate, 0 or more.</param>
    double Estimate(V3 p, out double error);
}
