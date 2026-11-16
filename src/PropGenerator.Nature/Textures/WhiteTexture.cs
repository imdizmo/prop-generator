// Ported from easy-props frontend/src/core/export/pipeline/merge.ts (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Textures;

/// <summary>
/// A plain white texture, for a mesh with no texture of its own (easy-props' <c>whiteTexture</c>). It changes nothing
/// about how the mesh looks, but Unreal builds a simpler material for a glTF mesh with no texture, and that material
/// ignores vertex colours, which is where all the paint is. The Unreal script skips it by its name, "white".
/// </summary>
public static class WhiteTexture
{
    private const int WhiteSize = 4;

    /// <summary>4 × 4 pixels of opaque white, named "white".</summary>
    public static RgbaImage Build()
    {
        byte[] data = new byte[WhiteSize * WhiteSize * 4];
        Array.Fill(data, (byte)255);
        return new RgbaImage(WhiteSize, WhiteSize, data) { Name = "white" };
    }
}
