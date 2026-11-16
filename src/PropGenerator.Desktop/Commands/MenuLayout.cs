// Adapted from easy-props (MIT, © 2026 AHS12).
namespace PropGenerator.Desktop.Commands;

/// <summary>One menu of the top bar: its title and its items as command ids, a null for each divider.</summary>
/// <param name="Title">The title on the bar, such as "File".</param>
/// <param name="Items">The command ids in order, with null where a divider goes.</param>
internal sealed record MenuLayout(string Title, IReadOnlyList<string?> Items);
