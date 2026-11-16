using System.Text.Json;
using PropGenerator.Engine;

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>
/// What engine-pin.json says the embedded bundle was built from (tools/engine/build.mjs writes it with the bundle), so
/// tests that check the engine names its release follow a rebuilt bundle instead of pinning one release.
/// </summary>
internal static class EnginePin
{
    private static readonly Lazy<(string Tag, string Commit, int Thumbnails)> Pin = new(() =>
    {
        using var doc = JsonDocument.Parse(EngineOptions.ReadResource("engine-pin.json")!);
        JsonElement root = doc.RootElement;
        return (root.GetProperty("tag").GetString()!, root.GetProperty("commit").GetString()!, root.GetProperty("thumbnails").GetInt32());
    });

    /// <summary>The release's tag, such as "v0.4.7".</summary>
    public static string Tag => Pin.Value.Tag;

    /// <summary>The full commit the tag pointed at.</summary>
    public static string Commit => Pin.Value.Commit;

    /// <summary>How many preset pictures the build copied: one per preset upstream ships.</summary>
    public static int Thumbnails => Pin.Value.Thumbnails;
}
