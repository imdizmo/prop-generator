// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Look;
using PropGenerator.Desktop.Menus;

namespace PropGenerator.Desktop.View;

/// <summary>
/// A tool over the 3D view that opens a menu below it, its right edge on the tool's (easy-props' ToolMenu, placed
/// "bottom end" 4 below): each row a choice with a check on the current one and what it does on a second line. The rows
/// are made from the commands each time the menu opens, so a greyed choice says why and the check is current. A click
/// on a row closes the menu and runs it; a click anywhere else closes it. Camera angle's menu has no help lines, as in
/// easy-props.
/// </summary>
internal sealed class ToolMenu
{
    private readonly Func<IReadOnlyList<Command>> _items;
    private readonly StackPanel _stack = new();
    private readonly List<MenuRow> _rows = [];
    private readonly bool _help;

    /// <summary>Makes the tool and its menu.</summary>
    /// <param name="trigger">The tool that opens it.</param>
    /// <param name="items">The choices, read each time it opens.</param>
    /// <param name="help">True to show what each choice does on a second line.</param>
    internal ToolMenu(ToolButton trigger, Func<IReadOnlyList<Command>> items, bool help = true)
    {
        _help = help;
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(items);
        Trigger = trigger;
        _items = items;
        Card = Chrome.StylePopover(new Border { Child = _stack });
        Popup = new Popup
        {
            Child = Card,
            PlacementTarget = trigger,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.BottomRight,
            PlacementGravity = PopupGravity.BottomLeft,
            VerticalOffset = 4,
            IsLightDismissEnabled = true,
        };
        Popup.Closed += (_, _) =>
        {
            _rows.Clear();
            _stack.Children.Clear();
            trigger.Classes.Remove("ep-open");
        };
        PopoverToggle.Wire(trigger, Popup, Open, Close);
    }

    /// <summary>Gets the tool that opens the menu.</summary>
    internal ToolButton Trigger { get; }

    /// <summary>Gets the popup that holds the menu (the view's overlay holds it).</summary>
    internal Popup Popup { get; }

    /// <summary>Gets the menu's card.</summary>
    internal Border Card { get; }

    /// <summary>Gets a value indicating whether the menu is open.</summary>
    internal bool IsOpen => Popup.IsOpen;

    /// <summary>Gets the rows of the open menu; empty when it is closed.</summary>
    internal IReadOnlyList<MenuRow> Rows => _rows;

    /// <summary>Gets the open menu as lines of text ("Label ✓ ✗" with only the parts it has), for the tests.</summary>
    internal IReadOnlyList<string> Lines => [.. _rows.Select(r => r.Line)];

    /// <summary>Opens the menu with its choices as they are now.</summary>
    internal void Open()
    {
        if (IsOpen)
        {
            return;
        }

        _rows.Clear();
        _stack.Children.Clear();
        foreach (Command c in _items())
        {
            var row = new MenuRow(c, _help);
            row.Click += (_, _) =>
            {
                Close();
                _ = c.Run();
            };
            _rows.Add(row);
            _stack.Children.Add(row);
        }

        Trigger.Classes.Add("ep-open");
        Popup.IsOpen = true;
    }

    /// <summary>Closes the menu.</summary>
    internal void Close() => Popup.IsOpen = false;
}
