using System.Collections.Immutable;
using PropGenerator.Desktop.Engine;

namespace PropGenerator.Desktop.Recipes;

/// <summary>Every recipe that builds its prop as one coloured mesh, in drawer order: the rock, the cliff, the stylized rock, then grass, flowers, bushes, the cactus, the painted tree, the old-style tree and the old-style palm; then a tree's pieces, which the drawer does not list.</summary>
internal static class MeshRecipes
{
    // The tests' doubles, by recipe name, found in place of the registered recipes until let go.
    private static ImmutableDictionary<string, IMeshRecipe> _overrides = [];

    // The recipes chosen at launch, on first read.
    private static Lineup? _lineup;

    // The test seam's recipes for an engine that failed to load, while one is in place.
    private static Lineup? _down;

    /// <summary>
    /// Gets every mesh recipe, in drawer order: <see cref="Registered"/> for the default engine host's load failure,
    /// chosen on first read (the launch reads easy-props before it reads the recipes).
    /// </summary>
    internal static IReadOnlyList<IMeshRecipe> All => Current.Recipes;

    /// <summary>
    /// Gets why easy-props' engine could not be loaded when the recipes were chosen (only our own kinds are then
    /// registered), or null when it loaded.
    /// </summary>
    internal static Exception? EngineFailure => Current.Failure;

    // The recipes and the failure they were chosen for: the test seam's while one is in place, else the launch's.
    private static Lineup Current => Volatile.Read(ref _down) ?? LazyInitializer.EnsureInitialized(ref _lineup, () => Choose(EngineHost.Default.LoadFailure));

    /// <summary>
    /// Gets easy-props' kinds run on its engine with our own cliff and stylized rock, in drawer order, then a tree's
    /// pieces: registered so a saved piece opens, but not in the drawer (<see cref="EngineKinds.InDrawer"/>).
    /// </summary>
    internal static IReadOnlyList<IMeshRecipe> Engine =>
    [
        EngineRecipes.For("cut-rock"), CliffRecipe.Instance, StylizedRockRecipe.Instance, EngineRecipes.For("grass"), EngineRecipes.For("plant"),
        EngineRecipes.For("bush"), EngineRecipes.For("cactus"), EngineRecipes.For("painted-tree"), EngineRecipes.For("branching-tree"),
        EngineRecipes.For("palm-tree"), EngineRecipes.For("tree-stump"), EngineRecipes.For("tree-log"), EngineRecipes.For("tree-branch"),
        EngineRecipes.For("tree-litter"),
    ];

    /// <summary>
    /// The recipes to register, in drawer order: easy-props' kinds from its engine (<see cref="Engine"/>). When the
    /// engine could not be loaded, only our own kinds (<see cref="Ours"/>): easy-props' kinds are still named
    /// (<see cref="LabelOf"/>) and refused with the reason (<see cref="Unavailable"/>).
    /// </summary>
    /// <param name="engineFailure">Why the engine failed to load, or null when it loaded.</param>
    /// <returns>The recipes.</returns>
    internal static IReadOnlyList<IMeshRecipe> Registered(Exception? engineFailure)
    {
        if (engineFailure is not null)
        {
            TraceLog.Write($"recipes: easy-props' engine could not be loaded ({engineFailure.Message}); only slab cliffs and stylized rocks are offered");
            return Ours;
        }

        return Engine;
    }

    /// <summary>Gets our own kinds, in drawer order: all there is when easy-props' engine cannot load.</summary>
    internal static IReadOnlyList<IMeshRecipe> Ours => [CliffRecipe.Instance, StylizedRockRecipe.Instance];

    /// <summary>Why a recipe cannot be used: easy-props' engine could not be loaded and the recipe is one of its kinds.</summary>
    /// <param name="recipe">A recipe's name, such as "bush"; null for none.</param>
    /// <returns>"easy-props' engine could not be loaded (reason)", or null when the recipe is usable or not easy-props'.</returns>
    internal static string? Unavailable(string? recipe) =>
        EngineFailure is { } failure && EngineKinds.Find(recipe) is not null && Find(recipe) is null ? EngineDown(failure) : null;

