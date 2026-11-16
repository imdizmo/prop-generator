using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace PropGenerator.Nature.Tests.Support;

/// <summary>A GLB file read by hand: its JSON chunk as written, its BIN chunk, and the bytes of each accessor and image.</summary>
internal sealed class GlbFile : IDisposable
{
    private static readonly Dictionary<string, int> Components = new(StringComparer.Ordinal)
    {
        ["SCALAR"] = 1,
        ["VEC2"] = 2,
        ["VEC3"] = 3,
        ["VEC4"] = 4,
        ["MAT4"] = 16,
    };

    private GlbFile(JsonDocument json, byte[] bin)
    {
        Json = json;
        Bin = bin;
    }

    /// <summary>The JSON chunk.</summary>
    public JsonDocument Json { get; }

    /// <summary>The BIN chunk (empty when the file has none).</summary>
    public byte[] Bin { get; }

    /// <summary>Reads a GLB 2.0 file: header, JSON chunk, then an optional BIN chunk.</summary>
    public static GlbFile Parse(byte[] bytes)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x46546C67 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != 2
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) != bytes.Length)
        {
            throw new InvalidDataException("Not a GLB 2.0 file of its own length");
        }

        int jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)) != 0x4E4F534A)
        {
            throw new InvalidDataException("The first chunk is not JSON");
        }

        var json = JsonDocument.Parse(Encoding.UTF8.GetString(bytes, 20, jsonLength).TrimEnd(' ', '\0'));
        int binAt = 20 + jsonLength;
        byte[] bin = [];
        if (binAt < bytes.Length)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(binAt + 4)) != 0x004E4942)
            {
                throw new InvalidDataException("The second chunk is not BIN");
            }

            bin = bytes.AsSpan(binAt + 8, (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(binAt))).ToArray();
        }

        return new GlbFile(json, bin);
    }

    /// <summary>The bytes one accessor reads: <c>count</c> packed elements from its buffer view.</summary>
    public byte[] AccessorBytes(JsonElement accessor)
    {
        int element = Components[accessor.GetProperty("type").GetString()!] * accessor.GetProperty("componentType").GetInt32() switch
        {
            5120 or 5121 => 1,
            5122 or 5123 => 2,
            5125 or 5126 => 4,
            int other => throw new InvalidDataException($"component type {other}"),
        };
        byte[] view = BufferView(accessor.GetProperty("bufferView").GetInt32(), element);
        int offset = accessor.TryGetProperty("byteOffset", out var at) ? at.GetInt32() : 0;
        return view.AsSpan(offset, accessor.GetProperty("count").GetInt32() * element).ToArray();
    }

    /// <summary>The bytes of an image stored in a buffer view.</summary>
    public byte[] ImageBytes(JsonElement image) => BufferView(image.GetProperty("bufferView").GetInt32(), null);

    /// <summary>The bytes of a buffer view of buffer 0 (the BIN chunk), packed: a stride, if any, must be the element size.</summary>
    private byte[] BufferView(int index, int? element)
    {
        var view = Json.RootElement.GetProperty("bufferViews")[index];
        if ((view.TryGetProperty("buffer", out var buffer) ? buffer.GetInt32() : 0) != 0)
        {
            throw new InvalidDataException("A buffer view outside the BIN chunk");
        }

        if (view.TryGetProperty("byteStride", out var stride) && stride.GetInt32() != element)
        {
            throw new InvalidDataException("An interleaved buffer view");
        }

        int offset = view.TryGetProperty("byteOffset", out var at) ? at.GetInt32() : 0;
        return Bin.AsSpan(offset, view.GetProperty("byteLength").GetInt32()).ToArray();
    }

    public void Dispose() => Json.Dispose();
}
