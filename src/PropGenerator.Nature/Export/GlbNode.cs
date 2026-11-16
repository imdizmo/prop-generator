// Ported from easy-props frontend/src/core/export/glb-exporter.ts (NamedMesh) (MIT, © 2026 AHS12).
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Export;

/// <summary>One mesh of a GLB file, a child of its root (easy-props' <c>NamedMesh</c>); the node and the mesh share its name.</summary>
/// <param name="Name">The node's and the mesh's name.</param>
/// <param name="Mesh">The geometry.</param>
/// <param name="AttributeOrder">The mesh's attributes in three's order, which is the order the file lists them.</param>
/// <param name="Materials">Its material, or with <paramref name="ByGroup"/> one per draw group.</param>
/// <param name="ByGroup">
/// True for a mesh drawn group by group (three's material array): one primitive per draw group. False for one primitive
/// drawing the whole index with the first material.
/// </param>
public sealed record GlbNode(
    string Name,
    PropMesh Mesh,
    IReadOnlyList<string> AttributeOrder,
    IReadOnlyList<GlbMaterial> Materials,
    bool ByGroup)
{
    /// <summary>The attributes of a collision prism: <see cref="Collision.Prism"/> sets positions, then computes normals.</summary>
    public static IReadOnlyList<string> PrismAttributes { get; } = ["position", "normal"];

    /// <summary>A merged mesh, drawn whole with its material.</summary>
    public static GlbNode Of(MergedMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        return new GlbNode(mesh.Name, mesh.Mesh, mesh.AttributeOrder, [mesh.Material], false);
    }

    /// <summary>A joined mesh, drawn group by group, each with its own material.</summary>
    public static GlbNode Of(JoinedMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        return new GlbNode(mesh.Name, mesh.Mesh, mesh.AttributeOrder, [.. mesh.Materials], true);
    }

    /// <summary>A collision prism, named for its engine, drawn with <paramref name="material"/>.</summary>
    public static GlbNode Shape(string name, PropMesh prism, GlbMaterial material) =>
        new(name, prism, PrismAttributes, [material], false);
}
