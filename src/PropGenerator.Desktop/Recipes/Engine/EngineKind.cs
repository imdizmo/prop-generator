namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// What is ours about one of easy-props' kinds run on its engine: what the app calls it, how it is exported, seeded,
/// grouped, moved by the wind and pictured. Everything else (settings, starting points, rules, meshes) comes from the
/// engine.
/// </summary>
/// <param name="Id">The upstream generator's id, which is also the recipe's id in work files: "cut-rock", "grass".</param>
/// <param name="Label">What the panel's header calls it.</param>
/// <param name="ExportName">The part of its exported model names: "rock", "grass".</param>
/// <param name="DefaultSeed">The seed used when the settings do not say (the seed row's default).</param>
/// <param name="SeedGroup">The panel group the seed is kept in.</param>
/// <param name="FallbackCategory">The drawer heading for a preset with no category of its own.</param>
/// <param name="Wind">How it moves in the wind in the view, or null for still.</param>
/// <param name="PictureDetail">The share of full detail the drawer's picture is built at.</param>
/// <param name="Tooltip">The app's own hover text for a setting, or null to use upstream's help (or the label).</param>
/// <param name="SortByCategories">
/// True when the drawer lists its presets by heading in upstream's heading order (the painted tree); false keeps the
/// presets' own order.
/// </param>
internal sealed record EngineKind(
    string Id,
    string Label,
    string ExportName,
    uint DefaultSeed,
    string SeedGroup,
    string FallbackCategory,
    WindSway? Wind,
    double PictureDetail,
    Func<string, string?> Tooltip,
    bool SortByCategories)
{
    /// <summary>
    /// Gets a value indicating whether the New drawer lists the kind's starting points: false for a tree's pieces, which
    /// are made only from a tree (ruling 5), yet open, edit and export like any prop once saved.
    /// </summary>
    internal bool InDrawer { get; init; } = true;

    /// <summary>
    /// Gets the values the kind carries that the panel does not draw (E12): a piece's <c>tree</c> (the tree's settings as
    /// JSON text), <c>treeSeed</c> and <c>treeName</c>. They are kept in the settings, saved, undone and sent to the engine
    /// as they are; empty for every other kind.
    /// </summary>
    internal IReadOnlyList<string> CarriedKeys { get; init; } = [];
}
