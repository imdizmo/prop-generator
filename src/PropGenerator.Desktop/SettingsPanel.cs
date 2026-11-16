// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Details;
using PropGenerator.Desktop.Recipes;

namespace PropGenerator.Desktop;

/// <summary>
/// The body of the controls panel, as easy-props' Details: the settings in folding groups (which are open is
/// remembered for each kind of prop), one row per setting: numbers (whole numbers too) as a slider with a number field
/// and its unit, choices as a drop-down, on/off as a switch, the wood colour as named swatches with a custom colour.
/// Each row has a reset to the starting point's value while it differs. The filter box finds a control by its name,
/// what it does or its group. The seed is not a row (it is kept, saved, and set by Surprise me, the palette and Claude).
/// Every change goes straight into <see cref="Current"/> and is announced at once (no Apply).
/// </summary>
public sealed class SettingsPanel : StackPanel
{
    /// <summary>The groups open for a kind of prop the prefs know nothing about (easy-props' default list).</summary>
    internal static readonly IReadOnlyList<string> DefaultOpen = ["Shape", "Wood", "Carving"];

    /// <summary>How long a revealed row stays lit.</summary>
    internal static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(1400);

    /// <summary>
    /// The setting that holds what was done to the prop's parts (moved, turned, resized, copied, removed, restored), as
    /// easy-props' components keep it; absent while nothing was. It has no row, and saves and undoes as any setting.
    /// </summary>
    internal const string PartEditsKey = "partEdits";

    /// <summary>
    /// The setting that holds a painted tree's own edits to its limbs and clumps (removed, moved, turned, resized), the
    /// tree's <c>edits</c> parameter as easy-props keeps it; absent until one is made. Like <see cref="PartEditsKey"/>
    /// it has no row, and saves and undoes as any setting.
    /// </summary>
    internal const string TreeEditsKey = "edits";

    /// <summary>
    /// The settings that have no row and go with the settings given: <see cref="PartEditsKey"/>, <see cref="TreeEditsKey"/>
    /// and what a tree's piece carries (<see cref="EngineKinds.PieceCarriedKeys"/>: its tree, the tree's seed and name).
    /// </summary>
    internal static readonly IReadOnlyList<string> RowlessKeys = [PartEditsKey, TreeEditsKey, .. EngineKinds.PieceCarriedKeys];

    /// <summary>How much a Shift drag slows the value down.</summary>
    internal const double FineDrag = 0.2;

    /// <summary>How long the colour must stay still after a pick before it counts as one change for the history.</summary>
    private static readonly TimeSpan ColourSettle = TimeSpan.FromMilliseconds(400);

    private readonly Dictionary<string, ParamSpec> _specs = [];
    private readonly Dictionary<string, NumberRow> _numbers = [];
    private readonly Dictionary<string, ChoiceRow> _choices = [];
    private readonly Dictionary<string, SwitchRow> _switches = [];
    private readonly Dictionary<string, ColourRow> _colourRows = [];
    private readonly Dictionary<string, Grid> _rows = [];
    private readonly Dictionary<string, SettingsGroup> _groupOf = [];
    private readonly List<SettingsGroup> _groups = [];
    private readonly Dictionary<string, IReadOnlyList<string>> _local = new(StringComparer.Ordinal);
    private readonly Debounce _colourSettle;
    private Settings _current;
    private Settings? _start;
    private string? _colourKey;
    private Prefs? _prefs;
    private Colours.ColourBook? _book;
    private Func<string?> _kind = () => null;
    private string? _forced;
    private string _filter = string.Empty;
    private string? _dragKey;
    private bool _dragChanged;
    private bool _shift;
    private (double Raw, double Value)? _fine;
    private bool _fineSetting;
    private bool _syncing;
    private bool _colourPending;
    private long _flash;
    private IReadOnlyCollection<string> _hidden = [];

    // The rows as the recipe shows them for the values now (label and group by key), as last applied; null for none.
    private IReadOnlyList<Recipes.ShownRow>? _shownRows;
    private Dictionary<string, Recipes.ShownRow> _shownOf = new(StringComparer.Ordinal);

    /// <summary>Builds the controls, each showing its setting's default.</summary>
    /// <param name="specs">The painter's settings list.</param>
    public SettingsPanel(IReadOnlyList<ParamSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        Specs = specs;
        _current = Settings.Defaults(specs);
        _colourSettle = new Debounce(ColourSettle, () =>
        {
            _colourPending = false;
            Committed?.Invoke(LabelOf(_colourKey!));
            NoteRecent(_colourKey!);
        });
        Filter = new FilterBox();
        Filter.Changed += OnFilter;
        NoMatch = new TextBlock
        {
            FontSize = Look.Theme.Label,
            Foreground = Look.Theme.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(Look.Theme.Space4),
            IsVisible = false,
        };
        BuildRows();
    }

    /// <summary>A colour, contrast or drift setting changed: only the colouring changes, the painting stays.</summary>
    public event Action? ToneChanged;

    /// <summary>A setting that changes the painting itself changed. True while the mouse is still dragging it.</summary>
    public event Action<bool>? PaintChanged;

    /// <summary>
    /// A change is finished (the slider let go, a value typed or stepped, a choice or a switch made, a reset, the colour
    /// left still): one step for the history, named by the setting's label.
    /// </summary>
    public event Action<string>? Committed;

    /// <summary>The values changed, however (a change in the panel, an undo, a file opened): the window checks what depends on them.</summary>
    internal event Action? CurrentChanged;

    /// <summary>Gets every setting's value now.</summary>
    public Settings Current
    {
        get => _current;
        private set
        {
            _current = value;
            ShowResets();
            Recipes.IMeshRecipe? rules = Rules;
            IReadOnlyCollection<string> hidden = rules?.HiddenKeys(value) ?? [];
            IReadOnlyList<Recipes.ShownRow>? shown = rules?.ShownRows(value);
            bool regroup = !SameRows(shown, _shownRows);
            bool rehide = !hidden.ToHashSet(StringComparer.Ordinal).SetEquals(_hidden);
            _hidden = hidden;
            if (regroup)
            {
                ApplyShownRows(shown);
            }
            else if (rehide)
            {
                ShowGroups();
            }

            CurrentChanged?.Invoke();
        }
    }

    /// <summary>Every setting the panel has, in panel order (the seed too, which has no row).</summary>
    internal IReadOnlyList<ParamSpec> Specs { get; private set; }

