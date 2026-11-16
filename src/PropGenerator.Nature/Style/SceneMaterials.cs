// Ported from easy-props frontend/src/core/scene/scene-model.ts (SceneMaterials) (MIT, © 2026 AHS12).

namespace PropGenerator.Nature.Style;

/// <summary>
/// The colours a prop is painted with: only two to choose from, as <c>#rrggbb</c> text. The darker leaf tone and the
/// lighter bark follow from them.
/// </summary>
/// <param name="Bark">The bark colour.</param>
/// <param name="Leaves">The leaf colour.</param>
public sealed record SceneMaterials(string Bark, string Leaves)
{
    /// <summary>The colours a new prop starts with (easy-props' <c>DEFAULT_MATERIALS</c>).</summary>
    public static SceneMaterials Default { get; } = new(Materials.BarkColour, Materials.LeavesColour);
}
