using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Cards;

namespace PropGenerator.Desktop.Crash;

/// <summary>
/// Asked at the start after a run that ended badly, when a team server is set: "Prop Generator closed unexpectedly.
/// Send the report to the team?" with Send, Don't send and Show what is sent (the report's whole text). Nothing is
/// sent without Send.
/// </summary>
internal sealed class CrashCard : Border
{
    private readonly CrashReport _report;
    private readonly Func<byte[], Task<(bool Sent, string Said)>> _send;
    private readonly Action<string, bool> _done;
    private readonly TextBox _shown;
    private readonly Button _sendButton;

    /// <summary>Builds the card.</summary>
    /// <param name="report">The report.</param>
    /// <param name="send">Sends the zip; says what happened.</param>
    /// <param name="done">Called once, with what to say (and whether it is an error), when the card is answered.</param>
    internal CrashCard(CrashReport report, Func<byte[], Task<(bool Sent, string Said)>> send, Action<string, bool> done)
    {
        _report = report;
        _send = send;
        _done = done;
        CardParts.Dress(this);
        Width = 620;
        _shown = new TextBox
        {
            Text = report.Shown,
            IsReadOnly = true,
            IsVisible = false,
            Height = 300,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("monospace"),
            FontSize = 11,
        };
        AutomationProperties.SetName(_shown, "What is sent");
        Look.Chrome.Tip(_shown, "Exactly what the report holds: a short summary and this run's whole log, with any file paths and prop names in it.");
        _sendButton = Look.Chrome.TextButton("Send", "Send this report to your team's server, so the problem can be found without asking you for files.");
        AutomationProperties.SetName(_sendButton, "Send");
        _sendButton.Click += async (_, _) => await SendAsync();
        Button dont = Look.Chrome.TextButton("Don't send", "Send nothing. The log stays on this computer with the last few runs' logs.");
        AutomationProperties.SetName(dont, "Don't send");
        dont.Click += (_, _) => DontSend();
        Button show = Look.Chrome.TextButton("Show what is sent", "Show the report's whole text before you decide: a short summary and this run's log.");
        AutomationProperties.SetName(show, "Show what is sent");
        show.Click += (_, _) => _shown.IsVisible = !_shown.IsVisible;
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Prop Generator closed unexpectedly", FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Look.Theme.TextPrimary },
                new TextBlock
                {
                    Text = "Send the report to the team? It holds the version, the system, the memory used and that run's log, so the problem can be found. Nothing is sent unless you press Send.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Look.Theme.TextSecondary,
                },
                _shown,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { show, dont, _sendButton } },
            },
        };
    }

    /// <summary>Gets a value indicating whether the report's text is shown.</summary>
    internal bool ShowingWhatIsSent => _shown.IsVisible;

    /// <summary>Sends, as Send does.</summary>
    /// <returns>Done when it is sent or failed.</returns>
    internal async Task SendAsync()
    {
        _sendButton.IsEnabled = false;
        (bool sent, string said) = await _send(_report.Zip);
        _done(said, !sent);
    }

    /// <summary>Sends nothing, as Don't send does.</summary>
    internal void DontSend() => _done("The crash report was not sent.", false);
}
