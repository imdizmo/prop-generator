namespace PropGenerator.Desktop;

/// <summary>
/// Every question the app puts to the user, behind one interface: the real one (<see cref="WindowDialogs"/>) uses the
/// system's file dialogs and a small panel inside the window; tests answer them without any window.
/// </summary>
public interface IDialogs
{
    /// <summary>Asks for the name and place to save a work file.</summary>
    /// <param name="suggestedName">The name to fill in (and select).</param>
    /// <param name="startFolder">Where the dialog opens, or null.</param>
    /// <returns>The chosen path, or null when cancelled.</returns>
    Task<string?> AskSavePathAsync(string suggestedName, string? startFolder);

    /// <summary>Asks which work file to open.</summary>
    /// <param name="startFolder">Where the dialog opens, or null.</param>
    /// <returns>The chosen path, or null when cancelled.</returns>
    Task<string?> AskOpenPathAsync(string? startFolder);

    /// <summary>Asks for a folder.</summary>
    /// <param name="title">What the folder is for.</param>
    /// <param name="startFolder">Where the dialog opens, or null.</param>
    /// <returns>The chosen folder, or null when cancelled.</returns>
    Task<string?> AskFolderAsync(string title, string? startFolder);

    /// <summary>Asks a yes or no question.</summary>
    /// <param name="question">The question, for example "Replace wood.propgen?".</param>
    /// <param name="yes">What the yes button says.</param>
    /// <param name="no">What the no button says.</param>
    /// <returns>True for yes.</returns>
    Task<bool> ConfirmAsync(string question, string yes, string no);

    /// <summary>Asks what to do with unsaved changes.</summary>
    /// <param name="name">The piece's file name.</param>
    /// <returns>The choice.</returns>
    Task<SaveChoice> AskSaveChangesAsync(string name);

    /// <summary>Tells the user something they must read (an error), with one OK button.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="message">The message.</param>
    /// <returns>Done when the user has read it.</returns>
    Task TellAsync(string title, string message);

    /// <summary>Asks for a short text, such as a name.</summary>
    /// <param name="title">The question, such as "Save as my preset".</param>
    /// <param name="label">What the box is for, such as "Name".</param>
    /// <param name="suggested">The text to fill in (and select).</param>
    /// <returns>The text typed, or null when cancelled.</returns>
    Task<string?> AskTextAsync(string title, string label, string suggested);

    /// <summary>Asks where to save a file of one type (a backup, a picture, a report).</summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="suggestedName">The file name to fill in.</param>
    /// <param name="extension">The extension without its dot, such as "zip".</param>
    /// <param name="startFolder">Where the dialog opens, or null.</param>
    /// <returns>The chosen path, or null when cancelled.</returns>
    Task<string?> AskSaveFileAsync(string title, string suggestedName, string extension, string? startFolder);

    /// <summary>Asks which file of one type to open (a backup to restore).</summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="extension">The extension without its dot, such as "zip".</param>
    /// <param name="startFolder">Where the dialog opens, or null.</param>
    /// <returns>The chosen path, or null when cancelled.</returns>
    Task<string?> AskOpenFileAsync(string title, string extension, string? startFolder);

    /// <summary>Asks for one or more pictures (PNG or JPEG) to attach to a suggestion.</summary>
    /// <param name="title">The dialog's title.</param>
    /// <returns>The chosen paths, none when cancelled.</returns>
    Task<IReadOnlyList<string>> AskOpenPicturesAsync(string title);
}
