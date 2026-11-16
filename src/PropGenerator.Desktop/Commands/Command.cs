// Adapted from easy-props (MIT, © 2026 AHS12).
namespace PropGenerator.Desktop.Commands;

/// <summary>
/// One thing the app can do, as easy-props' command list has it: the menus, the keyboard shortcuts, the command palette
/// and Claude's tools all read the same list, so a command cannot exist in one place and be missing from another.
/// </summary>
/// <param name="Id">A short stable name, such as "save" or "shading-flat".</param>
/// <param name="Label">What the menus and the palette call it, such as "Save a copy…".</param>
/// <param name="Group">The menu it belongs to: File, Edit, View, Prop, Help, or Tools for the view's tools.</param>
/// <param name="Keys">Its shortcut as it is pressed and shown, such as "Ctrl+Shift+S" or "R"; null for none.</param>
/// <param name="Icon">The lucide icon shown beside it; null for none.</param>
/// <param name="Unavailable">Why it cannot run now (shown greyed with this reason), or null when it can.</param>
/// <param name="Checked">For an on/off item, whether it is on now; null (from the function) for an item that is not one.</param>
/// <param name="Children">The choices of a sub-list (Shading, Detail level, Camera angle), read when shown; null for none.</param>
/// <param name="Run">Does it. When the command is unavailable, it only says why on the status line.</param>
/// <param name="Keywords">More words the palette matches it on; null for none.</param>
/// <param name="Tooltip">What it does, in a sentence or two, shown when the pointer rests on it.</param>
internal sealed record Command(
    string Id,
    string Label,
    string Group,
    string? Keys,
    string? Icon,
    Func<string?> Unavailable,
    Func<bool?> Checked,
    IReadOnlyList<Command>? Children,
    Func<Task> Run,
    string? Keywords = null,
    string? Tooltip = null);
