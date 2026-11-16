using System.Globalization;
using System.Text.Json.Nodes;
using PropGenerator.Nature.Kernel;
using SceneTransform = PropGenerator.Desktop.Scenes.Transform;

namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// One of easy-props' scene operations on a prop's parts (E1 of the phase 3 plan), as <c>entry.ts</c>'s <c>sceneOp</c>
/// takes it: one case per upstream function. Positions, transforms and patches are in the prop's own Y-up space.
/// </summary>
internal abstract record SceneOp
{
    private SceneOp()
    {
    }

    /// <summary>Gets a value indicating whether the op makes every part again (tree, seed), so it runs on the engine's thread.</summary>
    internal virtual bool Heavy => false;

    /// <summary>The op as <c>entry.ts</c> reads it: <c>{kind, componentId, ...}</c>.</summary>
    /// <returns>The object.</returns>
    internal abstract JsonObject ToJson();

    private static JsonObject Op(string kind, string componentId) => new() { ["kind"] = kind, ["componentId"] = componentId };

    private static JsonArray Vector(Vec3d v) => [v.X, v.Y, v.Z];

    // A patch's values: numbers, text, true or false, and lists or objects of them.
    private static JsonNode? ValueOf(object? value) => value switch
    {
        null => null,
        double d => JsonValue.Create(d),
        float f => JsonValue.Create((double)f),
        int i => JsonValue.Create((double)i),
        uint u => JsonValue.Create((double)u),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        IReadOnlyDictionary<string, object> o => new JsonObject(o.Select(kv => KeyValuePair.Create(kv.Key, ValueOf(kv.Value)))),
        System.Collections.IEnumerable list => new JsonArray([.. list.Cast<object?>().Select(ValueOf)]),
        _ => throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"A patch value cannot be sent: {value.GetType().Name}")),
    };

    private static JsonObject PatchOf(IReadOnlyDictionary<string, object> patch) =>
        new(patch.Select(kv => KeyValuePair.Create(kv.Key, ValueOf(kv.Value))));

    /// <summary>Copies a part and what hangs off it (upstream's <c>copyComponent</c>, the copies named as its editor names them).</summary>
    /// <param name="ComponentId">The part.</param>
    internal sealed record Copy(string ComponentId) : SceneOp
    {
        /// <inheritdoc/>
        internal override JsonObject ToJson() => Op("copy", ComponentId);
    }

    /// <summary>Removes a part and what hangs off it (<c>removeComponent</c>).</summary>
    /// <param name="ComponentId">The part.</param>
    internal sealed record Remove(string ComponentId) : SceneOp
    {
        /// <inheritdoc/>
        internal override JsonObject ToJson() => Op("remove", ComponentId);
    }

    /// <summary>Puts a part's own settings back to its generator's defaults (<c>restoreComponentDefault</c>).</summary>
    /// <param name="ComponentId">The part.</param>
    internal sealed record Restore(string ComponentId) : SceneOp
    {
        /// <inheritdoc/>
        internal override JsonObject ToJson() => Op("restore", ComponentId);
    }

    /// <summary>Breaks a tree's heavy limb partway (<c>breakComponent</c>).</summary>
    /// <param name="ComponentId">The limb.</param>
    internal sealed record Break(string ComponentId) : SceneOp
    {
        /// <inheritdoc/>
        internal override JsonObject ToJson() => Op("break", ComponentId);
    }

    /// <summary>Moves a part to a position, in metres (<c>setComponentPosition</c>; a painted part through its tree's edits).</summary>
    /// <param name="ComponentId">The part.</param>
    /// <param name="At">Where, Y-up.</param>
    internal sealed record Position(string ComponentId, Vec3d At) : SceneOp
    {
        /// <inheritdoc/>
        internal override JsonObject ToJson()
        {
            JsonObject o = Op("position", ComponentId);
            o["position"] = Vector(At);
            return o;
        }
    }

    /// <summary>
    /// Replaces a part's transform (<c>updateComponentTransform</c>). Ours beside it: how the user turned and sized the
    /// part (<see cref="Scenes.SceneComponent.Turned"/>, <see cref="Scenes.SceneComponent.Sized"/>), carried
    /// on the part so a rebuild keeps them.
    /// </summary>
    /// <param name="ComponentId">The part.</param>
    /// <param name="To">The new transform.</param>
    /// <param name="Turned">The user's turn of the part, or null to leave it.</param>
    /// <param name="Sized">The user's size of the part, or null to leave it.</param>
    internal sealed record Transform(string ComponentId, SceneTransform To, Vec3d? Turned = null, double? Sized = null) : SceneOp
    {
        /// <inheritdoc/>
        internal override JsonObject ToJson()
        {
            JsonObject o = Op("transform", ComponentId);
            o["transform"] = new JsonObject { ["position"] = Vector(To.Position), ["rotation"] = Vector(To.Rotation), ["scale"] = Vector(To.Scale) };
            return o;
        }
    }

    /// <summary>Merges a patch into a part's own parameters (<c>updateComponentParameters</c>).</summary>
    /// <param name="ComponentId">The part.</param>
    /// <param name="Patch">The parameters and their new values.</param>
    internal sealed record Parameters(string ComponentId, IReadOnlyDictionary<string, object> Patch) : SceneOp
    {
        /// <inheritdoc/>
        internal override JsonObject ToJson()
        {
            JsonObject o = Op("parameters", ComponentId);
            o["patch"] = PatchOf(Patch);
            return o;
        }
    }

    /// <summary>
    /// One of a painted tree's part actions (upstream's <c>applyPartAction</c>): patch, move, remove, break or restore;
    /// nothing for a part that is not a painted one.
    /// </summary>
    /// <param name="ComponentId">The part.</param>
    /// <param name="Action">"patch", "move", "remove", "break" or "restore".</param>
    /// <param name="Patch">A patch's settings, such as <c>turn</c> or <c>sizeScale</c>.</param>
    /// <param name="At">A move's position, Y-up.</param>
    internal sealed record Part(string ComponentId, string Action, IReadOnlyDictionary<string, object>? Patch = null, Vec3d? At = null) : SceneOp
    {
        /// <inheritdoc/>
        internal override JsonObject ToJson()
        {
            var action = new JsonObject { ["kind"] = Action };
            if (Patch is not null)
            {
                action["patch"] = PatchOf(Patch);
            }

            if (At is not null)
            {
                action["position"] = Vector(At);
            }

            JsonObject o = Op("part", ComponentId);
            o["action"] = action;
            return o;
        }
    }

    /// <summary>Changes the prop's own settings and makes its parts again (<c>updateTreeParameters</c>): heavy.</summary>
    /// <param name="Patch">The settings and their new values.</param>
    internal sealed record Tree(IReadOnlyDictionary<string, object> Patch) : SceneOp
    {
        /// <inheritdoc/>
        internal override bool Heavy => true;

        /// <inheritdoc/>
        internal override JsonObject ToJson() => new() { ["kind"] = "tree", ["patch"] = PatchOf(Patch) };
    }

    /// <summary>Gives the prop a new seed and makes its parts again (<c>setAssetSeed</c>): heavy.</summary>
    /// <param name="Value">The seed.</param>
    internal sealed record Seed(uint Value) : SceneOp
    {
        /// <inheritdoc/>
        internal override bool Heavy => true;

        /// <inheritdoc/>
        internal override JsonObject ToJson() => new() { ["kind"] = "seed", ["seed"] = (double)Value };
    }
}
