using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Tests.Support;

/// <summary>
/// The kernel reference (Fixtures/kernel-sample.json): a seeded sample of easy-props 0.4.2's random cases per family
/// (its header says how it was cut) and the fixed meshes whole.
/// </summary>
internal static class KernelGoldens
{
    private static readonly Lazy<JsonDocument> Index = new(() => Fixture.Json("kernel-sample.json"));
    private static readonly Lazy<byte[]> MeshBytes = new(() => Unzip(Convert.FromBase64String(Index.Value.RootElement.GetProperty("meshBytesGzip").GetString()!)));

    /// <summary>The eight families the golden holds, each of <see cref="CasesPerFamily"/> random cases.</summary>
    public static readonly string[] FamilyNames =
        ["compose", "applyMatrix4", "setFromEuler", "setFromUnitVectors", "setFromQuaternion", "color", "vector", "quaternion"];

    /// <summary>Random cases per family in the sample.</summary>
    public const int CasesPerFamily = 100;

    /// <summary>The family names the sample lists.</summary>
    public static string[] IndexedFamilyNames() =>
        [.. Index.Value.RootElement.GetProperty("families").EnumerateArray().Select(f => f.GetProperty("name").GetString()!)];

    /// <summary>One family of random cases, read whole (they are a few hundred kilobytes).</summary>
    public static KernelFamily Family(string name)
    {
        foreach (var family in Index.Value.RootElement.GetProperty("families").EnumerateArray())
        {
            if (family.GetProperty("name").GetString() == name)
            {
                string[] inputs = [.. family.GetProperty("inputs").EnumerateArray().Select(e => e.GetString()!)];
                string[] outputs = [.. family.GetProperty("outputs").EnumerateArray().Select(e => e.GetString()!)];
                double[] data = MemoryMarshal.Cast<byte, double>(Convert.FromBase64String(family.GetProperty("data").GetString()!)).ToArray();
                var result = new KernelFamily(name, inputs, outputs, data);
                if (result.Records != CasesPerFamily)
                {
                    throw new InvalidOperationException(
                        $"Fixture stale: kernel family {name} has {result.Records} cases, not {CasesPerFamily}");
                }

                return result;
            }
        }

        throw new InvalidOperationException($"Fixture stale: no kernel family {name}");
    }

    /// <summary>A fresh copy of a golden mesh, or null where three's merge gave none.</summary>
    public static PropMesh? Mesh(string name)
    {
        var entry = Entry(name);
        if (entry.GetProperty("null").GetBoolean())
        {
            return null;
        }

        var mesh = new PropMesh { Position = [] };
        foreach (var block in entry.GetProperty("blocks").EnumerateArray())
        {
            string blockName = block.GetProperty("name").GetString()!;
            switch (blockName)
            {
                case "position":
                    mesh.Position = Floats(block);
                    break;
                case "normal":
                    mesh.Normal = Floats(block);
                    break;
                case "color":
                    mesh.Color = Floats(block);
                    break;
                case "uv":
                    mesh.Uv = Floats(block);
                    break;
                case "uv1":
                    mesh.Uv1 = Floats(block);
                    break;
                case "index":
                    mesh.SetIndex(MemoryMarshal.Cast<byte, uint>(Bytes(block, 4)).ToArray());
                    break;
                default:
                    throw new InvalidOperationException($"{name}: unknown block {blockName}");
            }
        }

        foreach (var group in entry.GetProperty("groups").EnumerateArray())
        {
            mesh.Groups.Add((group.GetProperty("start").GetInt32(), group.GetProperty("count").GetInt32(),
                group.GetProperty("materialIndex").GetInt32()));
        }

        return mesh;
    }

    /// <summary>A golden mesh that three did make.</summary>
    public static PropMesh RequiredMesh(string name) =>
        Mesh(name) ?? throw new InvalidOperationException($"{name}: three made no mesh");

    /// <summary>The f64 values of one block of an entry: a box (min, max, size) or a matrix.</summary>
    public static double[] Doubles(string name, string block)
    {
        foreach (var b in Entry(name).GetProperty("blocks").EnumerateArray())
        {
            if (b.GetProperty("name").GetString() == block)
            {
                return MemoryMarshal.Cast<byte, double>(Bytes(b, 8)).ToArray();
            }
        }

        throw new InvalidOperationException($"{name}: no block {block}");
    }

    /// <summary>A box entry's isEmpty.</summary>
    public static bool BoxIsEmpty(string name) => Entry(name).GetProperty("isEmpty").GetBoolean();

    private static JsonElement Entry(string name)
    {
        foreach (var entry in Index.Value.RootElement.GetProperty("meshes").EnumerateArray())
        {
            if (entry.GetProperty("name").GetString() == name)
            {
                return entry;
            }
        }

        throw new InvalidOperationException($"Fixture stale: no kernel mesh entry {name}");
    }

    private static float[] Floats(JsonElement block) => MemoryMarshal.Cast<byte, float>(Bytes(block, 4)).ToArray();

    private static ReadOnlySpan<byte> Bytes(JsonElement block, int size)
    {
        int offset = block.GetProperty("offset").GetInt32();
        int values = block.GetProperty("count").GetInt32() * block.GetProperty("itemSize").GetInt32();
        return MeshBytes.Value.AsSpan(offset, values * size);
    }

    private static byte[] Unzip(byte[] gzipped)
    {
        using var gzip = new GZipStream(new MemoryStream(gzipped), CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        return bytes.ToArray();
    }
}
