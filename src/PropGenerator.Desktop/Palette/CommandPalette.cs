// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Look;
using PropGenerator.Desktop.Recipes;

namespace PropGenerator.Desktop.Searching;

/// <summary>
/// The command palette (Ctrl+K): one box that finds anything. Empty, it offers what is useful right now. Typing
/// searches the actions, the controls, the starting points, the saved props and the settings, in sections, and Enter
/// runs the top row. Searching only reads lists, so it never builds anything and stays instant. A choice opens its
/// options in a sub-list; a row that cannot run says why in the footer.
/// </summary>
internal sealed class CommandPalette : Border
{
    private static readonly string[] SuggestedIds = ["export", "surprise", "save"];

    private readonly MainWindow _w;
    private readonly Prefs _prefs;
    private readonly Grid _searchRow;
    private readonly Icon _searchIcon;
    private readonly Button _back;
    private readonly TextBlock _backLabel;
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroller;
    private readonly StackPanel _hints;
    private readonly TextBlock _noticeText;
    private readonly List<PaletteEntry> _stack = [];
    private readonly List<Border> _rowViews = [];
    private IReadOnlyList<PaletteEntry> _all = [];
    private IReadOnlyList<PaletteEntry> _results = [];
    private string _query = string.Empty;

    /// <summary>Builds the palette (hidden until <see cref="Open"/>).</summary>
    /// <param name="window">The window whose commands, controls and files it searches.</param>
    /// <param name="prefs">Where the recent commands and how often each ran are kept.</param>
    internal CommandPalette(MainWindow window, Prefs prefs)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(prefs);
        _w = window;
        _prefs = prefs;

