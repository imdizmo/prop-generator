// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Details;

/// <summary>
/// One choice on one line, as easy-props' Select row: the label, a drop-down of the options (26 high, sunken, a strong
/// border under the pointer, lucide's chevron) whose menu is the menu popover with a check on the current option, and
/// the reset slot. The row only shows: the settings panel moves the value.
/// </summary>
internal sealed class ChoiceRow : PanelRow
{
    // The check of each option's line in the menu, by its label (a line made again replaces its entry).
    private readonly Dictionary<string, Icon> _checks = new(StringComparer.Ordinal);

    /// <summary>Builds the row for a setting with options.</summary>
    /// <param name="spec">The setting.</param>
    internal ChoiceRow(ParamSpec spec)
        : base(spec, GridLength.Star, new GridLength(RowParts.ResetSize))
    {
        Combo = new ComboBox
        {
            ItemsSource = (spec.Options ?? []).Select(o => o.Label).ToList(),
            Height = Look.Theme.ControlHeight,
            MinHeight = 0,
            FontSize = Look.Theme.Label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Template = DropDownTemplate(),
            ItemTemplate = new FuncDataTemplate<string>((label, _) => Line(label)),
        };
        Combo.Resources["ComboBoxItemBackground"] = Brushes.Transparent;
        Combo.Resources["ComboBoxItemBackgroundPointerOver"] = Look.Theme.SurfaceActive;
        Combo.Resources["ComboBoxItemBackgroundPressed"] = Look.Theme.SurfaceActive;
        Combo.Resources["ComboBoxItemBackgroundSelected"] = Brushes.Transparent;
        Combo.Resources["ComboBoxItemBackgroundSelectedPointerOver"] = Look.Theme.SurfaceActive;
        Combo.Resources["ComboBoxItemBackgroundSelectedPressed"] = Look.Theme.SurfaceActive;
        Combo.Resources["ComboBoxItemForeground"] = Look.Theme.TextPrimary;
        Combo.Resources["ComboBoxItemForegroundPointerOver"] = Look.Theme.TextPrimary;
        Combo.Resources["ComboBoxItemForegroundSelected"] = Look.Theme.TextPrimary;
        Combo.Resources["ComboBoxItemForegroundSelectedPointerOver"] = Look.Theme.TextPrimary;
        Combo.SelectionChanged += (_, _) => ShowCheck();
        AutomationProperties.SetName(Combo, spec.Label);
        Chrome.Tip(Combo, spec.Tooltip);
        SetColumn(Combo, 1);
        Children.Add(Combo);

        Reset = RowParts.Reset(spec.Label);
        SetColumn(Reset, 2);
        Children.Add(Reset);
    }

    /// <summary>Gets the drop-down of the options, in the setting's order.</summary>
    internal ComboBox Combo { get; }

    /// <summary>Gets the reset button (shown only while the choice differs from the starting point's).</summary>
    internal Button Reset { get; }

    /// <summary>Gets the label of the option whose menu line shows the check, or null while the menu was never shown.</summary>
    internal string? Checked => _checks.Where(kv => kv.Value.IsVisible).Select(kv => kv.Key).FirstOrDefault();

    // One line of the menu: the option, and the check when it is the current one.
    private Grid Line(string label)
    {
        var check = new Icon("check", 13)
        {
            Foreground = Look.Theme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = Equals(Combo.SelectedItem, label),
        };
        _checks[label] = check;
        var text = new TextBlock { Text = label, FontSize = Look.Theme.Body, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(check, 1);
        return new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = Look.Theme.Space2, Children = { text, check } };
    }

    private void ShowCheck()
    {
        foreach ((string label, Icon check) in _checks)
        {
            check.IsVisible = Equals(Combo.SelectedItem, label);
        }
    }

    // easy-props' select: a sunken field with the value and lucide's chevron-down; its menu is the menu popover
    // (raised, strong border, radius 6, shadow) holding the options. The parts carry the names the drop-down looks for.
    private static FuncControlTemplate<ComboBox> DropDownTemplate() => new((combo, scope) =>
    {
        var value = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Foreground = Look.Theme.TextPrimary };
        value.Bind(ContentControl.ContentProperty, combo.GetObservable(SelectingItemsControl.SelectedItemProperty));
        var chevron = new Icon("chevron-down", 14) { Foreground = Look.Theme.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(chevron, Dock.Right);
        var face = new Border
        {
            Background = Look.Theme.SurfaceRaised,
            BorderBrush = Look.Theme.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Look.Theme.Radius2),
            Padding = new Thickness(8, 0, 6, 0),
            Child = new DockPanel { Children = { chevron, value } },
        };
        void Edge() => face.BorderBrush = combo.IsFocused || combo.IsDropDownOpen ? Look.Theme.Accent
            : combo.IsPointerOver ? Look.Theme.BorderStrong : Look.Theme.BorderSubtle;
        combo.PointerEntered += (_, _) => Edge();
        combo.PointerExited += (_, _) => Edge();
        combo.GotFocus += (_, _) => Edge();
        combo.LostFocus += (_, _) => Edge();

        var items = new ItemsPresenter { Name = "PART_ItemsPresenter" };
        items.Bind(ItemsPresenter.ItemsPanelProperty, combo.GetObservable(ItemsControl.ItemsPanelProperty));
        Border menu = Chrome.StylePopover(new Border { Child = new ScrollViewer { Content = items, MaxHeight = 320 } });
        var popup = new Popup
        {
            Name = "PART_Popup",
            Child = menu,
            PlacementTarget = face,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
        };
        popup[!!Popup.IsOpenProperty] = combo[!!ComboBox.IsDropDownOpenProperty];
        popup.Opened += (_, _) =>
        {
            menu.MinWidth = Math.Max(208, face.Bounds.Width);
            Edge();
        };
        popup.Closed += (_, _) => Edge();
        scope.Register(items.Name, items);
        scope.Register(popup.Name, popup);
        return new Panel { Children = { face, popup } };
    });
}
