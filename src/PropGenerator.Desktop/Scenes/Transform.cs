// Ported from easy-props frontend/src/assets/shared/component-types.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Desktop.Scenes;

/// <summary>Where a component sits in its prop: a position, an XYZ Euler rotation in radians, and a scale.</summary>
public sealed record Transform(Vec3d Position, Vec3d Rotation, Vec3d Scale)
{
    /// <summary>A new identity transform (easy-props' <c>IDENTITY_TRANSFORM</c>): at the origin, unturned, scale 1.</summary>
    public static Transform Identity() => new(new Vec3d(0, 0, 0), new Vec3d(0, 0, 0), new Vec3d(1, 1, 1));
}
