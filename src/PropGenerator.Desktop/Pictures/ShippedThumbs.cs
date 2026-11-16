using PropGenerator.Desktop.Engine;
using PropGenerator.Engine;

namespace PropGenerator.Desktop.Pictures;

/// <summary>
/// easy-props' own preset pictures, shipped in the engine's bundle (180 x 180 PNGs): the drawer shows them as they are
/// for easy-props' presets, so none of those is drawn by our painter while its picture still shows it. Read from the
/// app's engine catalog; when the engine could not be loaded there are none, and our painter draws every preset.
/// </summary>
internal static class ShippedThumbs
{
    /// <summary>A preset's shipped picture, when easy-props ships one that still shows it.</summary>
    /// <param name="presetId">The starting point's id, such as "oak".</param>
    /// <param name="recipeId">The recipe it makes: only a preset of that easy-props generator has a shipped picture.</param>
    /// <returns>The PNG's bytes, or null when the catalog has no such preset of that generator, its picture is stale
    /// (<see cref="EnginePreset.ThumbnailFresh"/> false), or the engine is not there.</returns>
    public static byte[]? For(string presetId, string recipeId)
    {
        ArgumentNullException.ThrowIfNull(presetId);
        ArgumentNullException.ThrowIfNull(recipeId);
        return Catalog() is { } catalog && Shipped(catalog.Presets.Presets, presetId, recipeId)
            ? EngineOptions.Thumbnail(presetId)
            : null;
    }

    /// <summary>Whether the shipped picture of a preset is the one to show: the preset is listed, of that generator, and its picture still shows it.</summary>
    /// <param name="presets">The catalog's presets.</param>
    /// <param name="presetId">The starting point's id.</param>
    /// <param name="recipeId">The recipe it makes.</param>
    /// <returns>True when its shipped picture is shown.</returns>
    internal static bool Shipped(IEnumerable<EnginePreset> presets, string presetId, string recipeId) =>
        presets.FirstOrDefault(p => p.Id == presetId) is { ThumbnailFresh: true } preset && preset.GeneratorId == recipeId;

    /// <summary>
    /// The bundle's tag and commit, for the keys of pictures our painter draws of an easy-props kind (the user's own
    /// presets, and presets whose shipped picture is stale): a newer bundle draws them again.
    /// </summary>
    /// <param name="recipeId">The recipe, such as "bush".</param>
    /// <returns>Such as "v0.4.2-&lt;commit&gt;", or null for a kind of our own (or when the engine is not there).</returns>
    public static string? EngineTag(string recipeId)
    {
        ArgumentNullException.ThrowIfNull(recipeId);
        return Catalog() is { } catalog && catalog.Info.Generators.Any(g => g.Id == recipeId)
            ? catalog.Info.Tag + "-" + catalog.Info.Commit
            : null;
    }

    // The app's catalog, or null when the engine failed to load (not tried again for every picture) or is shut down.
    private static EngineCatalog? Catalog()
    {
        EngineHost host = EngineHost.Default;
        if (host.LoadFailure is not null)
        {
            return null;
        }

        try
        {
            return host.Catalog;
        }
        catch (Exception e) when (e is EngineException or TimeoutException or ObjectDisposedException or OperationCanceledException
            or DllNotFoundException or TypeInitializationException or IOException)
        {
            return null;
        }
    }
}
