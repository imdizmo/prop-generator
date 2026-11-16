// Adapted from easy-props frontend/src/panels/VariationGrid.tsx (MIT, © 2026 AHS12).
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PropGenerator.Desktop.Batch;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Engine;
using PropGenerator.Nature.Random;

namespace PropGenerator.Desktop.Cards;

/// <summary>What Show me more shows around the prop.</summary>
internal enum ShowMoreMode
{
    /// <summary>The same settings on new seeds.</summary>
    Seeds,

    /// <summary>The prop on its own seed, broken in different ways (a prop with damage settings only).</summary>
    Damaged,
}

/// <summary>
/// Show me more: the prop as it is now, in the middle of eight others on new seeds (easy-props' page seeds), or, for a
/// prop with damage settings, eight damaged versions of it on its own seed (upstream's page damages; plan E10), chosen
/// with the New seeds / Damaged switch. Picking is easier than describing: a click on one takes it (its seed, or its
/// damage, one undo step) and closes the card; More shows a new page of eight. Each version can be ticked, and Export
/// these sends the ticked versions to Export many. Versions are told apart by upstream's <c>versionKey</c>. The pictures
/// are drawn one at a time off the window's thread (a page asked for while another is drawing waits for its picture in
/// hand), from the settings as they were when the card opened: a new seed without the part edits but with a painted
/// tree's own edits (as easy-props' withSeed), a damaged version with them all (as its versionScene); closing stops the
/// rest. The pictures drawn are kept while the card is open, up to <see cref="KeptPictures"/>.
/// </summary>
internal sealed class ShowMoreCard : Border, IDisposable
{
    /// <summary>How large each picture is drawn, in pixels.</summary>
    internal const int PictureSize = 104;

    /// <summary>The most pictures kept while the card is open (five pages): the oldest go first.</summary>
    internal const int KeptPictures = 45;

    // The share of full detail the pictures are built at: the middle level, enough to tell them apart.
    private const double PictureDetail = 0.4;

    private readonly IMeshRecipe _recipe;
    private readonly Settings _base;
    private readonly Action<PropVersion> _use;
    private readonly Action<IReadOnlyList<PropVersion>> _export;
    private readonly Action _close;
    private readonly DamagedVersions? _damaged;
    private readonly Dictionary<string, byte[]> _pictures = [];
    private readonly LinkedList<string> _order = new();
    private readonly OrderedDictionary<string, PropVersion> _ticked = [];
    private readonly UniformGrid _grid = new() { Columns = 3 };
    private readonly TextBlock _progress = new() { FontSize = Look.Theme.Label, Foreground = Look.Theme.TextSecondary };
    private readonly Button _tickAll;
    private readonly Button _exportThese;
    private CancellationTokenSource _drawing = new();
    private bool _disposed;
    private string[] _keys = [];

    /// <summary>Makes the card for a prop and starts drawing its first page.</summary>
    /// <param name="recipe">The prop's kind.</param>
    /// <param name="settings">Its settings now.</param>
    /// <param name="name">What the prop is called, for the progress line.</param>
    /// <param name="use">Takes a version: its seed, or its damage (one undo step).</param>
    /// <param name="export">Opens Export many with the ticked versions.</param>
    /// <param name="close">Closes the card.</param>
    /// <param name="damaged">The prop's damaged versions, or null when it has no damage settings (no switch then).</param>
    internal ShowMoreCard(IMeshRecipe recipe, Settings settings, string name, Action<PropVersion> use, Action<IReadOnlyList<PropVersion>> export, Action close, DamagedVersions? damaged = null)
    {
        _recipe = recipe;
        _base = settings;
        _use = use;
        _export = export;
        _close = close;
        _damaged = damaged;
        Name = name;
        Seed = settings.GetSeed("seed");

        NewSeedsButton = ModeChip("New seeds", "Show the same settings on new seeds, eight at a time.", ShowMoreMode.Seeds);
        DamagedButton = ModeChip("Damaged", "Show this prop on its own seed, broken in different ways, eight at a time.", ShowMoreMode.Damaged);
        ModeSwitch = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, IsVisible = damaged is not null };
        ModeSwitch.Children.Add(NewSeedsButton);
        ModeSwitch.Children.Add(DamagedButton);
        AutomationProperties.SetName(ModeSwitch, "Versions to show");

