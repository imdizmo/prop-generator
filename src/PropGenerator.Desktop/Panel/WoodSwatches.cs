namespace PropGenerator.Desktop.Details;

/// <summary>
/// The named woods the floor's colour row offers, pale to dark, as easy-props offers named bark and leaf colours: a
/// beginner knows the floor is walnut, not what hex walnut is. Walnut is the floor's own default (#45302B).
/// </summary>
internal static class WoodSwatches
{
    /// <summary>Gets the woods in the order shown: name, #RRGGBB, and what it looks like.</summary>
    internal static IReadOnlyList<(string Name, string Hex, string Looks)> All { get; } =
    [
        ("Birch", "#D9C4A0", "a pale, creamy wood"),
        ("Ash", "#C4A57E", "a light, straw-coloured wood"),
        ("Oak", "#A07A4E", "a warm, mid brown"),
        ("Cherry", "#8C5A3C", "a reddish brown"),
        ("Walnut", "#45302B", "a deep, dark brown (the floor's own colour)"),
        ("Ebony", "#2B211D", "an almost black wood"),
    ];
}
