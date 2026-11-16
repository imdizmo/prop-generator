// Ported from easy-props frontend/src/core/scene/scene-model.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Parameters;
using PropGenerator.Nature.Style;

namespace PropGenerator.Desktop.Scenes;

/// <summary>
/// A prop as the editor holds it: easy-props' scene with its one asset, plus the style it is built in. Its components
/// are the source of truth; geometry is built from them on demand.
/// </summary>
/// <param name="GeneratorId">The prop generator, such as "cut-rock".</param>
/// <param name="Parameters">The prop's own parameters as the scene holds them (a preset's, as given), in their key order.</param>
/// <param name="Seed">The prop's seed.</param>
/// <param name="Style">The style profile's id, "low-poly" or "detailed".</param>
/// <param name="Components">The parts, a flat list; <see cref="SceneComponent.ParentId"/> gives the hierarchy.</param>
/// <param name="Materials">The colours the prop is painted with; a part of each material is tinted by them.</param>
public sealed record Scene(
    string GeneratorId,
    ParameterDictionary Parameters,
    double Seed,
    string Style,
    IReadOnlyList<SceneComponent> Components,
    SceneMaterials Materials);
