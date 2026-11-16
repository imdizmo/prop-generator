using PropGenerator.Nature.Random;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class HashTests
{
    [Fact]
    public void SeedMatchesEveryGolden()
    {
        using var doc = Fixture.Json("rng.json");
        var cases = doc.RootElement.GetProperty("hashSeed");
        Assert.Equal(40, cases.GetArrayLength());
        foreach (var c in cases.EnumerateArray())
        {
            double[] parts = [.. c.GetProperty("parts").EnumerateArray().Select(p => p.GetDouble())];
            Assert.True(c.GetProperty("value").GetUInt32() == Hash.Seed(parts), $"parts [{string.Join(",", parts)}]");
        }
    }

    [Fact]
    public void KeyMatchesEveryGolden()
    {
        using var doc = Fixture.Json("rng.json");
        var cases = doc.RootElement.GetProperty("hashKey");
        Assert.Equal(8, cases.GetArrayLength());
        foreach (var c in cases.EnumerateArray())
        {
            string key = c.GetProperty("key").GetString()!;
            Assert.True(c.GetProperty("value").GetUInt32() == Hash.Key(key), $"key '{key}'");
        }
    }

    [Fact]
    public void SeedWithNoPartsIsTheOffsetBasis() => Assert.Equal(0x811c9dc5u, Hash.Seed());

    [Fact]
    public void SeedTruncatesFractionsAndTreatsNaNAsZero()
    {
        Assert.Equal(Hash.Seed(3), Hash.Seed(3.9));
        Assert.Equal(Hash.Seed(-3), Hash.Seed(-3.9));
        Assert.Equal(Hash.Seed(0), Hash.Seed(double.NaN));
    }
}
