namespace PropGenerator;

/// <summary>A triangle mesh with a colour on every vertex, as the 3D view draws a rock.</summary>
/// <param name="Positions">Vertex positions, three floats each, Z-up, in metres.</param>
/// <param name="Normals">Unit vertex normals, three floats each, one per vertex.</param>
/// <param name="Colours">Linear RGB vertex colours, three floats each, one per vertex.</param>
/// <param name="Indices">Triangle corners as vertex numbers, three per triangle, counter-clockwise seen from outside.</param>
public sealed record ColouredMesh(float[] Positions, float[] Normals, float[] Colours, int[] Indices)
{
    /// <summary>
    /// Gets where each vertex reads its picture: u, v and a third number, 1 for a vertex of a printed (cut-out) triangle
    /// that reads <see cref="Texture"/>, 2 for one of bark that reads <see cref="Bark"/> and <see cref="Relief"/>, and 0
    /// for one drawn in its colour alone. Null when nothing is printed (a rock, grass).
    /// </summary>
    public float[]? TexCoords { get; init; }

    /// <summary>Gets the picture the printed triangles read; null when nothing is printed.</summary>
    public MeshTexture? Texture { get; init; }

    /// <summary>Gets the brush strokes the bark is printed with (repeating, multiplied over its colour); null when there is no printed bark.</summary>
    public MeshTexture? Bark { get; init; }

    /// <summary>Gets the bark's relief, a tangent-space normal map (linear, repeating) read where <see cref="Bark"/> is; null with it.</summary>
    public MeshTexture? Relief { get; init; }

    /// <summary>Gets how a new piece of it is framed; null to frame it by its bounds.</summary>
    public PieceFrame? Frame { get; init; }

    /// <summary>
    /// Gets how much each vertex flutters in the wind on top of swaying, 0 to 1: 1 on a tree's leaves, 0 on its wood and
    /// on fruit, which swings whole. Null when the whole prop flutters alike or not at all (see the recipe's wind).
    /// </summary>
    public float[]? Flutter { get; init; }

    /// <summary>
    /// Gets how rough each vertex's surface is, 0 (a mirror) to 1 (matt): below 1 where a wet part shines. Null when the
    /// whole mesh is fully rough, as every dry prop is.
    /// </summary>
    public float[]? Roughness { get; init; }

    /// <summary>
    /// Gets which triangles make each part of the prop, in order, for picking a part in the view; null for a mesh not
    /// made of parts (the floor, a stylized rock).
    /// </summary>
    public IReadOnlyList<MeshPart>? Parts { get; init; }

    /// <summary>
    /// Gets the parts that are never drawn but are what the pointer lands on (a painted tree's limbs, each a plain tube
    /// just outside its bark), as a mesh of their own with its <see cref="Parts"/>, in the same space; null when the
    /// prop has none.
    /// </summary>
    public ColouredMesh? Proxies { get; init; }
}
