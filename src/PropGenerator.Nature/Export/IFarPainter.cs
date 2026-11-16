// Ported from easy-props frontend/src/core/export/pipeline/far.ts (FarPainter) (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Export;

/// <summary>Draws the far-away card's views. In the app that takes the GPU; tests paint a fixed pattern.</summary>
public interface IFarPainter
{
    /// <summary>Draws the four views of merged full-detail meshes, framed by <paramref name="frame"/>.</summary>
    /// <param name="meshes">The meshes, standing on the pivot.</param>
    /// <param name="frame">The box every view is framed by.</param>
    /// <param name="viewSize">Pixels across each view.</param>
    /// <returns>The pictures, or null when it can't draw, in which case there is no card.</returns>
    FarPictures? Paint(IReadOnlyList<MergedMesh> meshes, FarFrame frame, int viewSize);
}
