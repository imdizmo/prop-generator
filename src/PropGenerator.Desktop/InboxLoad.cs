namespace PropGenerator.Desktop;

/// <summary>A mesh sent from Blender was read.</summary>
/// <param name="Name">The file's name without .glb.</param>
/// <param name="Result">The mesh, its piece and any warning.</param>
public sealed record InboxLoad(string Name, GlbImport.Result Result);
