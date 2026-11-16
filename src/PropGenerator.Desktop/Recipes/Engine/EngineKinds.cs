namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// easy-props' kinds the app runs on its engine, with what is ours about each (the labels, export names, seeds, seed
/// groups, wind and picture detail the 0.4.2 port's wrappers had), in drawer order.
/// </summary>
internal static class EngineKinds
{
    /// <summary>The painted tree's id: the one kind whose own part edits are one of its parameters (<c>edits</c>).</summary>
    internal const string PaintedTree = "painted-tree";

    /// <summary>The drawer heading of the old-style tree and palm.</summary>
    internal const string OldStyleTrees = "Old-style trees";

    /// <summary>The value a piece carries its tree's settings in, as JSON text (upstream's <c>PieceParameters.tree</c>).</summary>
    internal const string TreeKey = "tree";

    /// <summary>
    /// The longest <see cref="TreeKey"/> text a piece may carry: twice the tree's own edits cap, so any tree's settings
    /// with its edits fit. A longer one is refused on open (<see cref="EngineRecipe.EditsProblem"/>) and never sent.
    /// </summary>
    internal const int MaxTreeChars = 512 * 1024;

    /// <summary>What a tree's piece carries that the panel does not draw (E12): its tree, the tree's seed and name.</summary>
    internal static readonly IReadOnlyList<string> PieceCarriedKeys = [TreeKey, "treeSeed", "treeName"];

    /// <summary>
    /// Gets every kind: the drawer's in the drawer order the port's wrappers had, then a tree's pieces (stump, log,
    /// branch, leaf litter), which the drawer does not list.
    /// </summary>
    internal static IReadOnlyList<EngineKind> All { get; } =
    [
        new("cut-rock", "Rock", "rock", 401, "Surface", "Rocks", null, 1, RockTooltips.TryFor, false),
        new("grass", "Grass", "grass", 1, "Shape", "Grass", new WindSway(Foliage: true, Evenly: true), 1, NoTooltip, false),
        new("plant", "Flowers", "plant", 1, "Shape", "Flowers", new WindSway(Foliage: true, Evenly: true), 1, NoTooltip, false),
        new("bush", "Bush", "bush", 1, "Shape", "Bushes", new WindSway(Foliage: true, Evenly: false), 1, NoTooltip, false),
        new("cactus", "Cactus", "cactus", 1, "Shape", "Cacti", null, 1, NoTooltip, false),
        new(PaintedTree, "Tree", "tree", 1, "Trunk", "Broadleaf", new WindSway(Foliage: false, Evenly: false, ByPart: true), 0.4, NoTooltip, true),
        new("branching-tree", "Old-style tree", "tree", 12345, "Shape", OldStyleTrees, new WindSway(Foliage: true, Evenly: false), 1, NoTooltip, false),
        new("palm-tree", "Old-style palm", "palm", 1, "Shape", OldStyleTrees, new WindSway(Foliage: true, Evenly: false), 1, NoTooltip, false),
        Piece("tree-stump", "Stump", "stump", null),
        Piece("tree-log", "Fallen log", "log", null),
        Piece("tree-branch", "Fallen branch", "branch", null),
        Piece("tree-litter", "Leaf litter", "litter", new WindSway(Foliage: true, Evenly: true)),
    ];

    /// <summary>Finds a kind by its id.</summary>
    /// <param name="id">The generator's id.</param>
    /// <returns>The kind, or null when the engine does not run it for the app.</returns>
    internal static EngineKind? Find(string? id) => All.FirstOrDefault(k => k.Id == id);

    /// <summary>Whether a recipe's kind is listed in the New drawer: every recipe but a tree's pieces.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <returns>False for a piece.</returns>
    internal static bool InDrawer(IMeshRecipe recipe) => recipe is not EngineRecipe engine || engine.Kind.InDrawer;

    // A tree's piece: named as upstream's PIECE_LABELS, not in the drawer, carrying its tree.
    private static EngineKind Piece(string id, string label, string exportName, WindSway? wind) =>
        new(id, label, exportName, 1, "Shape", "Pieces", wind, 1, NoTooltip, false) { InDrawer = false, CarriedKeys = PieceCarriedKeys };

    // No hover text of our own: upstream's help (or the label) is used.
    private static string? NoTooltip(string key) => null;
}
