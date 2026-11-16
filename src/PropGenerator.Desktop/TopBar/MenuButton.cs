// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Menus;

/// <summary>
/// One menu of the top bar: its title, and the card of rows it opens below itself, made from the command list each time
/// it opens so it shows what can run now. A row with a sub-list opens it to its right when pointed at or clicked.
/// </summary>
internal sealed class MenuButton : Button
{
    private readonly TopBar _bar;
    private readonly MenuLayer _layer;
    private readonly MenuLayout _layout;
    private readonly IReadOnlyList<Command> _commands;
    private readonly List<Control> _rows = [];
    private readonly List<MenuRow> _subRows = [];
    private Border? _card;
    private Border? _subCard;
    private MenuRow? _subOf;

    /// <summary>Makes the menu's title button.</summary>
    /// <param name="bar">The top bar, which keeps one menu open at a time.</param>
    /// <param name="layer">Where the menu's card is drawn.</param>
    /// <param name="layout">The title and the items.</param>
    /// <param name="commands">The command list the items come from.</param>
    /// <param name="tooltip">What the menu holds.</param>
    internal MenuButton(TopBar bar, MenuLayer layer, MenuLayout layout, IReadOnlyList<Command> commands, string tooltip)
    {
        _bar = bar;
        _layer = layer;
        _layout = layout;
        _commands = commands;
        Classes.Add("ep");
        Content = new TextBlock { Text = layout.Title, FontSize = Look.Theme.Body, VerticalAlignment = VerticalAlignment.Center };
        Height = Look.Theme.MenuItemHeight;
        MinHeight = 0;
        MinWidth = 0;
        Padding = new Thickness(10, 0);
        VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(this, layout.Title);
        Chrome.Tip(this, tooltip);
        Click += (_, _) => _bar.Toggle(this);
        PointerEntered += (_, _) => _bar.Hovered(this);
    }

    /// <summary>Gets the menu's title.</summary>
    internal string Title => _layout.Title;

    /// <summary>Gets a value indicating whether the menu's card is showing.</summary>
    internal bool IsOpen => _card is not null;

    /// <summary>Gets the rows of the open card (a <see cref="MenuRow"/> per item, a line per divider); empty when closed.</summary>
    internal IReadOnlyList<Control> Rows => _rows;

    /// <summary>Gets the rows of the open sub-list; empty when none is open.</summary>
    internal IReadOnlyList<MenuRow> SubRows => _subRows;

    /// <summary>Gets the open card as lines of text ("-" for a divider), for the tests.</summary>
    internal IReadOnlyList<string> Lines => [.. _rows.Select(r => r is MenuRow m ? m.Line : "-")];

    /// <summary>Gets the open sub-list as lines of text, for the tests.</summary>
    internal IReadOnlyList<string> SubLines => [.. _subRows.Select(r => r.Line)];

    /// <summary>Opens the card below the title (the top bar closes any other menu first).</summary>
    internal void Open()
    {
        if (IsOpen)
        {
            return;
        }

        var stack = new StackPanel();
        foreach (string? id in _layout.Items)
        {
            if (id is null)
            {
                Border line = Chrome.Divider();
                _rows.Add(line);
                stack.Children.Add(line);
                continue;
            }

            Command c = CommandList.Find(_commands, id) ?? throw new InvalidOperationException($"The menu {Title} names '{id}', which is no command.");
            MenuRow row = Row(c);
            if (row.HasChildren)
            {
                row.Click += (_, _) => OpenSub(row);
                row.PointerEntered += (_, _) => OpenSub(row);
            }
            else
            {
                row.PointerEntered += (_, _) => CloseSub();
            }

            _rows.Add(row);
            stack.Children.Add(row);
        }

        _card = Chrome.StylePopover(new Border { Child = stack });
        Classes.Add("ep-open");
        _layer.Show(_card, this.TranslatePoint(new Point(0, Bounds.Height + 2), _layer) ?? default);
    }

    /// <summary>Closes the card and its sub-list.</summary>
    internal void Close()
    {
        CloseSub();
        if (_card is not null)
        {
            _layer.Remove(_card);
            _card = null;
        }

        _rows.Clear();
        Classes.Remove("ep-open");
    }

    /// <summary>Opens a row's sub-list to its right (another one open is closed first).</summary>
    /// <param name="row">A row with a sub-list.</param>
    internal void OpenSub(MenuRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_subOf == row || row.Item.Children is not { } children)
        {
            return;
        }

        CloseSub();
        var stack = new StackPanel();
        foreach (Command c in children)
        {
            MenuRow sub = Row(c);
            _subRows.Add(sub);
            stack.Children.Add(sub);
        }

        _subOf = row;
        _subCard = Chrome.StylePopover(new Border { Child = stack });
        _layer.Show(_subCard, row.TranslatePoint(new Point(row.Bounds.Width + 7, -5), _layer) ?? default);
    }

    /// <summary>
    /// Moves along the open card with the keys: Up and Down between the rows that can run, Right into a sub-list, Left
    /// out of it. Enter and Space press the row that has the focus.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>True when the key was used.</returns>
    internal bool Move(Key key)
    {
        if (!IsOpen)
        {
            return false;
        }

        bool inSub = _subRows.Any(r => r.IsFocused);
        List<MenuRow> rows = inSub ? _subRows : [.. _rows.OfType<MenuRow>()];
        List<MenuRow> live = [.. rows.Where(r => r.IsEnabled)];
        int at = live.FindIndex(r => r.IsFocused);
        switch (key)
        {
            case Key.Down:
                live.ElementAtOrDefault(at < 0 ? 0 : Math.Min(at + 1, live.Count - 1))?.Focus(NavigationMethod.Directional);
                return true;
            case Key.Up:
                live.ElementAtOrDefault(at < 0 ? live.Count - 1 : Math.Max(at - 1, 0))?.Focus(NavigationMethod.Directional);
                return true;
            case Key.Right when !inSub && at >= 0 && live[at].HasChildren:
                OpenSub(live[at]);
                _subRows.FirstOrDefault(r => r.IsEnabled)?.Focus(NavigationMethod.Directional);
                return true;
            case Key.Left when inSub:
                MenuRow? parent = _subOf;
                CloseSub();
                parent?.Focus(NavigationMethod.Directional);
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(Button);

    private MenuRow Row(Command c)
    {
        var row = new MenuRow(c);
        if (!row.HasChildren)
        {
            row.Click += (_, _) =>
            {
                _bar.CloseMenus();
                _ = c.Run();
            };
        }

        return row;
    }

    private void CloseSub()
    {
        if (_subCard is not null)
        {
            _layer.Remove(_subCard);
            _subCard = null;
        }

        _subRows.Clear();
        _subOf = null;
    }
}
