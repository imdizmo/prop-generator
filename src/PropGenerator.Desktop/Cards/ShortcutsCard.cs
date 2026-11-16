// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Cards;

/// <summary>The keyboard shortcuts list (Ctrl+/): every command that has a key, by menu, with its key beside it.</summary>
internal sealed class ShortcutsCard : Border
{
    private static readonly string[] GroupOrder = ["File", "Edit", "View", "Tools", "Prop", "Help"];

    /// <summary>Lists the keys of the command list.</summary>
    /// <param name="commands">The window's command list.</param>
    /// <param name="close">Closes the card.</param>
    internal ShortcutsCard(IReadOnlyList<Command> commands, Action close)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var list = new StackPanel { Spacing = 2 };
        foreach (string group in GroupOrder)
        {
            Command[] keyed = [.. commands.Where(c => c.Group == group && c.Keys is not null)];
            if (keyed.Length == 0)
            {
                continue;
            }

            list.Children.Add(new TextBlock
            {
                Text = group,
                FontSize = Look.Theme.Caption,
                FontWeight = FontWeight.SemiBold,
                Foreground = Look.Theme.TextTertiary,
                Margin = new Thickness(0, Look.Theme.Space2, 0, 3),
            });
            foreach (Command c in keyed)
            {
                var row = new DockPanel { LastChildFill = true, MinHeight = 24 };
                Border chip = Chrome.KeyChip(c.Keys!);
                DockPanel.SetDock(chip, Dock.Right);
                row.Children.Add(chip);
                row.Children.Add(new TextBlock
                {
                    Text = c.Label,
                    FontSize = Look.Theme.Label,
                    Foreground = Look.Theme.TextSecondary,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                list.Children.Add(row);
            }
        }

        var body = new StackPanel { Spacing = Look.Theme.Space2 };
        body.Children.Add(CardParts.Header("Keyboard shortcuts", "Close the list of keyboard shortcuts and go back to the piece (Esc).", close));
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 520 });
        body.Children.Add(new TextBlock
        {
            Text = "While you type in a box the keys go to the box: only Ctrl+K still works there.",
            FontSize = Look.Theme.Caption,
            Foreground = Look.Theme.TextTertiary,
            TextWrapping = TextWrapping.Wrap,
        });
        Child = body;
        Width = 420;
        CardParts.Dress(this);
    }
}
