// Adapted from easy-props frontend/src/panels/SetColoursSection.tsx (MIT, © 2026 AHS12).
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PropGenerator.Desktop.Batch;
using PropGenerator.Desktop.Engine;
using PropGenerator.Engine;

namespace PropGenerator.Desktop.Cards;

/// <summary>
/// Export as a set, shared by Export many and Your props' set (decision E8): the set's name, a choice per colour
/// setting its props share (starting on the one most of them use), a small picture of up to eight props in those
/// colours, so a birch going brown is seen before it is exported, and where the set goes. The pictures are drawn one
/// at a time on the engine's thread; changing a colour stops the ones not drawn yet and starts again, so no more than
/// eight ever wait. The saved props are never changed.
/// </summary>
internal sealed class SetColoursSection : StackPanel, IDisposable
{
    private readonly Func<SetProp, string, CancellationToken, Task<byte[]?>> _draw;
    private readonly Func<string?, Task<string?>> _chooseFolder;
    private readonly StackPanel _groupRows = new() { Spacing = Look.Theme.Space2 };
    private readonly WrapPanel _pictures = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _hint = Line("Set colours: every prop below is exported in these. Your saved props stay as they are.");
    private readonly TextBlock _leftOut = Line(string.Empty);
    private readonly Dictionary<string, string> _picks = new(StringComparer.Ordinal);
    private readonly List<ComboBox> _boxes = [];
    private CancellationTokenSource _drawing = new();
    private Task _drawn = Task.CompletedTask;
    private bool _disposed;

    /// <summary>Makes the section for a set's props.</summary>
    /// <param name="props">The set's props.</param>
    /// <param name="groups">The colour settings they share (<see cref="ExportSet.Groups"/>).</param>
    /// <param name="folder">The folder the set's own folder goes in, as remembered, or null for none yet.</param>
    /// <param name="chooseFolder">Asks for a folder, starting in the one given; null when none is chosen.</param>
    /// <param name="leftOut">Props that cannot be in a set, each with why, listed under the choices; none by default.</param>
    /// <param name="draw">Draws one prop in these colours (a PNG, or null when it cannot); null for the engine's.</param>
    internal SetColoursSection(
        IReadOnlyList<SetProp> props,
        IReadOnlyList<EngineColourGroup> groups,
        string? folder,
        Func<string?, Task<string?>> chooseFolder,
        IReadOnlyList<string>? leftOut = null,
        Func<SetProp, string, CancellationToken, Task<byte[]?>>? draw = null)
    {
        _draw = draw ?? DrawAsync;
        _chooseFolder = chooseFolder;
        Folder = folder;
        Spacing = Look.Theme.Space2;

        NameBox = new TextBox { Text = ExportSet.DefaultName, MaxLength = ExportSet.MaxName, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(NameBox, "Set name");
        Look.Chrome.Tip(NameBox, "The set's folder, and the start of every material's name, so an engine can share them.");
        NameBox.TextChanged += (_, _) =>
        {
            // Pasted text is cut as typed text is.
            if (NameBox.Text is { Length: > ExportSet.MaxName } typed)
            {
                NameBox.Text = ExportSet.Cut(typed);
            }

            ShowWhere();
        };

        FolderButton = Look.Chrome.TextButton("Choose folder…", "Choose the folder the set's own folder is made in.");
        FolderButton.Click += async (_, _) => await ChooseFolderAsync();
        AutomationProperties.SetName(_pictures, "The set in its colours");

        Children.Add(Row("Set name", NameBox));
        Children.Add(_hint);
        Children.Add(_groupRows);
        Children.Add(_pictures);
        var where = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Look.Theme.Space2 };
        where.Children.Add(FolderLine);
        where.Children.Add(FolderButton);
        Children.Add(where);
        Children.Add(_leftOut);
        LeftOut = leftOut ?? [];
        _leftOut.Text = LeftOut.Count == 0 ? string.Empty : $"Left out of the set: {string.Join("; ", LeftOut)}.";
        _leftOut.IsVisible = LeftOut.Count > 0;
        FolderLine.TextWrapping = TextWrapping.Wrap;
        FolderLine.MaxWidth = 300;
        SetProps(props, groups);
    }

    /// <summary>Gets the set name's field.</summary>
    internal TextBox NameBox { get; }

