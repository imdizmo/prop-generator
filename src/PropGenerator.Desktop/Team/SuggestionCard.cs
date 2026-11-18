using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PropGenerator.Desktop.Cards;

namespace PropGenerator.Desktop.Team;

/// <summary>
/// Suggestion: write what you would like changed, attach pictures (the window as it was when Suggestion was pressed,
/// or picture files), and Send it to the team's server. Without a team server it says so and Send stays off. What was
/// written is kept if the card is closed, until it is sent.
/// </summary>
internal sealed class SuggestionCard : Border, IBusyCard
{
    private readonly Func<Suggestion, Task<(bool Sent, string Said)>>? _send;
    private readonly Func<Task<IReadOnlyList<string>>> _pick;
    private readonly Action<string, bool> _done;
    private readonly List<SuggestionPicture> _pictures = [];
    private readonly TextBox _text;
    private readonly StackPanel _rows;
    private readonly TextBlock _note;
    private readonly Button _sendButton;
    private readonly Button _windowButton;
    private byte[]? _window;
    private bool _sending;

    /// <summary>Builds the card.</summary>
    /// <param name="send">Sends a suggestion and says what happened, or null when no team server is set.</param>
    /// <param name="pick">Asks for picture files to attach; their paths, none when cancelled.</param>
    /// <param name="close">Closes the card (what was written is kept).</param>
    /// <param name="done">Called when it is sent, with what to say and whether it is an error.</param>
    internal SuggestionCard(Func<Suggestion, Task<(bool Sent, string Said)>>? send, Func<Task<IReadOnlyList<string>>> pick, Action close, Action<string, bool> done)
    {
        _send = send;
        _pick = pick;
        _done = done;
        CardParts.Dress(this);
        Width = 560;
        _text = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 160,
            MaxLength = Suggestion.MaxText,
            PlaceholderText = "What would make Prop Generator better? What were you doing, what got in the way, what did you expect?",
        };
        AutomationProperties.SetName(_text, "Suggestion");
        _text.TextChanged += (_, _) => Refresh();
        _rows = new StackPanel { Spacing = 6 };
        _note = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Look.Theme.TextSecondary, IsVisible = false };
        _windowButton = Look.Chrome.TextButton("Picture of the window", "Attach a picture of the window as it was when you pressed Suggestion.");
        AutomationProperties.SetName(_windowButton, "Picture of the window");
        _windowButton.Click += (_, _) => AddWindow();
        Button files = Look.Chrome.TextButton("Add screenshots…", $"Attach screenshots or other pictures (PNG or JPEG, up to {Suggestion.MaxPictures}, each under {Suggestion.MaxPictureBytes / (1024 * 1024)} MB, {Suggestion.MaxAllBytes / (1024 * 1024)} MB together).");
        AutomationProperties.SetName(files, "Add screenshots…");
        files.Click += async (_, _) => await AddFilesAsync();
        _sendButton = Look.Chrome.TextButton("Send", "Send your suggestion and its pictures to the team.");
        AutomationProperties.SetName(_sendButton, "Send");
        _sendButton.Click += async (_, _) => await SendAsync();
        var offline = new TextBlock
        {
            Text = "No team server is set, so suggestions can't be sent from here. Set one in Settings > Team, or start Prop Generator from the Jatify Launcher.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Look.Theme.TextPrimary,
            IsVisible = send is null,
        };
        AutomationProperties.SetName(offline, "No team server");
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CardParts.Header("Send a suggestion", "Close (what you wrote is kept until you send it).", close),
                new TextBlock
                {
                    Text = "It goes to your team's server with your name, the version of Prop Generator and the pictures below: nothing else. Nothing is sent until you press Send.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Look.Theme.TextSecondary,
                },
                offline,
                _text,
                _rows,
                _note,
                new DockPanel
                {
                    LastChildFill = false,
                    Children =
                    {
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _windowButton, files } },
                        Docked(_sendButton, Dock.Right),
                    },
                },
            },
        };
        Refresh();
    }

    /// <inheritdoc/>
    public bool Busy => _sending || !string.IsNullOrWhiteSpace(_text.Text) || _pictures.Count > 0;

    /// <summary>Gets the pictures attached now.</summary>
    internal IReadOnlyList<SuggestionPicture> Pictures => _pictures;

    /// <summary>Gets or sets the text written.</summary>
    internal string Text
    {
        get => _text.Text ?? string.Empty;
        set
        {
            _text.Text = value;
            Refresh();
        }
    }

    /// <summary>Gets what the card says under the pictures (a picture that could not be added), or null.</summary>
    internal string? Note => _note.IsVisible ? _note.Text : null;

    /// <summary>Gets a value indicating whether Send can be pressed.</summary>
    internal bool CanSend => _sendButton.IsEnabled;

    /// <summary>Uses a new picture of the window for Picture of the window (taken as Suggestion opens the card).</summary>
    /// <param name="png">The picture, or null when none could be taken.</param>
    internal void UseWindow(byte[]? png)
    {
        _window = png;
        Refresh();
    }

    /// <summary>Attaches the window's picture, as Picture of the window does.</summary>
    internal void AddWindow()
    {
        if (_window is not null)
        {
            Add(new SuggestionPicture(FreeName("window.png"), _window));
        }
    }

    /// <summary>Asks for picture files and attaches them, as Add screenshots… does.</summary>
    /// <returns>Done when they are attached.</returns>
    internal async Task AddFilesAsync()
    {
        _note.IsVisible = false;
        foreach (string path in await _pick())
        {
            string name = Path.GetFileName(path);
            byte[] bytes;
            try
            {
                if (new FileInfo(path).Length > Suggestion.MaxPictureBytes)
                {
                    Say($"{name} is too big to attach (pictures must be under {Suggestion.MaxPictureBytes / (1024 * 1024)} MB).");
                    continue;
                }

                bytes = await File.ReadAllBytesAsync(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Say($"{name} could not be read: {e.Message}");
                continue;
            }

            if (bytes.Length > Suggestion.MaxPictureBytes || !Suggestion.IsPicture(bytes))
            {
                Say($"{name} is not a PNG or JPEG picture, so it was not attached.");
                continue;
            }

            if (!Add(new SuggestionPicture(FreeName(name), bytes)))
            {
                break;
            }
        }
    }

    /// <summary>Sends, as Send does.</summary>
    /// <returns>Done when it is sent or failed.</returns>
    internal async Task SendAsync()
    {
        if (_send is null || !CanSend)
        {
            return;
        }

        _sending = true;
        Refresh();
        try
        {
            (bool sent, string said) = await _send(new Suggestion(Text.Trim(), [.. _pictures]));
            if (sent)
            {
                _done(said, false);
            }
            else
            {
                Say(said);
            }
        }
        finally
        {
            _sending = false;
            Refresh();
        }
    }

    private static Control Docked(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    private bool Add(SuggestionPicture picture)
    {
        if (_pictures.Count >= Suggestion.MaxPictures)
        {
            Say($"A suggestion takes up to {Suggestion.MaxPictures} pictures.");
            return false;
        }

        if (_pictures.Sum(p => (long)p.Bytes.Length) + picture.Bytes.Length > Suggestion.MaxAllBytes)
        {
            Say($"{picture.Name} was not attached: the pictures together must stay under {Suggestion.MaxAllBytes / (1024 * 1024)} MB.");
            return true;
        }

        _pictures.Add(picture);
        _rows.Children.Add(Row(picture));
        Refresh();
        return true;
    }

    private DockPanel Row(SuggestionPicture picture)
    {
        var thumb = new Image { Width = 64, Height = 40, Stretch = Stretch.Uniform };
        try
        {
            using var s = new MemoryStream(picture.Bytes);
            thumb.Source = Bitmap.DecodeToWidth(s, 128);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // ponytail: a picture that will not decode is still sent; it only has no thumbnail here.
        }

        Button remove = Look.Chrome.TextButton("Remove", $"Take {picture.Name} off the suggestion.");
        AutomationProperties.SetName(remove, "Remove " + picture.Name);
        var row = new DockPanel { LastChildFill = true };
        remove.Click += (_, _) =>
        {
            _pictures.Remove(picture);
            _rows.Children.Remove(row);
            Refresh();
        };
        row.Children.Add(Docked(thumb, Dock.Left));
        row.Children.Add(Docked(remove, Dock.Right));
        row.Children.Add(new TextBlock
        {
            Text = $"{picture.Name}  ({Math.Max(1, picture.Bytes.Length / 1024)} KB)",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(8, 0),
            Foreground = Look.Theme.TextPrimary,
        });
        return row;
    }

    private string FreeName(string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        string ext = Path.GetExtension(name);
        string free = name;
        for (int i = 2; _pictures.Any(p => p.Name.Equals(free, StringComparison.OrdinalIgnoreCase)); i++)
        {
            free = $"{stem}-{i}{ext}";
        }

        return free;
    }

    private void Say(string text)
    {
        _note.Text = text;
        _note.IsVisible = true;
    }

    private void Refresh()
    {
        _windowButton.IsEnabled = _window is not null && !_sending;
        _sendButton.IsEnabled = _send is not null && !_sending && !string.IsNullOrWhiteSpace(_text.Text);
        _sendButton.Content = _sending ? "Sending…" : "Send";
    }
}
