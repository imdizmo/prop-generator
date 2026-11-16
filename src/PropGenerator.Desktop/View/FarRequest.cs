using PropGenerator.Nature.Export;

namespace PropGenerator.Desktop.View;

/// <summary>A far-away card's pictures asked of the view: the prop, the card's box, and where the pictures go.</summary>
/// <param name="Mesh">The prop at full detail, Z-up.</param>
/// <param name="Frame">The card's box.</param>
/// <param name="Done">Given the pictures, or null when they cannot be drawn.</param>
internal sealed record FarRequest(ColouredMesh Mesh, FarFrame Frame, TaskCompletionSource<FarPictures?> Done);
