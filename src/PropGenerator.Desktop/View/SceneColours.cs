// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Numerics;

namespace PropGenerator.Desktop.View;

/// <summary>
/// The colours of a scene around the piece, sRGB-encoded from 0 to 1 as easy-props writes them in hex: the sky is drawn
/// and the fog mixed in exactly these values; the ground is turned to linear before it is lit.
/// </summary>
/// <param name="SkyTop">The sky at the top of the view.</param>
/// <param name="SkyHorizon">The sky at the horizon, 62 % of the way down the view, and below it.</param>
/// <param name="Fog">The colour far things fade into: the horizon's.</param>
/// <param name="Ground">The ground disc's colour.</param>
/// <param name="Bright">True for Studio with sky: a brighter sky light and a whiter sun.</param>
internal readonly record struct SceneColours(Vector3 SkyTop, Vector3 SkyHorizon, Vector3 Fog, Vector3 Ground, bool Bright);
