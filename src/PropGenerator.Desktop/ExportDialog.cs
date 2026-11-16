namespace PropGenerator.Desktop;

/// <summary>
/// Exporting to a Groundwork map: which map folder (asked for once, then remembered), and the GLB files and catalog
/// entries that go into it (two for a floor, one for a rock or a cliff).
/// </summary>
public sealed class ExportDialog
{
    private readonly IDialogs _dialogs;
    private readonly Prefs _prefs;

    /// <summary>Makes the export helper.</summary>
    /// <param name="dialogs">Asks for the folder.</param>
    /// <param name="prefs">Remembers it.</param>
    public ExportDialog(IDialogs dialogs, Prefs prefs)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(prefs);
        _dialogs = dialogs;
        _prefs = prefs;
    }

    /// <summary>The model name of a piece: <c>pg_</c>, the work name lower-cased with anything but a-z and 0-9 as <c>_</c>, then the part.</summary>
    /// <param name="workName">The work file's name without its extension.</param>
    /// <param name="part">"floor_tile", "floor_tri", "rock" or "cliff" (see <see cref="Part"/>).</param>
    /// <returns>For example pg_dark_oak_floor_tile.</returns>
    public static string ModelName(string workName, string part)
    {
        return $"pg_{Sanitise(workName)}_{part}";
    }

    /// <summary>
    /// Says why this piece cannot be exported to this map, or null when it can: the file name must leave something to
    /// name the models by, and no other label in the map may already use the same model name.
    /// </summary>
    /// <param name="mapFolder">The map folder.</param>
    /// <param name="workName">The work file's name without its extension.</param>
    /// <returns>A message asking for a rename, or null.</returns>
    /// <exception cref="CatalogException">The map's catalog cannot be read.</exception>
    public static string? Problem(string mapFolder, string workName) => Problem(mapFolder, workName, Recipes.FloorRecipe.Id);

    /// <summary>
    /// Says why this piece cannot be exported to this map, or null when it can: the file name must leave something to
    /// name the models by, and no other label in the map may already use one of this recipe's model names.
    /// </summary>
    /// <param name="mapFolder">The map folder.</param>
    /// <param name="workName">The work file's name without its extension.</param>
    /// <param name="recipe">The piece's recipe: "floor_tile" (two models), or a mesh recipe such as "cut-rock" (one).</param>
    /// <returns>A message asking for a rename, or null.</returns>
    /// <exception cref="CatalogException">The map's catalog cannot be read.</exception>
    public static string? Problem(string mapFolder, string workName, string recipe)
    {
        if (!Sanitise(workName).Any(c => c != '_'))
        {
            return $"Cannot export \"{workName}\": its file name has no letters or digits to name the models by. Rename the file and export again.";
        }

        var ours = Recipes.MeshRecipes.Find(recipe) is { } mesh
            ? new Dictionary<string, string> { [ModelName(workName, Part(mesh))] = MeshLabel(workName, mesh) }
            : new Dictionary<string, string>
            {
                [ModelName(workName, "floor_tile")] = $"{workName} floor tile",
                [ModelName(workName, "floor_tri")] = $"{workName} floor triangle",
            };
        foreach ((string model, string label) in CatalogMerge.ReadKinds(mapFolder))
        {
            if (ours.TryGetValue(model, out string? mine) && mine != label)
            {
                return $"Cannot export \"{workName}\": the model name {model} is already used in this map by \"{label}\". Rename this file so its name differs, then export again.";
            }
        }

        return null;
    }

    private static string Sanitise(string workName)
    {
        ArgumentNullException.ThrowIfNull(workName);
        var sb = new System.Text.StringBuilder();
        foreach (char c in workName.ToLowerInvariant())
        {
            sb.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '_');
        }

        return sb.ToString();
    }

    /// <summary>Gets the map folder to export to.</summary>
    /// <param name="change">True to ask even when one is remembered.</param>
    /// <returns>The folder, or null when the user cancelled.</returns>
    public async Task<string?> ChooseMapFolderAsync(bool change)
    {
        string? remembered = _prefs.MapFolder;
        if (!change && remembered is not null && Directory.Exists(remembered))
        {
            return remembered;
        }

        return await _dialogs.AskFolderAsync("Choose the Groundwork map folder to export to", remembered);
    }

    /// <summary>Remembers the map folder.</summary>
    /// <param name="folder">The folder.</param>
    public void Remember(string folder)
    {
        _prefs.MapFolder = folder;
        _prefs.Save();
    }

    /// <summary>Builds both GLBs from the composed picture and merges them into the map's catalog.</summary>
    /// <param name="mapFolder">The map folder.</param>
    /// <param name="workName">The work file's name without its extension.</param>
    /// <param name="rgb">The composed picture, RGBA8 top-down.</param>
    /// <param name="size">Its width and height.</param>
    /// <exception cref="CatalogException">The map's catalog could not be used; nothing was written.</exception>
    public static void Write(string mapFolder, string workName, byte[] rgb, int size)
    {
        string tile = ModelName(workName, "floor_tile");
        string tri = ModelName(workName, "floor_tri");
        var glbs = new Dictionary<string, byte[]>
        {
            [tile] = GlbExport.ToBytes(TileMesh.Square(), rgb, size, tile),
            [tri] = GlbExport.ToBytes(TileMesh.Triangle(), rgb, size, tri),
        };
        CatalogKind[] kinds =
        [
            new(tile, $"{workName} floor tile", "Construction", [1.0f, 1.0f]),
            new(tri, $"{workName} floor triangle", "Construction", [1.0f, 1.0f]),
        ];
        CatalogMerge.AddKinds(mapFolder, kinds, glbs);
    }

    /// <summary>
    /// Builds the GLB of a rock or a cliff (our own export) and merges it into the map's catalog, group
    /// Rocks: the model pg_&lt;name&gt;_rock or pg_&lt;name&gt;_cliff.
    /// </summary>
    /// <param name="mapFolder">The map folder.</param>
    /// <param name="workName">The work file's name without its extension.</param>
    /// <param name="recipe">The piece's recipe.</param>
    /// <param name="settings">The piece's settings.</param>
    /// <returns>The model's name.</returns>
    /// <exception cref="CatalogException">The map's catalog could not be used; nothing was written.</exception>
    /// <param name="options">How to export it: the look, the picture size and the set (null: lit, 512, no set).</param>
    internal static string WriteMesh(string mapFolder, string workName, Recipes.IMeshRecipe recipe, Settings settings, Recipes.ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        string model = ModelName(workName, Part(recipe));
        var glbs = new Dictionary<string, byte[]> { [model] = recipe.Glb(settings, model, options) };
        CatalogMerge.AddKinds(mapFolder, [new CatalogKind(model, MeshLabel(workName, recipe), "Rocks", [1.0f, 1.0f])], glbs);
        return model;
    }

    /// <summary>What a mesh recipe's prop is called in messages and catalog labels: its panel label in lower case.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <returns>"rock" or "cliff".</returns>
    internal static string KindLabel(Recipes.IMeshRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return recipe.Label.ToLowerInvariant();
    }

    /// <summary>The model name's part for a mesh recipe: its stable export name.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <returns>"rock" or "cliff".</returns>
    internal static string Part(Recipes.IMeshRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return recipe.ExportName;
    }

    private static string MeshLabel(string workName, Recipes.IMeshRecipe recipe) => $"{workName} {KindLabel(recipe)}";
}
