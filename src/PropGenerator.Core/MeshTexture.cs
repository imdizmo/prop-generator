namespace PropGenerator;

/// <summary>
/// A picture some of a <see cref="ColouredMesh"/>'s triangles are printed with, such as a bush's leaf atlas: the
/// vertex colour is multiplied by it, and its alpha cuts the outline out.
/// </summary>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
/// <param name="Rgba">sRGB colour and linear alpha, four bytes a pixel, the row at v = 0 first (as three's DataTexture).</param>
/// <param name="AlphaCut">Where the alpha cuts: a point whose alpha is below it is not drawn (three's alphaTest).</param>
public sealed record MeshTexture(int Width, int Height, byte[] Rgba, float AlphaCut);
