namespace PropGenerator.Desktop.Props;

/// <summary>What Your props shows of one saved prop.</summary>
/// <param name="Path">The work file's full path.</param>
/// <param name="Name">Its name: the file name without .propgen.</param>
/// <param name="Recipe">The recipe it is made with, such as "cut-rock"; null when the file cannot be read.</param>
/// <param name="KindLabel">The kind as the panel calls it, such as "Rock", or "Unreadable prop".</param>
/// <param name="StartedFrom">The starting point it was made from (its first undo step), or null.</param>
/// <param name="Seed">Its seed, or null when it has none.</param>
/// <param name="Made">When the file was made (its birth time where the disk keeps one, else its first write).</param>
/// <param name="Changed">When the file was last written.</param>
/// <param name="Size">The file's size in bytes.</param>
/// <param name="ColourHex">The floor's wood colour as #RRGGBB, or null.</param>
internal sealed record PropSummary(
    string Path,
    string Name,
    string? Recipe,
    string KindLabel,
    string? StartedFrom,
    long? Seed,
    DateTimeOffset Made,
    DateTimeOffset Changed,
    long Size,
    string? ColourHex)
{
    /// <summary>The kind label of a file that cannot be read.</summary>
    internal const string Unreadable = "Unreadable prop";

    /// <summary>What a prop of a kind this build does not know (one made by a newer build) is called.</summary>
    /// <param name="recipe">Its recipe's id, as the file names it.</param>
    /// <returns>"Unknown kind (id)".</returns>
    internal static string UnknownKind(string recipe) => $"Unknown kind ({recipe})";

    /// <summary>Gets a value indicating whether the work file could be read (only then can it be opened, copied or exported).</summary>
    internal bool Readable => Recipe is not null;
}
