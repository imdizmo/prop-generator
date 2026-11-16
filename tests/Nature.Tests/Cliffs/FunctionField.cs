using PropGenerator.Nature.Cliffs;

namespace PropGenerator.Nature.Tests.Cliffs;

/// <summary>A solid field from a function, counting how often it is evaluated; its estimate is the field unless given.</summary>
/// <param name="distance">The field.</param>
/// <param name="estimate">A cheap estimate and how far the field can be from it, or null to estimate with the field.</param>
internal sealed class FunctionField(Func<V3, double> distance, Func<V3, (double Estimate, double Error)>? estimate = null) : ISolidField
{
    private int _distances;

    /// <summary>Gets how often the field has been evaluated.</summary>
    public int Distances => _distances;

    /// <inheritdoc/>
    public double Distance(V3 p)
    {
        Interlocked.Increment(ref _distances);
        return distance(p);
    }

    /// <inheritdoc/>
    public double Estimate(V3 p, out double error)
    {
        if (estimate is null)
        {
            error = 0;
            return Distance(p);
        }

        (double value, error) = estimate(p);
        return value;
    }
}
