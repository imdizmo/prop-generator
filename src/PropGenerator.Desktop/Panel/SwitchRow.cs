// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Details;

/// <summary>
/// One on/off setting on one line, as easy-props' Switch row: the label, a small switch (30 × 16, the accent when on) and the
/// reset slot. The row only shows: the settings panel moves the value.
/// </summary>
internal sealed class SwitchRow : PanelRow
{
    /// <summary>Builds the row for an on/off setting.</summary>
    /// <param name="spec">The setting.</param>
    internal SwitchRow(ParamSpec spec)
        : base(spec, GridLength.Star, new GridLength(RowParts.ResetSize))
    {
        Switch = new ToggleSwitch
        {
            IsChecked = spec.Default.GetBoolean(),
            OnContent = null,
            OffContent = null,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Switch.Resources["ToggleSwitchFillOn"] = Look.Theme.Accent;
        Switch.Resources["ToggleSwitchFillOnPointerOver"] = Look.Theme.AccentHover;
        Switch.Resources["ToggleSwitchFillOnPressed"] = Look.Theme.AccentHover;
        Switch.Resources["ToggleSwitchStrokeOn"] = Look.Theme.Accent;
        Switch.Resources["ToggleSwitchStrokeOnPointerOver"] = Look.Theme.AccentHover;
        Switch.Resources["ToggleSwitchKnobFillOn"] = Brushes.White;
        Switch.Resources["ToggleSwitchKnobFillOnPointerOver"] = Brushes.White;
        Switch.Resources["ToggleSwitchFillOff"] = Look.Theme.SurfaceSunken;
        Switch.Resources["ToggleSwitchStrokeOff"] = Look.Theme.BorderStrong;
        Switch.Resources["ToggleSwitchKnobFillOff"] = Look.Theme.TextSecondary;
        AutomationProperties.SetName(Switch, spec.Label);
        Chrome.Tip(Switch, spec.Tooltip);

        // The Fluent switch is 40 × 20; easy-props' is 30 × 16, so it is drawn at three quarters.
        var small = new LayoutTransformControl
        {
            LayoutTransform = new ScaleTransform(0.75, 0.75),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = Switch,
        };
        SetColumn(small, 1);
        Children.Add(small);

        Reset = RowParts.Reset(spec.Label);
        SetColumn(Reset, 2);
        Children.Add(Reset);
    }

    /// <summary>Gets the switch.</summary>
    internal ToggleSwitch Switch { get; }

    /// <summary>Gets the reset button (shown only while the value differs from the starting point's).</summary>
    internal Button Reset { get; }
}
