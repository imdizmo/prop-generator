using System.Globalization;
using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Parameters;

namespace PropGenerator.Desktop.Scenes;

/// <summary>
/// Ours: checks part edits read from a work file before they are used. A work file is shared, so its edits are not
/// trusted: a crafted one must not freeze or exhaust the app with thousands of copies, huge sizes, numbers that are
/// not finite, parts of generators the prop does not use, or parameters outside their schema. Anything out of bounds
/// refuses the edits as a whole (the caller opens the prop without them); parameters are checked by the caller's rule
/// (in the app, against the engine's own part).
/// </summary>
public static class PartEditsCheck
{
    /// <summary>The most parts made by hand (copies) a prop may have: far more than anyone copies by hand.</summary>
    public const int MaxCopies = 256;

    /// <summary>The smallest a part may be resized to, as a share of the size its generator gives it (the Resize tool's limit).</summary>
    public const double MinSize = 0.1;

    /// <summary>The largest a part may be resized to (the Resize tool's limit).</summary>
    public const double MaxSize = 10;

    /// <summary>How far from the prop's origin a part may be, or be moved, in metres.</summary>
    public const double MaxReach = 1000;

    /// <summary>The largest scale a part's transform may have on any axis.</summary>
    public const double MaxScale = 100;

    /// <summary>
    /// The fewest points a trunk, branch or root's <c>controlPoints</c> may ask for: its generator's limit. An edited
    /// trunk builds its spine at this count before the generator's own limits are applied, so it is checked here.
    /// </summary>
    public const int MinControlPoints = 3;

    /// <summary>The most points a trunk, branch or root's <c>controlPoints</c> may ask for: its generator's limit.</summary>
    public const int MaxControlPoints = 12;

    /// <summary>
    /// The edits, checked against what the generator makes now: slots, generators, copies and numbers here, and every
    /// kept part's parameters by the caller's own rule (in the app, against the engine's own part).
    /// </summary>
    /// <param name="edits">The edits as read.</param>
    /// <param name="generated">The components the prop's generator makes now.</param>
    /// <param name="parameters">A kept part's parameters, checked: given the part and the generated components; throws <see cref="FormatException"/> to refuse.</param>
    /// <returns>The edits to use.</returns>
    /// <exception cref="FormatException">Something is out of bounds; the message says what.</exception>
    public static PartEdits Validate(PartEdits edits, IReadOnlyList<SceneComponent> generated, Func<SceneComponent, IReadOnlyList<SceneComponent>, ParameterDictionary> parameters)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(parameters);
        var slots = new Dictionary<string, SceneComponent>(StringComparer.Ordinal);
        foreach (SceneComponent c in generated.Where(c => c.Slot is not null))
        {
            slots[c.Slot!] = c;
        }

        var generators = generated.Select(c => c.GeneratorId).ToHashSet(StringComparer.Ordinal);
        int copies = edits.Kept.Count(c => c.Slot is null);
        if (copies > MaxCopies)
        {
            throw Refused($"it has {copies} copies of parts, more than the {MaxCopies} a prop may have");
        }

        if (edits.Removed.Count > slots.Count || edits.Removed.Any(slot => !slots.ContainsKey(slot))
            || edits.Removed.Distinct(StringComparer.Ordinal).Count() != edits.Removed.Count)
        {
            throw Refused("it removes a slot the prop does not have");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<SceneComponent>(edits.Kept.Count);
        foreach (SceneComponent part in edits.Kept)
        {
            if (part.Slot is not null
                && (!slots.TryGetValue(part.Slot, out SceneComponent? fresh) || fresh.GeneratorId != part.GeneratorId || !seen.Add(part.Slot)))
            {
                throw Refused($"part '{part.Id}' names a slot the prop does not have, or names one twice");
            }

            if (!generators.Contains(part.GeneratorId))
            {
                throw Refused($"part '{part.Id}' is made by '{part.GeneratorId}', which this prop does not use");
            }

            CheckNumbers(part);
            kept.Add(part with { Parameters = parameters(part, generated) });
        }

        return new PartEdits(kept, edits.Removed);
    }

    private static void CheckNumbers(SceneComponent part)
    {
        Transform t = part.Transform;
        foreach ((string what, Vec3d? v) in new[] { ("position", t.Position), ("turn", t.Rotation), ("scale", t.Scale), ("move", part.Moved), ("turn", part.Turned) })
        {
            if (v is not null && !(double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z)))
            {
                throw Refused($"part '{part.Id}' has a {what} that is not a finite number");
            }
        }

        if (Far(t.Position) || (part.Moved is { } moved && Far(moved)))
        {
            throw Refused($"part '{part.Id}' is too far from the prop (more than {MaxReach} m)");
        }

        if (Math.Max(Math.Abs(t.Scale.X), Math.Max(Math.Abs(t.Scale.Y), Math.Abs(t.Scale.Z))) > MaxScale
            || Math.Max(Math.Abs(t.Rotation.X), Math.Max(Math.Abs(t.Rotation.Y), Math.Abs(t.Rotation.Z))) > MaxReach)
        {
            throw Refused($"part '{part.Id}' is too large, or turned beyond reason");
        }

        if (part.Sized is double sized && !(sized >= MinSize && sized <= MaxSize))
        {
            throw Refused(string.Create(CultureInfo.InvariantCulture, $"part '{part.Id}' has a size of {sized}, outside {MinSize} to {MaxSize}"));
        }

        if (!(part.Seed >= 0 && part.Seed <= uint.MaxValue && part.Seed == Math.Truncate(part.Seed)))
        {
            throw Refused($"part '{part.Id}' has a seed that is not a whole number from 0 to 4294967295");
        }
    }

    private static bool Far(Vec3d v) => Math.Abs(v.X) > MaxReach || Math.Abs(v.Y) > MaxReach || Math.Abs(v.Z) > MaxReach;

    private static FormatException Refused(string why) => new($"The part edits were left out: {why}.");
}
