// Ported from easy-props frontend/src/core/export/pipeline/merge.ts (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Export;

/// <summary>
/// The material mergeParts gives a merged mesh. Fixed for every mesh: base colour white, vertex colours on, metalness 0,
/// normal scale (1, -1) (the relief is stored with its green already flipped, see <see cref="ReliefFull"/>).
/// </summary>
/// <param name="Name">The mesh's name, which the material shares.</param>
/// <param name="Roughness">1 for a part that paints itself, else 0.85.</param>
/// <param name="MapKey">The colour texture's key, or <c>white</c> (a plain white texture, so Unreal keeps vertex colours).</param>
/// <param name="ReliefKey">The relief's key, <c>relief:&lt;pattern&gt;</c>, or null for none.</param>
/// <param name="ReliefFull">
/// True when the relief is written full size (level 0), false for half size; either way with its green inverted, as
/// easy-props' <c>reliefForFile</c> makes it. Meaningless without <see cref="ReliefKey"/>.
/// </param>
/// <param name="AlphaTest">Where the map's alpha cuts the outline; 0 for none.</param>
public sealed record MergedMaterial(string Name, double Roughness, string MapKey, string? ReliefKey, bool ReliefFull, double AlphaTest)
    : GlbMaterial(Name)
{
    /// <summary>Metalness: always 0.</summary>
    public const double Metalness = 0;

    /// <summary>The normal map's scale: (1, -1), which tells GLTFExporter the relief's green is already flipped.</summary>
    public static IReadOnlyList<double> NormalScale { get; } = [1, -1];
}
