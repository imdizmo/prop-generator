namespace PropGenerator.Desktop.Recipes;

/// <summary>The own-colour rows of one choice of a kind (a rock's stone, a cactus's spines), which a palette saves and fills.</summary>
/// <param name="Set">Which colours these are ("stone", "grass", …): a palette fits every kind with the same set.</param>
/// <param name="Title">What the palettes call the set, such as "Rocks".</param>
/// <param name="ChoiceKey">The choice that is on My own colours ("custom") while these rows are used.</param>
/// <param name="Keys">The colour rows' keys, in order.</param>
internal sealed record OwnColourGroup(string Set, string Title, string ChoiceKey, IReadOnlyList<string> Keys)
{
    /// <summary>What a set is called, as easy-props' palettes name it.</summary>
    /// <param name="set">The set.</param>
    /// <returns>Its title.</returns>
    internal static string TitleOf(string set) => set switch
    {
        "tree" => "Trees",
        "stone" => "Rocks",
        "bush-leaves" => "Bush leaves",
        "grass" => "Grass",
        "flowers" => "Flowers",
        "cactus" => "Cacti",
        _ => set.Length == 0 ? set : char.ToUpperInvariant(set[0]) + set[1..].Replace('-', ' '),
    };
}