    /// <summary>What the app says when easy-props' engine could not be loaded.</summary>
    /// <param name="failure">Why.</param>
    /// <returns>"easy-props' engine could not be loaded (reason)".</returns>
    internal static string EngineDown(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return $"easy-props' engine could not be loaded ({failure.Message})";
    }

    /// <summary>
    /// The kind a recipe makes, as the panel calls it: the registered recipe's label, or for one of easy-props' kinds
    /// while its engine is down, the kind's own label (<see cref="EngineKinds"/>).
    /// </summary>
    /// <param name="recipe">A recipe's name.</param>
    /// <returns>The label, or null for a name this build does not know.</returns>
    internal static string? LabelOf(string? recipe) => Find(recipe)?.Label ?? EngineKinds.Find(recipe)?.Label;

    /// <summary>Finds the mesh recipe with this name.</summary>
    /// <param name="id">A recipe's name, such as "cut-rock"; null for none.</param>
    /// <returns>The recipe, or null for the floor, an unknown name or null.</returns>
    internal static IMeshRecipe? Find(string? id) =>
        id is not null && Volatile.Read(ref _overrides).TryGetValue(id, out IMeshRecipe? stand) ? stand : All.FirstOrDefault(r => r.Id == id);

    /// <summary>
    /// A saved prop's settings as they are to be opened: the one place every load path (Open, a restored backup, Your
    /// props' Export…, My presets) passes a file's settings and each undo step through, before the defaults, so an old
    /// rock keeps its earlier look (<see cref="IMeshRecipe.EarlierLooks"/>). The floor and an unknown recipe are left as
    /// they are.
    /// </summary>
    /// <param name="recipe">The recipe the settings are for.</param>
    /// <param name="s">The settings as saved.</param>
    /// <returns>The settings to open.</returns>
    /// <exception cref="WorkFileException">What the prop looked like could not be found (the engine failed).</exception>
    internal static Settings Loaded(string recipe, Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Find(recipe) is { } mesh ? mesh.EarlierLooks(s) : s;
    }

    /// <summary>
    /// A test seam: <see cref="Find"/> returns this recipe for its name until the result is disposed. Tests that use it
    /// run alone (their xunit collection disables parallel runs), since every window finds recipes here.
    /// </summary>
    /// <param name="recipe">The recipe found in place of the registered one of the same name.</param>
    /// <returns>Lets the double go.</returns>
    internal static IDisposable Override(IMeshRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        string id = recipe.Id;
        ImmutableInterlocked.Update(ref _overrides, o => o.SetItem(id, recipe));
        return new Release(() => ImmutableInterlocked.Update(ref _overrides, o => o.TryGetValue(id, out IMeshRecipe? now) && ReferenceEquals(now, recipe) ? o.Remove(id) : o));
    }

    /// <summary>
    /// A test seam: until the result is disposed, the app's recipes are chosen as for an engine that failed to load
    /// with <paramref name="failure"/> (<see cref="Registered"/>). Tests that use it run alone (their xunit collection
    /// disables parallel runs).
    /// </summary>
    /// <param name="failure">Why the engine is to have failed.</param>
    /// <returns>Puts the launch's recipes back.</returns>
    internal static IDisposable EngineFailed(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Lineup down = Choose(failure);
        Volatile.Write(ref _down, down);
        return new Release(() => Interlocked.CompareExchange(ref _down, null, down));
    }

    private static Lineup Choose(Exception? failure) => new(Registered(failure), failure);

    // The registered recipes and the engine failure they were chosen for.
    private sealed record Lineup(IReadOnlyList<IMeshRecipe> Recipes, Exception? Failure);

    // Runs its action once, when disposed.
    private sealed class Release(Action release) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
            {
                release();
            }
        }
    }
}
