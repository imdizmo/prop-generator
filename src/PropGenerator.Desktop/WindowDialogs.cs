using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace PropGenerator.Desktop;

/// <summary>
/// The real dialogs: the system's own file and folder pickers (through Avalonia's storage provider), and a small card
/// shown over the window for questions and messages. The card is added to the window only while it is asked.
/// </summary>
public sealed class WindowDialogs : IDialogs
{
    private static readonly FilePickerFileType WorkFiles = new("Prop Generator work files") { Patterns = ["*.propgen"] };

    private readonly Window _window;
    private readonly Panel _overlay;

    /// <summary>Makes the dialogs of one window.</summary>
    /// <param name="window">The window the pickers belong to.</param>
    /// <param name="overlay">A panel covering the window's content, where the card goes.</param>
    public WindowDialogs(Window window, Panel overlay)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(overlay);
        _window = window;
        _overlay = overlay;
    }

    /// <inheritdoc/>
    public async Task<string?> AskSavePathAsync(string suggestedName, string? startFolder)
    {
        IStorageFile? file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save work file",
            SuggestedFileName = suggestedName,
            DefaultExtension = "propgen",
            FileTypeChoices = [WorkFiles],
            SuggestedStartLocation = await Folder(startFolder),
            ShowOverwritePrompt = false,
        });
        return file?.TryGetLocalPath();
    }

    /// <inheritdoc/>
    public async Task<string?> AskOpenPathAsync(string? startFolder)
    {
        IReadOnlyList<IStorageFile> files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open work file",
            AllowMultiple = false,
            FileTypeFilter = [WorkFiles],
            SuggestedStartLocation = await Folder(startFolder),
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    /// <inheritdoc/>
    public async Task<string?> AskFolderAsync(string title, string? startFolder)
    {
        IReadOnlyList<IStorageFolder> folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await Folder(startFolder),
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    /// <inheritdoc/>
    public async Task<bool> ConfirmAsync(string question, string yes, string no) =>
        await Choose(
            question,
            string.Empty,
            defaultIndex: 0,
            cancelIndex: 1,
            (yes, $"{yes}: go ahead and do what the question above asks."),
            (no, $"{no}: leave everything exactly as it is and change nothing.")) == 0;

    /// <inheritdoc/>
    public async Task<SaveChoice> AskSaveChangesAsync(string name) =>
        await Choose(
            "Save changes?",
            $"{name} has changes that are not saved.",
            defaultIndex: 0,
            cancelIndex: 2,
            ("Save", "Save the piece, then carry on with what you were doing."),
            ("Don't save", "Carry on and throw away the changes made since the last save."),
            ("Cancel", "Go back to the piece without doing anything.")) switch
        {
            0 => SaveChoice.Save,
            1 => SaveChoice.Discard,
            _ => SaveChoice.Cancel,
        };

    /// <inheritdoc/>
    public async Task TellAsync(string title, string message) =>
        _ = await Choose(title, message, defaultIndex: 0, cancelIndex: 0, ("OK", "Close this message and go back to the piece."));

    /// <inheritdoc/>
    public Task<string?> AskTextAsync(string title, string label, string suggested)
    {
        var done = new TaskCompletionSource<string?>();
        var box = new TextBox { Text = suggested, MinWidth = 320 };
        ToolTip.SetTip(box, $"{label}: type it here, then press Enter or OK. Esc cancels.");
        Avalonia.Automation.AutomationProperties.SetName(box, label);
        var ok = new Button { Content = "OK" };
        ToolTip.SetTip(ok, "Use what is typed in the box.");
        var cancel = new Button { Content = "Cancel" };
        ToolTip.SetTip(cancel, "Go back without changing anything.");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = label });
        body.Children.Add(box);
        body.Children.Add(row);
        var card = new Border
        {
            Background = Look.Theme.SurfaceRaised,
            BorderBrush = Look.Theme.BorderStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(22),
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = body,
        };
        var shade = new Grid { Background = Look.Tokens.Scrim };
        shade.Children.Add(card);

        void OnKey(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Finish(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Finish(box.Text ?? string.Empty);
                e.Handled = true;
            }
        }

        void Finish(string? answer)
        {
            if (done.TrySetResult(answer))
            {
                _window.RemoveHandler(InputElement.KeyDownEvent, OnKey);
                _overlay.Children.Remove(shade);
            }
        }

        ok.Click += (_, _) => Finish(box.Text ?? string.Empty);
        cancel.Click += (_, _) => Finish(null);
        _window.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        _overlay.Children.Add(shade);
        box.AttachedToVisualTree += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return done.Task;
    }

    /// <inheritdoc/>
    public async Task<string?> AskSaveFileAsync(string title, string suggestedName, string extension, string? startFolder)
    {
        IStorageFile? file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant() + " files") { Patterns = ["*." + extension] }],
            SuggestedStartLocation = await Folder(startFolder),
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    /// <inheritdoc/>
    public async Task<string?> AskOpenFileAsync(string title, string extension, string? startFolder)
    {
        IReadOnlyList<IStorageFile> files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(extension.ToUpperInvariant() + " files") { Patterns = ["*." + extension] }],
            SuggestedStartLocation = await Folder(startFolder),
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    private async Task<IStorageFolder?> Folder(string? path) =>
        path is not null && Directory.Exists(path) ? await _window.StorageProvider.TryGetFolderFromPathAsync(path) : null;

    private Task<int> Choose(string title, string message, int defaultIndex, int cancelIndex, params (string Text, string Tip)[] buttons)
    {
        var done = new TaskCompletionSource<int>();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var card = new Border
        {
            Background = Look.Theme.SurfaceRaised,
            BorderBrush = Look.Theme.BorderStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(22),
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (message.Length > 0)
        {
            body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        }

        body.Children.Add(row);
        card.Child = body;
        var shade = new Grid { Background = Look.Tokens.Scrim };
        shade.Children.Add(card);

        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var b = new Button { Content = buttons[i].Text };
            ToolTip.SetTip(b, buttons[i].Tip);
            Avalonia.Automation.AutomationProperties.SetName(b, buttons[i].Text);
            b.Click += (_, _) => Finish(index);
            row.Children.Add(b);
        }

        // Esc is the cancel choice, Enter the default one (unless a button has the focus: Enter then presses that button).
        void OnKey(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Finish(cancelIndex);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _window.FocusManager?.GetFocusedElement() is not Button)
            {
                Finish(defaultIndex);
                e.Handled = true;
            }
        }

        void Finish(int chosen)
        {
            if (done.TrySetResult(chosen))
            {
                _window.RemoveHandler(InputElement.KeyDownEvent, OnKey);
                _overlay.Children.Remove(shade);
            }
        }

        _window.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        _overlay.Children.Add(shade);
        return done.Task;
    }
}
