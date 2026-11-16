using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PropGenerator.Engine.Tests;

/// <summary>
/// easy-props' own bundle, as the app embeds it (skipped until tools/engine has written Resources/engine.js): the
/// host's hooks reach the real shims, and a real build, cancel and export go through the host.
/// </summary>
public sealed class EmbeddedBundleTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void SkipWithoutBundle() =>
        Assert.SkipUnless(EngineOptions.HasEmbeddedBundle, "Resources/engine.js is not built yet (tools/engine).");

    private static string SceneOf(EnginePreset p) =>
        $$"""{"generatorId":"{{p.GeneratorId}}","seed":{{p.Seed}},"parameters":{{p.Parameters.GetRawText()}}}""";

    /// <summary>The tag engine-pin.json names (the release the bundle was built at).</summary>
    private static string PinnedTag()
    {
        using var pin = JsonDocument.Parse(EngineOptions.ReadResource("engine-pin.json")!);
        return pin.RootElement.GetProperty("tag").GetString()!;
    }

    [Fact]
    public void TheEmbeddedBundleLoadsAndItsInfoNamesItsPin()
    {
        SkipWithoutBundle();
        using var engine = new EasyPropsEngine();
        EngineInfo info = engine.Info(Ct);
        using var pin = JsonDocument.Parse(EngineOptions.ReadResource("engine-pin.json")!);
        Assert.Equal(pin.RootElement.GetProperty("tag").GetString(), info.Tag);
        Assert.Equal(pin.RootElement.GetProperty("commit").GetString(), info.Commit);
        Assert.NotEmpty(info.Generators);
    }

    [Fact]
    public void ItsInfoGivesUpstreamsMostVersionsOfABatch()
    {
        // Export many's limit is upstream's MAX_VARIATIONS (batch-items.ts), read from the bundle, not copied.
        SkipWithoutBundle();
        using var engine = new EasyPropsEngine();
        Assert.Equal(50, engine.Info(Ct).MaxVariations);
    }

    [Fact]
    public void TheBundleNamesItsSourcesTheSameOnAnyMachine()
    {
        // esbuild writes each module's path as a comment; when that path depends on where the build ran from (the
        // working folder, $HOME), so do the bundle's bytes and the SHA-256 that engine-pin.json pins.
        SkipWithoutBundle();
        string bundle = EngineOptions.ReadResource("engine.js")!;
        string[] paths = [.. bundle.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("// ", StringComparison.Ordinal) && line.Contains('/', StringComparison.Ordinal))
            .Select(line => line[3..])
            .Where(path => !path.Contains(' ', StringComparison.Ordinal) && !path.Contains("://", StringComparison.Ordinal))];
        Assert.Contains("ours:tools/engine/entry.ts", paths);
        Assert.Contains(paths, p => p.StartsWith("frontend/src/", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.StartsWith("../", StringComparison.Ordinal) || p.Contains(":/", StringComparison.Ordinal)
            || p.StartsWith('/') || p.Contains("/home/", StringComparison.Ordinal) || p.Contains(".cache", StringComparison.Ordinal));
    }

    [Fact]
    public void TheShimsSeeTheHostHooksAsFunctions()
    {
        SkipWithoutBundle();
        using var engine = new EasyPropsEngine();
        Assert.Equal("function function function function",
            engine.Evaluate("[typeof __host.cancelled, typeof __host.png, typeof __host.log, typeof __runTimers].join(' ')"));
        Assert.Equal(false, engine.Evaluate("__host.cancelled(0)"));
    }

    [Fact]
    public async Task ABoulderBuildsAndExportsWithItsPngsFromTheHost()
    {
        SkipWithoutBundle();
        using var thread = new EngineThread();
        EnginePresets presets = await thread.PresetsAsync(Ct).WaitAsync(Wait, Ct);
        Assert.True(presets.Presets.Count >= 100, $"{presets.Presets.Count} presets");
        string scene = SceneOf(presets.Presets.Single(p => p.Id == "boulder"));

        EngineMesh mesh = await thread.BuildAsync(scene, "low-poly", 0.12, cancel: Ct).WaitAsync(Wait, Ct);
        Assert.NotEmpty(mesh.Parts);
        Assert.All(mesh.Parts, p => Assert.Equal(p.VertexCount * 3, p.Positions.Length));

        const string Settings = """{"name":"boulder","engine":"godot","levels":1,"collision":true,"wind":false,"far":false,"look":"lit"}""";
        IReadOnlyList<EngineFile> files = await thread.ExportAsync(scene, "low-poly", Settings, cancel: Ct).WaitAsync(Wait, Ct);
        EngineFile glb = Assert.Single(files, f => f.Name == "boulder.glb");
        Assert.Equal("glTF", Encoding.ASCII.GetString(glb.Bytes, 0, 4));
        Assert.True(glb.Faces > 0);

        // Every image in the GLB was encoded by the host (PropGenerator.Core.PngEncoder) through __host.png.
        int jsonLength = BitConverter.ToInt32(glb.Bytes, 12);
        string json = Encoding.UTF8.GetString(glb.Bytes, 20, jsonLength);
        Assert.Contains("\"image/png\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOaksShippedThumbnailIsA180PixelPng()
    {
        SkipWithoutBundle();
        byte[]? png = EngineOptions.Thumbnail("oak");
        Assert.NotNull(png);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0d, 0x0a, 0x1a, 0x0a], png[..8]);
        (int width, int height, int rowBytes, byte[] pixels) = Decode(png);
        Assert.Equal((180, 180), (width, height));
        Assert.Equal(height * (1 + rowBytes), pixels.Length);
        Assert.Null(EngineOptions.Thumbnail("no-such-preset"));
        Assert.Equal(png, EngineOptions.ReadResourceBytes("thumbs/oak.png"));
        Assert.Null(EngineOptions.ReadResourceBytes("no-such-file"));
    }

    [Fact]
    public async Task BarkIsSrgbAndRepeatingAndReliefIsLinear()
    {
        SkipWithoutBundle();
        using var thread = new EngineThread();
        EnginePresets presets = await thread.PresetsAsync(Ct).WaitAsync(Wait, Ct);
        string oak = SceneOf(presets.Presets.Single(p => p.Id == "oak"));
        EngineMesh mesh = await thread.BuildAsync(oak, "low-poly", 0.12, cancel: Ct).WaitAsync(Wait, Ct);

        string bark = mesh.Parts.Select(p => p.Surface.Map).First(k => k?.StartsWith("bark:", StringComparison.Ordinal) == true)!;
        string relief = mesh.Parts.Select(p => p.Surface.NormalMap).First(k => k?.StartsWith("relief:", StringComparison.Ordinal) == true)!;
        EngineTexture barkTexture = await thread.TextureAsync(bark, cancel: Ct).WaitAsync(Wait, Ct);
        EngineTexture reliefTexture = await thread.TextureAsync(relief, cancel: Ct).WaitAsync(Wait, Ct);
        Assert.Equal("srgb", barkTexture.ColorSpace);
        Assert.True(barkTexture.Repeat);
        Assert.Equal("", reliefTexture.ColorSpace);
        Assert.All(mesh.Parts.Where(p => p.Surface.Meta.ValueKind == JsonValueKind.Object), p => Assert.Equal(p.Proxy, p.Surface.Proxy));
    }

    [Fact]
    public async Task ARealExportReportsProgressEndingAtOne()
    {
        SkipWithoutBundle();
        using var thread = new EngineThread();
        EnginePresets presets = await thread.PresetsAsync(Ct).WaitAsync(Wait, Ct);
        string scene = SceneOf(presets.Presets.Single(p => p.Id == "boulder"));
        var seen = new List<double>();
        var progress = new Inline(v =>
        {
            lock (seen)
            {
                seen.Add(v);
            }
        });

        const string Settings = """{"name":"boulder","engine":"unreal","levels":1,"collision":false,"wind":true,"look":"lit"}""";
        IReadOnlyList<EngineFile> files = await thread.ExportAsync(scene, "low-poly", Settings, progress, Ct).WaitAsync(Wait, Ct);
        Assert.Contains(files, f => f.Name == "boulder.glb");
        lock (seen)
        {
            Assert.NotEmpty(seen);
            Assert.Equal(1, seen[^1]);
            Assert.All(seen, v => Assert.InRange(v, 0, 1));
        }
    }

    [Fact]
    public async Task ARealBuildIsCancelledBetweenComponents()
    {
        SkipWithoutBundle();
        using var thread = new EngineThread();
        EnginePresets presets = await thread.PresetsAsync(Ct).WaitAsync(Wait, Ct);
        string oak = SceneOf(presets.Presets.Single(p => p.Id == "oak"));
        using var cancel = new CancellationTokenSource();

        Task<EngineMesh> build = thread.BuildAsync(oak, "low-poly", 1, cancel: cancel.Token);
        await Task.Delay(200, Ct);
        Assert.False(build.IsCompleted, "the oak was built before it could be cancelled");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build.WaitAsync(Wait, Ct));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"stopped after {watch.Elapsed}");
        Assert.Equal(PinnedTag(), (await thread.InfoAsync(Ct).WaitAsync(Wait, Ct)).Tag);
    }

    /// <summary>Reports on the reporting thread (no posting, so the last report is in when the export ends).</summary>
    private sealed class Inline(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    /// <summary>Reads a PNG's size and inflates its image data (the filtered rows, a filter byte before each).</summary>
    private static (int Width, int Height, int RowBytes, byte[] Filtered) Decode(byte[] png)
    {
        int width = 0, height = 0, rowBytes = 0;
        using var idat = new MemoryStream();
        for (int at = 8; at < png.Length;)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            string type = Encoding.ASCII.GetString(png, at + 4, 4);
            if (type == "IHDR")
            {
                width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at + 8));
                height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at + 12));
                int depth = png[at + 16];
                int channels = png[at + 17] switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, var c => throw new InvalidDataException($"colour type {c}") };
                rowBytes = ((width * channels * depth) + 7) / 8;
            }
            else if (type == "IDAT")
            {
                idat.Write(png, at + 8, length);
            }

            at += 12 + length;
        }

        idat.Position = 0;
        using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
        using var pixels = new MemoryStream();
        inflate.CopyTo(pixels);
        return (width, height, rowBytes, pixels.ToArray());
    }
}
