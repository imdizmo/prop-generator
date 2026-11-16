using PropGenerator.Nature.Random;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class SeededRandomTests
{
    [Fact]
    public void NextMatchesEveryGoldenStream()
    {
        using var doc = Fixture.Json("rng.json");
        var streams = doc.RootElement.GetProperty("createSeededRandom");
        Assert.Equal(14, streams.GetArrayLength());
        foreach (var stream in streams.EnumerateArray())
        {
            double seed = stream.GetProperty("seed").GetDouble();
            var random = new SeededRandom(seed);
            int i = 0;
            foreach (var expected in stream.GetProperty("outputs").EnumerateArray())
            {
                Assert.True(expected.GetDouble() == random.Next(), $"seed {seed}, draw {i}");
                i++;
            }

            Assert.Equal(64, i);
        }
    }

    [Fact]
    public void RangeIsMinPlusNextTimesSpan()
    {
        var a = new SeededRandom(7);
        var b = new SeededRandom(7);
        Assert.Equal(-3 + b.Next() * 10, a.Range(-3, 7));
    }

    [Fact]
    public void IntIncludesBothBounds()
    {
        var a = new SeededRandom(11);
        var b = new SeededRandom(11);
        var seen = new HashSet<int>();
        for (int i = 0; i < 400; i++)
        {
            int got = a.Int(2, 5);
            Assert.Equal((int)Math.Floor(2 + b.Next() * 4), got);
            seen.Add(got);
        }

        Assert.Equal<int>([2, 3, 4, 5], seen.Order());
    }

    [Fact]
    public void PickUsesOneDrawAndFloorOfDrawTimesCount()
    {
        string[] items = ["a", "b", "c", "d", "e"];
        var a = new SeededRandom(3);
        var b = new SeededRandom(3);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(items[(int)Math.Floor(b.Next() * items.Length)], a.Pick(items));
        }
    }

    [Fact]
    public void PickFromEmptyThrows() =>
        Assert.Throws<InvalidOperationException>(() => new SeededRandom(1).Pick(Array.Empty<int>()));

    [Fact]
    public void ShuffledIsFisherYatesFromTheEndAndLeavesInputAlone()
    {
        int[] items = [0, 1, 2, 3, 4, 5, 6];
        var b = new SeededRandom(99);
        int[] expected = [.. items];
        for (int i = expected.Length - 1; i > 0; i--)
        {
            int j = (int)Math.Floor(b.Next() * (i + 1));
            (expected[i], expected[j]) = (expected[j], expected[i]);
        }

        var a = new SeededRandom(99);
        int[] got = a.Shuffled(items);
        Assert.Equal(expected, got);
        Assert.Equal<int>([0, 1, 2, 3, 4, 5, 6], items);
        Assert.Equal(b.Next(), a.Next());
    }

    [Fact]
    public void ShuffledOfZeroOrOneItemDrawsNothing()
    {
        var a = new SeededRandom(5);
        Assert.Empty(a.Shuffled(Array.Empty<int>()));
        Assert.Equal<int>([9], a.Shuffled([9]));
        Assert.Equal(new SeededRandom(5).Next(), a.Next());
    }
}
