// Adapted from easy-props frontend/src/core/scene/variations.ts (PropVersion) and
// frontend/src/core/export/batch-items.ts (batchItems) (MIT, © 2026 AHS12).
using PropGenerator.Desktop.Recipes;

namespace PropGenerator.Desktop.Batch;

/// <summary>One version of the prop, as upstream's <c>PropVersion</c>: on another seed, or on its own seed with damage.</summary>
/// <param name="Seed">Its seed.</param>
/// <param name="Damage">Its damage settings by key; null for the prop's own.</param>
internal sealed record PropVersion(uint Seed, IReadOnlyDictionary<string, double>? Damage = null)
{
    /// <summary>The version as the engine takes it.</summary>
    /// <returns>JSON: <c>{seed, damage?}</c>.</returns>
    internal string ToJson() => new PropGenerator.Engine.EngineVersion { Seed = Seed, Damage = Damage }.ToJson();

    /// <summary>
    /// Settings with this version's damage set on them, as upstream's <c>updateTreeParameters</c> with the version's
    /// damage (the seed and hand edits are left as they are); the settings themselves when it has none.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The damaged settings.</returns>
    internal Settings Damaged(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (Damage is null)
        {
            return settings;
        }

        foreach (KeyValuePair<string, double> setting in Damage)
        {
            settings = settings.With(setting.Key, setting.Value);
        }

        return settings;
    }

    /// <summary>
    /// The prop in this version, as Export many makes it: on the prop's own seed exactly as it is, hand edits and all;
    /// on another, the same settings without the part edits but with a painted tree's own edits (easy-props' withSeed,
    /// <see cref="PartEditsSettings.OnSeed"/>); then the version's damage.
    /// </summary>
    /// <param name="basis">The prop's settings now.</param>
    /// <returns>The version's settings.</returns>
    internal Settings Of(Settings basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        uint own = basis.Values.ContainsKey("seed") ? basis.GetSeed("seed") : 0;
        return Damaged(Seed == own ? basis : PartEditsSettings.OnSeed(basis, Seed));
    }

    /// <inheritdoc/>
    public bool Equals(PropVersion? other) =>
        other is not null && Seed == other.Seed
        && (Damage is null ? other.Damage is null : other.Damage is not null && Damage.Count == other.Damage.Count
            && Damage.All(d => other.Damage.TryGetValue(d.Key, out double v) && v.Equals(d.Value)));

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Seed, Damage?.Count ?? -1);
}

/// <summary>One prop of Export many: its name as upstream's <c>batchItems</c> numbers it (oak_01) and its version.</summary>
/// <param name="Name">Its name; our file and model names are made from it (pg_oak_01_tree).</param>
/// <param name="Version">The version it is made in, at its turn.</param>
internal sealed record BatchVersion(string Name, PropVersion Version);
