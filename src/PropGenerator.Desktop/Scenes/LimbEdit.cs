// Ported from easy-props frontend/src/assets/trees/painted/edits.ts (LimbEdit) (MIT, © 2026 AHS12).
using PropGenerator.Nature.Rocks;

namespace PropGenerator.Desktop.Scenes;

/// <summary>A hand edit to one limb. Each field is null when it was not edited.</summary>
/// <param name="Offsets">How far each control point has been dragged, in metres.</param>
/// <param name="LengthScale">Its length against what the tree gave it.</param>
/// <param name="ThicknessScale">Its thickness against what the tree gave it.</param>
/// <param name="SizeScale">How big it is, with everything that grows on it.</param>
/// <param name="Turn">How far it is turned around its base, in degrees about the world's X, Y and Z.</param>
/// <param name="Removed">True when it was removed.</param>
public sealed record LimbEdit(
    IReadOnlyList<Vec3>? Offsets = null,
    double? LengthScale = null,
    double? ThicknessScale = null,
    double? SizeScale = null,
    Vec3? Turn = null,
    bool Removed = false)
{
    /// <summary>An untouched limb.</summary>
    public static readonly LimbEdit None = new();
}
