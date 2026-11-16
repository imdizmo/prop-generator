using Xunit;

namespace PropGenerator.Tests;

public class LayersTests
{
    [Fact]
    public void FromWorkerBytesRejectsAWrongByteCount()
    {
        Assert.Throws<ArgumentException>(() => Layers.FromWorkerBytes(256, new byte[10]));
    }

    [Fact]
    public void FromWorkerBytesReadsLayersInOrderAsHalfFloats()
    {
        const int n = 256 * 256;
        byte[] bytes = new byte[7 * n * 2];
        for (int layer = 0; layer < 7; layer++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan((layer * n + 5) * 2), (Half)(layer + 0.5f));
        }

        var l = Layers.FromWorkerBytes(256, bytes);
        Assert.Equal(0.5f, l.Marks[5]);
        Assert.Equal(1.5f, l.Rest[5]);
        Assert.Equal(2.5f, l.Drift[5]);
        Assert.Equal(3.5f, l.Region[5]);
        Assert.Equal(4.5f, l.BaseMul[5]);
        Assert.Equal(5.5f, l.Seam[5]);
        Assert.Equal(6.5f, l.Aa[5]);
    }
}
