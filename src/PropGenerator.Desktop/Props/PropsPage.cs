using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Cards;

namespace PropGenerator.Desktop.Props;

/// <summary>
/// Your props (File, My props), after easy-props' page in our look: every saved prop as a row with its kind and when it
/// changed, a search box, a kind filter and the order, and on the right the picked prop's details and what can be done to
/// it, or to every ticked one (open, rename, copy, remove, export). Removed props wait in Recently removed, the page's
/// second view, for 30 days. A click picks a prop, Ctrl+click or its box ticks it, a double click opens it.
/// </summary>
internal sealed class PropsPage : Border
{
    private readonly MainWindow _w;
    private readonly YourProps _props;
    private readonly TextBox _search = new() { PlaceholderText = "Search your props", Width = 220 };
    private readonly ComboBox _kind = new() { MinWidth = 130 };
    private readonly ComboBox _sort = new() { MinWidth = 130 };
    private readonly Button _binButton;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly StackPanel _details = new() { Spacing = 6, Width = 280 };
    private readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Look.Theme.TextPrimary };
    private readonly HashSet<string> _ticked = new(StringComparer.Ordinal);
    private IReadOnlyList<PropSummary> _shown = [];
    private IReadOnlyList<RemovedProp> _removed = [];
    private string? _picked;
    private string? _kindShown;
    private bool _syncing;

    /// <summary>Builds the page for a window.</summary>
    /// <param name="w">The window: its props, its open piece and its dialogs.</param>
    internal PropsPage(MainWindow w)
    {
        ArgumentNullException.ThrowIfNull(w);
        _w = w;
        _props = w.Props;
        CardParts.Dress(this);
        Width = 860;
        Height = 560;

        Look.Chrome.Tip(_search, "Type part of a name, a kind (Rock, Grass…) or the starting point a prop was made from.");
        AutomationProperties.SetName(_search, "Search your props");
        _search.TextChanged += (_, _) => Refresh();
        Look.Chrome.Tip(_kind, "Show every kind of prop, or only one kind.");
        AutomationProperties.SetName(_kind, "Kind");
        _kind.SelectionChanged += (_, _) =>
        {
            if (!_syncing)
            {
                _kindShown = _kind.SelectedIndex <= 0 ? null : _kind.SelectedItem as string;
                Refresh();
            }
        };
        foreach (string s in new[] { "Last changed", "Name", "Date made" })
        {
            _sort.Items.Add(s);
        }

        _sort.SelectedIndex = (int)_w.Files.Prefs.PropsSort;
        Look.Chrome.Tip(_sort, "The order: the one changed last first, by name, or the one made last first. It is remembered.");
        AutomationProperties.SetName(_sort, "Order");
        _sort.SelectionChanged += (_, _) =>
        {
            if (_sort.SelectedIndex >= 0 && (PropSort)_sort.SelectedIndex != _w.Files.Prefs.PropsSort)
            {
                _w.Files.Prefs.PropsSort = (PropSort)_sort.SelectedIndex;
                _w.Files.Prefs.Save();
                Refresh();
            }
        };
        _binButton = Action("Recently removed", "See the props you removed: they wait there for 30 days, to be put back or removed for good.");

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _search, _kind, _sort, _binButton } };
        var list = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var side = new ScrollViewer { Content = _details, Margin = new Thickness(12, 0, 0, 0) };
        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(side, Dock.Right);
        body.Children.Add(side);
        body.Children.Add(list);
        DockPanel head = CardParts.Header("Your props", "Close Your props and go back to the piece (Esc).", w.Cards.Close);
        head.Children.RemoveAt(head.Children.Count - 1);
        head.Children.Add(_title);
        var page = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(bar, Dock.Top);
        bar.Margin = new Thickness(0, 10);
        page.Children.Add(head);
        page.Children.Add(bar);
        page.Children.Add(body);
        Child = page;
        Refresh();
    }

    /// <summary>Gets a value indicating whether Recently removed is shown (else Your props).</summary>
    internal bool ShowingBin { get; private set; }

    /// <summary>Gets the names of the rows shown, in order.</summary>
    internal IReadOnlyList<string> Names => ShowingBin ? [.. _removed.Select(r => r.Name)] : [.. _shown.Select(p => p.Name)];

    /// <summary>Gets what the details side says, line by line.</summary>
    internal string Details => string.Join("\n", _details.Children.OfType<TextBlock>().Select(t => t.Text));

    /// <summary>Gets what the Recently removed button says, such as "Recently removed (2)".</summary>
    internal string BinLabel => _binButton.Content as string ?? string.Empty;

    /// <summary>Types into the search box.</summary>
    /// <param name="text">What it then says.</param>
    internal void Search(string text)
    {
        _search.Text = text;
        Refresh();
    }

    /// <summary>Chooses the order.</summary>
    /// <param name="sort">The order.</param>
    internal void SortBy(PropSort sort) => _sort.SelectedIndex = (int)sort;

    /// <summary>Shows one kind only, or every kind.</summary>
    /// <param name="kind">The kind's label, or null for all.</param>
    internal void ShowKind(string? kind)
    {
        _kindShown = kind;
        Refresh();
    }

    /// <summary>Picks a row, by its name (as a click does): the details show it and the ticks are kept.</summary>
    /// <param name="name">The name.</param>
    internal void Pick(string name)
    {
        _picked = ShowingBin ? _removed.First(r => r.Name == name).Id : _shown.First(p => p.Name == name).Path;
        Refresh();
    }

    /// <summary>Gets the work file of the prop picked (its details shown), or null when none is.</summary>
    internal string? Picked => ShowingBin ? null : _picked;

    /// <summary>Picks a prop by its work file, when it is listed: the details show it.</summary>
    /// <param name="path">The work file.</param>
    internal void PickPath(string path)
    {
        string full = Path.GetFullPath(path);
        if (_shown.Any(p => p.Path == full))
        {
            _picked = full;
            Refresh();
        }
    }

    /// <summary>Ticks or unticks a row, by its name (as Ctrl+click or its box does).</summary>
    /// <param name="name">The name.</param>
    internal void Tick(string name)
    {
        string path = _shown.First(p => p.Name == name).Path;
        if (!_ticked.Remove(path))
        {
            _ticked.Add(path);
        }

        Refresh();
    }

    /// <summary>Shows Recently removed, or Your props again.</summary>
    /// <param name="bin">True for Recently removed.</param>
    internal void ShowBin(bool bin)
    {
        ShowingBin = bin;
        _picked = null;
        _ticked.Clear();
        Refresh();
    }

    /// <summary>Does what one of the page's buttons does, by what it says.</summary>
    /// <param name="action">The button: Open, Rename…, Copy, Remove, Export…, Send to Groundwork, Clear the ticks, Undo, Recently removed, Put back, Remove for good, Put all back, Empty the bin or Back to Your props.</param>
    /// <returns>Done when it is done.</returns>
    internal async Task RunAsync(string action)
    {
        _w.Acted();
        try
        {
            await DoAsync(action);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _w.Say(e.Message, error: true);
        }

        // What the action wrote (a copy, a prop put back) is read off the window's thread too.
        await _props.LoadAsync();
        Refresh();
    }

    /// <summary>Reads the props again and shows them.</summary>
    internal void Refresh()
    {
        if (ShowingBin)
        {
            _removed = _props.Bin.List();
            if (_picked is not null && _removed.All(r => r.Id != _picked))
            {
                _picked = null;
            }
        }
        else
        {
            _shown = _props.List(_search.Text, _w.Files.Prefs.PropsSort, _kindShown);
            HashSet<string> paths = [.. _shown.Select(p => p.Path)];
            _ticked.IntersectWith(paths);
            if (_picked is not null && !paths.Contains(_picked))
            {
                _picked = null;
            }
        }

        _title.Text = ShowingBin ? "Recently removed" : "Your props";
        _binButton.Content = ShowingBin ? "Back to Your props" : $"Recently removed ({_props.Bin.List().Count})";
        _search.IsVisible = _kind.IsVisible = _sort.IsVisible = !ShowingBin;
        Kinds();
        Rows();
        Side();
    }

    private static string When(DateTimeOffset t)
    {
        TimeSpan ago = DateTimeOffset.UtcNow - t;
        return ago.TotalMinutes < 1 ? "just now"
            : ago.TotalHours < 1 ? $"{(int)ago.TotalMinutes} min ago"
            : ago.TotalHours < 2 ? "1 hour ago"
            : ago.TotalDays < 1 ? $"{(int)ago.TotalHours} hours ago"
            : ago.TotalDays < 2 ? "yesterday"
            : t.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    private static string Bytes(long n) => n < 1024 ? $"{n} bytes"
        : n < 1024 * 1024 ? string.Create(CultureInfo.InvariantCulture, $"{n / 1024.0:0} KB")
        : string.Create(CultureInfo.InvariantCulture, $"{n / (1024.0 * 1024):0.0} MB");

    private static TextBlock Line(string text, IBrush? brush = null) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = Look.Theme.Label, Foreground = brush ?? Look.Theme.TextSecondary };

    private Button Action(string text, string tip)
    {
        Button b = Look.Chrome.TextButton(text, tip);
        AutomationProperties.SetName(b, text);
        b.Click += async (_, _) => await RunAsync(b.Content as string ?? text);
        return b;
    }

    private void Kinds()
    {
        _syncing = true;
        string[] items = ["All kinds", .. _props.Kinds()];
        if (!_kind.Items.Cast<string>().SequenceEqual(items))
        {
            _kind.Items.Clear();
            foreach (string k in items)
            {
                _kind.Items.Add(k);
            }
        }

        _kind.SelectedIndex = _kindShown is null ? 0 : Math.Max(0, Array.IndexOf(items, _kindShown));
        _syncing = false;
    }

    private void Rows()
    {
        _rows.Children.Clear();
        if (ShowingBin)
        {
            if (_removed.Count == 0)
            {
                _rows.Children.Add(Line("Removed props wait here for 30 days, then they're gone for good. Nothing is here now."));
            }

            foreach (RemovedProp r in _removed)
            {
                _rows.Children.Add(Row(r.Id, r.Name, $"Removed {When(r.RemovedAt)}", null));
            }

            return;
        }

        if (_shown.Count == 0)
        {
            _rows.Children.Add(Line((_search.Text ?? string.Empty).Trim().Length > 0
                ? "No prop by that name. Try a shorter word."
                : _kindShown is not null ? "None of your props are of this kind yet." : "Props you save show up here."));
        }

        foreach (PropSummary p in _shown)
        {
            _rows.Children.Add(Row(p.Path, p.Name, $"{p.KindLabel} · {When(p.Changed)}", p));
        }
    }

    private Border Row(string key, string name, string note, PropSummary? prop)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        if (prop is not null)
        {
            var tick = new CheckBox { IsChecked = _ticked.Contains(key), MinWidth = 0 };
            AutomationProperties.SetName(tick, $"Tick {name}");
            Look.Chrome.Tip(tick, "Tick it to act on several props at once (Export…, Remove).");
            tick.IsCheckedChanged += (_, _) =>
            {
                if (tick.IsChecked == true ? _ticked.Add(key) : _ticked.Remove(key))
                {
                    Side();
                }
            };
            grid.Children.Add(tick);
        }

        var label = new TextBlock { Text = name, Foreground = Look.Theme.TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        TextBlock right = Line(note, Look.Theme.TextTertiary);
        right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        bool editing = prop is not null && prop.Path == _w.Files.FilePath;
        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(8, 5),
            CornerRadius = new CornerRadius(Look.Theme.Radius2),
            Background = key == _picked ? Look.Theme.SurfaceSelected : Brushes.Transparent,
            BorderBrush = editing ? Look.Theme.Accent : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        AutomationProperties.SetName(row, name);
        ToolTip.SetTip(row, prop is null
            ? $"{name}\nWas at {_removed.First(r => r.Id == key).OriginalPath}\nClick: pick it"
            : $"{name}\n{prop.Path}\nClick: pick it · Ctrl+click: tick it · double click: open it");
        row.PointerPressed += (_, e) =>
        {
            if (prop is not null && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                Tick(name);
            }
            else if (prop is not null && e.ClickCount == 2)
            {
                _picked = key;
                _ = RunAsync("Open");
            }
            else
            {
                _picked = key;
                Refresh();
            }

            e.Handled = true;
        };
        return row;
    }

    private void Side()
    {
        _details.Children.Clear();
        if (ShowingBin)
        {
            _details.Children.Add(Line("Removed props wait here for 30 days, then they're gone for good."));
            if (_removed.FirstOrDefault(r => r.Id == _picked) is { } r)
            {
                _details.Children.Add(Line(r.Name, Look.Theme.TextPrimary));
                _details.Children.Add(Line($"Removed {When(r.RemovedAt)}"));
                _details.Children.Add(Line($"Goes back to {r.OriginalPath}"));
                _details.Children.Add(Action("Put back", "Put this prop back where it was, in Your props."));
                _details.Children.Add(Action("Remove for good", "Remove this prop for good. Asks first: it cannot be undone."));
            }

            if (_removed.Count > 1)
            {
                _details.Children.Add(Action("Put all back", "Put every removed prop back where it was."));
            }

            if (_removed.Count > 0)
            {
                _details.Children.Add(Action("Empty the bin", "Remove every prop here for good. Asks first: it cannot be undone."));
            }

            return;
        }

        if (_w.LastRemoved is { Count: > 0 } last)
        {
            _details.Children.Add(Line(last.Count == 1 ? $"Removed \"{last[0].Name}\"" : $"Removed {last.Count} props", Look.Theme.Warning));
            _details.Children.Add(Action("Undo", "Put back what you just removed."));
        }

        if (_ticked.Count > 0)
        {
            _details.Children.Add(Line(_ticked.Count == 1 ? "1 prop" : $"{_ticked.Count} props", Look.Theme.TextPrimary));
            _details.Children.Add(Action("Export…", "Export every ticked prop to the Groundwork map folder, without opening them."));
            _details.Children.Add(Action("Send to Groundwork", "Send every ticked prop to the map open in Groundwork, in one delivery, without opening them."));
            if (_ticked.Count > 1)
            {
                _details.Children.Add(Action("Export as a set…", "Export the ticked props as one set: matched to the same colours, with materials an engine can share, into a folder of its own."));
            }

            _details.Children.Add(Action("Remove", "Move every ticked prop into Recently removed, where it waits 30 days."));
            _details.Children.Add(Action("Clear the ticks", "Untick every prop."));
            return;
        }

        if (_shown.FirstOrDefault(p => p.Path == _picked) is not { } prop)
        {
            _details.Children.Add(Line("Click a prop to see it here. Ctrl+click, or its box, ticks several."));
            return;
        }

        _details.Children.Add(Line(prop.Name + (prop.Path == _w.Files.FilePath ? " (editing)" : string.Empty), Look.Theme.TextPrimary));
        _details.Children.Add(Line(prop.StartedFrom is { } from ? $"{prop.KindLabel}, from {from}" : prop.KindLabel));
        _details.Children.Add(Line($"Made {prop.Made.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)}"));
        _details.Children.Add(Line($"Changed {When(prop.Changed)}"));
        if (prop.Seed is long seed)
        {
            _details.Children.Add(Line(string.Create(CultureInfo.InvariantCulture, $"Seed {seed}")));
        }

        _details.Children.Add(Line($"Size {Bytes(prop.Size)}"));
        _details.Children.Add(Line(prop.Path, Look.Theme.TextTertiary));
        if (prop.Readable)
        {
            _details.Children.Add(Action("Open", "Open this prop to work on it (asks first when the piece open now has unsaved changes)."));
            _details.Children.Add(Action("Export…", "Export this prop to the Groundwork map folder without opening it."));
            _details.Children.Add(Action("Send to Groundwork", "Send this prop to the map open in Groundwork without opening it."));
            _details.Children.Add(Action("Copy", "Make a copy of this prop beside it, called \"<name> copy\"."));
        }

        _details.Children.Add(Action("Rename…", "Give this prop a new name (its work file is renamed in its folder)."));
        _details.Children.Add(Action("Remove", "Move this prop into Recently removed, where it waits 30 days."));
    }

    private IReadOnlyList<string> Chosen() =>
        _ticked.Count > 0 ? [.. _shown.Where(p => _ticked.Contains(p.Path)).Select(p => p.Path)] : _picked is null ? [] : [_picked];

    private async Task DoAsync(string action)
    {
        IDialogs dialogs = _w.Files.Dialogs;
        switch (action)
        {
            case "Recently removed":
                ShowBin(true);
                return;
            case "Back to Your props":
                ShowBin(false);
                return;
            case "Clear the ticks":
                _ticked.Clear();
                return;
            case "Undo":
                _w.UndoRemove();
                return;
            case "Open" when _picked is { } open:
                _w.Cards.Close();
                await _w.Files.OpenPathAsync(open);
                return;
            case "Copy" when _picked is { } copy:
                string made = _props.Duplicate(copy);
                _picked = made;
                _w.Say($"Copied as \"{Path.GetFileNameWithoutExtension(made)}\"");
                _w.Drawer.RefreshMine();
                return;
            case "Rename…" when _picked is { } path:
                await RenameAsync(dialogs, path);
                return;
            case "Remove":
                _w.RemoveProps(Chosen());
                return;
            case "Export…":
                await _w.Files.ExportFilesAsync(Chosen());
                return;
            case "Send to Groundwork":
                await _w.Files.SendFilesToGroundworkAsync(Chosen());
                return;
            case "Export as a set…":
                OpenSet(Chosen());
                return;
            case "Put back" when _picked is { } id:
                RemovedProp back = _removed.First(r => r.Id == id);
                _props.PutBack([back]);
                _w.Say($"Put back \"{back.Name}\"");
                _w.Drawer.RefreshMine();
                return;
            case "Put all back":
                _props.PutBack(_removed);
                _w.Say(_removed.Count == 1 ? $"Put back \"{_removed[0].Name}\"" : $"Put back {_removed.Count} props");
                _w.Drawer.RefreshMine();
                return;
            case "Remove for good" when _picked is { } id:
                RemovedProp gone = _removed.First(r => r.Id == id);
                if (await dialogs.ConfirmAsync($"Remove “{gone.Name}” for good? This can't be undone.", "Remove for good", "Keep it"))
                {
                    _props.Bin.Forget(id);
                    _w.Say($"Removed \"{gone.Name}\" for good");
                }

                return;
            case "Empty the bin" when _removed.Count > 0:
                int n = _removed.Count;
                if (await dialogs.ConfirmAsync(n == 1 ? $"Remove “{_removed[0].Name}” for good? This can't be undone." : $"Remove all {n} props for good? This can't be undone.", "Empty the bin", "Keep them"))
                {
                    _props.Bin.Empty();
                    _w.Say(n == 1 ? "Emptied the bin" : $"Emptied the bin of {n} props");
                }

                return;
            default:
                return;
        }
    }

    // Export as a set of the ticked props: the easy-props kinds among them, the rest listed as left out.
    private void OpenSet(IReadOnlyList<string> paths)
    {
        (IReadOnlyList<Batch.SetProp> props, IReadOnlyList<string> leftOut) = FileActions.SetPropsOf(paths);
        if (props.Count == 0)
        {
            _w.Say($"None of these can be in a set: {string.Join("; ", leftOut)}.", error: true);
            return;
        }

        IReadOnlyList<PropGenerator.Engine.EngineColourGroup> groups;
        try
        {
            groups = Batch.ExportSet.Groups(props);
        }
        catch (Exception e) when (e is PropGenerator.Engine.EngineException or TimeoutException or ObjectDisposedException or OperationCanceledException)
        {
            _w.Say($"Export as a set is not available: easy-props' engine did not answer ({e.Message}).", error: true);
            return;
        }

        var section = new SetColoursSection(props, groups, _w.Versions.SetFolder(), _w.Versions.ChooseSetFolderAsync, leftOut);
        ExportSetCard? card = null;
        card = new ExportSetCard(
            section,
            (set, folder) => _ = ExportSetAsync(props, set, folder),
            () =>
            {
                if (ReferenceEquals(_w.Cards.Shown, card))
                {
                    _w.Cards.Close();
                }

                card?.Dispose();
            });
        SetCard = card;
        _w.Cards.Show(card);
    }

    /// <summary>Gets the last Export as a set card this page opened (for the tests), or null.</summary>
    internal ExportSetCard? SetCard { get; private set; }

    /// <summary>Gets the last set export this page started (for the tests), or null.</summary>
    internal Task<int>? SetExport { get; private set; }

    private Task<int> ExportSetAsync(IReadOnlyList<Batch.SetProp> props, Batch.SetChoice set, string folder)
    {
        SetExport = NamedThenExportAsync(props, set, folder);
        return SetExport;
    }

    private async Task<int> NamedThenExportAsync(IReadOnlyList<Batch.SetProp> props, Batch.SetChoice set, string folder)
    {
        IReadOnlyList<Batch.SetProp> named;
        try
        {
            named = await Batch.ExportSet.NamedAsync(props).ConfigureAwait(true);
        }
        catch (Exception e) when (e is PropGenerator.Engine.EngineException or TimeoutException or ObjectDisposedException or OperationCanceledException)
        {
            _w.Say($"Nothing was exported: easy-props' engine could not name the props ({e.Message}).", error: true);
            return 0;
        }

        return await _w.Files.ExportSetAsync(named, set, folder).ConfigureAwait(true);
    }

    private async Task RenameAsync(IDialogs dialogs, string path)
    {
        string old = Path.GetFileNameWithoutExtension(path);
        string? typed = await dialogs.AskTextAsync($"Rename {old}", "Name", old);
        if (typed is null)
        {
            return;
        }

        if (YourProps.NameProblem(typed, out string name) is string problem)
        {
            _w.Say(problem, error: true);
            return;
        }

        if (name.Length == 0 || name == old)
        {
            return;
        }

        if (path == _w.Files.FilePath)
        {
            // The open piece is renamed through its file, so the title and the next save follow.
            if (await _w.Files.RenameAsync(name))
            {
                _picked = _w.Files.FilePath;
                _w.Say($"Renamed to \"{name}\"");
            }

            return;
        }

        (string? renamed, string? error) = _props.Rename(path, name);
        if (error is not null)
        {
            _w.Say(error, error: true);
            return;
        }

        _picked = renamed;
        _w.Say($"Renamed to \"{name}\"");
        _w.Drawer.RefreshMine();
    }
}
