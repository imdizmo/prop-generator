// Ported from easy-props frontend/src/core/export/pipeline/far.ts (FarFrame) (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Export;

/// <summary>The box every view of the far-away card is framed by, in metres from the pivot.</summary>
/// <param name="Reach">Half the card's width: the prop's widest reach from its base.</param>
/// <param name="Bottom">The lowest point.</param>
/// <param name="Top">The highest point.</param>
public sealed record FarFrame(double Reach, double Bottom, double Top);
