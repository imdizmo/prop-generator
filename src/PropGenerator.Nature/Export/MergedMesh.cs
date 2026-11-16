// Ported from easy-props frontend/src/core/export/pipeline/merge.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Export;

/// <summary>One mesh of a merged prop: every part of one bucket, placed, painted and moved to the pivot.</summary>
/// <param name="Name">The mesh's name, such as <c>oak_wood</c>, <c>oak_leaves</c> or <c>boulder_rock</c> (<c>_2</c> added for a second of either).</param>
/// <param name="Mesh">The geometry, in the prop's space with the pivot at the origin.</param>
/// <param name="Material">Its material.</param>
/// <param name="Faces">Its triangles: the index's length over 3.</param>
public sealed record MergedMesh(string Name, PropMesh Mesh, MergedMaterial Material, int Faces)
{
    /// <summary>
    /// The attributes in three's order, which GLTFExporter writes: position, color, normal, uv, and uv1 when the mesh
    /// carries wind.
    /// </summary>
    public IReadOnlyList<string> AttributeOrder => Merge.AttributeOrderOf(Mesh);
}
