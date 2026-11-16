// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PropGenerator.Desktop.Cards;

/// <summary>About Prop Generator: the version, what the app makes, and where its parts come from.</summary>
internal sealed class AboutCard : Border
{
    private static readonly string[] Credits =
    [
        "The rocks, their starting points and this look are adapted from easy-props (MIT, © 2026 AHS12).",
        "Icons: lucide 1.65.0 (ISC, © Lucide Contributors).",
        "Claude's connection derives from Groundwork, built from Valheim World Editor by Thibault Bene (MIT).",
        "Built with Avalonia, Silk.NET, SharpGLTF and the MCP C# SDK. NOTICE.md lists every licence.",
    ];

    /// <summary>Makes the card.</summary>
    /// <param name="close">Closes the card.</param>
    internal AboutCard(Action close)
    {
        var body = new StackPanel { Spacing = Look.Theme.Space2 };
        body.Children.Add(CardParts.Header("About Prop Generator", "Close this card and go back to the piece (Esc).", close));
        body.Children.Add(new TextBlock { Text = $"Prop Generator {BuildInfo.Version}", FontSize = Look.Theme.Body, FontWeight = FontWeight.SemiBold });
        body.Children.Add(Line("Makes painted floor tiles and rocks for Groundwork maps, and sends them to Blender.", Look.Theme.TextSecondary));
        body.Children.Add(new TextBlock
        {
            Text = "Credits",
            FontSize = Look.Theme.Caption,
            FontWeight = FontWeight.SemiBold,
            Foreground = Look.Theme.TextTertiary,
            Margin = new Thickness(0, Look.Theme.Space2, 0, 0),
        });
        foreach (string credit in Credits)
        {
            body.Children.Add(Line(credit, Look.Theme.TextSecondary));
        }

        Child = body;
        Width = 400;
        CardParts.Dress(this);
    }

    private static TextBlock Line(string text, IBrush colour) =>
        new() { Text = text, FontSize = Look.Theme.Label, Foreground = colour, TextWrapping = TextWrapping.Wrap };
}
