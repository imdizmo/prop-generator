namespace PropGenerator.Desktop.Tests.Support;

/// <summary>Dialogs that answer from the test's settings and note every question asked.</summary>
internal sealed class FakeDialogs : IDialogs
{
    /// <summary>Gets the names offered to the save dialog.</summary>
    public List<string> SaveSuggestions { get; } = [];

    /// <summary>Gets the yes/no questions asked.</summary>
    public List<string> Questions { get; } = [];

    /// <summary>Gets how many times the unsaved-changes question was asked.</summary>
    public int SaveChangesAsked { get; private set; }

    /// <summary>Gets the folder prompts asked.</summary>
    public int FolderAsked { get; private set; }

    /// <summary>Gets the messages shown.</summary>
    public List<string> Told { get; } = [];

    /// <summary>Gets or sets a task the unsaved-changes question waits for (a card left open).</summary>
    public Task? Hold { get; set; }

    /// <summary>Gets or sets a value indicating whether the folder dialog throws.</summary>
    public bool FolderThrows { get; set; }

    /// <summary>Gets or sets the path the save dialog returns.</summary>
    public string? SavePath { get; set; }

    /// <summary>Gets or sets the path the open dialog returns.</summary>
    public string? OpenPath { get; set; }

    /// <summary>Gets or sets the folder the folder dialog returns.</summary>
    public string? Folder { get; set; }

    /// <summary>Gets or sets the answer to yes/no questions.</summary>
    public bool Yes { get; set; }

    /// <summary>Gets or sets the answer to the unsaved-changes question.</summary>
    public SaveChoice Choice { get; set; } = SaveChoice.Cancel;

    /// <inheritdoc/>
    public Task<string?> AskSavePathAsync(string suggestedName, string? startFolder)
    {
        SaveSuggestions.Add(suggestedName);
        return Task.FromResult(SavePath);
    }

    /// <inheritdoc/>
    public Task<string?> AskOpenPathAsync(string? startFolder) => Task.FromResult(OpenPath);

    /// <inheritdoc/>
    public Task<string?> AskFolderAsync(string title, string? startFolder)
    {
        FolderAsked++;
        if (FolderThrows)
        {
            throw new InvalidOperationException("folder dialog broke");
        }

        return Task.FromResult(Folder);
    }

    /// <inheritdoc/>
    public Task<bool> ConfirmAsync(string question, string yes, string no)
    {
        Questions.Add(question);
        return Task.FromResult(Yes);
    }

    /// <inheritdoc/>
    public async Task<SaveChoice> AskSaveChangesAsync(string name)
    {
        SaveChangesAsked++;
        if (Hold is not null)
        {
            await Hold;
        }

        return Choice;
    }

    /// <inheritdoc/>
    public Task TellAsync(string title, string message)
    {
        Told.Add(message);
        return Task.CompletedTask;
    }

    /// <summary>Gets the answers the text question gives, in turn (null when none is left: cancelled).</summary>
    public Queue<string?> Texts { get; } = new();

    /// <summary>Gets the text questions asked, as their titles.</summary>
    public List<string> TextsAsked { get; } = [];

    /// <summary>Gets or sets the path the save-a-file dialog returns.</summary>
    public string? SaveFilePath { get; set; }

    /// <summary>Gets the names offered to the save-a-file dialog.</summary>
    public List<string> SaveFileSuggestions { get; } = [];

    /// <summary>Gets or sets the path the open-a-file dialog returns.</summary>
    public string? OpenFilePath { get; set; }

    /// <inheritdoc/>
    public Task<string?> AskTextAsync(string title, string label, string suggested)
    {
        TextsAsked.Add(title);
        return Task.FromResult(Texts.Count > 0 ? Texts.Dequeue() : null);
    }

    /// <inheritdoc/>
    public Task<string?> AskSaveFileAsync(string title, string suggestedName, string extension, string? startFolder)
    {
        SaveFileSuggestions.Add(suggestedName);
        return Task.FromResult(SaveFilePath);
    }

    /// <inheritdoc/>
    public Task<string?> AskOpenFileAsync(string title, string extension, string? startFolder) => Task.FromResult(OpenFilePath);
}
