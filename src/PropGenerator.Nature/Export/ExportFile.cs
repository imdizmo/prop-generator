// Ported from easy-props frontend/src/core/export/pipeline/types.ts (ExportFile) (MIT, © 2026 AHS12).
using System.Diagnostics.CodeAnalysis;

namespace PropGenerator.Nature.Export;

/// <summary>One file of an export, not yet written anywhere.</summary>
/// <param name="Name">Its file name, such as <c>oak_LOD0.glb</c> or <c>oak.asset.json</c>.</param>
/// <param name="Bytes">Its contents.</param>
/// <param name="Faces">The triangles in a detail level's file; null for a file of another kind.</param>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The bytes are handed to the file writer as they are.")]
public sealed record ExportFile(string Name, byte[] Bytes, int? Faces);
