// Ported from easy-props frontend/src/core/export/pipeline/wind.ts (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Export;

/// <summary>What sway is measured against, in metres from the pivot.</summary>
/// <param name="Height">The prop's height above the pivot, at least 0.001.</param>
/// <param name="Reach">The furthest a vertex reaches from the pivot across the ground, at least 0.001.</param>
public sealed record WindExtent(double Height, double Reach);
