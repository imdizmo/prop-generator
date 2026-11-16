// Ported from easy-props frontend/src/core/export/pipeline/package.ts (asset.json) (MIT, © 2026 AHS12).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Export;

/// <summary>A prop's <c>asset.json</c>, which an engine's setup script reads beside its GLB files.</summary>
public static class AssetJson
{
    /// <summary>The asset format's version.</summary>
    public const int FormatVersion = 1;

    /// <summary>
    /// The file's text, byte for byte as easy-props writes it (<c>JSON.stringify(asset, null, 4)</c> and a newline):
    /// format, name, engine, faces per level (<c>LOD0</c> up), the wind extent or null, then the description's fields.
    /// </summary>
    public static string Write(string name, string engine, string look, IReadOnlyList<int> faces, WindExtent? wind, AssetDescription description, FarCardData? far = null)
    {
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(description);
        List<KeyValuePair<string, object?>> asset =
        [
            new("format", FormatVersion),
            new("name", name),
            new("engine", engine),
            new("look", look),
            // The card's faces come first: easy-props counts them as it draws the card, before level 0's.
            new("faces", (far is null ? [] : new List<KeyValuePair<string, object?>> { new("far", FarCard.Faces) })
                .Concat(faces.Select((count, level) => new KeyValuePair<string, object?>($"LOD{level}", count)))
                .ToList()),
            new("wind", wind is null
                ? null
                : new List<KeyValuePair<string, object?>> { new("height", wind.Height), new("reach", wind.Reach) }),
            new("far", far is null
                ? null
                : new List<KeyValuePair<string, object?>>
                {
                    new("width", Metres(far.Frame.Reach * 2)),
                    new("height", Metres(far.Frame.Top - far.Frame.Bottom)),
                    new("pictureSize", far.Pictures.Size),
                }),
            new("generator", description.Generator),
            new("generatorVersion", description.GeneratorVersion),
            new("seed", description.Seed),
            new("style", description.Style),
            new("parameters", description.Parameters),
            new("appVersion", description.AppVersion),
        ];
        return JsJson.Stringify(asset, 4) + "\n";
    }

    // Metres to the millimetre, as easy-props rounds the card's size.
    private static double Metres(double metres) => JsMath.Round(metres * 1000) / 1000;
}
