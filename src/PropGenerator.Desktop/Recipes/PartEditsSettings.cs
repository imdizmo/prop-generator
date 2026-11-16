using System.Text.Json;

namespace PropGenerator.Desktop.Recipes;

/// <summary>The part edits settings can hold, whichever recipe builds the kind.</summary>
internal static class PartEditsSettings
{
    /// <summary>
    /// The settings without part edits: the prop as its generator makes it. A piece's tree text past any tree's
    /// (<see cref="OversizedTree"/>) goes too, so the piece is made from the default tree.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <returns>The settings without the edits.</returns>
    internal static Settings Without(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        s = s.Without(SettingsPanel.PartEditsKey);
        if (OversizedTree(s) is not null)
        {
            s = s.Without(EngineKinds.TreeKey);
        }

        return s.Values.ContainsKey(SettingsPanel.TreeEditsKey) ? s.With(SettingsPanel.TreeEditsKey, string.Empty) : s;
    }

    /// <summary>
    /// The settings on another seed, as easy-props' <c>withSeed</c> makes a version: the part edits go (their component
    /// diffs belong to the parts the old seed made), a piece's oversized tree text too, and everything else stays,
    /// a painted tree's own limb and clump edits (<see cref="SettingsPanel.TreeEditsKey"/>, a parameter upstream's
    /// <c>planPaintedTree</c> applies on any seed) among them.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="seed">The other seed.</param>
    /// <returns>The settings on that seed.</returns>
    internal static Settings OnSeed(Settings s, uint seed)
    {
        ArgumentNullException.ThrowIfNull(s);
        s = s.Without(SettingsPanel.PartEditsKey);
        if (OversizedTree(s) is not null)
        {
            s = s.Without(EngineKinds.TreeKey);
        }

        return s.With("seed", seed);
    }

    /// <summary>The length of a piece's tree text when it is past any tree's (<see cref="EngineKinds.MaxTreeChars"/>).</summary>
    /// <param name="s">The settings.</param>
    /// <returns>The length, or null when there is no such text or it is not too long.</returns>
    internal static int? OversizedTree(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Values.TryGetValue(EngineKinds.TreeKey, out JsonElement tree) && tree.ValueKind == JsonValueKind.String
            && tree.GetString()!.Length is var length && length > EngineKinds.MaxTreeChars ? length : null;
    }
}
