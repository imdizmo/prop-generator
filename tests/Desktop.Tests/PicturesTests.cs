using System.Buffers.Binary;
using System.Diagnostics;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Pictures;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class PicturesTests(ITestOutputHelper output)
{
    /// <summary>Decodes our own PNGs: 8-bit RGBA with filter type 0 on every row.</summary>
    private static (int W, int H, byte[] Rgba) Decode(byte[] png)
    {
        var (w, h) = Size(png);
        using var packed = new MemoryStream();
        for (int at = 8; at < png.Length;)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            if (System.Text.Encoding.ASCII.GetString(png, at + 4, 4) == "IDAT")
            {
                packed.Write(png, at + 8, len);
            }

            at += 12 + len;
        }

        packed.Position = 0;
        using var z = new System.IO.Compression.ZLibStream(packed, System.IO.Compression.CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        byte[] rows = raw.ToArray();
        byte[] rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            Assert.Equal(0, rows[y * ((w * 4) + 1)]);
            Array.Copy(rows, (y * ((w * 4) + 1)) + 1, rgba, y * w * 4, w * 4);
        }

        return (w, h, rgba);
    }

    private static (int W, int H) Size(byte[] png) =>
        (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));

    [Fact]
    public void RockThumbHasTheRockInTheMiddle()
    {
        var p = RockRecipe.StartingPoints.Single(x => x.Id == "boulder");
        var sw = Stopwatch.StartNew();
        byte[] png = MeshThumb.Png(RockRecipe.Build(p.Settings));
        output.WriteLine($"thumbnail: {sw.ElapsedMilliseconds} ms");
        string? keep = Environment.GetEnvironmentVariable("THUMB_OUT");
        if (keep != null)
        {
            File.WriteAllBytes(keep, png);
        }

        var (w, h, rgba) = Decode(png);
        Assert.Equal((128, 128), (w, h));
        int opaque = 0;
        for (int i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] > 127)
            {
                opaque++;
            }
        }

        Assert.True(opaque >= w * h * 0.30, $"opaque {opaque}");
        foreach (int c in new[] { 0, w - 1, (h - 1) * w, (h * w) - 1 })
        {
            Assert.Equal(0, rgba[(c * 4) + 3]);
        }
    }

    [Fact]
    public void CacheKeepsAtMostCapFiles()
    {
        using var s = new Scratch();
        var cache = new ThumbCache(s.Dir, 3);
        byte[] png = MeshThumb.Png(new ColouredMesh([0, 0, 0, 1, 0, 0, 0, 0, 1], [0, -1, 0, 0, -1, 0, 0, -1, 0], [1, 1, 1, 1, 1, 1, 1, 1, 1], [0, 1, 2]), 16);
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        for (int i = 0; i < 5; i++)
        {
            var before = Directory.GetFiles(s.Dir).ToHashSet();
            cache.Put("k" + i, png);
            foreach (string f in Directory.GetFiles(s.Dir).Where(f => !before.Contains(f)))
            {
                File.SetLastWriteTimeUtc(f, t0.AddMinutes(i));
            }
        }

        Assert.Equal(3, Directory.GetFiles(s.Dir).Length);
        Assert.Null(cache.Get("k0"));
        Assert.Null(cache.Get("k1"));
        Assert.NotNull(cache.Get("k4"));
    }

    [Fact]
    public void BrokenCacheFileReadsAsMissing()
    {
        using var s = new Scratch();
        var cache = new ThumbCache(s.Dir);
        cache.Put("a", [1, 2, 3, 4, 5, 6, 7, 8, 9]);
        Assert.Null(cache.Get("a"));
        Assert.Null(cache.Get("never-put"));
    }

    [Fact]
    public void UnwritableCacheDirDoesNotThrow()
    {
        using var s = new Scratch();
        File.WriteAllText(s.P("blocker"), "x");
        var cache = new ThumbCache(s.P("blocker"));
        cache.Put("a", [0x89, (byte)'P', (byte)'N', (byte)'G', 0, 0, 0, 0]);
        Assert.Null(cache.Get("a"));
    }

    [Fact]
    public void FloorPictureShipsWithTheApp()
    {
        using Stream? st = typeof(MeshThumb).Assembly.GetManifestResourceStream("floor_v5.png");
        Assert.NotNull(st);
        using var ms = new MemoryStream();
        st.CopyTo(ms);
        Assert.Equal((128, 128), Size(ms.ToArray()));
    }

    [Fact]
    public void AFreshPresetsPictureIsTheBundlesPngAndQueuesNoBuild()
    {
        long before = EngineHost.Default.Builds.JobsQueued(EngineJobKind.Thumbnail);
        byte[]? shipped = ShippedThumbs.For("oak", "painted-tree");
        Assert.NotNull(shipped);
        Assert.Equal(EngineOptions.Thumbnail("oak"), shipped);
        Assert.Equal((180, 180), Size(shipped));
        Assert.Equal(before, EngineHost.Default.Builds.JobsQueued(EngineJobKind.Thumbnail));
    }

    [Fact]
    public void NoShippedPictureForAStalePresetAUserPresetOrAnotherRecipe()
    {
        // Every preset of the 0.4.2 bundle has a fresh picture, so a stale one is made here.
        EnginePreset oak = EngineHost.Default.Catalog.Presets.Presets.Single(p => p.Id == "oak");
        Assert.True(oak.ThumbnailFresh);
        EnginePreset[] stale = [oak with { ThumbnailFresh = false }];
        Assert.False(ShippedThumbs.Shipped(stale, "oak", "painted-tree"));
        Assert.True(ShippedThumbs.Shipped([oak], "oak", "painted-tree"));

        Assert.Null(ShippedThumbs.For("mine-0123456789ab", "bush"));
        Assert.Null(ShippedThumbs.For("oak", "slab-cliff"));
        Assert.Null(ShippedThumbs.For("long-cliff", "slab-cliff"));
    }

    [Fact]
    public void TheEngineTagNamesTheBundleForEasyPropsKindsOnly()
    {
        EngineInfo info = EngineHost.Default.Catalog.Info;
        string? tag = ShippedThumbs.EngineTag("bush");
        Assert.NotNull(tag);
        Assert.Contains(info.Tag, tag, StringComparison.Ordinal);
        Assert.Contains(info.Commit, tag, StringComparison.Ordinal);
        Assert.Equal(tag, ShippedThumbs.EngineTag("cut-rock"));
        Assert.Null(ShippedThumbs.EngineTag("slab-cliff"));
        Assert.Null(ShippedThumbs.EngineTag("stylized-rock"));
    }

    [Fact]
    public void APresetKeyCarriesTheEngineTagOnlyWhenGiven()
    {
        Assert.Equal("preset:long-cliff:slab-cliff:v6", ThumbCache.PresetKey("long-cliff", "slab-cliff", 6));
        Assert.Equal("preset:mine-1:bush:v3:v0.4.2-abc", ThumbCache.PresetKey("mine-1", "bush", 3, "v0.4.2-abc"));
    }
}
