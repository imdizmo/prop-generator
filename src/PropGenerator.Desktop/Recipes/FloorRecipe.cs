namespace PropGenerator.Desktop.Recipes;

/// <summary>The wooden floor tile as an app recipe: its name and the one starting point it offers.</summary>
public static class FloorRecipe
{
    /// <summary>The recipe's name, as work files and the Claude tools know it.</summary>
    public const string Id = "floor_tile";

    /// <summary>What the panel's header calls the floor.</summary>
    public const string Label = "Floor tile";

    /// <summary>The floor's starting point: the v5 floor tile with the default wood.</summary>
    /// <param name="floorSpecs">The painter's settings list, whose defaults the floor starts with.</param>
    /// <returns>The starting point "floor-v5", named "Floor tile (v5)", in the Wood group.</returns>
    public static StartingPoint StartingPoint(IReadOnlyList<ParamSpec> floorSpecs)
    {
        ArgumentNullException.ThrowIfNull(floorSpecs);
        return new Recipes.StartingPoint("floor-v5", "Floor tile (v5)", "Wood", Id, Settings.Defaults(floorSpecs));
    }
}
