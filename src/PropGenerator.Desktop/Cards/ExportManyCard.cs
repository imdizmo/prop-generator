// Adapted from easy-props frontend/src/panels/BatchExportDialog.tsx (MIT, © 2026 AHS12).
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using PropGenerator.Desktop.Batch;

namespace PropGenerator.Desktop.Cards;

/// <summary>
/// Export many: several versions of the open prop at once, each its own model in the map folder. Either the versions
/// ticked in Show me more (new seeds or damaged versions, upstream's <c>PropVersion</c>s), or a number of versions (the first is the prop as it is, the rest the same settings on seeds
/// drawn from it, so exporting twice makes the same props).
/// </summary>
internal sealed class ExportManyCard : Border, IDisposable
{
    private readonly Action<int> _export;
    private readonly Action _close;
    private readonly int _most;
    private readonly Func<int, (IReadOnlyList<SetProp> Props, IReadOnlyList<PropGenerator.Engine.EngineColourGroup> Groups)?>? _setOf;
    private readonly Func<string?, Task<string?>>? _chooseFolder;
    private readonly string? _setFolder;
    private readonly Action<int, SetChoice, string>? _exportSet;
    private readonly StackPanel _setPlace = new();
    private readonly TextBlock _setNote = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = Look.Theme.Label, Foreground = Look.Theme.Warning, IsVisible = false };

    /// <summary>Makes the card.</summary>
    /// <param name="name">What the prop is called; the models are named after it.</param>
    /// <param name="chosen">The versions ticked in Show me more, or null to choose a number of versions.</param>
    /// <param name="most">The most versions it may export (upstream's <c>MAX_VARIATIONS</c>, from the engine).</param>
    /// <param name="export">Exports that many versions (ignored for ticked versions).</param>
    /// <param name="close">Closes the card.</param>
    /// <param name="pictureSize">
    /// The picture size chosen now, offered as a choice beside the versions (easy-props' kinds), or null to leave the
    /// choice out (our own kinds have no pictures to size).
    /// </param>
    /// <param name="pictureSizeChosen">Remembers a picture size chosen on the card (the export reads it from the prefs).</param>
    /// <param name="setOf">
    /// The set's props for this many versions and their colour groups, or null when the engine did not answer; null to
    /// leave Export as a set out (our own kinds have no set colours).
    /// </param>
    /// <param name="exportSet">Exports that many versions as a set, into the folder chosen.</param>
    /// <param name="setFolder">The folder a set last went in, or null.</param>
    /// <param name="chooseFolder">Asks for the set's folder, starting in the one given.</param>
    internal ExportManyCard(
        string name,
        IReadOnlyList<PropVersion>? chosen,
        int most,
        Action<int> export,
        Action close,
        int? pictureSize = null,
        Action<int>? pictureSizeChosen = null,
        Func<int, (IReadOnlyList<SetProp> Props, IReadOnlyList<PropGenerator.Engine.EngineColourGroup> Groups)?>? setOf = null,
        Action<int, SetChoice, string>? exportSet = null,
        string? setFolder = null,
        Func<string?, Task<string?>>? chooseFolder = null)
    {
        _export = export;
        _close = close;
        _most = Math.Max(1, most);
        _setOf = setOf;
        _exportSet = exportSet;
        _setFolder = setFolder;
        _chooseFolder = chooseFolder;
        Chosen = chosen;
        Versions = new NumericUpDown
        {
            Minimum = 1,
            Maximum = _most,
            Value = 4,
            Increment = 1,
            FormatString = "0",
            Width = 120,
            IsVisible = chosen is null,
        };
        AutomationProperties.SetName(Versions, "Versions");
        Versions.ValueChanged += (_, _) =>
        {
            if (Set is not null)
            {
                ShowSet(true, keep: true);
            }
        };
        Look.Chrome.Tip(Versions, string.Create(CultureInfo.InvariantCulture, $"How many versions to export, 1 to {_most}. The first is the prop as it is now."));

        string what = chosen is null
            ? $"Export {name} in several versions, each its own model: {name}_01 is the prop as it is now, the rest are the same settings on other seeds."
            : string.Create(CultureInfo.InvariantCulture, $"Export the {chosen.Count} versions of {name} you ticked, each its own model: {name}_01, {name}_02 and on.");
        var go = Look.Chrome.PrimaryButton("file-down", "Export", "Write the versions into the map folder.");
        go.Click += (_, _) => Export();
        var cancel = Look.Chrome.TextButton("Cancel", "Close without exporting (Esc).");
        cancel.Click += (_, _) => _close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Look.Theme.Space2, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(go);

        var body = new StackPanel { Spacing = Look.Theme.Space3 };
        body.Children.Add(CardParts.Header("Export many", "Close without exporting (Esc).", _close));
        body.Children.Add(new TextBlock { Text = what, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = Look.Theme.Label, Foreground = Look.Theme.TextSecondary });
        body.Children.Add(Versions);
        if (pictureSize is int size)
        {
            ComboBox pictures = SettingsWindow.PictureSizeBox(size);
            pictures.SelectionChanged += (_, _) =>
            {
                if (pictures.SelectedIndex >= 0)
                {
                    pictureSizeChosen?.Invoke(Recipes.ExportOptions.PictureSizes[pictures.SelectedIndex]);
                }
            };
            PictureSize = pictures;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Look.Theme.Space2 };
            row.Children.Add(new TextBlock { Text = "Picture size", VerticalAlignment = VerticalAlignment.Center, FontSize = Look.Theme.Label });
            row.Children.Add(pictures);
            body.Children.Add(row);
        }

        if (setOf is not null)
        {
            AsSet = new ToggleSwitch { OnContent = null, OffContent = null, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(AsSet, "Export as a set");
            Look.Chrome.Tip(AsSet, "Export the versions as one set: matched to the same colours, with materials an engine can share, into a folder of its own.");
            AsSet.IsCheckedChanged += (_, _) => ShowSet(AsSet.IsChecked == true);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Look.Theme.Space2 };
            row.Children.Add(new TextBlock { Text = "Export as a set", VerticalAlignment = VerticalAlignment.Center, FontSize = Look.Theme.Label });
            row.Children.Add(AsSet);
            body.Children.Add(row);
            body.Children.Add(_setPlace);
            body.Children.Add(_setNote);
        }

        body.Children.Add(buttons);
        Child = body;
        Width = 420;
        CardParts.Dress(this);
    }

    /// <summary>Gets the versions ticked in Show me more, or null when a number of versions is chosen.</summary>
    internal IReadOnlyList<PropVersion>? Chosen { get; }

    /// <summary>Gets the seeds of the versions ticked in Show me more, or null when a number of versions is chosen.</summary>
    internal IReadOnlyList<uint>? Seeds => Chosen is null ? null : [.. Chosen.Select(static v => v.Seed)];

    /// <summary>Gets the picture size choice (an engine kind's), or null when the prop has no pictures to size.</summary>
    internal ComboBox? PictureSize { get; }

    /// <summary>Gets how many versions to export (hidden for ticked versions).</summary>
    internal NumericUpDown Versions { get; }

    /// <summary>Gets the Export as a set switch, or null when the prop cannot be in a set.</summary>
    internal ToggleSwitch? AsSet { get; }

    /// <summary>Gets the set's name, colours and pictures while Export as a set is on, else null.</summary>
    internal SetColoursSection? Set { get; private set; }

    /// <summary>Closes the card and exports: the versions as models in the map folder, or, as a set, into the set's folder.</summary>
    internal void Export()
    {
        int count = Count;
        if (Set is { } set)
        {
            if (set.Folder is not string folder)
            {
                _setNote.Text = "Choose the folder the set goes in first.";
                _setNote.IsVisible = true;
                return;
            }

            SetChoice choice = set.Choice();
            _close();
            _exportSet?.Invoke(count, choice, folder);
            return;
        }

        _close();
        _export(count);
    }

    /// <inheritdoc/>
    public void Dispose() => Set?.Dispose();

    private int Count => Chosen?.Count ?? (int)Math.Clamp(Versions.Value ?? 1, 1, _most);

    // Shows the set section for the versions as they are now (keeping its name and colours when only their number
    // changed), or takes it away.
    private void ShowSet(bool on, bool keep = false)
    {
        _setNote.IsVisible = false;
        if (!on || _setOf is null)
        {
            Set?.Dispose();
            Set = null;
            _setPlace.Children.Clear();
            return;
        }

        if (_setOf(Count) is not { } set)
        {
            _setNote.Text = "Export as a set is not available: easy-props' engine did not answer.";
            _setNote.IsVisible = true;
            return;
        }

        if (keep && Set is not null)
        {
            Set.SetProps(set.Props, set.Groups);
            return;
        }

        Set?.Dispose();
        _setPlace.Children.Clear();
        Set = new SetColoursSection(set.Props, set.Groups, _setFolder, _chooseFolder ?? (_ => Task.FromResult<string?>(null)));
        _setPlace.Children.Add(Set);
    }
}
