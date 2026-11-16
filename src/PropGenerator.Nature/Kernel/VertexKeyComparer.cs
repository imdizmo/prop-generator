namespace PropGenerator.Nature.Kernel;

/// <summary>
/// Compares <see cref="MeshOps.MergeVertices"/>' vertex keys: arrays of longs, equal when every element is equal.
/// </summary>
internal sealed class VertexKeyComparer : IEqualityComparer<long[]>
{
    /// <summary>The one instance.</summary>
    public static readonly VertexKeyComparer Instance = new();

    private VertexKeyComparer()
    {
    }

    public bool Equals(long[]? x, long[]? y) =>
        ReferenceEquals(x, y) || (x is not null && y is not null && x.AsSpan().SequenceEqual(y));

    public int GetHashCode(long[] obj)
    {
        var hash = default(HashCode);
        foreach (long part in obj)
        {
            hash.Add(part);
        }

        return hash.ToHashCode();
    }
}
