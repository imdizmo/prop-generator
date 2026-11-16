using PropGenerator.Tests.Support;
using Xunit;

namespace PropGenerator.Tests;

public sealed class PngEncoderTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "prop-generator", "scratch",
        "png-" + Guid.NewGuid().ToString("N"));

    public PngEncoderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void ANonSquarePictureReadsBackPixelForPixel()
    {
        const int W = 5, H = 3;
        byte[] rgba = new byte[W * H * 4];
        for (int i = 0; i < rgba.Length; i++)
        {
            rgba[i] = (i % 4) == 3 ? (byte)255 : (byte)(i * 37);
        }

        string path = Path.Combine(_dir, "p.png");
        File.WriteAllBytes(path, PngEncoder.Encode(rgba, W, H));

        (int w, int h, byte[] rgb) = PngReader.ReadRgb(path);
        Assert.Equal((W, H), (w, h));
        for (int p = 0; p < W * H; p++)
        {
            Assert.Equal(rgba.AsSpan(p * 4, 3).ToArray(), rgb.AsSpan(p * 3, 3).ToArray());
        }
    }

    [Fact]
    public void PixelsThatDoNotMatchTheSizeAreRefused()
    {
        Assert.Throws<ArgumentException>(() => PngEncoder.Encode(new byte[10], 2, 2));
    }
}