    /// <summary>Gets the seed's setting (a seed, or the floor's whole number with its own range), or null when there is none.</summary>
    internal ParamSpec? SeedSpec => Specs.FirstOrDefault(s => s.Key == "seed");

    // The mesh recipe of the kind shown, whose rules hide rows and adjust changes; null for the floor.
    private Recipes.IMeshRecipe? Rules => Recipes.MeshRecipes.Find(_kind());

    /// <summary>Gets the seed as it is now (0 when the settings have none).</summary>
    internal long SeedValue => SeedSpec switch
    {
        null => 0,
        { Type: "seed" } => Current.GetSeed("seed"),
        _ => Current.GetInt("seed"),
    };

    /// <summary>Gets the filter box, shown in the panel's header.</summary>
    internal FilterBox Filter { get; }

    /// <summary>Gets the note shown under the groups while filtering ("Nothing else matches…").</summary>
    internal TextBlock NoMatch { get; }

    /// <summary>Gets the folding groups, in panel order.</summary>
    internal IReadOnlyList<SettingsGroup> Groups => _groups;

    /// <summary>
    /// Gets what the panel calls the kind of prop it holds: "Rock", "Cliff", "Floor tile", or "Prop" when it is
    /// not told.
    /// </summary>
    internal string KindLabel => _kind() switch
    {
        FloorRecipe.Id => FloorRecipe.Label,
        string kind when MeshRecipes.Find(kind) is { } mesh => mesh.Label,
        _ => "Prop",
    };

    /// <summary>A setting shown as no row: the seed (kept in <see cref="Current"/> and saved).</summary>
    /// <param name="s">The setting.</param>
    /// <returns>True for the seed.</returns>
    internal static bool IsHidden(ParamSpec s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Key == "seed" || s.Type == "seed";
    }

