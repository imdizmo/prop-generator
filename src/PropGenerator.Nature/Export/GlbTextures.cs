// Ported from easy-props frontend/src/core/export/pipeline/merge.ts (whiteTexture) (MIT, © 2026 AHS12).
using PropGenerator.Nature.Textures;
using SharpGLTF.Schema2;

namespace PropGenerator.Nature.Export;

/// <summary>
/// The textures a GLB written here can carry, by the key a merged material names them with: only <c>white</c>, three's
/// plain <c>DataTexture</c> (clamped, nearest), which cliffs and stylized rocks use. easy-props' kinds write their GLBs
/// on the engine.
/// </summary>
internal static class GlbTextures
{
    /// <summary>A texture's pixels and its sampler settings.</summary>
    public static (RgbaImage Image, TextureWrapMode Wrap, TextureMipMapFilter Min, TextureInterpolationFilter Mag) Resolve(
        string key,
        TextureCache cache)
    {
        return key == "white"
            ? (cache.Get(key, WhiteTexture.Build), TextureWrapMode.CLAMP_TO_EDGE, TextureMipMapFilter.NEAREST, TextureInterpolationFilter.NEAREST)
            : throw new ArgumentException($"No texture is known by the key '{key}'", nameof(key));
    }
}
