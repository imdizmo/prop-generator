// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Details;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.View;

/// <summary>
/// The Wind tool and its popover, as easy-props' WindButton: the switch turns the wind in the view on or off (it is
/// remembered), Strength runs from calm to a storm, and the caption says when the prop on show holds still (the floor,
/// a rock). The tool is pressed while the prop moves.
/// </summary>
internal sealed class WindPopover
{
    /// <summary>What easy-props says for a prop that does not move in the wind.</summary>
    internal const string Still = "This prop doesn't move in the wind.";

    /// <summary>What easy-props says for a prop that moves in the wind.</summary>
    internal const string Moves = "Moves the prop as the wind materials will move it in your engine.";

    private static readonly ParamSpec WindSpec = new("wind", "Wind", Moves, "view", "bool", null, null, null, JsonSerializer.SerializeToElement(false));

    private static readonly ParamSpec StrengthSpec = new(
        "windStrength", "Strength", "From a calm day to a storm. The middle is how the engine kits start.", "view", "float",
        Prefs.MinWindStrength, Prefs.MaxWindStrength, 0.1, JsonSerializer.SerializeToElement(1.0));

    private readonly ViewState _state;
    private readonly Func<bool> _sways;
    private bool _syncing;

    /// <summary>Makes the tool and its popover, following the view state.</summary>
    /// <param name="state">The view state: the wind's switch and strength.</param>
    /// <param name="sways">Says whether the prop on show moves in the wind.</param>
    internal WindPopover(ViewState state, Func<bool> sways)
    {
        _state = state;
        _sways = sways;
        Trigger = new ToolButton("wind", "Wind", "Wind: move the prop as the wind materials will move it in your engine.");
        var row = new SwitchRow(WindSpec);
        row.Reset.IsVisible = false;
        Switch = row.Switch;
        Chrome.Tip(row.Label, Moves);
        Switch.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing)
            {
                _state.Wind = Switch.IsChecked == true;
            }
        };
        Strength = new NumberRow(StrengthSpec);
        Strength.Reset.IsVisible = false;
        Strength.LabelColumn = 60;
        Strength.ColumnDefinitions[2].Width = new GridLength(46);
        Strength.ColumnDefinitions[3].Width = new GridLength(18);
        Strength.Slider.ValueChanged += (_, e) =>
        {
            if (!_syncing)
            {
                _state.WindStrength = Math.Clamp(Math.Round(e.NewValue * 10) / 10, Prefs.MinWindStrength, Prefs.MaxWindStrength);
            }
        };
        Strength.Box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                TakeTyped();
                e.Handled = true;
            }
        };
        Strength.Box.LostFocus += (_, _) => TakeTyped();
        var ends = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        ends.Children.Add(new TextBlock { Text = "Calm", FontSize = Theme.Caption, Foreground = Theme.TextTertiary });
        var storm = new TextBlock { Text = "Storm", FontSize = Theme.Caption, Foreground = Theme.TextTertiary, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(storm, 1);
        ends.Children.Add(storm);
        Caption = new TextBlock
        {
            Text = Still,
            FontSize = Theme.Caption,
            Foreground = Theme.TextTertiary,
            TextWrapping = TextWrapping.Wrap,
        };
        Content = Chrome.StylePopover(new Border
        {
            Width = 230,
            Padding = new Thickness(Theme.Space2),
            Child = new StackPanel { Spacing = Theme.Space2, Children = { row, Strength, ends, Caption } },
        });
        Content.MinWidth = 0;
        Popup = new Popup
        {
            Child = Content,
            PlacementTarget = Trigger,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.BottomRight,
            PlacementGravity = PopupGravity.BottomLeft,
            VerticalOffset = 6,
            IsLightDismissEnabled = true,
        };
        Popup.Closed += (_, _) => Trigger.Classes.Remove("ep-open");
        PopoverToggle.Wire(Trigger, Popup, Open, Close);
    }

    /// <summary>Gets the Wind tool (never pressed: nothing moves).</summary>
    internal ToolButton Trigger { get; }

    /// <summary>Gets the popup (the view's overlay holds it).</summary>
    internal Popup Popup { get; }

    /// <summary>Gets the popover's card.</summary>
    internal Border Content { get; }

    /// <summary>Gets the wind switch, off and greyed.</summary>
    internal ToggleSwitch Switch { get; }

    /// <summary>Gets the caption under the switch.</summary>
    internal TextBlock Caption { get; }

    /// <summary>Gets the Strength row.</summary>
    internal NumberRow Strength { get; }

    // A strength typed into the box: a finite number, kept to calm..storm in tenths; anything else is put back.
    private void TakeTyped()
    {
        if (double.TryParse(Strength.Box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && double.IsFinite(v))
        {
            double kept = Math.Clamp(Math.Round(v * 10) / 10, Prefs.MinWindStrength, Prefs.MaxWindStrength);
            if (kept != v)
            {
                Strength.ShowNote(NumberRow.KeptTo(StrengthSpec));
            }

            _state.WindStrength = kept;
        }

        Refresh();
    }

    /// <summary>Shows the view state and the prop on show: the switch, the strength, the caption and whether the tool is pressed.</summary>
    internal void Refresh()
    {
        _syncing = true;
        Switch.IsChecked = _state.Wind;
        Strength.Slider.Value = _state.WindStrength;
        Strength.Box.Text = _state.WindStrength.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        _syncing = false;
        bool sways = _sways();
        Caption.Text = sways ? Moves : Still;
        Trigger.Pressed = _state.Wind && sways;
    }

    /// <summary>Gets a value indicating whether the popover is open.</summary>
    internal bool IsOpen => Popup.IsOpen;

    /// <summary>Opens the popover.</summary>
    internal void Open()
    {
        Trigger.Classes.Add("ep-open");
        Popup.IsOpen = true;
    }

    /// <summary>Closes the popover.</summary>
    internal void Close() => Popup.IsOpen = false;
}
