namespace PropGenerator.Desktop.Colours;

/// <summary>A saved palette: the colours of one set of own-colour rows (such as a rock's stone), under a name.</summary>
/// <param name="Id">A short stable name.</param>
/// <param name="Name">What the user called it.</param>
/// <param name="Set">Which colours these are ("stone", "grass", "flowers", …), so it fits every kind that has them.</param>
/// <param name="Colours">The colours as #rrggbb, in the rows' order.</param>
internal sealed record SavedPalette(string Id, string Name, string Set, IReadOnlyList<string> Colours);
