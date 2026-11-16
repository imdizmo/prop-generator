namespace PropGenerator;

/// <summary>A triangle mesh in the tile's own frame (Z up, as the Blender script builds it).</summary>
/// <param name="Positions">x, y, z for each vertex.</param>
/// <param name="Uvs">u, v for each vertex; v counts up from the bottom row of the image, as Blender does.</param>
/// <param name="Indices">Three vertex numbers for each triangle, counter-clockwise seen from outside.</param>
/// <param name="FaceIds">For each triangle, the number of the face (polygon) it belongs to.</param>
public sealed record Mesh(float[] Positions, float[] Uvs, int[] Indices, int[] FaceIds);