    /// <summary>
    /// Remembers which groups are open in these prefs, for the kind of prop the function names ("cut-rock",
    /// "slab-cliff", "floor_tile"), and opens the groups remembered for the kind shown now.
    /// </summary>
    /// <param name="prefs">Where the open groups are kept.</param>
    /// <param name="kind">The recipe of the entry shown.</param>
    internal void UseMemory(Prefs prefs, Func<string?> kind)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(kind);
        _prefs = prefs;
        _kind = kind;
        ShowGroups();
    }

    /// <summary>
    /// Keeps the colours chosen in a colour book: each colour row's picker shows Recent and My colours from it, takes
    /// pasted codes into it, and every colour chosen goes first in Recent.
    /// </summary>
    /// <param name="book">The colour book.</param>
    internal void UseColours(Colours.ColourBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        _book = book;
        book.Changed += ShowColourLists;
        ShowColourLists();
    }

    /// <summary>
    /// Takes the values of the starting point the entry came from, which each row's reset returns to; null (an entry
    /// opened from a file) returns to each setting's default.
    /// </summary>
    /// <param name="values">The starting point's settings, or null.</param>
    internal void SetStartingPoint(Settings? values)
    {
        _start = values;
        ShowResets();
        ShowGroups();
    }

    /// <summary>Gets every setting's default value.</summary>
    /// <returns>The defaults.</returns>
    public Settings Defaults() => Settings.Defaults(Specs);

    /// <summary>
    /// Swaps the whole list of settings: the old rows go, rows for these settings come, showing these values. Nothing
    /// is announced (no history step, no repaint), and the events stay attached. A box that had the focus commits
    /// nothing as it goes (what was typed in it and not confirmed is dropped). When the new rows cannot be made, the
    /// panel is left showing the old settings and their values, and the problem is thrown.
    /// </summary>
    /// <param name="specs">The new settings list.</param>
    /// <param name="values">The values to show.</param>
    public void Rebuild(IReadOnlyList<ParamSpec> specs, Settings values)
    {
        ArgumentNullException.ThrowIfNull(specs);
        ArgumentNullException.ThrowIfNull(values);
        IReadOnlyList<ParamSpec> oldSpecs = Specs;
        Settings oldValues = Current;
        bool was = _syncing;

        // Held for the whole swap: removing a focused box makes it lose the focus, and its handler must not commit.
        _syncing = true;
        try
        {
            try
            {
                Swap(specs, values);
            }
            catch (Exception) when (!ReferenceEquals(specs, oldSpecs))
            {
                Swap(oldSpecs, oldValues);
                throw;
            }
        }
        finally
        {
            _syncing = was;
        }
    }

    /// <summary>
    /// Shows these settings in every control without announcing anything (no history step, no repaint): the caller
    /// repaints. Used by undo, redo, opening a file and starting a new piece.
    /// </summary>
    /// <param name="s">The settings; a setting missing from them keeps its value.</param>
    public void Apply(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        _colourSettle.Cancel();
        _colourPending = false;
        _dragKey = null;
        _dragChanged = false;
        _fine = null;
        Settings merged = Current;
        foreach (ParamSpec spec in Specs)
        {
            if (s.Values.TryGetValue(spec.Key, out JsonElement v))
            {
                merged = merged.With(spec.Key, v);
            }
        }

        // The edits to the prop's parts have no row: they come and go with the settings given.
        foreach (string key in RowlessKeys)
        {
            merged = s.Values.TryGetValue(key, out JsonElement parts) ? merged.With(key, parts) : merged.Without(key);
        }

        Current = merged;
        bool was = _syncing;
        _syncing = true;
        try
        {
            foreach (ParamSpec spec in Specs)
            {
                Show(spec);
            }
        }
        finally
        {
            _syncing = was;
        }
    }

    /// <summary>Names the change just finished, for the history: the setting's label and its new value.</summary>
    /// <param name="label">The setting's label.</param>
    /// <returns>For example "Plank gap width 0.30".</returns>
    public string DescribeChange(string label)
    {
        ParamSpec? s = Specs.FirstOrDefault(x => LabelOf(x.Key) == label) ?? Specs.FirstOrDefault(x => x.Label == label);
        if (s is null)
        {
            return label;
        }

        string value = s.Type switch
        {
            "colour" => Hex(ToColor(Current.GetColour(s.Key))),
            "float" => Format(s, Current.GetFloat(s.Key)),
            "int" => Current.GetInt(s.Key).ToString(CultureInfo.InvariantCulture),
            "bool" => Current.GetBool(s.Key) ? "On" : "Off",
            "choice" => s.Options?.FirstOrDefault(o => o.Value == Current.GetString(s.Key))?.Label ?? Current.GetString(s.Key),
            "seed" => Current.GetSeed(s.Key).ToString(CultureInfo.InvariantCulture),
            _ => string.Empty,
        };
        return $"{label} {value}";
    }

    /// <summary>The number field of a number setting, or the #RRGGBB box of the colour.</summary>
    /// <param name="key">The setting.</param>
    /// <returns>The box.</returns>
    internal TextBox ValueBoxFor(string key) =>
        _colourRows.TryGetValue(key, out ColourRow? colour) ? colour.HexBox : _numbers[key].Box;

    /// <summary>Gets the row of a number setting, or null when the panel shows no such row.</summary>
    /// <param name="key">The setting.</param>
    /// <returns>The row.</returns>
    internal NumberRow? NumberRowFor(string key) => _numbers.GetValueOrDefault(key);

    /// <summary>Gets the row of a choice setting, or null when the panel shows no such row.</summary>
    /// <param name="key">The setting.</param>
    /// <returns>The row.</returns>
    internal ChoiceRow? ChoiceRowFor(string key) => _choices.GetValueOrDefault(key);

    /// <summary>Gets the row of an on/off setting, or null when the panel shows no such row.</summary>
    /// <param name="key">The setting.</param>
    /// <returns>The row.</returns>
    internal SwitchRow? SwitchRowFor(string key) => _switches.GetValueOrDefault(key);

    /// <summary>Gets the colour's row, or null when the panel has none (a rock).</summary>
    internal ColourRow? ColourRow { get; private set; }

    /// <summary>Gets the row of a colour setting, or null when the panel shows no such row.</summary>
    /// <param name="key">The setting.</param>
    /// <returns>The row.</returns>
    internal ColourRow? ColourRowFor(string key) => _colourRows.GetValueOrDefault(key);

    /// <summary>Gets the group a setting's row sits in, or null when the panel shows no such row.</summary>
    /// <param name="key">The setting.</param>
    /// <returns>The group.</returns>
    internal SettingsGroup? GroupFor(string key) => _groupOf.GetValueOrDefault(key);

    /// <summary>What a setting's row is called now: its shown label (which can depend on the values), else its own.</summary>
    /// <param name="key">The setting.</param>
    /// <returns>The label.</returns>
    internal string LabelOf(string key) =>
        _shownOf.TryGetValue(key, out Recipes.ShownRow? row) ? row.Label : _specs.TryGetValue(key, out ParamSpec? s) ? s.Label : key;

    /// <summary>The group a setting's row sits in now: its shown group (which can depend on the values), else its own; null for none.</summary>
    /// <param name="key">The setting.</param>
    /// <returns>The group's title, or null.</returns>
    internal string? ShownGroupOf(string key) =>
        _shownOf.TryGetValue(key, out Recipes.ShownRow? row) && row.Group is not null ? row.Group : _specs.GetValueOrDefault(key)?.Group;

    /// <summary>Gets the row of a setting (its label and its input), or null when the panel shows no such row.</summary>
    /// <param name="key">The setting.</param>
    /// <returns>The row.</returns>
    internal Grid? RowFor(string key) => _rows.GetValueOrDefault(key);

    /// <summary>Gets every row shown (one per setting but the seed), in panel order.</summary>
    internal IReadOnlyCollection<Grid> Rows => _rows.Values;

    /// <summary>
    /// Takes what was typed in a value box (as Enter or leaving the box does). A number forgives a unit or a comma;
    /// out of its range it is kept to the range and the row says so for 2.6 s; text with no digit is no number and
    /// changes nothing. Nothing happens while the rows are being swapped or set, or for a setting the panel no longer
    /// has.
    /// </summary>
    /// <param name="key">The setting.</param>
    internal void CommitValueBox(string key)
    {
        if (_syncing || !_specs.TryGetValue(key, out ParamSpec? s))
        {
            return;
        }

        if (s.Type == "colour")
        {
            if (_colourRows.TryGetValue(key, out ColourRow? colour))
            {
                CommitHex(s, colour.HexBox);
            }

            return;
        }

        if (!_numbers.TryGetValue(key, out NumberRow? row))
        {
            return;
        }

        // Nothing was typed (the field was only visited, or a step was taken in it): the value is not touched.
        if (row.Box.Text == Format(s, Current.Values[key].GetDouble()))
        {
            return;
        }

        if (Typed(row.Box.Text) is double typed)
        {
            double min = s.Min ?? 0, max = s.Max ?? 1;
            double clamped = Math.Clamp(typed, min, max);
            if (clamped != typed)
            {
                row.ShowNote(NumberRow.KeptTo(s));
            }

            row.Slider.Value = Snap(s, clamped);
        }

        row.Box.Text = Format(s, Current.Values[key].GetDouble());
    }

    /// <summary>
    /// Sets one setting as the user would (Claude's set_control): the control shows the value, a tone setting recolours,
    /// a painting setting paints at full detail, and the change is one finished step for the history. A value that is
    /// already set changes nothing.
    /// </summary>
    /// <param name="key">The setting's key.</param>
    /// <param name="value">A number, a true or false, an option's value for a choice, a whole number for a seed, or for the colour "#RRGGBB" or three numbers from 0 to 1.</param>
    /// <returns>The problem (naming what is valid), or null when it was set or already was.</returns>
    internal string? SetFromOutside(string key, JsonElement value)
    {
        if (!_specs.TryGetValue(key, out ParamSpec? s))
        {
            return $"There is no setting '{key}'. The valid keys are: {string.Join(", ", Specs.Select(x => x.Key))}.";
        }

        FlushColourSettle();
        if (_dragKey == key)
        {
            // The user is dragging this very slider: that drag is finished first, as letting go would.
            EndDrag(key);
        }

        return s.Type switch
        {
            "float" => SetFloat(s, value),
            "int" => SetWhole(s, value),
            "bool" => SetToggle(s, value),
            "choice" => SetChoice(s, value),
            "seed" => SetSeedFromOutside(s, value),
            _ => SetColourFromOutside(s, value),
        };
    }

    /// <summary>
    /// Puts the keyboard in a setting's box, its text selected, so a value can be typed or pasted. A setting without a
    /// box, or one the panel shows no row for (the seed), gets nothing.
    /// </summary>
    /// <param name="key">The setting.</param>
    internal void FocusSetting(string key)
    {
        if (_numbers.TryGetValue(key, out NumberRow? row))
        {
            row.Box.Focus();
            row.Box.SelectAll();
        }
    }

    /// <summary>
    /// Shows the user where a setting is (the palette's controls): the filter is cleared, the group holding the row is
    /// opened (for now, not remembered), the panel scrolls so the row is in the middle, the row lights up in the selected
    /// colour for 1.4 s, and the keyboard goes to its first input.
    /// </summary>
    /// <param name="key">The setting.</param>
    /// <returns>False when the panel shows no row for the setting.</returns>
    internal bool Reveal(string key)
    {
        if (!_rows.TryGetValue(key, out Grid? row))
        {
            return false;
        }

        Filter.Text = string.Empty;
        _forced = _groupOf[key].Title;
        ShowGroups();
        UpdateLayout();
        if (this.FindAncestorOfType<ScrollViewer>() is { } scroller && scroller.Content is Visual content)
        {
            scroller.UpdateLayout();
            if (row.TranslatePoint(new Point(0, row.Bounds.Height / 2), content) is Point centre)
            {
                double top = centre.Y - (scroller.Viewport.Height / 2);
                double most = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
                scroller.Offset = new Vector(scroller.Offset.X, Math.Clamp(top, 0, most));
            }
        }

        long mine = ++_flash;
        row.Background = Look.Theme.SurfaceSelected;
        _ = Dim(row, mine);
        FocusInput(key);
        return true;
    }

    // The old rows and every lookup of them go, the rows of these settings come, showing these values.
    private void Swap(IReadOnlyList<ParamSpec> specs, Settings values)
    {
        _colourSettle.Cancel();
        _colourPending = false;
        _dragKey = null;
        _dragChanged = false;
        _fine = null;
        _forced = null;

        // Another kind of prop comes up unfiltered.
        _filter = string.Empty;
        Filter.Text = string.Empty;
        Children.Clear();
        foreach (SettingsGroup g in _groups)
        {
            g.Body.Children.Clear();
        }

        _colourKey = null;
        ColourRow = null;
        _colourRows.Clear();
        _specs.Clear();
        _numbers.Clear();
        _choices.Clear();
        _switches.Clear();
        _rows.Clear();
        _groupOf.Clear();
        _groups.Clear();
        _shownRows = null;
        _shownOf = new(StringComparer.Ordinal);
        Specs = specs;
        _current = Settings.Defaults(specs);
        BuildRows();
        Apply(values);
        Current = values;
    }

    private void BuildRows()
    {
        bool was = _syncing;
        _syncing = true;
        try
        {
            foreach (ParamSpec s in Specs)
            {
                _specs[s.Key] = s;
                Grid? row = s.Type switch
                {
                    "colour" => ColourRowOf(s),
                    "float" or "int" => IsHidden(s) ? null : NumberRowOf(s),
                    "bool" => SwitchRowOf(s),
                    "choice" => ChoiceRowOf(s),
                    "seed" => null,
                    _ => throw new InvalidDataException($"Setting '{s.Key}' has a type the panel does not know: '{s.Type}'."),
                };
                if (row is null)
                {
                    continue;
                }

                // easy-props: a setting with no group sits under Shape.
                string title = s.Group ?? "Shape";
                SettingsGroup? group = _groups.FirstOrDefault(g => g.Title == title);
                if (group is null)
                {
                    group = new SettingsGroup(title);
                    group.Toggled += OnToggled;
                    _groups.Add(group);
                    Children.Add(group);
                }

                _rows[s.Key] = row;
                _groupOf[s.Key] = group;
                group.Body.Children.Add(row);
            }

            Children.Add(NoMatch);
            foreach (ParamSpec s in Specs)
            {
                Show(s);
            }

            ShowResets();
            ShowGroups();
        }
        finally
        {
            _syncing = was;
        }
    }

    // Shows a setting's value in its row (none for the seed). Called with the guard held: nothing is announced.
    private void Show(ParamSpec spec)
    {
        switch (spec.Type)
        {
            case "colour" when _colourRows.TryGetValue(spec.Key, out ColourRow? colourRow):
                Color c = ToColor(Current.GetColour(spec.Key));
                colourRow.Picker.Color = c;
                colourRow.HexBox.Text = Hex(c);
                colourRow.Show(Hex(c));
                break;
            case "float" or "int" when _numbers.TryGetValue(spec.Key, out NumberRow? number):
                double v = Current.Values[spec.Key].GetDouble();
                number.Slider.Value = v;
                number.Box.Text = Format(spec, v);
                break;
            case "bool" when _switches.TryGetValue(spec.Key, out SwitchRow? toggle):
                toggle.Switch.IsChecked = Current.GetBool(spec.Key);
                break;
            case "choice" when _choices.TryGetValue(spec.Key, out ChoiceRow? choice):
                choice.Combo.SelectedIndex = IndexOf(spec, Current.GetString(spec.Key));
                break;
        }
    }

    // What a row's reset returns to: the starting point's value, else the setting's default.
    private JsonElement StartOf(ParamSpec s) =>
        _start is not null && _start.Values.TryGetValue(s.Key, out JsonElement v) ? v : s.Default;

    // Each reset shows only while its value differs from the starting point's (a number by more than half a step).
    private void ShowResets()
    {
        foreach ((string key, NumberRow row) in _numbers)
        {
            if (Current.Values.TryGetValue(key, out JsonElement now))
            {
                ParamSpec s = row.Spec;
                row.Reset.IsVisible = Math.Abs(StartOf(s).GetDouble() - now.GetDouble()) > (s.Step ?? 0.01) / 2;
            }
        }

        foreach ((string key, ChoiceRow row) in _choices)
        {
            if (Current.Values.TryGetValue(key, out JsonElement now))
            {
                row.Reset.IsVisible = StartOf(row.Spec).GetString() != now.GetString();
            }
        }

        foreach ((string key, SwitchRow row) in _switches)
        {
            if (Current.Values.TryGetValue(key, out JsonElement now))
            {
                row.Reset.IsVisible = StartOf(row.Spec).GetBoolean() != now.GetBoolean();
            }
        }
    }

    // A reset is a change as the user makes it: one step for the history.
    private void ResetRow(ParamSpec s)
    {
        if (_syncing)
        {
            return;
        }

        JsonElement start = StartOf(s);
        switch (s.Type)
        {
            case "float" or "int":
                _numbers[s.Key].Slider.Value = Snap(s, start.GetDouble());
                break;
            case "choice":
                _choices[s.Key].Combo.SelectedIndex = IndexOf(s, start.GetString());
                break;
            case "bool":
                _switches[s.Key].Switch.IsChecked = start.GetBoolean();
                break;
        }
    }

    // Open groups: the one a reveal forced open, else those remembered for this kind (easy-props' list when none).
    // While filtering every group with a match is open, and the others are hidden.
    private bool IsOpen(string title)
    {
        if (title == _forced)
        {
            return true;
        }

        return (Remembered() ?? DefaultOpen).Contains(title);
    }

    // The groups remembered open for the kind shown: in the prefs, or in the panel's own memory when it has none.
    private IReadOnlyList<string>? Remembered()
    {
        string kind = _kind() ?? string.Empty;
        return _prefs is not null && _prefs.OpenGroups.TryGetValue(kind, out IReadOnlyList<string>? open)
            ? open
            : _local.GetValueOrDefault(kind);
    }

    private void ShowGroups()
    {
        foreach (SettingsGroup g in _groups)
        {
            bool any = false;
            foreach (Control row in g.Body.Children)
            {
                row.IsVisible = Answers(((PanelRow)row).Spec);
                any |= row.IsVisible;
            }

            g.IsVisible = any;
            g.IsOpen = _filter.Length > 0 || IsOpen(g.Title);
        }

        NoMatch.IsVisible = _filter.Length > 0;
        NoMatch.Text = $"Nothing else matches. Try a shorter word, or select the whole {KindLabel.ToLowerInvariant()}.";
    }

    // easy-props' rule: a control answers the filter by its name, what it does, or its group, case-blind.
    private bool Answers(ParamSpec s) =>
        !_hidden.Contains(s.Key)
        && Matches(s);

    private bool Matches(ParamSpec s) =>
        _filter.Length == 0
        || LabelOf(s.Key).Contains(_filter, StringComparison.OrdinalIgnoreCase)
        || s.Tooltip.Contains(_filter, StringComparison.OrdinalIgnoreCase)
        || (ShownGroupOf(s.Key) ?? "Shape").Contains(_filter, StringComparison.OrdinalIgnoreCase);

    // Whether two lists of shown rows say the same (null: none).
    private static bool SameRows(IReadOnlyList<Recipes.ShownRow>? a, IReadOnlyList<Recipes.ShownRow>? b) =>
        a is null || b is null ? a is null && b is null : a.SequenceEqual(b);

    // Puts the rows where and as the recipe shows them for the values now (D5): each row under its shown group (made
    // when it is new), the groups and the rows in the order shown, each row with its shown label; rows the list leaves
    // out (hidden ones) after them, as their settings say. Null puts every row back as its setting says. Then shows the
    // groups.
    private void ApplyShownRows(IReadOnlyList<Recipes.ShownRow>? shown)
    {
        _shownRows = shown;
        _shownOf = new(StringComparer.Ordinal);
        var order = new List<(string Key, string Title)>();
        foreach (Recipes.ShownRow row in shown ?? [])
        {
            if (_rows.ContainsKey(row.Key) && _shownOf.TryAdd(row.Key, row))
            {
                order.Add((row.Key, row.Group ?? _specs[row.Key].Group ?? "Shape"));
            }
        }

        foreach (ParamSpec spec in Specs)
        {
            if (_rows.ContainsKey(spec.Key) && !_shownOf.ContainsKey(spec.Key))
            {
                order.Add((spec.Key, spec.Group ?? "Shape"));
            }
        }

        List<string> titles = [.. order.Select(o => o.Title).Distinct(StringComparer.Ordinal)];
        foreach (string title in titles.Where(t => _groups.All(g => g.Title != t)))
        {
            var group = new SettingsGroup(title);
            group.Toggled += OnToggled;
            _groups.Add(group);
        }

        // The groups in the order shown (groups no row is in any more stay, after them, empty and so hidden).
        List<SettingsGroup> groups = [.. titles.Select(t => _groups.First(g => g.Title == t)), .. _groups.Where(g => !titles.Contains(g.Title))];
        bool was = _syncing;
        _syncing = true;
        try
        {
            var wanted = groups.ToDictionary(g => g, g => order.Where(o => o.Title == g.Title).Select(o => (Control)_rows[o.Key]).ToList());
            List<SettingsGroup> moved = [.. groups.Where(g => !g.Body.Children.SequenceEqual(wanted[g]))];
            foreach (SettingsGroup g in moved)
            {
                g.Body.Children.Clear();
            }

            foreach (SettingsGroup g in moved)
            {
                g.Body.Children.AddRange(wanted[g]);
            }

            foreach ((string key, string title) in order)
            {
                _groupOf[key] = groups.First(g => g.Title == title);
                ((PanelRow)_rows[key]).Label.Text = LabelOf(key);
            }

            if (!_groups.SequenceEqual(groups))
            {
                _groups.Clear();
                _groups.AddRange(groups);
                foreach (SettingsGroup g in groups)
                {
                    _ = Children.Remove(g);
                }

                Children.InsertRange(Math.Max(0, Children.IndexOf(NoMatch)), groups);
            }
        }
        finally
        {
            _syncing = was;
        }

        ShowGroups();
    }

    private void OnFilter(string text)
    {
        _filter = text.Trim();
        ShowGroups();
    }

    // easy-props' toggle: the groups open now, with this one flipped, are remembered for the kind.
    private void OnToggled(SettingsGroup group)
    {
        if (_filter.Length > 0)
        {
            // While filtering every matching group is open; the header does nothing then.
            return;
        }

        bool open = IsOpen(group.Title);
        List<string> now = [.. _groups.Select(g => g.Title).Where(IsOpen)];
        if (group.Title == _forced)
        {
            _forced = null;
        }

        if (open)
        {
            now.Remove(group.Title);
        }
        else if (!now.Contains(group.Title))
        {
            now.Add(group.Title);
        }

        string? kind = _kind();
        if (_prefs is not null && kind is not null)
        {
            _prefs.OpenGroups = new Dictionary<string, IReadOnlyList<string>>(_prefs.OpenGroups, StringComparer.Ordinal) { [kind] = now };
            _prefs.Save();
        }
        else
        {
            _local[kind ?? string.Empty] = now;
        }

        ShowGroups();
    }

    // The row lights down again once the flash is over, unless it was lit again meanwhile.
    private async Task Dim(Grid row, long mine)
    {
        await Task.Delay(FlashTime).ConfigureAwait(true);
        if (_flash == mine)
        {
            row.Background = null;
        }
    }

    // The first input of a setting's row takes the keyboard: its number field, else its drop-down, switch or colour.
    private void FocusInput(string key)
    {
        if (_numbers.ContainsKey(key))
        {
            FocusSetting(key);
        }
        else if (_choices.TryGetValue(key, out ChoiceRow? choice))
        {
            choice.Combo.Focus();
        }
        else if (_switches.TryGetValue(key, out SwitchRow? toggle))
        {
            toggle.Switch.Focus();
        }
        else if (_colourRows.TryGetValue(key, out ColourRow? colour))
        {
            colour.Custom.Focus();
        }
    }

    // A colour picked and still settling becomes its own history step now, before another change lands on top of it.
    private void FlushColourSettle()
    {
        if (!_colourPending)
        {
            return;
        }

        _colourSettle.Cancel();
        _colourPending = false;
        Committed?.Invoke(LabelOf(_colourKey!));
    }

    private static string RangeText(ParamSpec s) => string.Create(
        CultureInfo.InvariantCulture, $"from {s.Min ?? 0} to {s.Max ?? 1}");

    private string? SetFloat(ParamSpec s, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double v) || !double.IsFinite(v))
        {
            return $"'{s.Key}' ({s.Label}) takes a number {RangeText(s)}.";
        }

        if (v < (s.Min ?? 0) || v > (s.Max ?? 1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{v} is outside the range of '{s.Key}' ({s.Label}): it must be {RangeText(s)}.");
        }

        SetNumber(s, v);
        return null;
    }

    private string? SetWhole(ParamSpec s, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double v)
            || !double.IsFinite(v) || v != Math.Round(v))
        {
            return $"'{s.Key}' ({s.Label}) takes a whole number {RangeText(s)}.";
        }

        if (v < (s.Min ?? 0) || v > (s.Max ?? 100))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{v} is outside the range of '{s.Key}' ({s.Label}): it must be from {s.Min ?? 0} to {s.Max ?? 100}.");
        }

        SetNumber(s, v);
        return null;
    }

    // A number set as the user would: through its slider, or straight in for the seed, which has no row.
    private void SetNumber(ParamSpec s, double v)
    {
        if (_numbers.TryGetValue(s.Key, out NumberRow? row))
        {
            row.Slider.Value = Snap(s, v);
            row.Box.Text = Format(s, Current.Values[s.Key].GetDouble());
            return;
        }

        object stored = s.Type == "int" ? (int)Math.Round(v) : Snap(s, v);
        if (Current.Values[s.Key].GetDouble() == Convert.ToDouble(stored, CultureInfo.InvariantCulture))
        {
            return;
        }

        Current = Current.With(s.Key, stored);
        Announce(s, dragging: false);
        Committed?.Invoke(LabelOf(s.Key));
    }

    private string? SetChoice(ParamSpec s, JsonElement value)
    {
        string valid = string.Join(", ", (s.Options ?? []).Select(o => o.Value));
        int i = value.ValueKind == JsonValueKind.String ? IndexOf(s, value.GetString()) : -1;
        if (i < 0)
        {
            return $"'{s.Key}' ({s.Label}) takes one of these option values: {valid}.";
        }

        if (_choices.TryGetValue(s.Key, out ChoiceRow? row))
        {
            row.Combo.SelectedIndex = i;
        }

        return null;
    }

    private string? SetSeedFromOutside(ParamSpec s, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double v)
            || !double.IsFinite(v) || v != Math.Round(v) || v < 0 || v > uint.MaxValue)
        {
            return $"'{s.Key}' ({s.Label}) takes a whole number from 0 to 4294967295.";
        }

        // The same seed changes nothing; a new one is one history step "Seed n".
        uint seed = (uint)v;
        if (Current.GetSeed(s.Key) != seed)
        {
            Current = Current.With(s.Key, (long)seed);
            Announce(s, dragging: false);
            Committed?.Invoke("Seed " + seed.ToString(CultureInfo.InvariantCulture));
        }

        return null;
    }

    private string? SetToggle(ParamSpec s, JsonElement value)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return $"'{s.Key}' ({s.Label}) takes true or false.";
        }

        if (_switches.TryGetValue(s.Key, out SwitchRow? row))
        {
            row.Switch.IsChecked = value.GetBoolean();
        }

        return null;
    }

    private string? SetColourFromOutside(ParamSpec s, JsonElement value)
    {
        const string Valid = "a colour as \"#RRGGBB\" or as three numbers from 0 to 1 (red, green, blue)";
        Rgb c;
        if (value.ValueKind == JsonValueKind.String)
        {
            string text = (value.GetString() ?? string.Empty).Trim();
            if (text.TrimStart('#').Length != 6 || !text.TrimStart('#').All(Uri.IsHexDigit))
            {
                return $"'{s.Key}' ({s.Label}) takes {Valid}; '{text}' is neither.";
            }

            c = Rgb.FromHex(text);
        }
        else if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 3
            && value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number))
        {
            float[] f = [.. value.EnumerateArray().Select(x => x.GetSingle())];
            if (f.Any(x => !float.IsFinite(x) || x < 0f || x > 1f))
            {
                return $"'{s.Key}' ({s.Label}) takes {Valid}; every number must be from 0 to 1.";
            }

            c = new Rgb(f[0], f[1], f[2]);
        }
        else
        {
            return $"'{s.Key}' ({s.Label}) takes {Valid}.";
        }

        PickColour(s, c);
        return null;
    }

    // A whole colour taken at once (Claude, a swatch): the row shows it, and it is one history step.
    private void PickColour(ParamSpec s, Rgb c)
    {
        FlushColourSettle();
        if (Current.GetColour(s.Key) == c)
        {
            return;
        }

        bool was = _syncing;
        _syncing = true;
        try
        {
            if (_colourRows.TryGetValue(s.Key, out ColourRow? row))
            {
                row.Picker.Color = ToColor(c);
                row.HexBox.Text = Hex(ToColor(c));
            }
        }
        finally
        {
            _syncing = was;
        }

        SetColour(s, c);
        Committed?.Invoke(LabelOf(s.Key));
        NoteRecent(s.Key);
    }

    private static int IndexOf(ParamSpec s, string? value)
    {
        IReadOnlyList<ChoiceItem> options = s.Options ?? [];
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].Value == value)
            {
                return i;
            }
        }

        return -1;
    }

    // easy-props' parse: a comma for a point is forgiven, everything but digits, points and signs is dropped (a unit,
    // say), and what is left needs a digit and must be a number.
    private static double? Typed(string? text)
    {
        string t = text ?? string.Empty;
        int comma = t.IndexOf(',', StringComparison.Ordinal);
        if (comma >= 0)
        {
            t = string.Concat(t.AsSpan(0, comma), ".", t.AsSpan(comma + 1));
        }

        string kept = new([.. t.Where(ch => char.IsAsciiDigit(ch) || ch is '.' or '+' or '-')]);
        return kept.Any(char.IsAsciiDigit)
            && double.TryParse(kept, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double v)
            && double.IsFinite(v)
            ? v
            : null;
    }

    private static string Format(ParamSpec s, double v) =>
        v.ToString("F" + RowParts.Decimals(s.Step ?? 0.01).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    // On the setting's own grid of steps, inside its range, rounded so 0.1 + 0.2 reads as 0.3.
    private static double Snap(ParamSpec s, double v)
    {
        double min = s.Min ?? 0, max = s.Max ?? 1, step = s.Step ?? 0;
        double snapped = step > 0 ? min + (Math.Round((v - min) / step) * step) : v;
        int places = Math.Max(0, (int)Math.Ceiling(-Math.Log10(step > 0 ? step : 0.01) - 1e-9)) + 1;
        return Math.Round(Math.Clamp(snapped, min, max), places);
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color ToColor(Rgb c)
    {
        static byte B(float x) => (byte)Math.Clamp((int)Math.Round(x * 255f), 0, 255);
        return Color.FromRgb(B(c.R), B(c.G), B(c.B));
    }

    private NumberRow NumberRowOf(ParamSpec s)
    {
        var row = new NumberRow(s);
        _numbers[s.Key] = row;
        Slider slider = row.Slider;
        TextBox box = row.Box;

        // A drag is the time the left button is held on the slider. The slider follows the mouse from its own handlers,
        // also on the tunnel, after these (they are added before its template is), and even when the press was handled.
        slider.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed)
            {
                _dragKey = s.Key;
                _dragChanged = false;
                _fine = null;
                _shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        slider.AddHandler(InputElement.PointerMovedEvent, (_, e) => _shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift), RoutingStrategies.Tunnel, handledEventsToo: true);
        slider.AddHandler(InputElement.PointerReleasedEvent, (_, _) => EndDrag(s.Key), RoutingStrategies.Tunnel, handledEventsToo: true);
        slider.PointerCaptureLost += (_, _) => EndDrag(s.Key);
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                SliderMoved(s, row);
            }
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitValueBox(s.Key);
                e.Handled = true;
            }
        };
        box.AddHandler(InputElement.KeyDownEvent, (_, e) => NumberKey(s, row, e), RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) =>
        {
            if (_numbers.TryGetValue(s.Key, out NumberRow? shown) && ReferenceEquals(shown, row))
            {
                CommitValueBox(s.Key);
            }
        };
        row.Reset.Click += (_, _) => ResetRow(s);
        return row;
    }

    // In the number field ↑ and ↓ step the value (Shift: ten steps), and Esc leaves it as it was.
    private void NumberKey(ParamSpec s, NumberRow row, KeyEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        if (e.Key is Key.Up or Key.Down)
        {
            double step = s.Step ?? 0.01;
            double now = Current.Values[s.Key].GetDouble();
            double next = Math.Clamp(now + ((e.Key == Key.Up ? 1 : -1) * step * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1)), s.Min ?? 0, s.Max ?? 1);
            row.Slider.Value = Snap(s, Math.Round(next, 6));
            row.Box.Text = Format(s, Current.Values[s.Key].GetDouble());
            row.Box.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Abandoned: what was typed goes, and the keyboard leaves the field (to the slider) without a change.
            row.Box.Text = Format(s, Current.Values[s.Key].GetDouble());
            row.Slider.Focus();
            e.Handled = true;
        }
    }

    private void SliderMoved(ParamSpec s, NumberRow row)
    {
        if (_syncing)
        {
            return;
        }

        bool dragging = _dragKey == s.Key;
        if (dragging && !_fineSetting && FineValue(s, row.Slider.Value) is double fine)
        {
            // Shift is held: the value moves a fifth as far as the hand, from where Shift went down.
            _fineSetting = true;
            try
            {
                row.Slider.Value = fine;
            }
            finally
            {
                _fineSetting = false;
            }

            return;
        }

        double v = Snap(s, row.Slider.Value);
        row.Box.Text = Format(s, v);
        if (Current.Values[s.Key].GetDouble() == v)
        {
            return;
        }

        Current = Current.With(s.Key, s.Type == "int" ? (int)Math.Round(v) : v);
        if (dragging)
        {
            _dragChanged = true;
        }

        Announce(s, dragging);
        if (!dragging)
        {
            Committed?.Invoke(LabelOf(s.Key));
        }
    }

    // easy-props' fine drag: with Shift held, the slider's place counts a fifth from where Shift went down, on the step.
    // Null when Shift is not held, or when the fine value is where the slider is anyway.
    private double? FineValue(ParamSpec s, double raw)
    {
        if (!_shift)
        {
            _fine = null;
            return null;
        }

        _fine ??= (raw, Current.Values[s.Key].GetDouble());
        double step = s.Step ?? 0.01;
        double next = _fine.Value.Value + ((raw - _fine.Value.Raw) * FineDrag);
        next = Snap(s, Math.Round(next / step) * step);
        return next == raw ? null : next;
    }

    private void EndDrag(string key)
    {
        if (_dragKey != key)
        {
            return;
        }

        _dragKey = null;
        _fine = null;
        if (_dragChanged)
        {
            _dragChanged = false;
            Committed?.Invoke(LabelOf(key));
        }
    }

    private void Announce(ParamSpec s, bool dragging)
    {
        if (s.Kind == "tone")
        {
            ToneChanged?.Invoke();
        }
        else
        {
            PaintChanged?.Invoke(dragging);
        }
    }

    private SwitchRow SwitchRowOf(ParamSpec s)
    {
        var row = new SwitchRow(s);
        _switches[s.Key] = row;
        ToggleSwitch toggle = row.Switch;
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (_syncing || toggle.IsChecked is not bool on || Current.GetBool(s.Key) == on)
            {
                return;
            }

            Current = Current.With(s.Key, on);
            Announce(s, dragging: false);
            Committed?.Invoke(LabelOf(s.Key));
        };
        row.Reset.Click += (_, _) => ResetRow(s);
        return row;
    }

    private ChoiceRow ChoiceRowOf(ParamSpec s)
    {
        var row = new ChoiceRow(s);
        _choices[s.Key] = row;
        ComboBox combo = row.Combo;
        IReadOnlyList<ChoiceItem> options = s.Options ?? [];
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing || combo.SelectedIndex < 0)
            {
                return;
            }

            string v = options[combo.SelectedIndex].Value;
            if (Current.GetString(s.Key) == v)
            {
                return;
            }

            Settings before = Current;
            Current = Current.With(s.Key, v);
            AdjustAfter(before);
            Announce(s, dragging: false);
            Committed?.Invoke(LabelOf(s.Key));
        };
        row.Reset.Click += (_, _) => ResetRow(s);
        return row;
    }

    // The recipe's rules on a change just made (a switch to My own colours starts its colours): shown at once, part of
    // the same step.
    private void AdjustAfter(Settings before)
    {
        if (Rules is not { } rules)
        {
            return;
        }

        Settings adjusted = rules.Adjust(before, Current);
        if (adjusted.Equals(Current))
        {
            return;
        }

        Current = adjusted;
        bool was = _syncing;
        _syncing = true;
        try
        {
            foreach (ParamSpec spec in Specs.Where(spec => spec.Type == "colour"))
            {
                Show(spec);
            }
        }
        finally
        {
            _syncing = was;
        }
    }

    // The default colour (v5's 0.272, 0.188, 0.170) has no exact #RRGGBB: it is kept as those numbers until the colour
    // is touched; the swatches, the square and the box show it rounded.
    private ColourRow ColourRowOf(ParamSpec s)
    {
        var row = new ColourRow(s);
        ColourRow ??= row;
        _colourRows[s.Key] = row;
        row.Picker.ColorChanged += (_, e) =>
        {
            if (_syncing)
            {
                return;
            }

            if (_colourPending && _colourKey != s.Key)
            {
                FlushColourSettle();
            }

            _colourKey = s.Key;
            Color c = e.NewColor;
            row.HexBox.Text = Hex(c);
            SetColour(s, new Rgb(c.R / 255f, c.G / 255f, c.B / 255f));

            // A pick in the square is a stream of changes while the mouse moves: one history step once it stays still.
            _colourPending = true;
            _colourSettle.Restart();
        };
        foreach ((Button swatch, string hex) in row.Swatches.Zip(row.SwatchHexes))
        {
            string picked = hex;
            swatch.Click += (_, _) =>
            {
                if (!_syncing)
                {
                    PickColour(s, Rgb.FromHex(picked));
                }
            };
        }

        row.HexBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitHex(s, row.HexBox);
                e.Handled = true;
            }
        };
        row.HexBox.LostFocus += (_, _) => CommitHex(s, row.HexBox);
        row.Picked += hex =>
        {
            if (!_syncing)
            {
                PickColour(s, Rgb.FromHex(hex));
            }
        };
        row.AddCurrent += () =>
        {
            if (_book is not null && _book.AddMine([Hex(ToColor(Current.GetColour(s.Key)))]) > 0)
            {
                row.SayNote("Added to My colours");
            }
        };
        row.Removed += hex => _book?.RemoveMine(hex);
        row.Pasted += text => Paste(s, row, text);
        row.ShowLists(_book?.Recent ?? [], _book?.Mine ?? []);
        return row;
    }

    // Nothing happens while the rows are being swapped or set, or for a box no longer shown (its focus lost late).
    private void CommitHex(ParamSpec s, TextBox box)
    {
        if (_syncing || !_colourRows.TryGetValue(s.Key, out ColourRow? row) || !ReferenceEquals(row.HexBox, box))
        {
            return;
        }

        // Another row's pick still settling becomes its own step first.
        if (_colourKey != s.Key)
        {
            FlushColourSettle();
        }

        Color shown = ToColor(Current.GetColour(s.Key));
        string text = (box.Text ?? string.Empty).Trim();
        Rgb typed;
        try
        {
            typed = Rgb.FromHex(text);
        }
        catch (FormatException)
        {
            box.Text = Hex(shown);
            return;
        }

        Color c = ToColor(typed);
        box.Text = Hex(c);
        if (c == shown)
        {
            // The colour already shown (the default's exact numbers stay).
            return;
        }

        _syncing = true;
        row.Picker.Color = c;
        _syncing = false;
        SetColour(s, typed);
        Committed?.Invoke(LabelOf(s.Key));
        NoteRecent(s.Key);
    }

    // A colour chosen goes first in Recent.
    private void NoteRecent(string key)
    {
        if (_book is not null && Current.Values.ContainsKey(key))
        {
            _book.NoteRecent(Hex(ToColor(Current.GetColour(key))));
        }
    }

    private void ShowColourLists()
    {
        foreach (ColourRow row in _colourRows.Values)
        {
            row.ShowLists(_book?.Recent ?? [], _book?.Mine ?? []);
        }
    }

    // easy-props' paste: one code is used; several go into My colours; none says what a code looks like.
    private void Paste(ParamSpec s, ColourRow row, string text)
    {
        (IReadOnlyList<string> codes, int bad) = Colours.ColourCodes.ParseList(text);
        if (codes.Count == 0)
        {
            row.SayNote("Not a colour code. Try #4e7d3e.");
        }
        else if (codes.Count == 1 && bad == 0)
        {
            row.SayNote(string.Empty);
            PickColour(s, Rgb.FromHex(codes[0]));
        }
        else
        {
            int added = _book?.AddMine(codes) ?? 0;
            row.SayNote(added == 1 ? "Added to My colours" : $"Added {added} to My colours");
        }
    }

    private void SetColour(ParamSpec s, Rgb c)
    {
        if (Current.GetColour(s.Key) == c)
        {
            return;
        }

        Current = Current.With(s.Key, c);
        _colourRows.GetValueOrDefault(s.Key)?.Show(Hex(ToColor(c)));
        Announce(s, dragging: false);
    }
}
