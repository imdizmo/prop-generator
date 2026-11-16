// Ported from easy-props frontend/src/core/export/glb-exporter.ts (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Export;

/// <summary>
/// A material a GLB node is drawn with: a merged mesh's (<see cref="MergedMaterial"/>) or a collision file's own
/// (<see cref="ShapeMaterial"/>). A file writes each instance once, however many nodes share it, as GLTFExporter does
/// for one three material.
/// </summary>
/// <param name="Name">The material's name.</param>
public abstract record GlbMaterial(string Name);
