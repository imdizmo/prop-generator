// Ported from easy-props frontend/src/assets/shared/generator-types.ts (PaintedSurface) (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Export;

/// <summary>
/// How a part that paints itself is drawn: it keeps its colour per vertex and its own shading normals, and may cut its
/// outline out of a texture. The optional fields are null where easy-props leaves them out.
/// </summary>
/// <param name="VertexColors">Whether the part's vertex colours are used.</param>
/// <param name="MapKey">The colour texture's key (<c>dab</c>, <c>leaf</c> or <c>bark:&lt;pattern&gt;:&lt;strength&gt;</c>); null for none.</param>
/// <param name="ReliefKey">The relief (normal map) texture's key, <c>relief:&lt;pattern&gt;</c>; null for none. It shares the map's coordinates.</param>
/// <param name="AlphaTest">Where the map's alpha cuts the outline (0.5 for leaves); null when left out.</param>
/// <param name="ReceiveShadow">False for leaves, where a shadow map would only speckle soft shading; null when left out.</param>
/// <param name="Proxy">True for a part that draws nothing and is never exported, only clicked; null when left out.</param>
public sealed record PaintedSurface(
    bool VertexColors,
    string? MapKey = null,
    string? ReliefKey = null,
    double? AlphaTest = null,
    bool? ReceiveShadow = null,
    bool? Proxy = null);