    /// <summary>Gets the set's name as typed, cut at <see cref="ExportSet.MaxName"/> characters.</summary>
    internal string SetName => ExportSet.Cut(NameBox.Text);

    /// <summary>Gets the colour settings the set's props share.</summary>
    internal IReadOnlyList<EngineColourGroup> Groups { get; private set; } = [];

    /// <summary>Gets the choice for each group, by its id: what was picked, else the usual one.</summary>
    internal IReadOnlyDictionary<string, string> Colours
    {
        get
        {
            Dictionary<string, string> colours = ExportSet.Usual(Groups);
            foreach (EngineColourGroup group in Groups)
            {
                if (_picks.TryGetValue(group.Id, out string? pick))
                {
                    colours[group.Id] = pick;
                }
            }

            return colours;
        }
    }

    /// <summary>Gets the choice of each group, in the groups' order.</summary>
    internal IReadOnlyList<ComboBox> ColourBoxes => _boxes;

    /// <summary>Gets the folder the set's own folder goes in, or null while none is chosen.</summary>
    internal string? Folder { get; private set; }

    /// <summary>Gets the line saying where the set goes.</summary>
    internal TextBlock FolderLine { get; } = Line(string.Empty);

    /// <summary>Gets the button that chooses the folder.</summary>
    internal Button FolderButton { get; }

    /// <summary>Gets the props left out of the set, each with why.</summary>
    internal IReadOnlyList<string> LeftOut { get; }

    /// <summary>Gets the set's props.</summary>
    internal IReadOnlyList<SetProp> Props { get; private set; } = [];

    /// <summary>Gets how many pictures of the current colours are still waiting to be drawn.</summary>
    internal int PreviewsQueued { get; private set; }

    /// <summary>Gets the most pictures that were ever waiting at once.</summary>
    internal int MostPreviewsQueued { get; private set; }

    /// <summary>Gets how many pictures have been drawn in the current colours.</summary>
    internal int PreviewsDrawn { get; private set; }

    /// <summary>Gets a value indicating whether the set can be exported: it has props and a folder.</summary>
    internal bool Ready => Props.Count > 0 && Folder is not null;

    /// <summary>The set as chosen now.</summary>
    /// <returns>Its name, groups and colours.</returns>
    internal SetChoice Choice() => new(SetName, Groups, Colours);

    /// <summary>Shows other props (Export many's number of versions changed): their groups, the colours kept where a group stays, and their pictures.</summary>
    /// <param name="props">The props.</param>
    /// <param name="groups">Their colour groups.</param>
    internal void SetProps(IReadOnlyList<SetProp> props, IReadOnlyList<EngineColourGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(groups);
        Props = props;
        Groups = groups;
        foreach (string gone in _picks.Keys.Where(id => !groups.Any(g => g.Id == id)).ToList())
        {
            _picks.Remove(gone);
        }

        _boxes.Clear();
        _groupRows.Children.Clear();
        IReadOnlyDictionary<string, string> colours = Colours;
        foreach (EngineColourGroup group in groups)
        {
            var box = new ComboBox { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (EngineColourOption option in group.Options)
            {
                box.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
            }

            int at = group.Options.ToList().FindIndex(o => o.Value == colours[group.Id]);
            box.SelectedIndex = Math.Max(0, at);
            AutomationProperties.SetName(box, group.Label);
            Look.Chrome.Tip(box, string.Create(CultureInfo.InvariantCulture, $"{group.Props} {(group.Props == 1 ? "prop has" : "props have")} this. It starts on the one most of them use."));
            string id = group.Id;
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is ComboBoxItem { Tag: string value } && value != Colours[id])
                {
                    _picks[id] = value;
                    Redraw();
                }
            };
            _boxes.Add(box);
            _groupRows.Children.Add(Row(group.Label, box));
        }

