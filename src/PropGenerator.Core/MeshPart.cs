namespace PropGenerator;

/// <summary>One part of a prop in a <see cref="ColouredMesh"/>: its id and its run of triangles.</summary>
/// <param name="Id">The part's id in the prop, such as <c>cmp-bush-leaves</c>.</param>
/// <param name="FirstTriangle">Its first triangle.</param>
/// <param name="Triangles">How many triangles it has.</param>
public sealed record MeshPart(string Id, int FirstTriangle, int Triangles);
