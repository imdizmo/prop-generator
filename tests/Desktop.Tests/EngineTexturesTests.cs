using System.Text.Json;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>The engine's textures as the view reads them: fetched once, kept in an LRU bounded by bytes.</summary>
public sealed class EngineTexturesTests
{
    private const int MiB = 1 << 20;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void EvictsOldestPastTheByteCap()
    {
        var asked = new List<string>();
        var textures = new EngineTextures(3L * MiB, (key, _, _) =>
        {
            asked.Add(key);
            return Task.FromResult(Square(512, (byte)key[0]));
        });

        foreach (string key in new[] { "a", "b", "c", "d" })
        {
            Assert.Equal(512, textures.Get(key, EngineJobKind.Live, Ct).Width);
        }

        Assert.Equal(4, textures.Fetches);
        Assert.True(textures.Bytes <= 3L * MiB, $"{textures.Bytes} bytes kept");

        // "b", "c" and "d" are kept; "a" went first and is fetched again.
        _ = textures.Get("d", EngineJobKind.Live, Ct);
        _ = textures.Get("b", EngineJobKind.Live, Ct);
        Assert.Equal(4, textures.Fetches);
        Assert.Equal((byte)'a', textures.Get("a", EngineJobKind.Live, Ct).Rgba[0]);
        Assert.Equal(5, textures.Fetches);
        Assert.Equal(["a", "b", "c", "d", "a"], asked);

        // "c" was the least recently read, so it made room for "a".
        _ = textures.Get("c", EngineJobKind.Live, Ct);
        Assert.Equal(6, textures.Fetches);
        Assert.True(textures.Bytes <= 3L * MiB);
    }

    [Fact]
    public async Task TwoThreadsMissingOneKeyFetchOnce()
    {
        var gate = new TaskCompletionSource<EngineTexture>(TaskCreationOptions.RunContinuationsAsynchronously);
        int asked = 0;
        var textures = new EngineTextures(64L * MiB, (_, _, _) =>
        {
            Interlocked.Increment(ref asked);
            return gate.Task;
        });

        Task<MeshTexture> first = Task.Run(() => textures.Get("leaf", EngineJobKind.Live, Ct), Ct);
        Task<MeshTexture> second = Task.Run(() => textures.Get("leaf", EngineJobKind.Thumbnail, Ct), Ct);
        SpinWait.SpinUntil(() => Volatile.Read(ref asked) > 0, TimeSpan.FromSeconds(10));
        Thread.Sleep(100);
        gate.SetResult(Square(16, 7));
        Assert.Same((await first).Rgba, (await second).Rgba);
        Assert.Equal(1, textures.Fetches);
        Assert.Equal(1, asked);
    }

    [Fact]
    public void AFailedFetchIsNotKeptAndIsTriedAgain()
    {
        int asked = 0;
        var textures = new EngineTextures(64L * MiB, (_, _, _) =>
            Interlocked.Increment(ref asked) == 1
                ? Task.FromException<EngineTexture>(new EngineScriptException("no such texture"))
                : Task.FromResult(Square(16, 3)));
        Assert.Throws<EngineScriptException>(() => textures.Get("leaf", EngineJobKind.Live, Ct));
        Assert.Equal(0, textures.Bytes);
        Assert.Equal(3, textures.Get("leaf", EngineJobKind.Live, Ct).Rgba[0]);
        Assert.Equal(2, textures.Fetches);
    }

    [Fact]
    public void ACancelledGetStopsWaiting()
    {
        var never = new TaskCompletionSource<EngineTexture>();
        var textures = new EngineTextures(64L * MiB, (_, _, _) => never.Task);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.ThrowsAny<OperationCanceledException>(() => textures.Get("leaf", EngineJobKind.Live, stop.Token));
    }

    [Fact]
    public async Task GetOnTheEngineThreadThrows()
    {
        EngineThread builds = EngineHost.Default.Builds;
        var textures = new EngineTextures(64L * MiB, (_, _, _) => Task.FromResult(Square(16, 1)), builds);
        Exception? seen = await builds.Enqueue(EngineJobKind.Live, (_, _) =>
        {
            try
            {
                textures.Get("leaf", EngineJobKind.Live, CancellationToken.None);
                return null;
            }
            catch (InvalidOperationException e)
            {
                return e;
            }
        }, Ct).WaitAsync(TimeSpan.FromMinutes(1), Ct);
        Assert.NotNull(seen);
        Assert.Equal(0, textures.Fetches);
    }

    [Fact]
    public void ATextureReadsRightSideUp()
    {
        // The leaf atlas's rows, each summed over its bytes, as they were when the engine's atlas was checked within one
        // per byte of the port's (Fixtures/port-0.4.2/leaf-rows.json, written at phase 4 before the port went). The
        // engine's image.data has the v = 0 row first, as MeshTexture does: no flip. A flipped atlas would put the
        // profile upside down, which differs from it by far more than one per byte.
        MeshTexture leaf = EngineHost.Default.Textures.Get("leaf", EngineJobKind.Live, Ct);
        using JsonDocument pinned = PortFixtures.Json("leaf-rows.json");
        Assert.Equal((pinned.RootElement.GetProperty("width").GetInt32(), pinned.RootElement.GetProperty("height").GetInt32()), (leaf.Width, leaf.Height));
        long[] want = [.. pinned.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetInt64())];
        long tolerance = leaf.Width * 4L;
        for (int y = 0; y < leaf.Height; y++)
        {
            long row = 0;
            for (int i = y * leaf.Width * 4; i < (y + 1) * leaf.Width * 4; i++)
            {
                row += leaf.Rgba[i];
            }

            Assert.True(Math.Abs(row - want[y]) <= tolerance, $"row {y}: {row}, pinned {want[y]}");
        }

        Assert.Contains(Enumerable.Range(0, leaf.Height), y => Math.Abs(want[y] - want[leaf.Height - 1 - y]) > tolerance);
        Assert.Equal(0, leaf.AlphaCut);
    }

    // A square texture of one byte value.
    private static EngineTexture Square(int size, byte value)
    {
        byte[] rgba = new byte[size * size * 4];
        Array.Fill(rgba, value);
        return new EngineTexture(size, size, rgba, JsonDocument.Parse("{}").RootElement);
    }
}
