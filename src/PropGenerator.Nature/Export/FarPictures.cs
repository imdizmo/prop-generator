// Ported from easy-props frontend/src/core/export/pipeline/far.ts (FarPictures) (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Export;

/// <summary>Two square images for the far-away card, each four views in a 2 × 2 grid, rows from the bottom.</summary>
/// <param name="Size">Pixels along each edge of each image.</param>
/// <param name="Colour">sRGB colour, with the see-through amount in alpha.</param>
/// <param name="Relief">Which way the surface faces, as a relief image reads it, in the viewport's convention.</param>
public sealed record FarPictures(int Size, byte[] Colour, byte[] Relief);
