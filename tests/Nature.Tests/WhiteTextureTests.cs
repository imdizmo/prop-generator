using PropGenerator.Nature.Textures;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class WhiteTextureTests
{
    [Fact]
    public void IsNamedWhite() => Assert.Equal("white", WhiteTexture.Build().Name);
}
