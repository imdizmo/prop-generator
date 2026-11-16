using System.Diagnostics;
using PropGenerator.Engine.Tests.Support;
using Xunit;

namespace PropGenerator.Engine.Tests;

/// <summary>
/// The smoke check to run after every upgrade of the bundle: every preset the engine lists builds at the three levels
/// and exports (three LODs, collision, wind), each step well under the engine's 60 s job limit, and the runtime's size
/// is reported. The presets come from the bundle itself, not from the goldens, so a new release is checked as it is.
/// </summary>
public sealed class EngineSmokeTests(AppEngine fixture) : IClassFixture<AppEngine>
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private static readonly Lazy<List<string>> PresetIds = new(() =>
    {
        if (!EngineOptions.HasEmbeddedBundle)
        {
            return ["(no bundle)"];
        }

        using var engine = new EasyPropsEngine();
        return [.. engine.Presets().Presets.Select(p => p.Id)];
    });

    public static TheoryData<string> Presets => [.. PresetIds.Value];

    [Theory]
    [MemberData(nameof(Presets))]
    public void EveryPresetBuildsAtEveryLevelAndExports(string id)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        EnginePreset preset = fixture.Presets.Presets.Single(p => p.Id == id);
        string scene = EngineFixture.SceneOf(preset);
        EngineInfo info = fixture.Engine.Info(ct);
        var report = new List<string>();
        foreach (double detail in info.LevelShares)
        {
            var watch = Stopwatch.StartNew();
            EngineMesh mesh = fixture.Engine.Build(scene, "low-poly", detail, cancel: ct);
            Assert.True(watch.Elapsed < Limit, $"{id} at {detail} took {watch.Elapsed}");
            Assert.NotEmpty(mesh.Parts);
            Assert.All(mesh.Parts, p => Assert.Equal(p.VertexCount * 3, p.Positions.Length));
            report.Add($"build {detail}: {watch.ElapsedMilliseconds} ms, {mesh.Parts.Sum(p => p.Faces)} faces");
        }

        string name = id.Replace('-', '_');
        string settings = $$"""{"name":"{{name}}","engine":"godot","levels":{{info.LevelShares.Count}},"collision":true,"wind":true,"join":false}""";
        var exportWatch = Stopwatch.StartNew();
        IReadOnlyList<EngineFile> files = fixture.Engine.ExportPackage(scene, "low-poly", settings, ct);
        Assert.True(exportWatch.Elapsed < Limit, $"{id}: the export took {exportWatch.Elapsed}");
        Assert.Contains(files, f => f.Name == $"{name}.asset.json");
        Assert.Equal(info.LevelShares.Count, files.Count(f => f.Name.StartsWith($"{name}_LOD", StringComparison.Ordinal) && f.Name.EndsWith(".glb", StringComparison.Ordinal)));
        Assert.All(files.Where(f => f.Name.EndsWith(".glb", StringComparison.Ordinal)), f => Assert.Equal("glTF"u8.ToArray(), f.Bytes[..4]));
        report.Add($"export: {exportWatch.ElapsedMilliseconds} ms, {files.Count} files, {files.Sum(f => (long)f.Bytes.Length) >> 10} KiB");

        long now = fixture.Sample();
        report.Add($"engine now {now >> 20} MiB, peak so far {fixture.PeakBytes >> 20} MiB (heap + external, after each call)");
        TestContext.Current.TestOutputHelper?.WriteLine($"{id}: {string.Join("; ", report)}");
    }
}
