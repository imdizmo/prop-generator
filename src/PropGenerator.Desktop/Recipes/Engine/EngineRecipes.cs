using System.Collections.Concurrent;
using PropGenerator.Desktop.Engine;

namespace PropGenerator.Desktop.Recipes;

/// <summary>The app's recipes of easy-props' kinds run on its engine: one per kind, on the app's engine host.</summary>
internal static class EngineRecipes
{
    /// <summary>
    /// The fewest pool threads the pool adds without waiting. An engine recipe blocks a pool thread on each engine job it
    /// waits for (ruling 2: a view's build, its shapes and its count, Show me more, the drawer's pictures, exports), and
    /// ClearScript runs V8's own tasks on the pool too. At the pool's usual minimum (the processor count, 3 under the
    /// tests' CPU quota) a few waiting builds starve it: the pool then adds one thread every half second, the engine's
    /// job waits on V8 tasks queued behind the waits, those tasks pile up by the million and memory runs past 4 GB in
    /// seconds (seen with two windows building at once). Threads are still only made when work waits for one.
    /// </summary>
    internal const int MinPoolThreads = 32;

    private static readonly ConcurrentDictionary<string, EngineRecipe> Made = new(StringComparer.Ordinal);

    static EngineRecipes()
    {
        ThreadPool.GetMinThreads(out int workers, out int io);
        if (workers < MinPoolThreads)
        {
            _ = ThreadPool.SetMinThreads(MinPoolThreads, io);
        }
    }

    /// <summary>The recipe of one of easy-props' kinds, made once.</summary>
    /// <param name="id">The kind's id, such as "cut-rock" (one of <see cref="EngineKinds.All"/>).</param>
    /// <returns>The recipe.</returns>
    /// <exception cref="ArgumentException">The engine does not run that kind for the app.</exception>
    internal static EngineRecipe For(string id) =>
        Made.GetOrAdd(id, key => new EngineRecipe(
            EngineKinds.Find(key) ?? throw new ArgumentException($"easy-props' engine runs no kind '{key}' for the app.", nameof(id)),
            EngineHost.Default));

    /// <summary>The recipe whose parts a piece of this recipe is edited through: one of easy-props' kinds, or none.</summary>
    /// <param name="recipeId">A recipe's name; null for none.</param>
    /// <returns>The engine recipe, or null for our own kinds, the floor and unknown names.</returns>
    internal static EngineRecipe? WithParts(string? recipeId) => EngineKinds.Find(recipeId) is null ? null : For(recipeId!);

    /// <summary>
    /// Gets where File, New old-style palm starts: the palm's own defaults (easy-props offers no palm preset, so the
    /// drawer has none), under the old-style trees.
    /// </summary>
    internal static StartingPoint PalmDefaults =>
        new("palm-tree", "Old-style palm", EngineKinds.OldStyleTrees, "palm-tree", Settings.Defaults(For("palm-tree").Specs));
}
