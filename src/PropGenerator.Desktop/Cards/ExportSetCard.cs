// Adapted from easy-props frontend/src/panels/BatchExportDialog.tsx (its Export as a set part) (MIT, © 2026 AHS12).
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using PropGenerator.Desktop.Batch;

namespace PropGenerator.Desktop.Cards;

/// <summary>
/// Your props' Export as a set (decision E8): the ticked props as one set, matched to the same colours, with the set's
/// name, its colours, small pictures and folder (<see cref="SetColoursSection"/>); kinds that cannot be in a set are
/// listed as left out.
/// </summary>
internal sealed class ExportSetCard : Border, IDisposable
{
    private readonly Action<SetChoice, string> _export;
    private readonly Action _close;
    private readonly TextBlock _note = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = Look.Theme.Label, Foreground = Look.Theme.Warning, IsVisible = false };

    /// <summary>Makes the card.</summary>
    /// <param name="section">The set's name, colours, pictures and folder.</param>
    /// <param name="export">Exports the set into the folder chosen.</param>
    /// <param name="close">Closes the card.</param>
    internal ExportSetCard(SetColoursSection section, Action<SetChoice, string> export, Action close)
    {
        ArgumentNullException.ThrowIfNull(section);
        Set = section;
        _export = export;
        _close = close;

        var go = Look.Chrome.PrimaryButton("file-down", "Export", "Write the set's props and its list into the set's folder.");
        go.Click += (_, _) => Export();
        var cancel = Look.Chrome.TextButton("Cancel", "Close without exporting (Esc).");
        cancel.Click += (_, _) => _close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Look.Theme.Space2, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(go);

        int n = section.Props.Count;
        var body = new StackPanel { Spacing = Look.Theme.Space3 };
        body.Children.Add(CardParts.Header("Export as a set", "Close without exporting (Esc).", _close));
        body.Children.Add(new TextBlock
        {
            Text = string.Create(CultureInfo.InvariantCulture, $"Export {(n == 1 ? "1 prop" : $"{n} props")} as one set: in the same colours, with materials an engine can share, into a folder of its own."),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = Look.Theme.Label,
            Foreground = Look.Theme.TextSecondary,
        });
        body.Children.Add(section);
        body.Children.Add(_note);
        body.Children.Add(buttons);
        Child = body;
        Width = 460;
        CardParts.Dress(this);
    }

    /// <summary>Gets the set's name, colours, pictures and folder.</summary>
    internal SetColoursSection Set { get; }

    /// <summary>Closes the card and exports the set, once a folder is chosen.</summary>
    internal void Export()
    {
        if (Set.Folder is not string folder)
        {
            _note.Text = "Choose the folder the set goes in first.";
            _note.IsVisible = true;
            return;
        }

        SetChoice choice = Set.Choice();
        _close();
        _export(choice, folder);
    }

    /// <inheritdoc/>
    public void Dispose() => Set.Dispose();
}
