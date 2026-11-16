// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Layout;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Details;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.View;

/// <summary>
/// The Sun and shadow tool and its popover, as easy-props' LightingButton: 230 wide, the sun dial with "Drag to move
/// the sun" under it, the four times of day two by two (the one whose sun matches is pressed), and the Shadow slider
/// from 0 to 1 in steps of 0.05. Every change goes to the view state's light at once.
/// </summary>
internal sealed class SunPopover
{
    private static readonly ParamSpec ShadowSpec = new(
        "shadow", "Shadow", "How dark the shadows are, from none to full.", "view", "float", 0, 1, 0.05, JsonSerializer.SerializeToElement(0.8));

    private readonly ViewState _state;
    private bool _syncing;

    /// <summary>Makes the tool and its popover.</summary>
    /// <param name="state">The view state whose light it shows and sets.</param>
    internal SunPopover(ViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        Trigger = new ToolButton("sun", "Sun and shadow", "Sun and shadow: where the sun stands and how dark its shadow is.");
        Dial = new SunDial(() => _state.Lighting, l => _state.Lighting = l);
        Caption = new TextBlock
        {
            Text = "Drag to move the sun",
            FontSize = Theme.Caption,
            Foreground = Theme.TextTertiary,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = Theme.Space1, RowSpacing = Theme.Space1 };
        var times = new List<Button>();
        for (int i = 0; i < Presets.Count; i++)
        {
            (string label, Lighting light) = Presets[i];
            var b = new Button
            {
                Content = label,
                Height = 24,
                MinHeight = 0,
                MinWidth = 0,
                Padding = new Thickness(0),
                FontSize = Theme.Caption,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            b.Classes.Add("ep");
            b.Classes.Add("ep-time");
            Chrome.Tip(b, string.Create(CultureInfo.InvariantCulture, $"{label} light: the sun {light.Azimuth:0}° round and {light.Elevation:0}° up, warmth {light.Warmth:0.##}, shadow {light.Shadow:0.##}."));
            Lighting chosen = light;
            b.Click += (_, _) => _state.Lighting = chosen;
            Grid.SetColumn(b, i % 2);
            Grid.SetRow(b, i / 2);
            grid.Children.Add(b);
            times.Add(b);
        }

        Times = times;
        Shadow = new NumberRow(ShadowSpec);
        Shadow.Reset.IsVisible = false;

        // easy-props' popover row: 50 for the label, the slider, 46 for the number and the reset's 18.
        Shadow.LabelColumn = 50;
        Shadow.ColumnDefinitions[2].Width = new GridLength(46);
        Shadow.ColumnDefinitions[3].Width = new GridLength(18);
        Shadow.Slider.ValueChanged += (_, e) =>
        {
            if (!_syncing)
            {
                _state.Lighting = _state.Lighting with { Shadow = Math.Clamp(Math.Round(e.NewValue / 0.05) * 0.05, 0, 1) };
            }
        };
        Shadow.Box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                TakeTyped();
                e.Handled = true;
            }
        };
        Shadow.Box.LostFocus += (_, _) => TakeTyped();

        var dial = new StackPanel { Spacing = Theme.Space1, HorizontalAlignment = HorizontalAlignment.Center, Children = { Dial, Caption } };
        Content = Chrome.StylePopover(new Border
        {
            Width = 230,
            Padding = new Thickness(Theme.Space2),
            Child = new StackPanel { Spacing = Theme.Space2, Children = { dial, grid, Shadow } },
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
        state.Changed += Refresh;
        Refresh();
    }

    /// <summary>Gets easy-props' times of day: azimuth, elevation, warmth and shadow.</summary>
    internal static IReadOnlyList<(string Label, Lighting Light)> Presets { get; } =
    [
        ("Morning", new Lighting(48, 30, 0.72, 0.85)),
        ("Midday", new Lighting(165, 68, 0.2, 0.5)),
        ("Golden", new Lighting(232, 18, 1, 0.8)),
        ("Dusk", new Lighting(268, 11, 0.5, 0.4)),
    ];

    /// <summary>Gets the Sun and shadow tool.</summary>
    internal ToolButton Trigger { get; }

    /// <summary>Gets the popup (the view's overlay holds it).</summary>
    internal Popup Popup { get; }

    /// <summary>Gets the popover's card.</summary>
    internal Border Content { get; }

    /// <summary>Gets the sun dial.</summary>
    internal SunDial Dial { get; }

    /// <summary>Gets the line under the dial.</summary>
    internal TextBlock Caption { get; }

    /// <summary>Gets the time of day buttons: Morning, Midday, Golden and Dusk.</summary>
    internal IReadOnlyList<Button> Times { get; }

    /// <summary>Gets the Shadow row.</summary>
    internal NumberRow Shadow { get; }

    /// <summary>Gets a value indicating whether the popover is open.</summary>
    internal bool IsOpen => Popup.IsOpen;

    /// <summary>Opens the popover.</summary>
    internal void Open()
    {
        Refresh();
        Trigger.Classes.Add("ep-open");
        Popup.IsOpen = true;
    }

    /// <summary>Closes the popover.</summary>
    internal void Close() => Popup.IsOpen = false;

    // The time whose sun matches within half a degree is pressed, as in easy-props; the slider and dial follow the light.
    private void Refresh()
    {
        Lighting l = _state.Lighting;
        for (int i = 0; i < Presets.Count; i++)
        {
            Lighting p = Presets[i].Light;
            Times[i].Classes.Set("ep-pressed", Math.Abs(p.Azimuth - l.Azimuth) < 0.5 && Math.Abs(p.Elevation - l.Elevation) < 0.5);
        }

        _syncing = true;
        Shadow.Slider.Value = l.Shadow;
        _syncing = false;
        if (!Shadow.Box.IsFocused)
        {
            Shadow.Box.Text = l.Shadow.ToString("0.00", CultureInfo.InvariantCulture);
        }

        Dial.InvalidateVisual();
    }

    private void TakeTyped()
    {
        if (double.TryParse(Shadow.Box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v))
        {
            double kept = Math.Clamp(v, 0, 1);
            if (kept != v)
            {
                Shadow.ShowNote(NumberRow.KeptTo(ShadowSpec));
            }

            _state.Lighting = _state.Lighting with { Shadow = kept };
        }

        Shadow.Box.Text = _state.Lighting.Shadow.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
