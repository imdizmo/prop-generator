namespace PropGenerator.Desktop;

/// <summary>What the user chose when asked about unsaved changes.</summary>
public enum SaveChoice
{
    /// <summary>Save the piece first, then go on.</summary>
    Save,

    /// <summary>Go on and lose the changes.</summary>
    Discard,

    /// <summary>Stay where things are.</summary>
    Cancel,
}
