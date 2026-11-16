// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Details;

/// <summary>
/// A colour on one line, as easy-props' ColourRow: the label, named swatches first (the current one ringed), then the
/// custom circle, which opens the colour square with a #RRGGBB box under it. The row only shows: the settings panel
/// moves the value.
/// </summary>
internal sealed class ColourRow : PanelRow
{
    private static readonly IBrush SwatchEdge = new SolidColorBrush(Color.FromArgb(102, 128, 128, 128));
    private static readonly Color Panel = ((ISolidColorBrush)Look.Theme.SurfacePanel).Color;
    private static readonly Color Ring = ((ISolidColorBrush)Look.Theme.TextPrimary).Color;

    private readonly List<(Button Button, Border Face, string Hex)> _swatches = [];
    private readonly WrapPanel _recentSwatches = new() { MaxWidth = 220 };
    private readonly WrapPanel _mineSwatches = new() { MaxWidth = 200 };
    private readonly TextBlock _note = new() { FontSize = Look.Theme.Caption, Foreground = Look.Theme.TextSecondary, TextWrapping = TextWrapping.Wrap, MaxWidth = 220 };

    /// <summary>Builds the row for a colour setting.</summary>
    /// <param name="spec">The setting.</param>
    internal ColourRow(ParamSpec spec)
        : base(spec, GridLength.Star)
    {

        var swatches = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3) };
        AutomationProperties.SetName(swatches, spec.Label);
        // A colour with named swatches of its own (a rock's stone row) offers those; the floor's wood offers its woods.
        IEnumerable<(string Name, string Hex, string Tip)> named = spec.Swatches is { } own
            ? own.Select(w => (w.Label, w.Value, $"{w.Label}: this colour in the {w.Label} palette. Click to use it."))
            : WoodSwatches.All.Take(7).Select(w => (w.Name, w.Hex, $"{w.Name}: {w.Looks}. Click to give the wood this colour."));
        foreach ((string name, string hex, string tip) in named)
        {
            (Button b, Border face) = Swatch(Color.Parse(hex), new CornerRadius(Look.Theme.Radius1));
            AutomationProperties.SetName(b, name);
            Chrome.Tip(b, tip);
            _swatches.Add((b, face, hex));
            swatches.Children.Add(b);
        }

        (Custom, CustomFace) = Swatch(Colors.Transparent, new CornerRadius(9));
        Custom.Margin = new Thickness(6, 1.5, 1.5, 1.5);
        CustomFace.BorderThickness = new Thickness(0);
        CustomFace.BoxShadow = new BoxShadows(new BoxShadow { IsInset = true, Spread = 1.5, Color = Color.FromArgb(89, 255, 255, 255) });
        AutomationProperties.SetName(Custom, $"{spec.Label}: pick any colour");
        Chrome.Tip(Custom, "Pick any colour: opens the colour square, where any colour can be picked or typed as #RRGGBB.");
        swatches.Children.Add(Custom);
        SetColumn(swatches, 1);
        Children.Add(swatches);

        Picker = new ColorView
        {
            IsAlphaEnabled = false,
            IsAlphaVisible = false,

            // Only the square with its brightness bar: the box under it takes an exact #RRGGBB, so the named palette
            // and the number boxes are left out.
            IsColorPaletteVisible = false,
            IsColorComponentsVisible = false,
            IsColorModelVisible = false,
            IsHexInputVisible = false,
        };
        AutomationProperties.SetName(Picker, spec.Label);
        Chrome.Tip(Picker, spec.Tooltip);
        Picker.Styles.AddRange(FlyoutTips());
        HexBox = new TextBox { Width = 90, HorizontalAlignment = HorizontalAlignment.Left, FontSize = Look.Theme.Label };
        AutomationProperties.SetName(HexBox, spec.Label + " hex");
        Chrome.Tip(HexBox, spec.Tooltip + " Type a colour as #RRGGBB here and press Enter.");
        AddMine = new Button { Content = "+", MinWidth = 0, Padding = new Thickness(6, 0), FontSize = Look.Theme.Label };
        AutomationProperties.SetName(AddMine, spec.Label + ": add to My colours");
        Chrome.Tip(AddMine, "Add the colour shown now to My colours, to use it again in any colour row.");
        AddMine.Click += (_, _) => AddCurrent?.Invoke();
        PasteBox = new TextBox { Width = 200, FontSize = Look.Theme.Label, PlaceholderText = "Paste colour codes" };
        AutomationProperties.SetName(PasteBox, spec.Label + " paste codes");
        Chrome.Tip(PasteBox, "Paste one colour code to use it, or a list (#4e7d3e, 4e7d3e, #abc or rgb(78, 125, 62), split by spaces, commas or semicolons) to add them all to My colours. Press Enter.");
        PasteBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Paste(PasteBox.Text ?? string.Empty);
                e.Handled = true;
            }
        };
        var mineRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _mineSwatches, AddMine } };
        var card = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Picker, HexBox,
                Caption("Recent"), _recentSwatches,
                Caption("My colours"), mineRow,
                PasteBox, _note,
            },
        };
        Custom.Flyout = new Flyout { Content = card, Placement = PlacementMode.LeftEdgeAlignedTop };
    }

    /// <summary>Raised when a colour of Recent or My colours is clicked, with its #rrggbb.</summary>
    internal event Action<string>? Picked;

    /// <summary>Raised when the + of My colours is clicked.</summary>
    internal event Action? AddCurrent;

    /// <summary>Raised when a colour is taken out of My colours (its menu), with its #rrggbb.</summary>
    internal event Action<string>? Removed;

    /// <summary>Raised when codes are pasted (Enter in the paste box), with the text.</summary>
    internal event Action<string>? Pasted;

    /// <summary>Gets the + that adds the colour shown to My colours.</summary>
    internal Button AddMine { get; }

    /// <summary>Gets the box colour codes are pasted into.</summary>
    internal TextBox PasteBox { get; }

    /// <summary>Gets the swatches of Recent, newest first.</summary>
    internal IReadOnlyList<Button> RecentSwatches => [.. _recentSwatches.Children.OfType<Button>()];

    /// <summary>Gets the colours of Recent, newest first.</summary>
    internal IReadOnlyList<string> RecentHexes => [.. RecentSwatches.Select(b => (string)b.Tag!)];

    /// <summary>Gets the swatches of My colours.</summary>
    internal IReadOnlyList<Button> MineSwatches => [.. _mineSwatches.Children.OfType<Button>()];

    /// <summary>Gets My colours as shown.</summary>
    internal IReadOnlyList<string> MineHexes => [.. MineSwatches.Select(b => (string)b.Tag!)];

    /// <summary>Gets the short note under the paste box ("Added 3 to My colours", or why a code was not taken).</summary>
    internal string Note => _note.Text ?? string.Empty;

    /// <summary>Shows Recent and My colours.</summary>
    /// <param name="recent">The recent colours, newest first.</param>
    /// <param name="mine">My colours.</param>
    internal void ShowLists(IReadOnlyList<string> recent, IReadOnlyList<string> mine)
    {
        Fill(_recentSwatches, recent, mine: false);
        Fill(_mineSwatches, mine, mine: true);
    }

    /// <summary>Shows a short note under the paste box.</summary>
    /// <param name="text">The note.</param>
    internal void SayNote(string text) => _note.Text = text;

    /// <summary>Pastes codes into the paste box and presses Enter.</summary>
    /// <param name="text">The text pasted.</param>
    internal void Paste(string text)
    {
        PasteBox.Text = text;
        Pasted?.Invoke(text);
        PasteBox.Text = string.Empty;
    }

    /// <summary>Takes a colour out of My colours, as its menu does.</summary>
    /// <param name="hex">The colour.</param>
    internal void RemoveFromMine(string hex) => Removed?.Invoke(hex);

    private static TextBlock Caption(string text) =>
        new() { Text = text, FontSize = Look.Theme.Caption, Foreground = Look.Theme.TextTertiary };

    private void Fill(WrapPanel panel, IReadOnlyList<string> hexes, bool mine)
    {
        if (panel.Children.OfType<Button>().Select(b => (string)b.Tag!).SequenceEqual(hexes))
        {
            return;
        }

        panel.Children.Clear();
        foreach (string hex in hexes)
        {
            (Button b, _) = Swatch(Color.Parse(hex), new CornerRadius(Look.Theme.Radius1));
            b.Tag = hex;
            AutomationProperties.SetName(b, hex);
            Chrome.Tip(b, mine ? $"{hex}, one of My colours. Click to use it; right click to take it out." : $"{hex}, chosen lately. Click to use it again.");
            b.Click += (_, _) => Picked?.Invoke(hex);
            if (mine)
            {
                var remove = new MenuItem { Header = "Take out of My colours" };
                remove.Click += (_, _) => Removed?.Invoke(hex);
                b.ContextMenu = new ContextMenu { ItemsSource = new[] { remove } };
            }

            panel.Children.Add(b);
        }
    }

    /// <summary>Gets the named swatches, pale to dark.</summary>
    internal IReadOnlyList<Button> Swatches => [.. _swatches.Select(s => s.Button)];

    /// <summary>Gets each swatch's colour, as #RRGGBB, in the order of <see cref="Swatches"/>.</summary>
    internal IReadOnlyList<string> SwatchHexes => [.. _swatches.Select(s => s.Hex)];

    /// <summary>Gets the custom circle, which opens the colour square.</summary>
    internal Button Custom { get; }

    /// <summary>Gets the colour square, shown when the custom circle is clicked.</summary>
    internal ColorView Picker { get; }

    /// <summary>Gets the #RRGGBB box under the colour square.</summary>
    internal TextBox HexBox { get; }

    /// <summary>Gets the name of the swatch ringed as the current colour, or null when the colour is none of them.</summary>
    internal string? Current { get; private set; }

    private Border CustomFace { get; }

    /// <summary>Shows a colour: the swatch of that colour gets the ring, and the custom circle is filled with it.</summary>
    /// <param name="hex">The colour as #RRGGBB.</param>
    internal void Show(string hex)
    {
        Current = null;
        CustomFace.Background = new SolidColorBrush(Color.Parse(hex));
        for (int i = 0; i < _swatches.Count; i++)
        {
            bool on = string.Equals(_swatches[i].Hex, hex, StringComparison.OrdinalIgnoreCase);
            _swatches[i].Face.BoxShadow = on
                ? new BoxShadows(new BoxShadow { Spread = 1, Color = Panel }, [new BoxShadow { Spread = 2.5, Color = Ring }])
                : default;
            if (on)
            {
                Current = AutomationProperties.GetName(_swatches[i].Button);
            }
        }
    }

    /// <summary>Opens the colour square under the custom circle (as a click on it does).</summary>
    internal void OpenPicker() => Custom.Flyout!.ShowAt(Custom);

    // An 18 × 18 swatch: the colour with a faint edge that turns light under the pointer.
    private static (Button Button, Border Face) Swatch(Color colour, CornerRadius radius)
    {
        var face = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = radius,
            Background = new SolidColorBrush(colour),
            BorderBrush = SwatchEdge,
            BorderThickness = new Thickness(1),
        };
        var b = new Button
        {
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(1.5),
            Cursor = new Cursor(StandardCursorType.Hand),
            Template = new FuncControlTemplate<Button>((_, _) => face),
        };
        b.PointerEntered += (_, _) => face.BorderBrush = Look.Theme.TextPrimary;
        b.PointerExited += (_, _) => face.BorderBrush = SwatchEdge;
        return (b, face);
    }

    // The colour square's parts are its template, so they get their tooltips through styles.
    private static Style[] FlyoutTips()
    {
        static Style Tip(Func<Selector?, Selector> select, string tip) =>
            new(select) { Setters = { new Setter(ToolTip.TipProperty, tip) } };

        static Selector Accent(Selector? x, string name) => x.OfType<Border>().Name(name);

        return
        [
            Tip(x => x.OfType<TabItem>(), "The colour square with its brightness bar. Pick the wood colour in it; the box under it takes an exact #RRGGBB."),
            Tip(x => x.OfType<ColorSpectrum>(), "Click or drag in this square to pick the wood colour: across it the hue changes (red, orange, yellow and on), up and down how strong the colour is against grey."),
            Tip(x => x.OfType<ColorSlider>(), "Drag this bar to make the wood colour brighter or darker without changing its hue; the square beside it follows."),
            Tip(x => x.OfType<ColorPreviewer>(), "The wood colour picked now, in the middle, with a lighter and a darker shade of it on each side. Click a shade to take it."),
            Tip(x => Accent(x, "PART_AccentDecrement2Border"), "Two steps darker than the colour picked now: click to make the wood this much darker."),
            Tip(x => Accent(x, "PART_AccentDecrement1Border"), "One step darker than the colour picked now: click to make the wood a little darker."),
            Tip(x => Accent(x, "PART_AccentIncrement1Border"), "One step lighter than the colour picked now: click to make the wood a little lighter."),
            Tip(x => Accent(x, "PART_AccentIncrement2Border"), "Two steps lighter than the colour picked now: click to make the wood this much lighter."),
        ];
    }
}
