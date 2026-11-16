namespace PropGenerator.Nature.Textures;

/// <summary>
/// A texture as easy-props builds it: <paramref name="W"/> by <paramref name="H"/> pixels of red, green, blue and
/// alpha bytes, row by row from the first row easy-props writes. <see cref="Name"/> is three's texture name, which only
/// the white texture sets.
/// </summary>
/// <param name="W">Width in pixels.</param>
/// <param name="H">Height in pixels.</param>
/// <param name="Rgba">The pixels, four bytes each; the image owns this array.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The pixels are handed to the PNG writer and the viewport as they are.")]
public sealed record RgbaImage(int W, int H, byte[] Rgba)
{
    /// <summary>The pixels, four bytes each; their count must be <c>W * H * 4</c>.</summary>
    public byte[] Rgba { get; } = Rgba is not null && W > 0 && H > 0 && Rgba.Length == W * H * 4
        ? Rgba
        : throw new ArgumentException($"An RGBA image of {W} by {H} needs {W * H * 4} bytes", nameof(Rgba));

    /// <summary>three's texture name: empty, except "white" for the plain white texture.</summary>
    public string Name { get; init; } = "";
}
