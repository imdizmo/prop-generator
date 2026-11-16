// Adapted from easy-props (MIT, © 2026 AHS12).
namespace PropGenerator.Desktop.Searching;

/// <summary>One row the palette can show: something to run, or an entry that opens a list of options.</summary>
/// <param name="Id">A stable name, used to remember its use.</param>
/// <param name="Label">What it is called.</param>
/// <param name="Section">The heading it is listed under.</param>
/// <param name="Keywords">More words it matches on.</param>
/// <param name="Icon">The lucide icon beside it; null for none.</param>
/// <param name="Keys">Its keyboard shortcut; null for none.</param>
/// <param name="Reason">Why it cannot run now. It stays listed, with its reason.</param>
/// <param name="Note">A short note beside it (a control's group, a preset's kind).</param>
/// <param name="Checked">Whether the option is the current one (a check is drawn); null for not an option.</param>
/// <param name="Children">The options, when choosing it opens a sub-list; null otherwise.</param>
/// <param name="Run">What it does; null for an entry that only opens a sub-list.</param>
/// <param name="Tooltip">What the row does, in a sentence.</param>
internal sealed record PaletteEntry(
    string Id,
    string Label,
    string Section,
    string Keywords,
    string? Icon,
    string? Keys,
    string? Reason,
    string? Note,
    bool? Checked,
    Func<IReadOnlyList<PaletteEntry>>? Children,
    Func<Task>? Run,
    string Tooltip);
