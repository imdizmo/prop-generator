using System.IO.Compression;

namespace PropGenerator;

/// <summary>
/// Writes a PNG: 8 bits per channel, RGBA, no filtering. The one encoder for the exported textures and Claude's
/// screenshots.
/// </summary>
public static class PngEncoder
{
    private static readonly uint[] Table = MakeTable();

    /// <summary>Encodes RGBA8 pixels, rows top-down, as a PNG.</summary>
    /// <param name="rgba">width x height x 4 bytes.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The PNG file's bytes.</returns>
    /// <exception cref="ArgumentException">The size does not match the pixels.</exception>
    public static byte[] Encode(byte[] rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgba.Length != width * height * 4)
        {
            throw new ArgumentException("The picture's size does not match its pixels.");
        }

        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] head = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(head, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(4), height);
        head[8] = 8;
        head[9] = 6;
        Chunk(png, "IHDR", head);

        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Fastest, leaveOpen: true))
        {
            int row = width * 4;
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgba, y * row, row);
            }
        }

        Chunk(png, "IDAT", packed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream to, string type, byte[] data)
    {
        byte[] length = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        to.Write(length);
        byte[] name = System.Text.Encoding.ASCII.GetBytes(type);
        to.Write(name);
        to.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (byte b in name)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        byte[] sum = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(sum, ~crc);
        to.Write(sum);
    }

    private static uint[] MakeTable()
    {
        uint[] t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            t[n] = c;
        }

        return t;
    }
}
