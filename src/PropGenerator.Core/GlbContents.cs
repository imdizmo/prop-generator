using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PropGenerator;

/// <summary>What a binary glTF (GLB) holds, read from its JSON chunk: enough to tell a file that stands alone.</summary>
public static class GlbContents
{
    /// <summary>
    /// Says why a GLB does not stand alone, or null when it does: every image is inside it (in a buffer view, or a
    /// data: URI) and every buffer is its own binary chunk or a data: URI, so nothing points at a file beside it.
    /// </summary>
    /// <param name="glb">The file's bytes.</param>
    /// <returns>The problem, or null.</returns>
    public static string? NotSelfContained(ReadOnlySpan<byte> glb)
    {
        if (Json(glb) is not { } root)
        {
            return "it is not a glTF binary with a JSON chunk";
        }

        foreach ((string list, string what) in new[] { ("images", "picture"), ("buffers", "buffer") })
        {
            if (root[list] is not JsonArray items)
            {
                continue;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is not JsonObject item)
                {
                    continue;
                }

                string? uri = (item["uri"] as JsonValue)?.TryGetValue(out string? u) == true ? u : null;
                if (uri is not null && !uri.StartsWith("data:", StringComparison.Ordinal))
                {
                    return $"its {what} {i} is the file {uri} beside it, not inside it";
                }

                if (list == "images" && uri is null && item["bufferView"] is null)
                {
                    return $"its picture {i} has no data";
                }
            }
        }

        return null;
    }

    /// <summary>How many pictures the GLB holds.</summary>
    /// <param name="glb">The file's bytes.</param>
    /// <returns>The count, or -1 when it is not a GLB.</returns>
    public static int Images(ReadOnlySpan<byte> glb) =>
        Json(glb) is { } root ? (root["images"] as JsonArray)?.Count ?? 0 : -1;

    // The JSON chunk: a 12-byte header (magic, version, length), then the chunk's length and type "JSON".
    private static JsonObject? Json(ReadOnlySpan<byte> glb)
    {
        if (glb.Length < 20 || !glb[..4].SequenceEqual("glTF"u8) || !glb.Slice(16, 4).SequenceEqual("JSON"u8))
        {
            return null;
        }

        uint length = BinaryPrimitives.ReadUInt32LittleEndian(glb.Slice(12, 4));
        if (length > (uint)(glb.Length - 20))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(glb.Slice(20, (int)length)) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
