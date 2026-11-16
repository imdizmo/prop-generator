// Adapted from easy-props (MIT, © 2026 AHS12).
namespace PropGenerator.Desktop.Recipes;

/// <summary>One entry of the objects drawer: a recipe with the settings it starts from.</summary>
/// <param name="Id">A short stable name, such as "boulder".</param>
/// <param name="Name">The name shown in the drawer.</param>
/// <param name="Group">The drawer heading it sits under, such as "Boulders".</param>
/// <param name="Recipe">The recipe it makes: "floor_tile", "cut-rock" or "slab-cliff".</param>
/// <param name="Settings">The settings the recipe starts with.</param>
public sealed record StartingPoint(string Id, string Name, string Group, string Recipe, Settings Settings);
