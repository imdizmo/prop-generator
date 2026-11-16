// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Details;

/// <summary>The foot of the controls panel, as easy-props' Details footer: 32 high, a line over it, and Restore to preset.</summary>
internal sealed class PanelFooter : Border
{
    /// <summary>Builds the footer.</summary>
    /// <param name="commands">The window's commands; Restore to preset runs its own.</param>
    internal PanelFooter(IReadOnlyList<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        Height = 32;
        Padding = new Thickness(Look.Theme.Space1, 0);
        BorderBrush = Look.Theme.BorderSubtle;
        BorderThickness = new Thickness(0, 1, 0, 0);

        Command restore = commands.Single(c => c.Id == "restore-preset");
        Restore = new Button
        {
            Height = 24,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new TextBlock { Text = "Restore to preset", FontSize = Look.Theme.Label, VerticalAlignment = VerticalAlignment.Center },
        };
        Restore.Classes.Add("ep");
        Restore.Classes.Add("ep-text");
        AutomationProperties.SetName(Restore, "Restore to preset");
        Chrome.Tip(Restore, restore.Tooltip ?? "Put every setting and the seed back as the starting point had them. One undo step.");
        Restore.Click += async (_, _) => await restore.Run();
        Child = Restore;
    }

    /// <summary>Gets the Restore to preset button.</summary>
    internal Button Restore { get; }
}
