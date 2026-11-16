using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>
/// Work files as the 0.4.2 C# port made them, and summaries of easy-props 0.4.2's cut-rock goldens, committed under
/// tests/Desktop.Tests/Fixtures/port-0.4.2 before the port and its goldens went (phase 4, D4): the proof that old files
/// keep opening as they did.
/// </summary>
internal static class PortFixtures
{
    /// <summary>Gets the fixtures folder.</summary>
    internal static string Dir { get; } = Path.Combine(RepoRoot(), "tests", "Desktop.Tests", "Fixtures", "port-0.4.2");

    /// <summary>The full path of a fixture.</summary>
    /// <param name="name">Its file name, such as "bush.propgen".</param>
    /// <returns>The path.</returns>
    internal static string PathOf(string name) => Path.Combine(Dir, name);

    /// <summary>Copies a fixture (a work file) to where a test opens it, so the committed one is never written.</summary>
    /// <param name="name">Its file name.</param>
    /// <param name="to">Where it goes.</param>
    /// <returns><paramref name="to"/>.</returns>
    internal static string Copy(string name, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(PathOf(name), to, overwrite: true);
        return to;
    }

    /// <summary>A JSON fixture; a <c>.gz</c> one is unpacked.</summary>
    /// <param name="name">Its file name.</param>
    /// <returns>The document.</returns>
    internal static JsonDocument Json(string name)
    {
        if (!name.EndsWith(".gz", StringComparison.Ordinal))
        {
            return JsonDocument.Parse(File.ReadAllBytes(PathOf(name)));
        }

        using FileStream file = File.OpenRead(PathOf(name));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    /// <summary>
    /// A detail level's summary, as cut-rock-summaries.json holds the goldens': the parts' faces, the bounds of their
    /// positions (rounded to 1e-4) and a SHA-256 of the positions rounded to 1e-4, parts in order.
    /// </summary>
    /// <param name="parts">Each part's positions (xyz per vertex) and faces.</param>
    /// <returns>The summary.</returns>
    internal static JsonObject Summary(IEnumerable<(float[] Positions, int Faces)> parts)
    {
        var list = parts.ToList();
        double[] min = [double.MaxValue, double.MaxValue, double.MaxValue], max = [double.MinValue, double.MinValue, double.MinValue];
        var text = new StringBuilder();
        foreach ((float[] p, _) in list)
        {
            for (int i = 0; i < p.Length; i++)
            {
                min[i % 3] = Math.Min(min[i % 3], p[i]);
                max[i % 3] = Math.Max(max[i % 3], p[i]);
                text.Append(CultureInfo.InvariantCulture, $"{(long)Math.Round(p[i] * 1e4, MidpointRounding.ToEven)},");
            }

            text.Append('|');
        }

        return new JsonObject
        {
            ["faces"] = list.Sum(x => x.Faces),
            ["min"] = new JsonArray([.. min.Select(v => (JsonNode)Math.Round(v, 4))]),
            ["max"] = new JsonArray([.. max.Select(v => (JsonNode)Math.Round(v, 4))]),
            ["hash"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))),
        };
    }

    // The repository root: the first folder above the test assembly that holds PropGenerator.slnx.
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PropGenerator.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"No PropGenerator.slnx above {AppContext.BaseDirectory}");
    }
}
