using System.Runtime.InteropServices;
using PropGenerator.Nature.Style;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class FlatColoursTests
{
    [Fact]
    public void FlattenMatchesEasyPropsBitForBit()
    {
        using var doc = Fixture.Json("flat.json.gz");
        int count = 0;
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            double[] colours = [.. c.GetProperty("colours").EnumerateArray().Select(e => e.GetDouble())];
            float[] want = [.. c.GetProperty("flat").EnumerateArray().Select(e => (float)e.GetDouble())];
            float[] ours = FlatColours.Flatten(colours, c.GetProperty("join").GetBoolean());
            Assert.True(
                MemoryMarshal.AsBytes(want.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(ours.AsSpan())),
                $"case {count}: {colours.Length / 3} colours, join {c.GetProperty("join").GetBoolean()}");
            count++;
        }

        Assert.Equal(40, count);
    }

    [Fact]
    public void NoColoursGiveNoColours() => Assert.Empty(FlatColours.Flatten([], join: true));

    [Fact]
    public void AFlowerAmongLeavesKeepsItsOwnTone()
    {
        // Many greens with grain and one red: the greens join into one tone, the red stays apart.
        var colours = new List<double>();
        for (int i = 0; i < 50; i++)
        {
            double shade = 1 - (i % 5 * 0.04);
            colours.AddRange([0.1 * shade, 0.5 * shade, 0.1 * shade]);
        }

        colours.AddRange([0.8, 0.1, 0.1]);
        float[] flat = FlatColours.Flatten([.. colours], join: true);
        Assert.Single(Enumerable.Range(0, 50).Select(i => (flat[i * 3], flat[(i * 3) + 1], flat[(i * 3) + 2])).Distinct());
        Assert.NotEqual((flat[0], flat[1], flat[2]), (flat[150], flat[151], flat[152]));
    }
}
