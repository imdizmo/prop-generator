// Ported from easy-props frontend/src/core/export/pipeline/merge.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Textures;

/// <summary>
/// A relief as a file wants it (easy-props' <c>reliefForFile</c>). The viewport works out its directions from the
/// texture coordinates and a file's reader the other way up, so green is inverted. The lighter detail levels get it at
/// half size each way: fine bark is noise to a PNG, so this is most of a lighter file's weight.
/// </summary>
public static class FileRelief
{
    /// <summary>
    /// The relief with green flipped (<c>255 - g</c>); when <paramref name="full"/> is false, first halved each way by
    /// averaging each 2 × 2 block, <c>Math.round(sum / 4)</c> per channel. The input is not changed. Halving needs an
    /// even width and height.
    /// </summary>
    public static RgbaImage Build(RgbaImage relief, bool full)
    {
        ArgumentNullException.ThrowIfNull(relief);
        RgbaImage source = full ? relief : Halved(relief);
        byte[] flipped = (byte[])source.Rgba.Clone();

        for (int at = 1; at < flipped.Length; at += 4)
        {
            flipped[at] = JsNumber.ToUint8(255 - flipped[at]);
        }

        return new RgbaImage(source.W, source.H, flipped);
    }

    /// <summary>Half a relief's size each way.</summary>
    private static RgbaImage Halved(RgbaImage image)
    {
        if (image.W % 2 != 0 || image.H % 2 != 0)
        {
            throw new ArgumentException($"Only an even size halves; this relief is {image.W} by {image.H}", nameof(image));
        }

        int width = image.W;
        byte[] data = image.Rgba;
        int smallWidth = width / 2;
        int smallHeight = image.H / 2;
        byte[] small = new byte[smallWidth * smallHeight * 4];

        for (int y = 0; y < smallHeight; y++)
        {
            for (int x = 0; x < smallWidth; x++)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    int sum =
                        data[((((y * 2) * width) + (x * 2)) * 4) + channel] +
                        data[((((y * 2) * width) + (x * 2) + 1) * 4) + channel] +
                        data[(((((y * 2) + 1) * width) + (x * 2)) * 4) + channel] +
                        data[(((((y * 2) + 1) * width) + (x * 2) + 1) * 4) + channel];

                    small[(((y * smallWidth) + x) * 4) + channel] = JsNumber.ToUint8(JsMath.Round(sum / 4.0));
                }
            }
        }

        return new RgbaImage(smallWidth, smallHeight, small);
    }
}
