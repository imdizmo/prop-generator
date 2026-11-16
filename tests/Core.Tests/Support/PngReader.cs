using System.IO.Compression;

namespace PropGenerator.Tests.Support;

/// <summary>A small PNG reader for tests: 8-bit RGB or RGBA, not interlaced. Returns RGB, rows top-down.</summary>
internal static class PngReader
{
    public static (int Width, int Height, byte[] Rgb) ReadRgb(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        int pos = 8;
        int w = 0, h = 0, channels = 0;
        using var idat = new MemoryStream();
        while (pos < file.Length)
        {
            int len = ReadInt(file, pos);
            string type = System.Text.Encoding.ASCII.GetString(file, pos + 4, 4);
            int data = pos + 8;
            if (type == "IHDR")
            {
                w = ReadInt(file, data);
                h = ReadInt(file, data + 4);
                if (file[data + 8] != 8 || file[data + 12] != 0)
                {
                    throw new NotSupportedException("Only 8-bit, non-interlaced PNGs.");
                }

                channels = file[data + 9] switch
                {
                    2 => 3,
                    6 => 4,
                    _ => throw new NotSupportedException("Only RGB or RGBA PNGs."),
                };
            }
            else if (type == "IDAT")
            {
                idat.Write(file, data, len);
            }

            pos = data + len + 4;
        }

        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        int stride = w * channels;
        byte[] raw = new byte[(stride + 1) * h];
        z.ReadExactly(raw);
        byte[] px = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            int filter = raw[y * (stride + 1)];
            int src = y * (stride + 1) + 1;
            for (int x = 0; x < stride; x++)
            {
                int a = x >= channels ? px[y * stride + x - channels] : 0;
                int b = y > 0 ? px[(y - 1) * stride + x] : 0;
                int c = x >= channels && y > 0 ? px[(y - 1) * stride + x - channels] : 0;
                int v = raw[src + x];
                v += filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new NotSupportedException("Bad PNG filter."),
                };
                px[y * stride + x] = (byte)v;
            }
        }

        byte[] rgb = new byte[w * h * 3];
        for (int i = 0; i < w * h; i++)
        {
            rgb[i * 3] = px[i * channels];
            rgb[i * 3 + 1] = px[i * channels + 1];
            rgb[i * 3 + 2] = px[i * channels + 2];
        }

        return (w, h, rgb);
    }

    private static int ReadInt(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
