using System.Text.Json.Nodes;
using PropGenerator.Desktop.Scenes;

namespace PropGenerator.Desktop.Recipes;

/// <summary>The scene the engine builds a prop from, as <c>entry.ts</c>'s <c>sceneOf</c> reads one.</summary>
internal static class EngineScenes
{
    /// <summary>A kind's scene (see <see cref="Of(string, string, uint, Scene?)"/>).</summary>
    /// <param name="kind">The kind; its id is the upstream generator.</param>
    /// <param name="valuesJson">The values, a JSON object.</param>
    /// <param name="seed">The seed.</param>
    /// <param name="edited">The scene with the part edits folded in, or null when there are none.</param>
    /// <returns>The scene JSON.</returns>
    public static string Of(EngineKind kind, string valuesJson, uint seed, Scene? edited)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return Of(kind.Id, valuesJson, seed, edited);
    }

    /// <summary>
    /// A prop's scene: its generator, seed and values, and, when the user has edited its parts, the edited components
    /// (full parameter objects, which <c>entry.ts</c>'s <c>expand</c> passes through). With no edits the components are
    /// left out and the engine makes them itself.
    /// </summary>
    /// <param name="generatorId">The upstream generator, such as "bush".</param>
    /// <param name="valuesJson">The values, a JSON object.</param>
    /// <param name="seed">The seed.</param>
    /// <param name="edited">The scene with the part edits folded in, or null when there are none.</param>
    /// <returns>The scene JSON.</returns>
    /// <exception cref="System.Text.Json.JsonException">The values are not JSON.</exception>
    /// <exception cref="ArgumentException">The values are not a JSON object.</exception>
    public static string Of(string generatorId, string valuesJson, uint seed, Scene? edited)
    {
        ArgumentNullException.ThrowIfNull(generatorId);
        ArgumentNullException.ThrowIfNull(valuesJson);
        if (JsonNode.Parse(valuesJson) is not JsonObject values)
        {
            throw new ArgumentException("The values are not a JSON object.", nameof(valuesJson));
        }

        var scene = new JsonObject
        {
            ["generatorId"] = generatorId,
            ["seed"] = seed,
            ["parameters"] = values,
        };
        if (edited is not null)
        {
            scene["components"] = new JsonArray([.. edited.Components.Select(c => (JsonNode)ComponentJson.Write(c))]);
        }

        return scene.ToJsonString();
    }
}
