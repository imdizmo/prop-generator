// Ported from easy-props frontend/src/core/export/pipeline/far.ts (buildFarCard's material) (MIT, © 2026 AHS12).
using PropGenerator.Nature.Textures;

namespace PropGenerator.Nature.Export;

/// <summary>The far-away card's material as a file wants it: its picture, its relief for the file, cut out at 0.5.</summary>
/// <param name="Name">The material's name.</param>
/// <param name="Map">The colour picture (sRGB), named after the card.</param>
/// <param name="Relief">The relief, green flipped for the file.</param>
public sealed record FarMaterial(string Name, RgbaImage Map, RgbaImage Relief) : GlbMaterial(Name)
{
    /// <summary>Where an engine cuts the picture out, as glTF's alpha cut-off.</summary>
    public const double AlphaTest = FarCard.Cutoff;
}
