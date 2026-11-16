// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Cards;

/// <summary>What the small cards share: the dialog surface and a title row with a close button.</summary>
internal static class CardParts
{
    /// <summary>Dresses a card as a dialog: the raised surface, a strong border, radius 16, the dialog shadow, padding 16.</summary>
    /// <param name="card">The card.</param>
    internal static void Dress(Border card)
    {
        card.Background = Theme.SurfaceRaised;
        card.BorderBrush = Theme.BorderStrong;
        card.BorderThickness = new Thickness(1);
        card.CornerRadius = new CornerRadius(Theme.Radius4);
        card.BoxShadow = Theme.Shadow3;
        card.Padding = new Thickness(Theme.Space4);
    }

    /// <summary>The title row: the title, and a close button at the right.</summary>
    /// <param name="title">The card's title.</param>
    /// <param name="closeTip">What the close button's tooltip says.</param>
    /// <param name="close">Closes the card.</param>
    /// <returns>The row.</returns>
    internal static DockPanel Header(string title, string closeTip, Action close)
    {
        Button x = Chrome.IconButton("x", closeTip);
        AutomationProperties.SetName(x, "Close");
        x.Click += (_, _) => close();
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(x, Dock.Right);
        row.Children.Add(x);
        row.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Theme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }
}
