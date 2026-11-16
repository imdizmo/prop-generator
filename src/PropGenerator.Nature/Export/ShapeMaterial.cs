// Ported from easy-props frontend/src/core/export/pipeline/package.ts (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Export;

/// <summary>
/// The one material of a collision file, named <c>{name}_collision</c>: three's <c>MeshStandardMaterial</c> with only a
/// name, so white, roughness 1, metalness 0 and no texture.
/// </summary>
/// <param name="Name">The material's name.</param>
public sealed record ShapeMaterial(string Name) : GlbMaterial(Name);
