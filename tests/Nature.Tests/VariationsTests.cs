using PropGenerator.Nature.Random;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class VariationsTests
{
    [Fact]
    public void PageSeedsAndVariationSeedsMatchEveryGolden()
    {
        using var doc = Fixture.Json("variations.json");
        int pages = 0;
        int variations = 0;
        int seedsInPages = 0;
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            double seed = entry.GetProperty("seed").GetDouble();
            foreach (var p in entry.GetProperty("pages").EnumerateArray())
            {
                int page = p.GetProperty("page").GetInt32();
                uint[] expected = [.. p.GetProperty("seeds").EnumerateArray().Select(s => s.GetUInt32())];
                Assert.Equal(expected, Variations.PageSeeds(seed, page));
                seedsInPages += expected.Length;
                pages++;
            }

            foreach (var v in entry.GetProperty("variations").EnumerateArray())
            {
                int variation = v.GetProperty("variation").GetInt32();
                Assert.True(
                    v.GetProperty("seed").GetDouble() == Variations.VariationSeed(seed, variation),
                    $"seed {seed}, variation {variation}");
                variations++;
            }
        }

        // Every distinct preset seed of easy-props 0.4.2 (103 of them), 3 pages of 8 seeds and 10 variations each.
        const int distinctSeeds = 103;
        Assert.Equal(distinctSeeds, doc.RootElement.GetArrayLength());
        Assert.Equal(distinctSeeds * 3, pages);
        Assert.Equal(distinctSeeds * 10, variations);
        Assert.Equal(distinctSeeds * 3 * 8, seedsInPages);
    }

    [Fact]
    public void VariationZeroIsTheSeedItself()
    {
        Assert.Equal(12345.0, Variations.VariationSeed(12345, 0));
        Assert.Equal(-7.5, Variations.VariationSeed(-7.5, 0));
    }

    [Fact]
    public void GridPutsTheCurrentSeedInTheMiddleOfTheEightOthers()
    {
        uint[] others = Variations.PageSeeds(301, 2);
        uint[] grid = Variations.Grid(301, 2);
        Assert.Equal(9, grid.Length);
        Assert.Equal(301u, grid[4]);
        Assert.Equal(others[..4], grid[..4]);
        Assert.Equal(others[4..], grid[5..]);
    }
}
