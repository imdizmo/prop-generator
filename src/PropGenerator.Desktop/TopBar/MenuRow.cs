// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Menus;

/// <summary>
/// One row of a menu: the command's icon (a check when it is on), its label, its shortcut and, for a sub-list, a
/// chevron. A command that cannot run is greyed and its reason is the row's tooltip; otherwise the tooltip says what
/// it does. The row shows the command as it was when the menu opened. The view toolbar's menus show what the command
/// does under its label too, as easy-props' help lines.
/// </summary>
internal sealed class MenuRow : Button
{
    /// <summary>Builds the row for a command as it is now.</summary>
    /// <param name="item">The command.</param>
    /// <param name="help">True to show what the command does (its tooltip) on a second line under the label.</param>
    internal MenuRow(Command item, bool help = false)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        Label = item.Label;
        Keys = item.Keys;
        HasChildren = item.Children is not null;
        IsOn = item.Checked() == true;
        string? reason = item.Unavailable();

        Classes.Add("ep");
        Classes.Add("ep-menu-item");
        MinHeight = Look.Theme.MenuItemHeight;
        MinWidth = 0;
        Padding = new Thickness(6, 3, 8, 3);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*,Auto,Auto"), ColumnSpacing = Look.Theme.Space2 };
        string? icon = IsOn ? "check" : item.Icon;
        if (icon is not null)
        {
            grid.Children.Add(new Icon(icon, 14)
            {
                Foreground = IsOn ? Look.Theme.Accent : Look.Theme.TextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var label = new TextBlock { Text = Label, FontSize = Look.Theme.Body, VerticalAlignment = VerticalAlignment.Center };
        Help = help ? item.Tooltip : null;
        Control text = label;
        if (Help is not null)
        {
            text = new StackPanel
            {
                MaxWidth = 260,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    label,
                    new TextBlock
                    {
                        Text = Help,
                        FontSize = Look.Theme.Caption,
                        Foreground = Look.Theme.TextSecondary,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                },
            };
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (Keys is not null)
        {
            Border chip = Chrome.KeyChip(Keys);
            Grid.SetColumn(chip, 2);
            grid.Children.Add(chip);
        }

        if (HasChildren)
        {
            var more = new Icon("chevron-right", 13) { Foreground = Look.Theme.TextTertiary, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(more, 3);
            grid.Children.Add(more);
        }

        Content = grid;
        AutomationProperties.SetName(this, Label);
        Chrome.Tip(this, reason ?? item.Tooltip ?? Label);
        if (reason is not null)
        {
            IsEnabled = false;
            Opacity = 0.75;
            label.Foreground = Look.Theme.TextTertiary;
            ToolTip.SetShowOnDisabled(this, true);
        }
    }

    /// <summary>Gets the command the row runs.</summary>
    internal Command Item { get; }

    /// <summary>Gets the help line shown under the label, or null when the row has none.</summary>
    internal string? Help { get; }

    /// <summary>Gets the label shown.</summary>
    internal string Label { get; }

    /// <summary>Gets the shortcut shown, or null for none.</summary>
    internal string? Keys { get; }

    /// <summary>Gets a value indicating whether the row opens a sub-list.</summary>
    internal bool HasChildren { get; }

    /// <summary>Gets a value indicating whether the row shows a check (an on/off item that is on, or the current choice).</summary>
    internal bool IsOn { get; }

    /// <summary>Gets the row as one line of text, for the tests: "Label [Keys] &gt; ✓ ✗" with only the parts it has.</summary>
    internal string Line =>
        Label + (Keys is null ? string.Empty : $" [{Keys}]") + (HasChildren ? " >" : string.Empty)
        + (IsOn ? " ✓" : string.Empty) + (IsEnabled ? string.Empty : " ✗");

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(Button);
}
