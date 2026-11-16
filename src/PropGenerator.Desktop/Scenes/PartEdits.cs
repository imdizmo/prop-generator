using System.Text.Json.Nodes;

namespace PropGenerator.Desktop.Scenes;

/// <summary>
/// Ours: what the user did to a prop's parts, as little as says it, so a work file and each undo step keep only the
/// parts that differ from what the generator makes. The parts are made again from the settings each time, and these
/// edits folded in with easy-props' <c>mergeGenerated</c> (passed in: the app runs easy-props' own): a part moved, turned, resized or restored is
/// kept by its slot, a copy as it is, and a removed part stays removed (in easy-props a rebuild brings a removed part
/// back; here it stays gone until undone).
/// </summary>
/// <param name="Kept">The parts the user changed (moved, turned, resized, restored) and the parts the user made (copies), in the prop's order.</param>
/// <param name="Removed">The slots of the generated parts the user removed.</param>
public sealed record PartEdits(IReadOnlyList<SceneComponent> Kept, IReadOnlyList<string> Removed)
{
    /// <summary>Gets no edits.</summary>
    public static PartEdits None { get; } = new([], []);

    /// <summary>Gets a value indicating whether there are no edits.</summary>
    public bool IsEmpty => Kept.Count == 0 && Removed.Count == 0;

    /// <summary>The edits that make <paramref name="components"/> from what the generator makes.</summary>
    /// <param name="generated">The components the generator makes.</param>
    /// <param name="components">The components after the user's edits.</param>
    /// <returns>The edits.</returns>
    public static PartEdits Of(IReadOnlyList<SceneComponent> generated, IReadOnlyList<SceneComponent> components)
    {
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(components);
        var slots = components.Where(c => c.Slot is not null).Select(c => c.Slot!).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<SceneComponent> kept =
        [
            .. components.Where(c => c.Slot is null || c.Moved is not null || c.Edited || c.Turned is not null || c.Sized is not null),
        ];
        IReadOnlyList<string> removed = [.. generated.Where(g => g.Slot is not null && !slots.Contains(g.Slot)).Select(g => g.Slot!)];
        return new PartEdits(kept, removed);
    }

    /// <summary>The prop's components: what the generator makes, with these edits folded in.</summary>
    /// <param name="generated">The components the generator makes now.</param>
    /// <param name="merge">
    /// easy-props' <c>mergeGenerated(kept, generated)</c>: the kept parts folded into what the generator makes (in the
    /// app, the engine's own; see <c>RulesEngine.MergeGenerated</c>).
    /// </param>
    /// <returns>The components.</returns>
    public IReadOnlyList<SceneComponent> Apply(
        IReadOnlyList<SceneComponent> generated,
        Func<IReadOnlyList<SceneComponent>, IReadOnlyList<SceneComponent>, IReadOnlyList<SceneComponent>> merge)
    {
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(merge);
        if (IsEmpty)
        {
            return generated;
        }

        IReadOnlyList<SceneComponent> merged = merge(Kept, generated);
        if (Removed.Count == 0)
        {
            return merged;
        }

        var removed = Removed.ToHashSet(StringComparer.Ordinal);
        var doomed = new HashSet<string>(StringComparer.Ordinal);
        foreach (SceneComponent c in merged.Where(c => c.Slot is not null && removed.Contains(c.Slot)))
        {
            doomed.UnionWith(SceneOps.WithDescendants(merged, c.Id).Select(d => d.Id));
        }

        return [.. merged.Where(c => !doomed.Contains(c.Id))];
    }

    /// <summary>The edits as JSON: <c>{"kept": [components], "removed": [slots]}</c>.</summary>
    /// <returns>The object.</returns>
    public JsonObject ToJson() => new()
    {
        ["kept"] = new JsonArray([.. Kept.Select(c => (JsonNode)ComponentJson.Write(c))]),
        ["removed"] = new JsonArray([.. Removed.Select(s => (JsonNode)JsonValue.Create(s))]),
    };

    /// <summary>The edits <see cref="ToJson"/> wrote; none for null.</summary>
    /// <param name="node">The object, or null.</param>
    /// <returns>The edits.</returns>
    /// <exception cref="FormatException">It is not edits.</exception>
    public static PartEdits FromJson(JsonNode? node)
    {
        if (node is null)
        {
            return None;
        }

        if (node is not JsonObject o || o["kept"] is not JsonArray kept || o["removed"] is not JsonArray removed)
        {
            throw new FormatException("The part edits are not an object with kept parts and removed slots.");
        }

        try
        {
            return new PartEdits(
                [.. kept.Select(c => ComponentJson.Read(c ?? throw new FormatException("A kept part is empty.")))],
                [.. removed.Select(s => s?.GetValue<string>() ?? throw new FormatException("A removed slot is empty."))]);
        }
        catch (InvalidOperationException e)
        {
            throw new FormatException($"The part edits are not readable: {e.Message}", e);
        }
    }
}
