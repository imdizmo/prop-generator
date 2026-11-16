using PropGenerator.Desktop.Recipes;

namespace PropGenerator.Desktop;

/// <summary>
/// What a piece is in Groundwork's inbox: its models, named as Export names them, their labels and the drawer group
/// each kind goes in (the inbox has its own groups, so a kind is not always in Export's).
/// </summary>
internal static class GroundworkSend
{
    /// <summary>The inbox group a mesh recipe's model goes in, by its export name.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <returns>Rocks for a rock or a cliff, Trees, Bushes, Plants for grass, flowers and the cactus, Props otherwise.</returns>
    internal static string Category(IMeshRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return Category(recipe.ExportName);
    }

    /// <summary>The inbox group of a model by the part of its name its kind exports as.</summary>
    /// <param name="exportName">The kind's export name: "rock", "tree", "grass"…</param>
    /// <returns>The group.</returns>
    internal static string Category(string exportName) =>
        exportName switch
        {
            "rock" or "cliff" => "Rocks",
            "tree" or "palm" => "Trees",
            "bush" => "Bushes",
            "grass" or "plant" or "cactus" => "Plants",
            _ => "Props",
        };

    /// <summary>The group the floor's tiles go in: the inbox has no Construction, so Buildings.</summary>
    internal const string FloorCategory = "Buildings";

    /// <summary>Whether a work name leaves something to name the models by (a letter or a digit).</summary>
    /// <param name="workName">The work file's name without its extension.</param>
    /// <returns>True when it does.</returns>
    internal static bool HasName(string workName) =>
        ExportDialog.ModelName(workName, string.Empty)[3..].Any(c => c != '_');

    /// <summary>The model of a mesh recipe's prop: pg_&lt;name&gt;_&lt;part&gt;, labelled "&lt;name&gt; &lt;kind&gt;", as Export has it.</summary>
    /// <param name="workName">The work file's name without its extension.</param>
    /// <param name="recipe">The recipe.</param>
    /// <returns>The kind.</returns>
    internal static InboxKind MeshKind(string workName, IMeshRecipe recipe) =>
        new(ExportDialog.ModelName(workName, ExportDialog.Part(recipe)), $"{workName} {ExportDialog.KindLabel(recipe)}", Category(recipe), [1f, 1f]);

    /// <summary>The floor's two tiles, square and triangle, built from the composed picture with it inside each GLB.</summary>
    /// <param name="workName">The work file's name without its extension.</param>
    /// <param name="rgb">The composed picture, RGBA8 top-down.</param>
    /// <param name="size">Its width and height.</param>
    /// <returns>Each tile's kind and GLB.</returns>
    internal static IReadOnlyList<(InboxKind Kind, byte[] Glb)> Floor(string workName, byte[] rgb, int size)
    {
        string tile = ExportDialog.ModelName(workName, "floor_tile");
        string tri = ExportDialog.ModelName(workName, "floor_tri");
        return
        [
            (new InboxKind(tile, $"{workName} floor tile", FloorCategory, [1f, 1f]), GlbExport.ToBytes(TileMesh.Square(), rgb, size, tile)),
            (new InboxKind(tri, $"{workName} floor triangle", FloorCategory, [1f, 1f]), GlbExport.ToBytes(TileMesh.Triangle(), rgb, size, tri)),
        ];
    }
}