        _hint.IsVisible = groups.Count > 0;
        ShowWhere();
        Redraw();
    }

    /// <summary>Picks a colour for a group, as its choice does.</summary>
    /// <param name="groupId">The group's id.</param>
    /// <param name="value">The colour's value.</param>
    internal void Pick(string groupId, string value)
    {
        int at = Groups.ToList().FindIndex(g => g.Id == groupId);
        ComboBox box = _boxes[at];
        box.SelectedIndex = Groups[at].Options.ToList().FindIndex(o => o.Value == value);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _drawing.Cancel();
        _drawing.Dispose();
    }

    // Upstream's: a prop made again in the set's colours, drawn small; on the engine's show-more turn.
    private static async Task<byte[]?> DrawAsync(SetProp prop, string colours, CancellationToken stop)
    {
        Settings s = await ExportSet.InSetColoursAsync(prop, colours, EngineJobKind.ShowMore, stop).ConfigureAwait(false);
        return await Task.Run(() => Pictures.MeshThumb.Png(prop.Recipe.Build(s, prop.Recipe.PictureDetail, Recipes.BuildPurpose.ShowMore, stop), ExportSet.PreviewSize), stop).ConfigureAwait(false);
    }

    private static TextBlock Line(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = Look.Theme.Label,
        Foreground = Look.Theme.TextSecondary,
    };

    private static StackPanel Row(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Look.Theme.Space2 };
        row.Children.Add(new TextBlock { Text = label, Width = 130, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = Look.Theme.Label });
        row.Children.Add(control);
        return row;
    }

    // A picture, or none where there is no platform to decode it (the headless tests).
    private static Bitmap? Decode(byte[] png)
    {
        try
        {
            using var stream = new MemoryStream(png, writable: false);
            return new Bitmap(stream);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            TraceLog.Write($"Export as a set: a picture could not be decoded: {e.Message}");
            return null;
        }
    }

    private async Task ChooseFolderAsync()
    {
        string? chosen = await _chooseFolder(Folder);
        if (chosen is not null)
        {
            Folder = chosen;
            ShowWhere();
        }
    }

    private void ShowWhere()
    {
        if (Folder is null)
        {
            FolderLine.Text = "Choose the folder the set goes in.";
            return;
        }

        string inside;
        try
        {
            inside = EngineHost.Default.Rules.ExportNames(ExportSet.NameOf(SetName)).Folder;
        }
        catch (Exception e) when (e is EngineException or TimeoutException or ObjectDisposedException or OperationCanceledException)
        {
            inside = ExportSet.NameOf(SetName);
        }

        FolderLine.Text = $"The set goes in {Path.Combine(Folder, inside.Length > 0 ? inside : "my_set")}";
    }

    // Stops the pictures not drawn yet and draws the first eight props again in the colours chosen now. A picture
    // already being drawn cannot be stopped halfway, so the new ones wait for it.
    private void Redraw()
    {
        if (_disposed)
        {
            return;
        }

        // Not disposed here: a picture still being drawn may yet look at its token.
        _drawing.Cancel();
        _drawing = new CancellationTokenSource();
        CancellationToken stop = _drawing.Token;
        SetProp[] shown = [.. Props.Take(ExportSet.Previews)];
        string colours = ExportSet.ColoursJson(Colours);
        _pictures.Children.Clear();
        var images = new Image[shown.Length];
        for (int i = 0; i < shown.Length; i++)
        {
            images[i] = new Image { Width = ExportSet.PreviewSize, Height = ExportSet.PreviewSize, Stretch = Stretch.Uniform, Margin = new Avalonia.Thickness(0, 0, 4, 4) };
            _pictures.Children.Add(new Border { Child = images[i], Background = Look.Theme.SurfaceSunken, CornerRadius = new Avalonia.CornerRadius(4) });
        }

        PreviewsQueued = shown.Length;
        PreviewsDrawn = 0;
        MostPreviewsQueued = Math.Max(MostPreviewsQueued, PreviewsQueued);
        Task before = _drawn;
        _drawn = DrawAfterAsync(before, shown, images, colours, stop);
    }

    private async Task DrawAfterAsync(Task before, SetProp[] shown, Image[] images, string colours, CancellationToken stop)
    {
        await before;
        for (int i = 0; i < shown.Length; i++)
        {
            if (stop.IsCancellationRequested)
            {
                return;
            }

            byte[]? png;
            try
            {
                png = await _draw(shown[i], colours, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                TraceLog.Write($"Export as a set: {shown[i].Name} not drawn: {e.Message}");
                png = null;
            }

            if (stop.IsCancellationRequested)
            {
                return;
            }

            PreviewsQueued--;
            PreviewsDrawn++;
            if (png is not null)
            {
                images[i].Source = Decode(png);
            }

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, CancellationToken.None);
        }
    }
}