        var more = Look.Chrome.IconTextButton("layout-grid", "More", "Show eight more versions.");
        more.Click += (_, _) => ShowPage(Page + 1);
        _tickAll = Look.Chrome.TextButton("Tick all", "Tick every version on this page, to export them.");
        _tickAll.Click += (_, _) => TickAll();
        var closeButton = Look.Chrome.TextButton("Close", "Close and keep the prop as it is (Esc).");
        closeButton.Click += (_, _) => Close();
        _exportThese = Look.Chrome.PrimaryButton("file-down", "Export these", "Export the ticked versions, each as its own model, with Export many.");
        _exportThese.Click += (_, _) => ExportTicked();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Look.Theme.Space2, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(more);
        buttons.Children.Add(_tickAll);
        buttons.Children.Add(closeButton);
        buttons.Children.Add(_exportThese);

        var body = new StackPanel { Spacing = Look.Theme.Space3 };
        body.Children.Add(CardParts.Header("Show me more", "Close and keep the prop as it is (Esc).", Close));
        body.Children.Add(ModeSwitch);
        body.Children.Add(_progress);
        body.Children.Add(_grid);
        body.Children.Add(buttons);
        Child = body;
        Width = (3 * (PictureSize + 12)) + 40;
        CardParts.Dress(this);
        ShowPage(0);
    }

    /// <summary>Gets the seed of the prop as it is now, shown in the middle.</summary>
    internal uint Seed { get; }

    /// <summary>Gets the page shown: 0 first, one more for each More.</summary>
    internal int Page { get; private set; }

    /// <summary>Gets what is shown around the prop: new seeds, or damaged versions.</summary>
    internal ShowMoreMode Mode { get; private set; }

    /// <summary>Gets a value indicating whether the prop has damage settings, so damaged versions are offered.</summary>
    internal bool OffersDamaged => _damaged is not null;

    /// <summary>Gets the New seeds / Damaged switch (shown only when damaged versions are offered).</summary>
    internal StackPanel ModeSwitch { get; }

    /// <summary>Gets the switch's New seeds chip.</summary>
    internal ToggleButton NewSeedsButton { get; }

    /// <summary>Gets the switch's Damaged chip.</summary>
    internal ToggleButton DamagedButton { get; }

    /// <summary>Gets the nine versions shown, left to right and top to bottom, the prop as it is in the middle.</summary>
    internal IReadOnlyList<PropVersion> Versions { get; private set; } = [];

    /// <summary>Gets the nine seeds shown, in the order of <see cref="Versions"/> (all the prop's own on a damaged page).</summary>
    internal IReadOnlyList<uint> Seeds => [.. Versions.Select(static v => v.Seed)];

    /// <summary>Gets what each version is called, in the order of <see cref="Versions"/>: "Seed 12", or its damage in upstream's words.</summary>
    internal IReadOnlyList<string> Captions { get; private set; } = [];

    /// <summary>Gets the versions ticked for export, on every page and in both modes, in the order ticked.</summary>
    internal IReadOnlyCollection<PropVersion> Ticked => _ticked.Values;

    /// <summary>Gets how many of the page's pictures are drawn.</summary>
    internal int Drawn => _keys.Count(_pictures.ContainsKey);

    /// <summary>Gets the task drawing the page's pictures (done when all are, or the card closed).</summary>
    internal Task Drawing { get; private set; } = Task.CompletedTask;

    /// <summary>Gets the version buttons of the page, in the order of <see cref="Versions"/>.</summary>
    internal IReadOnlyList<Button> Tiles => [.. _grid.Children.OfType<Panel>().Select(p => p.Children.OfType<Button>().First())];

    /// <summary>Gets the tick boxes of the page, in the order of <see cref="Versions"/>.</summary>
    internal IReadOnlyList<CheckBox> Ticks => [.. _grid.Children.OfType<Panel>().Select(p => p.Children.OfType<CheckBox>().First())];

    /// <summary>A version's key (upstream's <c>versionKey</c>): the same each time the version is made.</summary>
    /// <param name="version">The version.</param>
    /// <returns>The key.</returns>
    internal string KeyOf(PropVersion version) => DamagedVersions.KeyOf(version, _damaged);

    /// <summary>Switches between new seeds and damaged versions, from the first page; damaged only when offered.</summary>
    /// <param name="mode">What to show.</param>
    internal void ShowMode(ShowMoreMode mode)
    {
        if (mode == ShowMoreMode.Damaged && _damaged is null)
        {
            return;
        }

        Mode = mode;
        ShowPage(0);
    }

    /// <summary>Shows a page of versions and draws its pictures.</summary>
    /// <param name="page">The page.</param>
    internal void ShowPage(int page)
    {
        Page = page;
        string? failed = null;
        if (Mode == ShowMoreMode.Damaged && _damaged is not null)
        {
            try
            {
                IReadOnlyList<PropVersion> others = _damaged.Page(Seed, page);
                PropVersion now = new(Seed);
                Versions = [.. others.Take(4), now, .. others.Skip(4)];
                Captions = [.. Versions.Select(v => v.Damage is null ? SeedCaption(v.Seed) : _damaged.Names(v))];
                _keys = [.. Versions.Select(KeyOf)];
            }
            catch (Exception e) when (e is EngineException or TimeoutException or ObjectDisposedException or OperationCanceledException)
            {
                // The engine did not answer: back to new seeds, which need nothing of it.
                failed = $"Damaged versions are not available: easy-props' engine did not answer ({e.Message}).";
                Mode = ShowMoreMode.Seeds;
            }
        }

        if (Mode == ShowMoreMode.Seeds)
        {
            Versions = [.. Variations.Grid(Seed, page).Select(static seed => new PropVersion(seed))];
            Captions = [.. Versions.Select(static v => SeedCaption(v.Seed))];
            _keys = [.. Versions.Select(KeyOf)];
        }

        _grid.Children.Clear();
        for (int i = 0; i < Versions.Count; i++)
        {
            _grid.Children.Add(Tile(Versions[i], _keys[i], Captions[i]));
        }

        Refresh();
        if (failed is not null)
        {
            _progress.Text = failed;
        }

        if (_disposed)
        {
            return;
        }

        _drawing.Cancel();
        _drawing.Dispose();
        _drawing = new CancellationTokenSource();
        Drawing = DrawAfterAsync(Drawing, [.. Versions.Zip(_keys)], _drawing.Token);
    }

    /// <summary>Takes a version: its seed or its damage becomes the prop's (one undo step), and the card closes.</summary>
    /// <param name="version">The version.</param>
    internal void Use(PropVersion version)
    {
        Stop();
        _use(version);
        _close();
    }

    /// <summary>Ticks or unticks a version for export.</summary>
    /// <param name="version">The version.</param>
    internal void Toggle(PropVersion version)
    {
        string key = KeyOf(version);
        if (!_ticked.Remove(key))
        {
            _ticked.Add(key, version);
        }

        Refresh();
    }

    /// <summary>Ticks every version of the page, or unticks them when all are ticked; other pages' ticks stay.</summary>
    internal void TickAll()
    {
        bool all = _keys.All(_ticked.ContainsKey);
        for (int i = 0; i < _keys.Length; i++)
        {
            if (all)
            {
                _ticked.Remove(_keys[i]);
            }
            else
            {
                _ticked.TryAdd(_keys[i], Versions[i]);
            }
        }

        Refresh();
    }

    /// <summary>Sends the ticked versions to Export many and closes; with none ticked, says so.</summary>
    internal void ExportTicked()
    {
        if (_ticked.Count == 0)
        {
            _progress.Text = "Tick the versions to export first.";
            return;
        }

        Stop();
        IReadOnlyList<PropVersion> versions = [.. _ticked.Values];
        _close();
        _export(versions);
    }

    /// <summary>Stops drawing and closes the card.</summary>
    internal void Close()
    {
        Stop();
        _close();
    }

    /// <summary>Stops drawing and lets go of what the drawing holds.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _drawing.Cancel();
            _drawing.Dispose();
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Stop()
    {
        if (!_disposed)
        {
            _drawing.Cancel();
        }
    }

    private static string SeedCaption(uint seed) => string.Create(CultureInfo.InvariantCulture, $"Seed {seed}");

    private ToggleButton ModeChip(string text, string tip, ShowMoreMode mode)
    {
        var chip = new ToggleButton { Content = text, FontSize = 11.5, Padding = new Thickness(9, 3), MinHeight = 0 };
        chip.Classes.Add("ep-toggle");
        Look.Chrome.Tip(chip, tip);
        AutomationProperties.SetName(chip, text);
        chip.Click += (_, _) => ShowMode(mode);
        return chip;
    }

    private StackPanel Tile(PropVersion version, string key, string caption)
    {
        bool now = version.Damage is null && version.Seed == Seed;
        string seedText = version.Seed.ToString(CultureInfo.InvariantCulture);
        bool damaged = version.Damage is not null;
        var picture = new Image { Width = PictureSize, Height = PictureSize, Stretch = Stretch.Uniform };
        var button = new Button
        {
            Content = picture,
            Padding = new Thickness(2),
            Background = Look.Theme.SurfaceRaised,
            BorderBrush = now ? Look.Theme.AccentText : Look.Theme.BorderSubtle,
            BorderThickness = new Thickness(now ? 2 : 1),
            CornerRadius = new CornerRadius(Look.Theme.Radius3),
        };
        AutomationProperties.SetName(button, now ? $"Keep this one, seed {seedText}" : damaged ? $"Use this damaged version: {caption}" : $"Use seed {seedText}");
        Look.Chrome.Tip(button, now ? $"The prop as it is now, seed {seedText}. Click to keep it." : $"{caption}. Click to use this one.");
        button.Click += (_, _) => Use(version);
        var tick = new CheckBox
        {
            IsChecked = _ticked.ContainsKey(key),
            Content = new TextBlock { Text = now ? "This one" : damaged ? caption : seedText, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = PictureSize - 24 },
            FontSize = Look.Theme.Caption,
        };
        AutomationProperties.SetName(tick, damaged ? $"Export this damaged version: {caption}" : $"Export seed {seedText}");
        Look.Chrome.Tip(tick, damaged ? $"Tick to export this damaged version ({caption}) with Export these." : $"Tick to export seed {seedText} with Export these.");
        tick.IsCheckedChanged += (_, _) =>
        {
            if ((tick.IsChecked == true) != _ticked.ContainsKey(key))
            {
                Toggle(version);
            }
        };
        if (_pictures.TryGetValue(key, out byte[]? png))
        {
            picture.Source = Decode(png);
        }

        var tile = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 10, 4), Tag = key };
        tile.Children.Add(button);
        tile.Children.Add(tick);
        return tile;
    }

    private void Refresh()
    {
        int drawn = Drawn;
        _progress.Text = drawn < _keys.Length
            ? string.Create(CultureInfo.InvariantCulture, $"Drawing versions of {Name}, {drawn} of {_keys.Length}")
            : "Click one to use it, or tick some and export them.";
        if (_tickAll.Content is TextBlock all)
        {
            all.Text = _keys.Length > 0 && _keys.All(_ticked.ContainsKey) ? "Untick all" : "Tick all";
        }

        NewSeedsButton.IsChecked = Mode == ShowMoreMode.Seeds;
        DamagedButton.IsChecked = Mode == ShowMoreMode.Damaged;

        Look.Chrome.Tip(_exportThese, _ticked.Count == 0 ? "Tick the versions to export first." : $"Export the {_ticked.Count} ticked versions, each as its own model.");
        foreach (CheckBox tick in Ticks)
        {
            string key = (string)((Panel)tick.Parent!).Tag!;
            tick.IsChecked = _ticked.ContainsKey(key);
        }
    }

    // Draws a page once the page before it has stopped: a build already running cannot be cancelled, so More clicked
    // again and again waits for it instead of starting another; a page passed over meanwhile is never drawn.
    private async Task DrawAfterAsync(Task before, IReadOnlyList<(PropVersion Version, string Key)> versions, CancellationToken stop)
    {
        await before;
        await DrawAsync(versions, stop);
    }

    /// <summary>
    /// The settings a version's picture is drawn from: a seed version as easy-props' withSeed makes it (part edits
    /// dropped, a painted tree's own edits kept), a damaged one as the prop with that damage.
    /// </summary>
    /// <param name="basis">The prop's settings when the card opened.</param>
    /// <param name="version">The version.</param>
    /// <returns>Its settings.</returns>
    internal static Settings SettingsOf(Settings basis, PropVersion version)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(version);
        return version.Damage is null ? PartEditsSettings.OnSeed(basis, version.Seed) : version.Damaged(basis);
    }

    private async Task DrawAsync(IReadOnlyList<(PropVersion Version, string Key)> versions, CancellationToken stop)
    {
        foreach ((PropVersion version, string key) in versions)
        {
            if (stop.IsCancellationRequested)
            {
                return;
            }

            if (_pictures.ContainsKey(key))
            {
                continue;
            }

            uint seed = version.Seed;
            Settings settings = SettingsOf(_base, version);
            byte[] png;
            try
            {
                png = await Task.Run(() => Pictures.MeshThumb.Png(_recipe.Build(settings, PictureDetail, BuildPurpose.ShowMore, stop), PictureSize), stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                TraceLog.Write($"Show me more: version {key} not drawn: {e.Message}");
                continue;
            }

            if (stop.IsCancellationRequested)
            {
                return;
            }

            Keep(key, png);
            if (_grid.Children.OfType<Panel>().FirstOrDefault(p => (string)p.Tag! == key)?.Children.OfType<Button>().First().Content is Image image)
            {
                image.Source = Decode(png);
            }

            Refresh();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, CancellationToken.None);
        }
    }

    private void Keep(string key, byte[] png)
    {
        _pictures[key] = png;
        _order.AddLast(key);
        while (_order.Count > KeptPictures)
        {
            _pictures.Remove(_order.First!.Value);
            _order.RemoveFirst();
        }
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
            TraceLog.Write($"Show me more: a picture could not be decoded: {e.Message}");
            return null;
        }
    }
}
