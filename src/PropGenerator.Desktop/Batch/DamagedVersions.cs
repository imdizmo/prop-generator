// Adapted from easy-props frontend/src/core/scene/variations.ts (damageSettings, pageDamages, damageNames,
// versionKey) (MIT, © 2026 AHS12).
using System.Globalization;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Engine;

namespace PropGenerator.Desktop.Batch;

/// <summary>
/// A prop's damaged versions for Show me more (plan E10): its damage settings, the eight damaged versions of each page
/// on its own seed, what each has in its settings' words and its key, all upstream's, asked of the rules engine (each
/// answer cached there).
/// </summary>
internal sealed class DamagedVersions
{
    private readonly RulesEngine _rules;
    private readonly string _settingsJson;

    private DamagedVersions(RulesEngine rules, IReadOnlyList<EngineDamageSetting> settings)
    {
        _rules = rules;
        Settings = settings;
        _settingsJson = EngineDamageSetting.ToJson(settings);
    }

    /// <summary>Gets the prop's damage settings, in upstream's order (never empty).</summary>
    internal IReadOnlyList<EngineDamageSetting> Settings { get; }

    /// <summary>The damaged versions of a prop, or null for one that cannot be damaged (or a kind of ours).</summary>
    /// <param name="recipe">The prop's kind.</param>
    /// <param name="settings">Its settings now.</param>
    /// <returns>The versions, or null.</returns>
    /// <exception cref="EngineException">The rules engine failed.</exception>
    internal static DamagedVersions? Of(IMeshRecipe recipe, Settings settings)
    {
        if (recipe is not EngineRecipe engine)
        {
            return null;
        }

        IReadOnlyList<EngineDamageSetting> damage = engine.DamageSettings(settings);
        return damage.Count == 0 ? null : new DamagedVersions(engine.Rules, damage);
    }

    /// <summary>The eight damaged versions of a page, on the prop's own seed (upstream's <c>pageDamages</c>).</summary>
    /// <param name="seed">The prop's seed.</param>
    /// <param name="page">The page, from 0.</param>
    /// <returns>The versions.</returns>
    /// <exception cref="EngineException">The rules engine failed.</exception>
    internal IReadOnlyList<PropVersion> Page(uint seed, int page) =>
        [.. _rules.PageDamages(seed, page, _settingsJson).Select(static v => new PropVersion(v.Seed, v.Damage))];

    /// <summary>What a damaged version has, in its settings' words ("Snapped top, Split trunk"); "Undamaged" for none.</summary>
    /// <param name="version">The version.</param>
    /// <returns>The words.</returns>
    /// <exception cref="EngineException">The rules engine failed.</exception>
    internal string Names(PropVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        string names = _rules.DamageNames(version.ToJson(), _settingsJson);
        return names.Length == 0 ? "Undamaged" : names;
    }

    /// <summary>
    /// A version's key, the same each time it is made (upstream's <c>versionKey</c>): asked of the engine for a damaged
    /// version; a seed's own is its number, as upstream's.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="damaged">The prop's damaged versions, or null when it has none (no damaged version can be asked about).</param>
    /// <returns>The key.</returns>
    /// <exception cref="EngineException">The rules engine failed.</exception>
    internal static string KeyOf(PropVersion version, DamagedVersions? damaged)
    {
        ArgumentNullException.ThrowIfNull(version);
        return version.Damage is null || damaged is null
            ? version.Seed.ToString(CultureInfo.InvariantCulture)
            : damaged._rules.VersionKey(version.ToJson());
    }
}
