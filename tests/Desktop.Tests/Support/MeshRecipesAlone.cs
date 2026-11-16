using Xunit;

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>
/// The tests that put a recipe double in place with <c>MeshRecipes.Override</c>: every window finds its recipes there,
/// so they run alone.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MeshRecipesAlone
{
    /// <summary>The collection's name.</summary>
    public const string Name = "MeshRecipes";
}
