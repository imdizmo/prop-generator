// Adapted from easy-props (MIT, © 2026 AHS12).
namespace PropGenerator.Desktop.Searching;

/// <summary>The headings of the palette's rows, and the order they are listed in.</summary>
internal static class PaletteSection
{
    /// <summary>Every command but the palette's own.</summary>
    internal const string Actions = "Actions";

    /// <summary>The settings of the piece being worked on.</summary>
    internal const string Controls = "Controls";

    /// <summary>Every starting point.</summary>
    internal const string Presets = "Presets";

    /// <summary>The work files saved or opened before.</summary>
    internal const string SavedProps = "Saved props";

    /// <summary>The app's own settings (the Settings window).</summary>
    internal const string Settings = "Settings";

    /// <summary>What the empty palette offers for the selection (nothing yet).</summary>
    internal const string ForSelection = "For this selection";

    /// <summary>The last commands run.</summary>
    internal const string Recent = "Recent";

    /// <summary>The commands offered when nothing was typed and nothing was run.</summary>
    internal const string Suggested = "Suggested";

    /// <summary>The options of an entry that opened a sub-list.</summary>
    internal const string ChooseOne = "Choose one";

    /// <summary>The sections a search lists, in order.</summary>
    internal static readonly string[] Order = [Actions, Controls, Presets, SavedProps, Settings];
}