        Box = new TextBox
        {
            PlaceholderText = "Search commands, controls, presets, settings…",
            FontSize = 14,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = Look.Theme.TextPrimary,
        };
        foreach (string name in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
        {
            Box.Resources[name] = Brushes.Transparent;
        }

        foreach (string name in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
        {
            Box.Resources[name] = Brushes.Transparent;
        }

        foreach (string name in new[] { "TextControlBorderThemeThickness", "TextControlBorderThemeThicknessFocused" })
        {
            Box.Resources[name] = new Thickness(0);
        }

        AutomationProperties.SetName(Box, "Search commands, controls, presets and settings");
        Chrome.Tip(Box, "Type part of a name to find it. Start with > for actions only, # for controls, @ for starting points. Up and Down choose a row, Enter runs it.");
        Box.TextChanged += (_, _) => OnQuery();

        _searchIcon = new Icon("search", 15) { Foreground = Look.Theme.TextTertiary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        _backLabel = new TextBlock { FontSize = Look.Theme.Body, VerticalAlignment = VerticalAlignment.Center };
        _back = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { new Icon("chevron-left", 15), _backLabel },
            },
            Height = Look.Theme.ControlHeight,
            MinHeight = 0,
            Padding = new Thickness(4, 0, 8, 0),
            Margin = new Thickness(0, 0, 8, 0),
            IsVisible = false,
        };
        _back.Classes.Add("ep");
        AutomationProperties.SetName(_back, "Back");
        Chrome.Tip(_back, "Go back to the full list (Backspace on an empty box, or Esc).");
        _back.Click += (_, _) => GoBack();

        _searchRow = new Grid { Height = 42, ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Margin = new Thickness(12, 0) };
        Grid.SetColumn(_back, 0);
        Grid.SetColumn(_searchIcon, 1);
        Grid.SetColumn(Box, 2);
        _searchRow.Children.Add(_back);
        _searchRow.Children.Add(_searchIcon);
        _searchRow.Children.Add(Box);
        var searchBar = new Border { BorderBrush = Look.Theme.BorderSubtle, BorderThickness = new Thickness(0, 0, 0, 1), Child = _searchRow };

        _scroller = new ScrollViewer
        {
            Content = _list,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Padding = new Thickness(0),
        };
        _list.Margin = new Thickness(4, 0, 4, 4);

        _hints = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        _hints.Children.Add(Hint("↑↓", "move"));
        _hints.Children.Add(Hint("Enter", "run"));
        var prefixes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        prefixes.Children.Add(Hint(">", "actions"));
        prefixes.Children.Add(Hint("#", "controls"));
        prefixes.Children.Add(Hint("@", "presets"));
        _noticeText = new TextBlock
        {
            FontSize = Look.Theme.Caption,
            Foreground = Look.Theme.Warning,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsVisible = false,
        };
        var footRow = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 0) };
        DockPanel.SetDock(prefixes, Dock.Right);
        footRow.Children.Add(prefixes);
        footRow.Children.Add(_hints);
        footRow.Children.Add(_noticeText);
        var footer = new Border
        {
            Height = 28,
            BorderBrush = Look.Theme.BorderSubtle,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footRow,
        };

        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(searchBar, 0);
        Grid.SetRow(_scroller, 1);
        Grid.SetRow(footer, 2);
        body.Children.Add(searchBar);
        body.Children.Add(_scroller);
        body.Children.Add(footer);

        Child = body;
        Width = 600;
        Background = Look.Theme.SurfacePanel;
        BorderBrush = Look.Theme.BorderStrong;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(Look.Theme.Radius4);
        BoxShadow = BoxShadows.Parse("0 16 56 0 #47000000");
        ClipToBounds = true;
    }

    /// <summary>Gets a value indicating whether the palette is on show.</summary>
    internal bool IsOpen => ReferenceEquals(_w.Cards.Shown, this);

    /// <summary>Gets the rows shown now, with the heading each is listed under ("Choose one" in a sub-list).</summary>
    internal IReadOnlyList<(string Section, string Label)> Rows => [.. _results.Select(e => (e.Section, e.Label))];

    /// <summary>Gets the row the keyboard is on (Enter runs it).</summary>
    internal int Active { get; private set; }

    /// <summary>Gets the text in the footer in place of the key hints, or null when the hints show.</summary>
    internal string? Notice => _noticeText.IsVisible ? _noticeText.Text : null;

    /// <summary>Gets the box that has what was typed.</summary>
    internal TextBox Box { get; }

    /// <summary>Gets the name of the list open now, or null for the main list.</summary>
    internal string? SubList => _stack.Count > 0 ? _stack[^1].Label : null;

    /// <summary>Opens the palette over the window, empty, with the keyboard in its box (Ctrl+K).</summary>
    internal void Open()
    {
        _all = BuildAll();
        _stack.Clear();
        _query = string.Empty;
        Box.Text = string.Empty;
        Active = 0;
        SetNotice(null);
        _w.Cards.Show(this, nearTop: true);
        Refresh();
        Dispatcher.UIThread.Post(() => Box.Focus());
    }

    /// <summary>Closes the palette.</summary>
    internal void Close()
    {
        if (IsOpen)
        {
            _w.Cards.Close();
        }
    }

    /// <summary>Sets what is typed, as if typed in the box (for tests).</summary>
    /// <param name="text">The whole text of the box.</param>
    internal void Type(string text)
    {
        Box.Text = text;
        OnQuery();
    }

    /// <summary>
    /// Takes a key while the palette is on show. Up and Down move (no wrap), Enter runs the row, Esc goes back from a
    /// sub-list or closes, Backspace in an empty box goes back, Ctrl+K closes. Any other key is the box's.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifiers held.</param>
    /// <returns>True when the palette used the key.</returns>
    internal bool HandleKey(Key key, KeyModifiers modifiers)
    {
        switch (key)
        {
            case Key.Down when modifiers == KeyModifiers.None:
                MoveTo(Active + 1);
                return true;
            case Key.Up when modifiers == KeyModifiers.None:
                MoveTo(Active - 1);
                return true;
            case Key.Enter when modifiers == KeyModifiers.None:
                _ = ChooseAsync(Active);
                return true;
            case Key.Escape:
                if (_stack.Count > 0)
                {
                    GoBack();
                }
                else
                {
                    Close();
                }

                return true;
            case Key.Back when _stack.Count > 0 && string.IsNullOrEmpty(Box.Text):
                GoBack();
                return true;
            case Key.K when modifiers == KeyModifiers.Control:
                Close();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Does what a row does: runs it (and closes), opens its options, or says in the footer why it cannot run.
    /// </summary>
    /// <param name="row">The row's place in <see cref="Rows"/>.</param>
    /// <returns>A task that ends when what the row ran is done.</returns>
    internal async Task ChooseAsync(int row)
    {
        if (row < 0 || row >= _results.Count)
        {
            return;
        }

        PaletteEntry entry = _results[row];
        if (entry.Reason is string reason)
        {
            SetNotice(reason);
            return;
        }

        if (entry.Children is not null)
        {
            _stack.Add(entry);
            _query = string.Empty;
            Box.Text = string.Empty;
            Active = 0;
            SetNotice(null);
            Refresh();
            return;
        }

        _prefs.NotePaletteUsed(_stack.Count > 0 ? _stack[0].Id : entry.Id);
        _prefs.Save();
        Close();
        if (entry.Run is null)
        {
            return;
        }

        try
        {
            await entry.Run();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _w.Say($"{entry.Label.TrimEnd('…')} did not work: {e.Message}", error: true);
        }
    }

    private static StackPanel Hint(string key, string text) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 4,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            Chrome.KeyChip(key),
            new TextBlock { Text = text, FontSize = Look.Theme.Caption, Foreground = Look.Theme.TextTertiary, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    private static bool HasIcon(string? name) => name is not null && Icons.Names.Contains(name);

    private void OnQuery()
    {
        string now = Box.Text ?? string.Empty;
        if (now == _query)
        {
            return;
        }

        _query = now;
        Active = 0;
        SetNotice(null);
        Refresh();
    }

    private void GoBack()
    {
        if (_stack.Count == 0)
        {
            return;
        }

        _stack.RemoveAt(_stack.Count - 1);
        _query = string.Empty;
        Box.Text = string.Empty;
        Active = 0;
        SetNotice(null);
        Refresh();
    }

    private void MoveTo(int row)
    {
        Active = Math.Clamp(row, 0, Math.Max(0, _results.Count - 1));
        for (int i = 0; i < _rowViews.Count; i++)
        {
            _rowViews[i].Background = i == Active ? Look.Theme.SurfaceActive : Brushes.Transparent;
        }

        if (Active < _rowViews.Count)
        {
            _rowViews[Active].BringIntoView();
        }
    }

    private void SetNotice(string? text)
    {
        _noticeText.Text = text;
        _noticeText.IsVisible = text is not null;
        _hints.IsVisible = text is null;
    }

    private void Refresh()
    {
        _results = Compute();
        _back.IsVisible = _stack.Count > 0;
        _searchIcon.IsVisible = _stack.Count == 0;
        _backLabel.Text = SubList ?? string.Empty;
        Box.PlaceholderText = _stack.Count > 0 ? "Choose one" : "Search commands, controls, presets, settings…";

        _list.Children.Clear();
        _rowViews.Clear();
        if (_results.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "Nothing matches that. Try a shorter word.",
                FontSize = Look.Theme.Label,
                Foreground = Look.Theme.TextTertiary,
                Margin = new Thickness(12, 14),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }

        string? last = null;
        for (int i = 0; i < _results.Count; i++)
        {
            PaletteEntry entry = _results[i];
            if (_stack.Count == 0 && entry.Section != last)
            {
                _list.Children.Add(new TextBlock
                {
                    Text = entry.Section,
                    FontSize = Look.Theme.Caption,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Look.Theme.TextTertiary,
                    Margin = new Thickness(8, 8, 8, 3),
                });
            }

            last = entry.Section;
            Border view = RowView(entry, i);
            _rowViews.Add(view);
            _list.Children.Add(view);
        }

        MoveTo(Active);
    }

    private Border RowView(PaletteEntry entry, int index)
    {
        var line = new DockPanel { LastChildFill = true };
        Control icon = entry.Checked == true
            ? new Icon("check", 14) { Foreground = Look.Theme.TextSecondary }
            : HasIcon(entry.Icon) ? new Icon(entry.Icon!, 14) { Foreground = Look.Theme.TextTertiary } : new Panel();
        icon.Width = 16;
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);

        if (entry.Children is not null)
        {
            var more = new Icon("chevron-right", 13) { Foreground = Look.Theme.TextTertiary, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(more, Dock.Right);
            line.Children.Add(more);
        }

        if (entry.Keys is not null)
        {
            Border chip = Chrome.KeyChip(entry.Keys);
            chip.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(chip, Dock.Right);
            line.Children.Add(chip);
        }

        if ((entry.Reason ?? entry.Note) is string side)
        {
            var note = new TextBlock
            {
                Text = side,
                FontSize = Look.Theme.Caption,
                Foreground = Look.Theme.TextTertiary,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 240,
            };
            DockPanel.SetDock(note, Dock.Right);
            line.Children.Add(note);
        }

        line.Children.Add(new TextBlock
        {
            Text = entry.Label,
            FontSize = Look.Theme.Body,
            Foreground = entry.Reason is null ? Look.Theme.TextPrimary : Look.Theme.TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var row = new Border
        {
            MinHeight = 28,
            Padding = new Thickness(8, 0),
            CornerRadius = new CornerRadius(Look.Theme.Radius2),
            Background = Brushes.Transparent,
            Child = line,
        };
        AutomationProperties.SetName(row, entry.Label);
        Chrome.Tip(row, entry.Reason is null ? entry.Tooltip : $"{entry.Tooltip} Not available now: {entry.Reason}.");
        row.PointerMoved += (_, _) =>
        {
            if (Active != index)
            {
                MoveTo(index);
            }
        };
        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                _ = ChooseAsync(index);
            }
        };
        return row;
    }

    // ---- What is listed ----

    private IReadOnlyList<PaletteEntry> Compute()
    {
        if (_stack.Count > 0)
        {
            IReadOnlyList<PaletteEntry> options = _stack[^1].Children!();
            return string.IsNullOrWhiteSpace(_query) ? options : PaletteSearch.Rank(options, _query, e => e.Label, e => e.Keywords);
        }

        return _query.Length == 0 ? Starting() : Search();
    }

    // What an empty palette offers: nothing for the selection yet, the last commands run, then the usual first steps.
    private List<PaletteEntry> Starting()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List<PaletteEntry> list = [];
        foreach (string id in _prefs.PaletteRecent)
        {
            PaletteEntry? entry = _all.FirstOrDefault(e => e.Id == id);
            if (entry is not null && seen.Add(id))
            {
                list.Add(entry with { Section = PaletteSection.Recent });
            }
        }

        foreach (string id in SuggestedIds)
        {
            PaletteEntry? entry = _all.FirstOrDefault(e => e.Id == id);
            if (entry is not null && seen.Add(id))
            {
                list.Add(entry with { Section = PaletteSection.Suggested });
            }
        }

        return list;
    }

    private PaletteEntry[] Search()
    {
        (string? section, string text) = PaletteSearch.ParsePrefix(_query);
        PaletteEntry[] entries = [.. _all.Where(e => section is null || e.Section == section)];
        if (string.IsNullOrWhiteSpace(text))
        {
            return entries;
        }

        IReadOnlyList<PaletteEntry> ranked = PaletteSearch.Rank(
            entries, text, e => e.Label, e => e.Keywords, e => _prefs.PaletteUsage.GetValueOrDefault(e.Id));

        // Whichever section holds the best match leads, so Enter takes the best row while the rest stay in the
        // promised order. Enter should not land on a row that can only explain itself: those come last in their section.
        string? best = ranked.FirstOrDefault(e => e.Reason is null)?.Section;
        return
        [
            .. PaletteSection.Order
                .SelectMany(name =>
                {
                    PaletteEntry[] rows = [.. ranked.Where(e => e.Section == name)];
                    return rows.Where(e => e.Reason is null).Concat(rows.Where(e => e.Reason is not null));
                })
                .OrderByDescending(e => e.Section == best),
        ];
    }

    // Every entry there is, in the order of the sections.
    private List<PaletteEntry> BuildAll()
    {
        List<PaletteEntry> all = [];
        all.AddRange(_w.Commands.Where(c => c.Id != "palette").Select(c => CommandEntry(c, PaletteSection.Actions)));
        // The seed has no row: its entry opens the seed panel in the status bar.
        all.AddRange(_w.Panel.Specs.Select(ControlEntry));
        foreach (StartingPoint p in _w.StartingPoints)
        {
            StartingPoint point = p;
            all.Add(new PaletteEntry(
                $"preset-{point.Recipe}-{point.Id}", point.Name, PaletteSection.Presets, $"{point.Group} {point.Recipe}", "shapes", null, null,
                point.Group, null, null, () => _w.Files.StartFromAsync(point),
                $"Start a new piece from {point.Name}. Asks first when this piece has unsaved changes."));
        }

        foreach (LibraryEntry file in _w.Library.List())
        {
            string path = file.Path;
            all.Add(new PaletteEntry(
                $"saved-{path}", file.Name, PaletteSection.SavedProps, "saved work file", "tree-pine", null, null,
                null, null, null, () => _w.Files.OpenPathAsync(path),
                $"Open the work file {file.Name}. Asks first when this piece has unsaved changes."));
        }

        all.AddRange(SettingEntries());
        return all;
    }

    private static PaletteEntry CommandEntry(Command c, string section) => new(
        c.Id,
        c.Label,
        section,
        $"{c.Group} {c.Keywords}",
        c.Icon,
        c.Keys,
        c.Unavailable(),
        null,
        c.Checked(),
        c.Children is null ? null : () => [.. c.Children.Select(child => CommandEntry(child, PaletteSection.ChooseOne))],
        c.Children is null ? c.Run : null,
        c.Tooltip ?? c.Label);

    // A number, switch or colour shows where it is in the panel, the seed opens its panel in the status bar; a choice opens its options.
    private PaletteEntry ControlEntry(ParamSpec spec)
    {
        string key = spec.Key;
        PaletteEntry shared = new(
            $"control-{key}", spec.Label, PaletteSection.Controls, $"{spec.Group} {spec.Tooltip}", "sliders-horizontal", null, null,
            spec.Group, null, null, () =>
            {
                _w.RevealSetting(key);
                return Task.CompletedTask;
            },
            spec.Tooltip);
        if (spec.Type != "choice")
        {
            return shared;
        }

        return shared with
        {
            Run = null,
            Children = () => [.. (spec.Options ?? []).Select(option => new PaletteEntry(
                $"control-{key}-{option.Value}", option.Label, PaletteSection.ChooseOne, string.Empty, null, null, null, null,
                _w.Panel.Current.GetString(key) == option.Value, null,
                () =>
                {
                    // One step of the history, as if picked in the panel.
                    string? problem = _w.Panel.SetFromOutside(key, JsonSerializer.SerializeToElement(option.Value));
                    if (problem is not null)
                    {
                        _w.Say(problem, error: true);
                    }

                    return Task.CompletedTask;
                },
                $"Set {spec.Label} to {option.Label}."))],
        };
    }

    // The Settings window holds Claude's connection for now: its entries all open the window.
    private IEnumerable<PaletteEntry> SettingEntries()
    {
        (string Label, string Words, string Tip)[] items =
        [
            ("Allow Claude to connect", "claude connection mcp server on off", "Open the Settings window, where Claude's connection is turned on or off."),
            ("Claude connection address and token", "claude address token secret", "Open the Settings window, where the address and the token for Claude are."),
            ("Connect Claude Code", "claude code command terminal", "Open the Settings window, which shows the line that connects Claude Code."),
        ];
        foreach ((string label, string words, string tip) in items)
        {
            yield return new PaletteEntry(
                $"setting-{label}", label, PaletteSection.Settings, words, "settings-2", null, null, "Claude", null, null,
                () =>
                {
                    _w.ShowSettings();
                    return Task.CompletedTask;
                },
                tip);
        }
    }
}
