namespace PropGenerator.Desktop.Pictures;

/// <summary>
/// A small square picture of the 3D view, for a saved work file's tile in the drawer: the middle square of the view,
/// shrunk by averaging, opaque.
/// </summary>
internal static class ViewThumb
{
    /// <summary>The middle square of a picture, shrunk (or stretched) to a set size.</summary>
    /// <param name="rgba">The picture, RGBA8, top row first.</param>
    /// <param name="width">Its width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="size">The side of the square made.</param>
    /// <returns>The square, RGBA8 top row first and fully opaque, and its side.</returns>
    /// <exception cref="ArgumentException">The picture's bytes do not match its size.</exception>
    public static (byte[] Rgba, int Size) Square(byte[] rgba, int width, int height, int size)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException("The picture's bytes do not match its width and height.", nameof(rgba));
        }

        int side = Math.Min(width, height);
        int left = (width - side) / 2;
        int top = (height - side) / 2;
        byte[] result = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            int y0 = top + (y * side / size);
            int y1 = Math.Max(y0 + 1, top + ((y + 1) * side / size));
            for (int x = 0; x < size; x++)
            {
                int x0 = left + (x * side / size);
                int x1 = Math.Max(x0 + 1, left + ((x + 1) * side / size));
                long r = 0, g = 0, b = 0;
                for (int sy = y0; sy < y1; sy++)
                {
                    for (int sx = x0; sx < x1; sx++)
                    {
                        int i = ((sy * width) + sx) * 4;
                        r += rgba[i];
                        g += rgba[i + 1];
                        b += rgba[i + 2];
                    }
                }

                int n = (y1 - y0) * (x1 - x0);
                int o = ((y * size) + x) * 4;
                result[o] = (byte)(r / n);
                result[o + 1] = (byte)(g / n);
                result[o + 2] = (byte)(b / n);
                result[o + 3] = 255;
            }
        }

        return (result, size);
    }

    /// <summary>The middle square of a picture of the view, as a PNG.</summary>
    /// <param name="rgba">The picture, RGBA8, top row first.</param>
    /// <param name="width">Its width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="size">The side of the PNG.</param>
    /// <returns>The PNG's bytes.</returns>
    public static byte[] Png(byte[] rgba, int width, int height, int size = 128)
    {
        (byte[] square, int side) = Square(rgba, width, height, size);
        return PngEncoder.Encode(square, side, side);
    }
}
