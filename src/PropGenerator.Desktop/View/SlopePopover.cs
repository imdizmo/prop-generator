// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Media;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Details;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.View;

/// <summary>
/// The Slope tool and its popover, as easy-props' GroundButton: Slope from 0 to 35° in whole degrees and Downhill from
/// 0 to 355° in fives, each with a reset to 0, and upstream's caption. Only the view changes (the view state keeps them
/// in the prefs). The tool is pressed while the ground slopes; while the floor tile is shown it is greyed, as the floor
/// stays level.
/// </summary>
internal sealed class SlopePopover
{
    /// <summary>Why the tool cannot be used while the floor tile is shown.</summary>
    internal const string FloorLevel = "The floor tile stays level";

    /// <summary>Upstream's caption under the rows.</summary>
    internal const string Tilts = "Tilts the ground, to see how the prop sits on a hillside. The prop keeps its shape.";

    /// <summary>Upstream's Slope setting (settings-spec.ts groundSlope).</summary>
    internal static readonly ParamSpec SlopeSpec = new(
        "groundSlope", "Slope", "Tilts the ground under the prop, to see how it sits on a hillside. Only the view changes: the prop keeps its shape.",
        "view", "int", ViewState.MinSlope, ViewState.MaxSlope, ViewState.SlopeStep, JsonSerializer.SerializeToElement(0), Unit: "°");

    /// <summary>Upstream's Downhill setting (settings-spec.ts groundDownhill).</summary>
    internal static readonly ParamSpec DownhillSpec = new(
        "groundDownhill", "Downhill", "Which way the sloping ground falls, turning around the prop. 0 falls towards the front.",
        "view", "int", 0, ViewState.MaxDownhill, ViewState.DownhillStep, JsonSerializer.SerializeToElement(0), Unit: "°");

    /// <summary>Upstream's search words for Slope.</summary>
    internal const string SlopeKeywords = "hill hillside slope tilt ground";

    /// <summary>Upstream's search words for Downhill.</summary>
    internal const string DownhillKeywords = "hill hillside slope direction ground";

    private readonly ViewState _state;
    private readonly Func<string?> _unavailable;
    private readonly Action<string> _say;
    private bool _syncing;

    /// <summary>Makes the tool and its popover, following the view state.</summary>
    /// <param name="state">The view state: the slope and the way it falls.</param>
    /// <param name="unavailable">Says why the slope cannot be shown now (the floor tile), or null when it can.</param>
    /// <param name="say">Puts the reason on the status line when the greyed tool is clicked.</param>
    internal SlopePopover(ViewState state, Func<string?> unavailable, Action<string> say)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _unavailable = unavailable;
        _say = say;
        Trigger = new ToolButton("triangle-right", "Slope", "Slope: tilt the ground under the prop, to see how it sits on a hillside. Only the view changes.");
        Slope = Row(SlopeSpec, v => _state.GroundSlope = v);
        Downhill = Row(DownhillSpec, v => _state.GroundDownhill = v);
        Caption = new TextBlock
        {
            Text = Tilts,
            FontSize = Theme.Caption,
            Foreground = Theme.TextTertiary,
            TextWrapping = TextWrapping.Wrap,
        };
        Content = Chrome.StylePopover(new Border
        {
            Width = 230,
            Padding = new Thickness(Theme.Space2),
            Child = new StackPanel { Spacing = Theme.Space2, Children = { Slope, Downhill, Caption } },
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

    /// <summary>Gets the Slope tool (pressed while the ground slopes).</summary>
    internal ToolButton Trigger { get; }

    /// <summary>Gets the popup (the view's overlay holds it).</summary>
    internal Popup Popup { get; }

    /// <summary>Gets the popover's card.</summary>
    internal Border Content { get; }

    /// <summary>Gets the Slope row.</summary>
    internal NumberRow Slope { get; }

    /// <summary>Gets the Downhill row.</summary>
    internal NumberRow Downhill { get; }

    /// <summary>Gets the caption under the rows.</summary>
    internal TextBlock Caption { get; }

    /// <summary>Gets a value indicating whether the popover is open.</summary>
    internal bool IsOpen => Popup.IsOpen;

    /// <summary>Shows the view state: the rows, their resets, whether the tool can be used and whether it is pressed.</summary>
    internal void Refresh()
    {
        _syncing = true;
        Show(Slope, _state.GroundSlope);
        Show(Downhill, _state.GroundDownhill);
        _syncing = false;
        string? why = _unavailable();
        if (Trigger.Unavailable != why)
        {
            Trigger.Unavailable = why;
        }

        Trigger.Pressed = why is null && _state.GroundSlope > 0;
        if (why is not null && Popup.IsOpen)
        {
            Close();
        }
    }

    /// <summary>Opens the popover, or only says why when the slope cannot be shown now.</summary>
    internal void Open()
    {
        if (_unavailable() is string why)
        {
            _say(why);
            return;
        }

        Refresh();
        Trigger.Classes.Add("ep-open");
        Popup.IsOpen = true;
    }

    /// <summary>Closes the popover.</summary>
    internal void Close() => Popup.IsOpen = false;

    private static void Show(NumberRow row, double value)
    {
        row.Slider.Value = value;
        row.Box.Text = value.ToString("0", CultureInfo.InvariantCulture);
        row.Reset.IsVisible = value != 0;
    }

    // easy-props' popover row (as the Wind's Strength): 60 for the label, the slider, 46 for the number and the reset's
    // 18. The slider, a typed number (Enter or leaving the box) and the reset all go to the view state, which keeps them
    // to the range and the step.
    private NumberRow Row(ParamSpec spec, Action<double> set)
    {
        var row = new NumberRow(spec) { LabelColumn = 60 };
        row.ColumnDefinitions[2].Width = new GridLength(46);
        row.ColumnDefinitions[3].Width = new GridLength(18);
        AutomationTip(row.Reset, spec.Label);
        row.Slider.ValueChanged += (_, e) =>
        {
            if (!_syncing)
            {
                set(e.NewValue);
            }
        };
        row.Box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                TakeTyped(row, set);
                e.Handled = true;
            }
        };
        row.Box.LostFocus += (_, _) => TakeTyped(row, set);
        row.Reset.Click += (_, _) => set(0);
        return row;
    }

    private static void AutomationTip(Button reset, string label) =>
        Chrome.Tip(reset, $"Reset: put {label} back to 0.");

    // A number typed into a row's box: a finite number goes to the view state (kept to the range, with the note when it
    // was outside); anything else is put back.
    private void TakeTyped(NumberRow row, Action<double> set)
    {
        if (double.TryParse(row.Box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v))
        {
            if (v < row.Spec.Min || v > row.Spec.Max)
            {
                row.ShowNote(NumberRow.KeptTo(row.Spec));
            }

            set(v);
        }

        Refresh();
    }
}
