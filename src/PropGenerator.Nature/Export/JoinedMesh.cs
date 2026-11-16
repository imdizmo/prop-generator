// Ported from easy-props frontend/src/core/export/pipeline/merge.ts (JoinedMesh) (MIT, © 2026 AHS12).
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Export;

/// <summary>
/// Merged meshes made one, for an engine that places a mesh many times over: one draw group per former mesh, each with
/// its own material (group <c>n</c> draws with <c>Materials[n]</c>).
/// </summary>
/// <param name="Name">The prop's name.</param>
/// <param name="Mesh">The geometry, with its groups.</param>
/// <param name="Materials">The former meshes' materials, in their order.</param>
/// <param name="Faces">Its triangles: the index's length over 3.</param>
public sealed record JoinedMesh(string Name, PropMesh Mesh, IReadOnlyList<MergedMaterial> Materials, int Faces)
{
    /// <summary>The attributes in three's order, as <see cref="MergedMesh.AttributeOrder"/>.</summary>
    public IReadOnlyList<string> AttributeOrder => Merge.AttributeOrderOf(Mesh);
}
