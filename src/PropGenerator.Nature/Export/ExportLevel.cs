// Ported from easy-props frontend/src/core/export/pipeline/package.ts (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Export;

/// <summary>One detail level of an export, as <see cref="Package.Levels"/> builds it before any file is written.</summary>
/// <param name="Level">0 for full detail, then 1 and 2.</param>
/// <param name="Merged">The prop's merged meshes at this level.</param>
/// <param name="Joined">The merged meshes made one, when the export joins; null otherwise.</param>
/// <param name="Wind">The wind extent every level is written with (measured once, at level 0); null for none.</param>
/// <param name="Shapes">The collision shapes, chosen once, at level 0; null on the other levels and without collision.</param>
public sealed record ExportLevel(
    int Level,
    IReadOnlyList<MergedMesh> Merged,
    IReadOnlyList<JoinedMesh>? Joined,
    WindExtent? Wind,
    IReadOnlyList<CollisionShape>? Shapes)
{
    /// <summary>The triangles in this level's file.</summary>
    public int Faces => Joined is null ? Merged.Sum(mesh => mesh.Faces) : Joined.Sum(mesh => mesh.Faces);
}
