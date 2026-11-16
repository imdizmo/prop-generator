using PropGenerator.Nature.Textures;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class RgbaImageTests
{
    [Fact]
    public void KeepsThePixelsItWasGiven()
    {
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8];
        var image = new RgbaImage(2, 1, pixels);
        Assert.Same(pixels, image.Rgba);
        Assert.Equal("", image.Name);
    }

    [Fact]
    public void WrongPixelCountThrows()
    {
        Assert.Throws<ArgumentException>(() => new RgbaImage(2, 2, new byte[15]));
        Assert.Throws<ArgumentException>(() => new RgbaImage(0, 0, []));
        Assert.Throws<ArgumentException>(() => new RgbaImage(1, 1, null!));
    }
}
