using PropGenerator.Nature.Textures;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class TextureCacheTests
{
    private static RgbaImage Tiny() => new(1, 1, [1, 2, 3, 4]);

    [Fact]
    public void HoldsEightAfterTwentyKeys()
    {
        var cache = new TextureCache();
        for (int i = 0; i < 20; i++)
        {
            cache.Get($"key{i}", Tiny);
        }

        Assert.Equal(8, cache.Count);
        Assert.Equal(8, TextureCache.Capacity);
    }

    [Fact]
    public void AHitReturnsTheSameInstanceWithoutMaking()
    {
        var cache = new TextureCache();
        RgbaImage first = cache.Get("bark:none:1", Tiny);
        RgbaImage second = cache.Get("bark:none:1", () => throw new InvalidOperationException("made again"));
        Assert.Same(first, second);
    }

    [Fact]
    public void EvictsTheLeastRecentlyUsed()
    {
        var cache = new TextureCache();
        RgbaImage kept = cache.Get("key0", Tiny);
        for (int i = 1; i < 8; i++)
        {
            cache.Get($"key{i}", Tiny);
        }

        // key0 is used again, so key1 is now the oldest and goes when key8 comes in.
        Assert.Same(kept, cache.Get("key0", Tiny));
        cache.Get("key8", Tiny);
        Assert.Same(kept, cache.Get("key0", () => throw new InvalidOperationException("key0 was evicted")));
        int made = 0;
        cache.Get("key1", () =>
        {
            made++;
            return Tiny();
        });
        Assert.Equal(1, made);
        Assert.Equal(8, cache.Count);
    }
}
