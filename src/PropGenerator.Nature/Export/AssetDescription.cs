// Ported from easy-props frontend/src/core/export/scene-source.ts (describe) (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Export;

/// <summary>Everything needed to make a prop again, as its <c>asset.json</c> lists it after the export's own fields.</summary>
/// <param name="Generator">The prop generator's id; null for an empty scene.</param>
/// <param name="GeneratorVersion">That generator's version; null when it is not known here.</param>
/// <param name="Seed">The prop's seed; null for an empty scene.</param>
/// <param name="Style">The style's id.</param>
/// <param name="Parameters">
/// The prop's own parameters as the scene holds them: a JSON-like value (see <see cref="Js.JsJson"/>), usually a
/// <see cref="Parameters.ParameterDictionary"/>.
/// </param>
/// <param name="AppVersion">The app version that wrote it.</param>
public sealed record AssetDescription(
    string? Generator,
    int? GeneratorVersion,
    double? Seed,
    string Style,
    object Parameters,
    string AppVersion);
