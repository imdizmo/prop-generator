// Ported from easy-props frontend/src/core/export/pipeline/wind.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Js;
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Export;

/// <summary>
/// How much each vertex may move, for a material in the engine to use (uv1: sway in u, flutter in v). Both depend only
/// on where a vertex is, measured against the full-detail prop, so every level moves alike and a leaf stays on its twig.
/// </summary>
public static class Wind
{
    /// <summary>How the height of a vertex turns into sway: slow low down, faster near the top.</summary>
    public const double SwayPower = 1.5;

    /// <summary>The share of sway every height has, the rest coming with distance from the trunk.</summary>
    public const double SwayFromHeight = 0.6;

    /// <summary>Leaves always flutter this much, and more towards the outside.</summary>
    public const double FlutterBase = 0.5;

    /// <summary>A thin part flutters a little, whatever it is.</summary>
    public const double FlutterThin = 0.25;

    /// <summary>Further than any prop, so being far out adds nothing and a patch's rim moves as its middle does.</summary>
    public const double EvenReach = 1e6;

    /// <summary>Sway and flutter at a point given relative to the pivot, for a part of <paramref name="role"/>.</summary>
    public static (double Sway, double Flutter) Weight(double x, double y, double z, string role, WindExtent extent)
    {
        ArgumentNullException.ThrowIfNull(extent);
        double outward = Clamp01(JsMath.Hypot(x, z) / extent.Reach);
        double sway = JsMath.Pow(Clamp01(y / extent.Height), SwayPower) * (SwayFromHeight + ((1 - SwayFromHeight) * outward));
        double flutter = role == "foliage" ? FlutterBase + ((1 - FlutterBase) * outward) : role == "thin" ? FlutterThin : 0;
        return (sway, flutter);
    }

    /// <summary>
    /// easy-props' <c>measureWindExtent</c>: the prop's height above the pivot and its reach from it across the ground,
    /// over every vertex of every part but proxies, through each part's matrix; never below 0.001.
    /// </summary>
    public static WindExtent MeasureExtent(IReadOnlyList<GeneratedPart> parts, Vec3d pivot)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(pivot);
        var point = new Vec3d();
        var matrix = new Mat4d();
        double height = 0;
        double reach = 0;

        foreach (GeneratedPart part in parts)
        {
            if (part.IsProxy)
            {
                continue;
            }

            matrix.FromArray(part.Matrix.Elements);
            float[] position = part.Mesh.Position;
            for (int i = 0; i < position.Length / 3; i++)
            {
                point.FromBufferAttribute(position, i).ApplyMatrix4(matrix);
                height = Math.Max(height, point.Y - pivot.Y);
                reach = Math.Max(reach, JsMath.Hypot(point.X - pivot.X, point.Z - pivot.Z));
            }
        }

        // Never zero, so a flat or single-point prop still divides cleanly.
        return new WindExtent(Math.Max(height, 1e-3), Math.Max(reach, 1e-3));
    }

    /// <summary>
    /// The extent an export uses (easy-props' <c>buildExport</c>): none for a prop that does not sway or when wind is off;
    /// otherwise measured from the full-detail parts, with the reach made <see cref="EvenReach"/> for a prop that sways
    /// evenly.
    /// </summary>
    public static WindExtent? ForExport(IReadOnlyList<GeneratedPart> fullDetail, Vec3d pivot, bool wind, bool sways, bool swaysEvenly)
    {
        if (!wind || !sways)
        {
            return null;
        }

        var extent = MeasureExtent(fullDetail, pivot);
        return swaysEvenly ? extent with { Reach = EvenReach } : extent;
    }

    /// <summary>
    /// easy-props' <c>windAttribute</c>: sway and flutter for every vertex of a mesh already placed in the prop's space
    /// (before the pivot is taken off), two floats each. A point marked rigid sways but never flutters.
    /// </summary>
    /// <param name="placed">The mesh, placed in the prop's space.</param>
    /// <param name="part">The part it came from.</param>
    /// <param name="pivot">The prop's pivot.</param>
    /// <param name="extent">The wind extent measured at full detail.</param>
    /// <param name="rigid">The part's rigid attribute (one float per vertex), or null.</param>
    public static float[] Uv1(PropMesh placed, GeneratedPart part, Vec3d pivot, WindExtent extent, float[]? rigid = null)
    {
        ArgumentNullException.ThrowIfNull(placed);
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(pivot);
        float[] position = placed.Position;
        string role = part.RoleOfPart;
        float[] values = new float[placed.VertexCount * 2];

        for (int i = 0; i < placed.VertexCount; i++)
        {
            var (sway, flutter) = Weight(
                position[i * 3] - pivot.X,
                position[(i * 3) + 1] - pivot.Y,
                position[(i * 3) + 2] - pivot.Z,
                role,
                extent);
            values[i * 2] = (float)sway;
            values[(i * 2) + 1] = (float)(rigid is null ? flutter : flutter * (1 - rigid[i]));
        }

        return values;
    }

    /// <summary>JavaScript's <c>Math.min(1, Math.max(0, value))</c>; NaN stays NaN.</summary>
    private static double Clamp01(double value) => Math.Min(1, Math.Max(0, value));
}
