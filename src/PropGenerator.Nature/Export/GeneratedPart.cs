// Ported from easy-props frontend/src/assets/shared/generator-types.ts (GeneratedPart, roleOfPart) and
// frontend/src/core/export/pipeline/types.ts (PlacedPart) (MIT, © 2026 AHS12).
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Export;

/// <summary>
/// The geometry of one component as an export reads it (easy-props' <c>PlacedPart</c> around its <c>GeneratedPart</c>):
/// the mesh in the part's own space, what it is made of, how its surface is drawn, and how it is placed in the prop.
/// </summary>
/// <param name="Mesh">The geometry, in the part's own space.</param>
/// <param name="Material">easy-props' <c>MaterialKind</c>: "bark", "bark-light", "leaves", "leaves-dark", "coconut" or "painted".</param>
/// <param name="Role">"solid", "foliage" or "thin"; null when it follows from the material (see <see cref="RoleOfPart"/>).</param>
/// <param name="Surface">How a part that paints itself is drawn; null for a part that does not.</param>
/// <param name="AttributeOrder">
/// The mesh's attributes in the order three holds them (easy-props builds them in a fixed order per generator); the
/// export writes them in that order.
/// </param>
/// <param name="Matrix">From the part's space to the prop's: its transform, its scale times <paramref name="Growth"/>.</param>
/// <param name="Growth">How much a lighter level grew the part to cover for those it left out; 1 otherwise.</param>
public sealed record GeneratedPart(
    PropMesh Mesh,
    string Material,
    string? Role,
    PaintedSurface? Surface,
    IReadOnlyList<string> AttributeOrder,
    Mat4d Matrix,
    double Growth)
{
    /// <summary>
    /// The colour the part's material multiplies it by, linear (easy-props' <c>tint</c>): set when the scene places
    /// the part, from the scene's colours; white until then.
    /// </summary>
    public Color3d Tint { get; init; } = new();

    /// <summary>Whether the style draws this part faceted: set when the scene places the part, from its style.</summary>
    public bool FlatShading { get; init; }

    /// <summary>Gets the id of the component the part was built from: set when the scene places the part; null until then.</summary>
    public string? ComponentId { get; init; }

    /// <summary>True for a part that draws nothing and is never exported.</summary>
    public bool IsProxy => Surface?.Proxy == true;

    /// <summary>easy-props' <c>roleOfPart</c>: its own role, or "foliage" for leaves, dark leaves and coconuts, else "solid".</summary>
    public string RoleOfPart =>
        Role ?? (Material is "leaves" or "leaves-dark" or "coconut" ? "foliage" : "solid");
}
