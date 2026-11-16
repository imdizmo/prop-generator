using System.IO.Compression;
using System.Text.Json;

namespace PropGenerator.Nature.Tests.Support;

/// <summary>
/// The reference data committed under tests/Nature.Tests/Fixtures: easy-props' own numbers copied or sampled from the
/// 0.4.2 goldens before they went, and the export path's pinned hashes. Nothing here reads outside the repository.
/// </summary>
internal static class Fixture
{
    /// <summary>The fixtures folder: tests/Nature.Tests/Fixtures in the repository.</summary>
    public static string Dir => System.IO.Path.Combine(RepoRoot(), "tests", "Nature.Tests", "Fixtures");

    /// <summary>The full path of a fixture.</summary>
    /// <param name="name">The fixture's file name, such as "rng.json".</param>
    /// <returns>The path.</returns>
    public static string Path(string name) => System.IO.Path.Combine(Dir, name);

    /// <summary>A fixture's bytes; a <c>.gz</c> fixture is unpacked.</summary>
    /// <param name="name">The fixture's file name.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Read(string name)
    {
        string path = Path(name);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Fixture missing: {path}");
        }

        if (!name.EndsWith(".gz", StringComparison.Ordinal))
        {
            return File.ReadAllBytes(path);
        }

        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        return bytes.ToArray();
    }

    /// <summary>A JSON fixture.</summary>
    /// <param name="name">The fixture's file name.</param>
    /// <returns>The document.</returns>
    public static JsonDocument Json(string name) => JsonDocument.Parse(Read(name));

    /// <summary>The repository root: the first folder above the test assembly that holds PropGenerator.slnx.</summary>
    /// <returns>The folder.</returns>
    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "PropGenerator.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"No PropGenerator.slnx above {AppContext.BaseDirectory}");
    }
}
