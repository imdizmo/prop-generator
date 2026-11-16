// Ported from easy-props frontend/src/core/export/pipeline/far.ts (FarCard) (MIT, © 2026 AHS12).
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Export;

/// <summary>A far-away card: its frame, its pictures and its two crossed cards.</summary>
/// <param name="Frame">The box the views are framed by.</param>
/// <param name="Pictures">The pictures as painted.</param>
/// <param name="Geometry">The two crossed cards.</param>
public sealed record FarCardData(FarFrame Frame, FarPictures Pictures, PropMesh Geometry);
