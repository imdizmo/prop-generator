// Ported from easy-props frontend/src/core/scene/resizing.ts and turning.ts (canTurn) (MIT, © 2026 AHS12).
using PropGenerator.Nature.Js;

namespace PropGenerator.Desktop.Scenes;

/// <summary>
/// Resizing a branch, root or leaf clump of a painted tree. A branch or root grows and shrinks around its base, as it
/// turns, so it stays joined to what it grows from, and what grows on it follows; a clump around its middle.
/// </summary>
public static class PartResizing
{
    // The painted tree's part generators (easy-props' painted-limb and painted-clump).
    private const string LimbId = "painted-limb";
    private const string ClumpId = "painted-clump";

    /// <summary>A limb's Size control's range (easy-props' LIMB_SPECS).</summary>
    public static readonly (double Min, double Max) LimbSize = (0.25, 3);

    /// <summary>A clump's Size control's range (easy-props' CLUMP_SPECS).</summary>
    public static readonly (double Min, double Max) ClumpSize = (0.3, 2.5);

    /// <summary>Whether a part can be turned: a painted branch or root, never the trunk.</summary>
    /// <param name="part">The part, or null.</param>
    /// <returns>True when it turns.</returns>
    public static bool CanTurn(SceneComponent? part) =>
        part?.GeneratorId == LimbId && !(part.Parameters.TryGetValue("key", out object? key) && key is "t");

    /// <summary>Whether a part can be resized: a painted branch, root or leaf clump, never the trunk.</summary>
    /// <param name="part">The part, or null.</param>
    /// <returns>True when it resizes.</returns>
    public static bool CanResize(SceneComponent? part) => part?.GeneratorId == ClumpId || CanTurn(part);

    /// <summary>A part's size, 1 as the tree grew it.</summary>
    /// <param name="part">The part.</param>
    /// <returns>Its size.</returns>
    public static double SizeOf(SceneComponent? part) =>
        part is not null && part.Parameters.TryGetValue("sizeScale", out object? size) && size is double d && double.IsFinite(d) ? d : 1;

    /// <summary>The smallest and largest a part can be, from its Size control; 1 and 1 for a part without one.</summary>
    /// <param name="generatorId">The part's generator.</param>
    /// <returns>The limits.</returns>
    public static (double Min, double Max) SizeLimits(string? generatorId) => generatorId switch
    {
        ClumpId => ClumpSize,
        LimbId => LimbSize,
        _ => (1, 1),
    };

    /// <summary>
    /// The size a drag asks for: the starting size times how far the pointer has moved out from the part's middle,
    /// within the part's limits, snapped to a tenth with Shift and to a hundredth otherwise.
    /// </summary>
    /// <param name="start">The size when the drag began.</param>
    /// <param name="ratio">How far out the pointer is against where it started.</param>
    /// <param name="limits">The part's limits.</param>
    /// <param name="snap">True to snap to a tenth.</param>
    /// <returns>The size.</returns>
    public static double ResizedBy(double start, double ratio, (double Min, double Max) limits, bool snap)
    {
        double step = snap ? 0.1 : 0.01;
        double size = JsMath.Round(start * Math.Max(ratio, 0) / step) * step;

        return JsMath.Round(Math.Min(Math.Max(size, limits.Min), limits.Max) * 100) / 100;
    }
}
