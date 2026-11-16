using System.Text.Json;
using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;

namespace PropGenerator.Engine;

/// <summary>
/// Reads what the bundle returns into plain C# data. Meshes and pixels come as <c>{meta: string, arrays:
/// TypedArray[]}</c>; each array is copied out once, with one bulk read.
/// </summary>
internal static class ScriptData
{
    /// <summary>JSON as the bundle writes it (camelCase).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static T Parse<T>(object? result, string call)
    {
        string text = Text(result, call);
        return JsonSerializer.Deserialize<T>(text, Json)
            ?? throw new EngineScriptException(call + " returned null.");
    }

    public static string Text(object? result, string call) => result as string
        ?? throw new EngineScriptException($"{call} returned {Describe(result)}, not a JSON string.");

    /// <summary>A JSON answer that may be <c>null</c> (a refusal): its text, or null.</summary>
    public static string? TextOrNull(object? result, string call)
    {
        string text = Text(result, call);
        return text == "null" ? null : text;
    }

    /// <summary>A JSON string answer (such as <c>"\"5\""</c>), read as the string it holds.</summary>
    public static string JsonString(object? result, string call) => Parse<string>(result, call);

    /// <summary>One property of a JSON object answer, as its own JSON text.</summary>
    public static string Property(JsonElement root, string name, string call) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement v)
            ? v.GetRawText()
            : throw new EngineScriptException($"{call} returned no '{name}'.");

    /// <summary>Make it fit's answer: <c>{scene, changes, description, counts: {faces, budget, fits, stopped}}</c>.</summary>
    public static EngineFit Fit(object? result)
    {
        using var doc = JsonDocument.Parse(Text(result, "makeItFit"));
        JsonElement root = doc.RootElement;
        JsonElement counts = root.GetProperty("counts");
        return new EngineFit(
            Property(root, "scene", "makeItFit"),
            root.GetProperty("changes").Deserialize<List<EngineFitChange>>(Json) ?? [],
            root.GetProperty("description").GetString() ?? "",
            (int)counts.GetProperty("faces").GetDouble(),
            (int)counts.GetProperty("budget").GetDouble(),
            counts.GetProperty("fits").GetBoolean(),
            counts.GetProperty("stopped").GetBoolean());
    }

    /// <summary>Pieces: <c>[{kind, name, scene}]</c>, each scene kept as its JSON text.</summary>
    public static List<EnginePiece> Pieces(object? result)
    {
        using var doc = JsonDocument.Parse(Text(result, "makePieces"));
        return [.. doc.RootElement.EnumerateArray().Select(static p => new EnginePiece(
            p.GetProperty("kind").GetString() ?? "",
            p.GetProperty("name").GetString() ?? "",
            Property(p, "scene", "makePieces")))];
    }

    /// <summary>An array answer whose items are kept whole beside their typed reading (damage settings).</summary>
    public static List<EngineDamageSetting> DamageSettings(object? result, string call)
    {
        using var doc = JsonDocument.Parse(Text(result, call));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new EngineScriptException(call + " returned no array.");
        }

        return [.. doc.RootElement.EnumerateArray().Select(e =>
            (e.Deserialize<EngineDamageSetting>(Json) ?? throw new EngineScriptException(call + " returned a null setting.")) with { Spec = e.Clone() })];
    }

    /// <summary>
    /// A built mesh. Meta: <c>{parts: [{componentId, generatorId, material, role, roleOfPart, flatShading, matrix,
    /// tint, growth, proxy, surface, vertices, faces, attributes: [{name, itemSize, normalized, array}], index: {type,
    /// array} | null}], fullFaces?}</c>, where each <c>array</c> is a position in <c>arrays</c>: Float32Array for
    /// attributes, Uint16Array or Uint32Array for the index.
    /// </summary>
    public static EngineMesh Mesh(object? result)
    {
        (JsonElement meta, ScriptObject arrays) = Packed(result, "build");
        var parts = new List<EnginePart>();
        try
        {
            if (meta.TryGetProperty("parts", out JsonElement list))
            {
                foreach (JsonElement p in list.EnumerateArray())
                {
                    parts.Add(Part(p, arrays));
                }
            }
        }
        finally
        {
            Release(arrays, result);
        }

        int? fullFaces = meta.TryGetProperty("fullFaces", out JsonElement f) && f.ValueKind == JsonValueKind.Number
            ? f.GetInt32()
            : null;
        return new EngineMesh(parts, fullFaces, meta);
    }

    private static EnginePart Part(JsonElement p, ScriptObject arrays)
    {
        var attributes = new Dictionary<string, EngineVertexData>(StringComparer.Ordinal);
        if (p.TryGetProperty("attributes", out JsonElement list))
        {
            foreach (JsonElement a in list.EnumerateArray())
            {
                string name = Str(a, "name") ?? throw new EngineScriptException("build: an attribute has no name.");
                float[] data = Take(Item(arrays, ArrayAt(a)), Floats);
                attributes[name] = new EngineVertexData(name, (int)(Number(a, "itemSize") ?? 1), Flag(a, "normalized"), data);
            }
        }

        uint[]? index = null;
        bool index16 = false;
        if (p.TryGetProperty("index", out JsonElement i) && i.ValueKind == JsonValueKind.Object)
        {
            object array = Item(arrays, ArrayAt(i));
            index16 = array is ITypedArray<ushort>;
            index = Take(array, Index);
        }

        JsonElement surface = p.TryGetProperty("surface", out JsonElement s) ? s : default;
        return new EnginePart
        {
            ComponentId = Str(p, "componentId"),
            GeneratorId = Str(p, "generatorId"),
            Material = Str(p, "material"),
            Role = Str(p, "role"),
            RoleOfPart = Str(p, "roleOfPart"),
            FlatShading = Flag(p, "flatShading"),
            Proxy = Flag(p, "proxy"),
            Matrix = Numbers(p, "matrix") ?? [],
            Tint = Numbers(p, "tint"),
            Growth = Number(p, "growth") ?? 1,
            Surface = new EngineSurface
            {
                VertexColors = Flag(surface, "vertexColors"),
                Map = Str(surface, "map"),
                NormalMap = Str(surface, "normalMap"),
                AlphaTest = Number(surface, "alphaTest"),
                Roughness = Number(surface, "roughness"),
                ExportName = Str(surface, "exportName"),
                Meta = surface,
            },
            VertexCount = (int)(Number(p, "vertices") ?? 0),
            Faces = (int)(Number(p, "faces") ?? 0),
            Attributes = attributes,
            Index = index,
            Index16 = index16,
            Meta = p,
        };
    }

    private static int ArrayAt(JsonElement e) => (int)(Number(e, "array")
        ?? throw new EngineScriptException("build: an attribute or index names no array."));

    /// <summary>A texture: meta <c>{w, h}</c> (or width, height), one Uint8Array of RGBA.</summary>
    public static EngineTexture Texture(object? result, string key)
    {
        (JsonElement meta, ScriptObject arrays) = Packed(result, "texture " + key);
        int w = (int)(Number(meta, "w") ?? Number(meta, "width") ?? 0);
        int h = (int)(Number(meta, "h") ?? Number(meta, "height") ?? 0);
        byte[] rgba;
        try
        {
            rgba = Take(Item(arrays, 0), Bytes);
        }
        finally
        {
            Release(arrays, result);
        }

        return rgba.Length != w * h * 4
            ? throw new EngineScriptException($"texture {key}: {rgba.Length} bytes for {w} x {h} pixels.")
            : new EngineTexture(w, h, rgba, meta);
    }

    /// <summary>
    /// An export package: an array of <c>{name, bytes}</c>, or <c>{meta: '[{name}]', arrays: Uint8Array[]}</c>.
    /// </summary>
    public static List<EngineFile> Files(object? result)
    {
        var files = new List<EngineFile>();
        if (result is ScriptObject packed && packed.GetProperty("meta") is string)
        {
            (JsonElement meta, ScriptObject arrays) = Packed(result, "exportPackage");
            try
            {
                int i = 0;
                foreach (JsonElement f in meta.EnumerateArray())
                {
                    files.Add(new EngineFile(Str(f, "name") ?? "", Take(Item(arrays, i++), Bytes)));
                }
            }
            finally
            {
                Release(arrays, result);
            }

            return files;
        }

        if (result is not ScriptObject list)
        {
            throw new EngineScriptException($"exportPackage returned {Describe(result)}.");
        }

        try
        {
            int n = Length(list);
            for (int i = 0; i < n; i++)
            {
                using var file = (ScriptObject)Item(list, i);
                int? faces = file.GetProperty("faces") is int or double ? Convert.ToInt32(file.GetProperty("faces"), System.Globalization.CultureInfo.InvariantCulture) : null;
                files.Add(new EngineFile(file.GetProperty("name") as string ?? "", Take(file.GetProperty("bytes"), Bytes), faces));
            }
        }
        finally
        {
            Release(list);
        }

        return files;
    }

    private static (JsonElement Meta, ScriptObject Arrays) Packed(object? result, string call)
    {
        if (result is not ScriptObject o || o.GetProperty("meta") is not string meta
            || o.GetProperty("arrays") is not ScriptObject arrays)
        {
            throw new EngineScriptException($"{call} returned {Describe(result)}, not {{meta, arrays}}.");
        }

        using var doc = JsonDocument.Parse(meta);
        return (doc.RootElement.Clone(), arrays);
    }

    /// <summary>
    /// Reads a script value, then disposes its proxy. A proxy left to the finalizer keeps its V8 object (a mesh's
    /// arrays) alive until the finalizer runs, and with several engines busy it falls behind: memory grows by the
    /// gigabyte. Every proxy read here is let go as soon as it is copied.
    /// </summary>
    private static T Take<T>(object item, Func<object, T> read)
    {
        try
        {
            return read(item);
        }
        finally
        {
            (item as IDisposable)?.Dispose();
        }
    }

    /// <summary>Disposes script proxies (see <see cref="Take"/>).</summary>
    private static void Release(params object?[] items)
    {
        foreach (object? item in items)
        {
            (item as IDisposable)?.Dispose();
        }
    }

    private static int Length(ScriptObject array) => Convert.ToInt32(array.GetProperty("length"), System.Globalization.CultureInfo.InvariantCulture);

    private static object Item(ScriptObject array, int i) => i < Length(array)
        ? array.GetProperty(i)
        : throw new EngineScriptException($"The result has {Length(array)} arrays; array {i} is missing.");

    public static float[] Floats(object array)
    {
        if (array is not ITypedArray<float> f)
        {
            throw new EngineScriptException($"Expected a Float32Array, got {Describe(array)}.");
        }

        float[] copy = new float[f.Length];
        f.Read(0, f.Length, copy, 0);
        return copy;
    }

    public static uint[] Index(object array)
    {
        switch (array)
        {
            case ITypedArray<uint> u32:
            {
                uint[] copy = new uint[u32.Length];
                u32.Read(0, u32.Length, copy, 0);
                return copy;
            }

            case ITypedArray<ushort> u16:
            {
                ushort[] narrow = new ushort[u16.Length];
                u16.Read(0, u16.Length, narrow, 0);
                return Array.ConvertAll(narrow, static v => (uint)v);
            }

            default:
                throw new EngineScriptException($"Expected a Uint16Array or Uint32Array index, got {Describe(array)}.");
        }
    }

    public static byte[] Bytes(object array) => array switch
    {
        ITypedArray<byte> u8 => ReadBytes(u8),
        IArrayBufferView view => view.GetBytes(),
        IArrayBuffer buffer => buffer.GetBytes(),
        _ => throw new EngineScriptException($"Expected bytes, got {Describe(array)}."),
    };

    private static byte[] ReadBytes(ITypedArray<byte> u8)
    {
        byte[] copy = new byte[u8.Length];
        u8.Read(0, u8.Length, copy, 0);
        return copy;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    private static bool Flag(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

    private static double? Number(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;

    private static double[]? Numbers(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Select(static x => x.GetDouble())]
            : null;

    public static string Describe(object? value) => value switch
    {
        null => "null",
        Undefined => "undefined",
        string s => $"a string ({s.Length} characters)",
        _ => value.GetType().Name,
    };
}
