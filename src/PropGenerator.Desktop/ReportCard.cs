using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Cards;

namespace PropGenerator.Desktop;

/// <summary>
/// Report a problem (Help): the report filled in (see <see cref="ProblemReport"/>), which can be edited, then copied to
/// the clipboard or saved to a file. Nothing is sent.
/// </summary>
internal sealed class ReportCard : Border
{
    private readonly MainWindow _w;
    private readonly TextBox _text;

    /// <summary>Builds the card with the report of the window as it is now.</summary>
    /// <param name="w">The window.</param>
    internal ReportCard(MainWindow w)
    {
        _w = w;
        CardParts.Dress(this);
        Width = 640;
        _text = new TextBox
        {
            Text = ProblemReport.Make(w),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 360,
            FontFamily = new FontFamily("monospace"),
            FontSize = Look.Theme.Label,
        };
        AutomationProperties.SetName(_text, "Report");
        Look.Chrome.Tip(_text, "The report. Write what happened at the top; the rest is filled in. Nothing is sent: copy it or save it, and pass it on yourself.");
        Button copy = Look.Chrome.TextButton("Copy report", "Copy the report to the clipboard, to paste it wherever you report the problem.");
        AutomationProperties.SetName(copy, "Copy report");
        copy.Click += async (_, _) => await CopyAsync();
        Button save = Look.Chrome.TextButton("Save to a file…", "Save the report as a text file.");
        AutomationProperties.SetName(save, "Save to a file…");
        save.Click += async (_, _) => await SaveAsync();
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CardParts.Header("Report a problem", "Close the report (Esc). Nothing was sent.", w.Cards.Close),
                new TextBlock
                {
                    Text = "Nothing is sent from the app. Copy the report or save it, then pass it on.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Look.Theme.TextSecondary,
                },
                _text,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { copy, save } },
            },
        };
    }

    /// <summary>Gets the report as it reads now.</summary>
    internal string Text => _text.Text ?? string.Empty;

    /// <summary>Copies the report to the clipboard.</summary>
    /// <returns>Done when copied, or the problem said.</returns>
    internal async Task CopyAsync()
    {
        bool copied = await _w.CopyText(Text);
        _w.Say(copied ? "Copied the report. Nothing was sent." : "Couldn't copy the report: save it to a file instead.", error: !copied);
    }

    /// <summary>Asks where, then saves the report as a text file.</summary>
    /// <returns>Done when saved or cancelled.</returns>
    internal async Task SaveAsync()
    {
        string? path = await _w.Files.Dialogs.AskSaveFileAsync("Save the report", "prop-generator-report.txt", "txt", _w.Files.Prefs.SaveFolder);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, Text);
            _w.Say($"Saved the report to {path}. Nothing was sent.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _w.Say($"The report couldn't be saved: {e.Message}", error: true);
        }
    }
}
