// Ported from easy-props frontend/src/assets/trees/painted/edits.ts (ClumpEdit) (MIT, © 2026 AHS12).
using PropGenerator.Nature.Rocks;

namespace PropGenerator.Desktop.Scenes;

/// <summary>A hand edit to one clump of leaves. Each field is null when it was not edited.</summary>
/// <param name="Offset">How far it was moved.</param>
/// <param name="SizeScale">Its size against the rest of the tree.</param>
/// <param name="FlattenScale">Below 1 squashes it, above 1 stretches it taller.</param>
/// <param name="FullnessScale">How many leaves cover it.</param>
/// <param name="LeafSizeScale">How big its patches of leaves are.</param>
/// <param name="LumpinessScale">How broken up it is.</param>
/// <param name="Colour">Its own colour, <c>#rrggbb</c>.</param>
/// <param name="Removed">True when it was removed.</param>
public sealed record ClumpEdit(
    Vec3? Offset = null,
    double? SizeScale = null,
    double? FlattenScale = null,
    double? FullnessScale = null,
    double? LeafSizeScale = null,
    double? LumpinessScale = null,
    string? Colour = null,
    bool Removed = false)
{
    /// <summary>An untouched clump.</summary>
    public static readonly ClumpEdit None = new();
}
