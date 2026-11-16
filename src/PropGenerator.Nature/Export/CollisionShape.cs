// Ported from easy-props frontend/src/assets/shared/generator-types.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Export;

/// <summary>
/// A simple solid for an engine to collide with: two parallel rings, in the prop's own space. Each ring is a circle (an
/// eight-sided ring, see <see cref="Collision.Prism"/>), or, given <paramref name="Ring"/>, the same convex polygon at
/// both ends (a box, for a cliff's blocks).
/// </summary>
/// <param name="Start">The centre of the first ring (easy-props' <c>start</c>).</param>
/// <param name="End">The centre of the second ring (<c>end</c>).</param>
/// <param name="R0">The first ring's radius (<c>startRadius</c>); for a polygon ring, the distance to its furthest corner.</param>
/// <param name="R1">The second ring's radius (<c>endRadius</c>); for a polygon ring, the distance to its furthest corner.</param>
/// <param name="Ring">
/// The corners of a polygon ring around each centre, as offsets from it, turning the way the round ring's corners turn
/// (for an upright shape, from -z toward -x), so its faces point out; null for the round ring.
/// </param>
public sealed record CollisionShape(Vec3d Start, Vec3d End, double R0, double R1, IReadOnlyList<Vec3d>? Ring = null);
