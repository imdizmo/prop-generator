// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Numerics;

namespace PropGenerator.Desktop.View;

/// <summary>
/// Where the sun's shadow map looks from and what it covers, as easy-props fits it to the piece's bounding sphere: the
/// sun stands max(3 r, 12) m from the sphere's centre towards the sun and looks at the centre through a square box
/// ±max(r, 2) wide, from 0.1 m to past the piece's longest ground shadow (at least easy-props' 6 × half-size). The depth bias is −0.0002 and the normal bias
/// max(0.035, 2 × half-size / 2048 × 8), so a big rock's coarser texels get no acne.
/// </summary>
/// <param name="Eye">Where the sun's camera stands.</param>
/// <param name="Centre">What it looks at: the bounding sphere's centre.</param>
/// <param name="HalfSize">Half the box's width and height, in metres.</param>
/// <param name="Near">The box's near end, in metres from the eye.</param>
/// <param name="Far">The box's far end, in metres from the eye.</param>
/// <param name="Bias">Added to a point's depth before it is compared with the map.</param>
/// <param name="NormalBias">How far a point is moved along its normal, in metres, before it is looked up.</param>
internal readonly record struct ShadowBox(Vector3 Eye, Vector3 Centre, float HalfSize, float Near, float Far, float Bias, float NormalBias)
{
    /// <summary>The shadow map's width and height, in texels.</summary>
    internal const int MapSize = 2048;

    /// <summary>The soft edge's radius, in texels.</summary>
    internal const float Radius = 3;

    /// <summary>Gets the matrix from the world to the sun's clip space (row vectors, as System.Numerics multiplies).</summary>
    internal Matrix4x4 ViewProjection =>
        Matrix4x4.CreateLookAt(Eye, Centre, MathF.Abs(Vector3.Normalize(Centre - Eye).Z) > 0.999f ? Vector3.UnitY : Vector3.UnitZ)
        * Matrix4x4.CreateOrthographicOffCenter(-HalfSize, HalfSize, -HalfSize, HalfSize, Near, Far);

    /// <summary>
    /// Gets the matrix from the world to the shadow map's texture coordinates and depth (each from 0 to 1), as OpenGL
    /// stores them: clip space halved and moved up by a half.
    /// </summary>
    internal Matrix4x4 Lookup => ViewProjection * Matrix4x4.CreateScale(0.5f) * Matrix4x4.CreateTranslation(0.5f, 0.5f, 0.5f);

    /// <summary>
    /// Fits the shadow to a piece's bounding sphere and the sun, on level ground. One change from easy-props: the far end
    /// reaches past the sphere's whole shadow on the ground. easy-props' 6 × half-size ends at the centre of a piece under
    /// about 4.8 m across (its sun stands 12 m away then), and a low sun throws the shadow far past the sphere. The far
    /// end is the sun's distance plus the larger of three half-sizes and the depth of the sphere's ground shadow beyond
    /// its centre: the top of the sphere, at height h, lands h / sin(elevation) along the sun's rays, plus a half-size for
    /// the sphere's width. Seen from the sun the shadow is the sphere's own outline, so the box's width needs nothing more.
    /// </summary>
    /// <param name="centre">The bounding sphere's centre.</param>
    /// <param name="radius">The bounding sphere's radius.</param>
    /// <param name="towardsSun">A unit vector towards the sun.</param>
    /// <returns>The box.</returns>
    internal static ShadowBox For(Vector3 centre, float radius, Vector3 towardsSun) => For(centre, radius, towardsSun, Vector3.UnitZ);

    /// <summary>
    /// Fits the shadow as <see cref="For(Vector3, float, Vector3)"/> does, on ground tilted about the origin to face
    /// <paramref name="groundUp"/> (the view's slope): heights and the sun's elevation are measured from the tilted
    /// ground, so a slope falling away from the sun, where the shadow lands farther down the rays, is still covered. On
    /// level ground (up = Z) the box is the level one.
    /// </summary>
    /// <param name="centre">The bounding sphere's centre.</param>
    /// <param name="radius">The bounding sphere's radius.</param>
    /// <param name="towardsSun">A unit vector towards the sun.</param>
    /// <param name="groundUp">The ground's up, a unit vector (Z on level ground).</param>
    /// <returns>The box.</returns>
    internal static ShadowBox For(Vector3 centre, float radius, Vector3 towardsSun, Vector3 groundUp)
    {
        float half = MathF.Max(radius, 2);
        float distance = MathF.Max(radius * 3, 12);
        float normalBias = MathF.Max(0.035f, half * 2 / MapSize * 8);
        var toSun = Vector3.Normalize(towardsSun);
        var up = Vector3.Normalize(groundUp);
        float top = MathF.Max(Vector3.Dot(centre, up), 0) + radius;
        float groundShadow = (top / MathF.Max(Vector3.Dot(toSun, up), 0.05f)) + half;
        return new ShadowBox(
            centre + (toSun * distance), centre, half, 0.1f, distance + MathF.Max(half * 3, groundShadow), -0.0002f, normalBias);
    }
}
