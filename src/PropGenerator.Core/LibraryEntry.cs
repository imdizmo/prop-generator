namespace PropGenerator;

/// <summary>One work file the app has opened before.</summary>
/// <param name="Path">The full path.</param>
/// <param name="Name">The file name without its extension.</param>
/// <param name="LastOpened">When it was last opened (UTC).</param>
/// <param name="ColourHex">The wood colour the file was saved with, as #rrggbb; null when it could not be read.</param>
public sealed record LibraryEntry(string Path, string Name, DateTimeOffset LastOpened, string? ColourHex);
