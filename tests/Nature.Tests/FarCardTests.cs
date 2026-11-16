using PropGenerator.Nature.Export;
using PropGenerator.Nature.Kernel;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class FarCardTests
{
    [Fact]
    public void TheCardIsTwoCrossedCardsOfEightFacesFramingTheProp()
    {
        var frame = new FarFrame(1.5, -0.2, 3);
        PropMesh card = FarCard.Geometry(frame, null);
        Assert.Equal(16, card.VertexCount);
        Assert.Equal(FarCard.Faces * 3, card.Index!.Length);
        Assert.Null(card.Uv1);
        Assert.Equal(1.5f, card.Position.Where((_, i) => i % 3 == 0).Max());
        Assert.Equal(3f, card.Position.Where((_, i) => i % 3 == 1).Max());
        Assert.NotNull(FarCard.Geometry(frame, new WindExtent(3, 1)).Uv1);
    }
}
