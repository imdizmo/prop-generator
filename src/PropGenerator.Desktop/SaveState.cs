namespace PropGenerator.Desktop;

/// <summary>Where the piece stands against its work file, as the status bar says it.</summary>
public enum SaveState
{
    /// <summary>Changed since it was last saved or opened ("Unsaved").</summary>
    Unsaved,

    /// <summary>Unchanged, but never saved to a work file ("Not saved yet").</summary>
    NotSavedYet,

    /// <summary>Unchanged since it was saved to, or opened from, its work file ("Saved").</summary>
    Saved,
}
