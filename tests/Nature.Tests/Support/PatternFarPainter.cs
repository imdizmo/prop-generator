using PropGenerator.Nature.Export;
using PropGenerator.Nature.Random;

namespace PropGenerator.Nature.Tests.Support;

/// <summary>
/// The far-away card's pictures without a GPU, as the harness's <c>patternFarPainter</c> draws them: every pixel from
/// mulberry32 seeded with hashSeed(meshes, size), with a hole (alpha 0) wherever ((x &gt;&gt; 3) + (y &gt;&gt; 2)) % 4 is 0.
/// </summary>
internal sealed class PatternFarPainter : IFarPainter
{
    public FarPictures? Paint(IReadOnlyList<MergedMesh> meshes, FarFrame frame, int viewSize) => Draw(meshes.Count, viewSize);

    public static FarPictures Draw(int meshes, int viewSize)
    {
        int size = viewSize * 2;
        var random = new SeededRandom(Hash.Seed(meshes, size));
        byte[] colour = new byte[size * size * 4];
        byte[] relief = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int at = ((y * size) + x) * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    colour[at + channel] = (byte)Math.Floor(random.Next() * 256);
                }

                if (((x >> 3) + (y >> 2)) % 4 == 0)
                {
                    colour[at + 3] = 0;
                }
            }
        }

        for (int at = 0; at < relief.Length; at += 4)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                relief[at + channel] = (byte)Math.Floor(random.Next() * 256);
            }

            relief[at + 3] = 255;
        }

        return new FarPictures(size, colour, relief);
    }
}
