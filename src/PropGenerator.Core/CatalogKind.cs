namespace PropGenerator;

/// <summary>One entry of a Groundwork catalog: a model and how the object drawer shows it.</summary>
/// <param name="Model">The model's name (its file without ".glb").</param>
/// <param name="Label">The name shown in the drawer.</param>
/// <param name="Category">The drawer's group.</param>
/// <param name="Scale">The size range, two numbers.</param>
public sealed record CatalogKind(string Model, string Label, string Category, float[] Scale);
