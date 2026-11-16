// Ported from easy-props frontend/src/core/export/pipeline/types.ts (ExportSource) (MIT, © 2026 AHS12).
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Export;

/// <summary>
/// What the export pipeline asks of a prop (easy-props' <c>ExportSource</c>). Nothing here knows what a tree is: a
/// generator hands over parts, tagged with what they are, and the pipeline does the same to all of them.
/// </summary>
/// <param name="BuildAtDetail">The prop's parts at a share of its full detail (1, 0.4 or 0.12), each placed in the prop.</param>
/// <param name="Collision">Simple solid shapes, in the prop's own space; empty lets the pipeline make its own.</param>
/// <param name="Pivot">Where the base of the prop meets the ground.</param>
/// <param name="Sways">Whether the prop moves in the wind. A rock does not.</param>
/// <param name="SwaysEvenly">True when every part of it moves about alike, as across a patch of grass.</param>
/// <param name="Description">Everything needed to make the prop again, for its <c>asset.json</c>.</param>
/// <param name="MaxCollisionShapes">The most collision shapes the export keeps: <see cref="Export.Collision.MaxShapes"/> unless the prop allows more.</param>
/// <param name="SolidName">
/// What the prop's solid (not foliage) mesh and material are named after: <c>{name}_{SolidName}</c>. "wood" (a tree's,
/// as easy-props names it) unless the prop says what it is made of, such as "rock" or "cliff".
/// </param>
public sealed record ExportSource(
    Func<double, IReadOnlyList<GeneratedPart>> BuildAtDetail,
    IReadOnlyList<CollisionShape> Collision,
    Vec3d Pivot,
    bool Sways,
    bool SwaysEvenly,
    AssetDescription Description,
    int MaxCollisionShapes = Export.Collision.MaxShapes,
    string SolidName = Merge.Wood)
{
    /// <summary>Gets a value indicating whether export offers a far-away card for it: props that grow upwards and are seen from far off.</summary>
    public bool FarAway { get; init; }
}
