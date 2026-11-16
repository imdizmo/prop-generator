namespace PropGenerator.Desktop.Props;

/// <summary>A prop in Recently removed.</summary>
/// <param name="Id">Its name in the bin.</param>
/// <param name="Name">Its name when it was removed.</param>
/// <param name="OriginalPath">Where its work file was, and goes back to.</param>
/// <param name="RemovedAt">When it was removed.</param>
internal sealed record RemovedProp(string Id, string Name, string OriginalPath, DateTimeOffset RemovedAt);
