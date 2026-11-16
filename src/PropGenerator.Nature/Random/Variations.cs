// Ported from easy-props frontend/src/core/scene/variations.ts (pageSeeds) and
// frontend/src/core/export/batch-items.ts (variationSeed) (MIT, © 2026 AHS12).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Random;

/// <summary>The seeds "Show me more" offers and the seeds of batch variations.</summary>
public static class Variations
{
    /// <summary>How many others go around the prop as it is now.</summary>
    public const int OthersPerPage = 8;

    /// <summary>The eight seeds of one page: the same prop and page always give the same ones.</summary>
    public static uint[] PageSeeds(double seed, int page)
    {
        uint[] seeds = new uint[OthersPerPage];
        for (int index = 0; index < OthersPerPage; index++)
        {
            seeds[index] = Hash.Seed(seed, page + 1, index + 1);
        }

        return seeds;
    }

    /// <summary>The nine seeds shown on a page: the eight others around the current seed, which sits at index 4.</summary>
    public static uint[] Grid(double seed, int page)
    {
        uint[] others = PageSeeds(seed, page);
        uint[] grid = new uint[OthersPerPage + 1];
        Array.Copy(others, 0, grid, 0, 4);
        grid[4] = JsNumber.ToUint32(seed);
        Array.Copy(others, 4, grid, 5, 4);
        return grid;
    }

    /// <summary>The seed of a variation. The first (0) is the prop's own seed, unchanged.</summary>
    public static double VariationSeed(double seed, int variation) =>
        variation == 0 ? seed : Hash.Seed(seed, variation);
}
