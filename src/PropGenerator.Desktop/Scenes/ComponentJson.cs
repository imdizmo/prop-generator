using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Parameters;

namespace PropGenerator.Desktop.Scenes;

/// <summary>
/// A component as JSON, as a work file keeps a part the user edited: easy-props' field names, numbers as doubles,
/// parameters in their key order (a nested object or a list of them kept as such).
/// </summary>
public static class ComponentJson
{
    /// <summary>The component as a JSON object.</summary>
    /// <param name="c">The component.</param>
    /// <returns>The object.</returns>
    public static JsonObject Write(SceneComponent c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var o = new JsonObject
        {
            ["id"] = c.Id,
            ["type"] = c.Type,
            ["generatorId"] = c.GeneratorId,
            ["generatorVersion"] = c.GeneratorVersion,
            ["parentId"] = c.ParentId,
            ["transform"] = new JsonObject
            {
                ["position"] = Vector(c.Transform.Position),
                ["rotation"] = Vector(c.Transform.Rotation),
                ["scale"] = Vector(c.Transform.Scale),
            },
            ["parameters"] = Value(c.Parameters),
            ["styleOverrides"] = c.StyleOverrides is null ? null : Value(c.StyleOverrides),
            ["seed"] = c.Seed,
            ["slot"] = c.Slot,
            ["edited"] = c.Edited,
        };
        if (c.Moved is not null)
        {
            o["moved"] = Vector(c.Moved);
        }

        if (c.Turned is not null)
        {
            o["turned"] = Vector(c.Turned);
        }

        if (c.Sized is double sized)
        {
            o["sized"] = sized;
        }

        return o;
    }

    /// <summary>A component read back from <see cref="Write"/>'s JSON.</summary>
    /// <param name="node">The object.</param>
    /// <returns>The component.</returns>
    /// <exception cref="FormatException">A field is missing or of the wrong kind.</exception>
    public static SceneComponent Read(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        try
        {
            JsonNode t = node["transform"]!;
            return new SceneComponent(
                node["id"]!.GetValue<string>(),
                node["type"]!.GetValue<string>(),
                node["generatorId"]!.GetValue<string>(),
                node["generatorVersion"]!.GetValue<int>(),
                node["parentId"]?.GetValue<string>(),
                new Transform(Required(t["position"], "position"), Required(t["rotation"], "rotation"), Required(t["scale"], "scale")),
                (ParameterDictionary)ReadValue(node["parameters"] as JsonObject ?? throw new FormatException("A part has no parameters."))!,
                node["styleOverrides"] is JsonObject so ? (ParameterDictionary)ReadValue(so)! : null,
                node["seed"]!.GetValue<double>(),
                node["slot"]?.GetValue<string>(),
                node["edited"]!.GetValue<bool>(),
                ReadVector(node["moved"]),
                ReadVector(node["turned"]),
                node["sized"]?.GetValue<double>());
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException or InvalidCastException or JsonException)
        {
            throw new FormatException($"A part is not readable: {e.Message}", e);
        }
    }

    /// <summary>Parameters as JSON, as <see cref="Write"/> writes a part's: numbers as doubles, nested objects and lists kept.</summary>
    /// <param name="parameters">The parameters.</param>
    /// <returns>The object.</returns>
    public static JsonObject WriteParameters(IReadOnlyDictionary<string, object> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return (JsonObject)Value(parameters)!;
    }

    /// <summary>Parameters read back from JSON, as <see cref="Read"/> reads a part's (null values kept as null).</summary>
    /// <param name="o">The object.</param>
    /// <returns>The parameters, in their key order.</returns>
    public static ParameterDictionary ReadParameters(JsonObject o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return (ParameterDictionary)ReadValue(o)!;
    }

    private static JsonArray Vector(Vec3d v) => [v.X, v.Y, v.Z];

    // A vector a part cannot do without: three numbers, or the part is not readable.
    private static Vec3d Required(JsonNode? node, string what) =>
        ReadVector(node) ?? throw new FormatException($"A part's {what} is not three numbers.");

    private static Vec3d? ReadVector(JsonNode? node) =>
        node is JsonArray a && a.Count == 3
            ? new Vec3d(a[0]!.GetValue<double>(), a[1]!.GetValue<double>(), a[2]!.GetValue<double>())
            : null;

    private static JsonNode? Value(object? value) => value switch
    {
        null => null,
        double d => JsonValue.Create(d),
        int i => JsonValue.Create((double)i),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        IReadOnlyDictionary<string, object> dictionary => new JsonObject(dictionary.Select(kv => KeyValuePair.Create(kv.Key, Value(kv.Value)))),
        System.Collections.IEnumerable list => new JsonArray([.. list.Cast<object?>().Select(Value)]),
        _ => throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"A part's parameter cannot be kept: {value.GetType().Name}")),
    };

    private static object? ReadValue(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject o:
                var dictionary = new ParameterDictionary();
                foreach (KeyValuePair<string, JsonNode?> kv in o)
                {
                    dictionary.Set(kv.Key, ReadValue(kv.Value)!);
                }

                return dictionary;
            case JsonArray a:
                return a.Select(ReadValue).ToList();
            default:
                JsonValue v = node.AsValue();
                return v.GetValueKind() switch
                {
                    JsonValueKind.Number => v.GetValue<double>(),
                    JsonValueKind.String => v.GetValue<string>(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null,
                };
        }
    }
}
