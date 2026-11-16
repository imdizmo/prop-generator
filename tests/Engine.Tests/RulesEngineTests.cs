using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using PropGenerator.Engine.Tests.Support;
using Xunit;

namespace PropGenerator.Engine.Tests;

/// <summary>The rules runtime: synchronous calls for the panel, never behind a build.</summary>
[Collection(MeasuredAlone.Name)]
public sealed class RulesEngineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly int[] Turn = [0, 20, 0];

    private static readonly double[] Ahead = [0.5, 0, 0];

    private static readonly double[] Spin = [0, 0.3, 0];

    private static readonly int[] Unscaled = [1, 1, 1];

    [Fact]
    public void ShownRowsFollowTheValues()
    {
        using var rules = new RulesEngine(Standin.Options());
        Assert.Equal(["height"], rules.Shown("painted-tree", """{"height":8}""").Select(r => r.Key));
        IReadOnlyList<EngineShownRow> tall = rules.Shown("painted-tree", """{"height":12}""");
        Assert.Equal(["height", "spread"], tall.Select(r => r.Key));
        Assert.Equal("Crown", tall[1].Group);
        Assert.Equal("Spread", tall[1].Label);
    }

    [Fact]
    public void AShownRowWithNoGroupReadsNull()
    {
        using var rules = new RulesEngine(Standin.Options());
        IReadOnlyList<EngineShownRow> rows = rules.Shown("painted-tree", """{"height":25}""");
        Assert.Equal("Crown", rows[1].Group);
        EngineShownRow note = rows.Single(r => r.Key == "note");
        Assert.Null(note.Group);
    }

    [Fact]
    public void InfoPresetsAndOnePresetComeFromTheRulesEngine()
    {
        using var rules = new RulesEngine(Standin.Options());
        Assert.Equal("v0.4.2", rules.Info(Ct).Tag);
        Assert.Equal(3, rules.Presets(Ct).Presets.Count);
        EnginePreset? retired = rules.Preset("wide-canopy", Ct);
        Assert.NotNull(retired);
        Assert.True(retired.Retired);
        Assert.False(rules.Preset("oak", Ct)?.Retired);
        Assert.Null(rules.Preset("no-such-preset", Ct));
    }

    [Fact]
    public void AdjustValidateTraitsAndComponentsAreRead()
    {
        using var rules = new RulesEngine(Standin.Options());
        IReadOnlyDictionary<string, JsonElement> patch = rules.Adjust("painted-tree", """{"palette":"oak"}""", """{"palette":"birch"}""");
        Assert.Equal("#d8d2c4", patch["barkDeep"].GetString());
        Assert.Empty(rules.Adjust("painted-tree", "{}", """{"palette":"oak"}"""));

        EngineValidation bad = rules.Validate("painted-tree", """{"height":"tall"}""");
        Assert.False(bad.Valid);
        Assert.Equal("height", Assert.Single(bad.Issues).Key);
        Assert.Equal(8, bad.Parameters.GetProperty("height").GetDouble());
        Assert.True(rules.Validate("painted-tree", """{"height":9}""").Valid);

        EngineTraits traits = rules.Traits("painted-tree", "{}");
        Assert.True(traits.FarAway);
        Assert.True(traits.Sways);
        Assert.False(rules.Traits("cut-rock", "{}").Sways);

        using var components = JsonDocument.Parse(rules.Components("painted-tree", "{}", 7));
        Assert.Equal("$asset", components.RootElement[0].GetProperty("parameters").GetString());
    }

    [Fact]
    public async Task RuleCallsDoNotWaitBehindALongBuild()
    {
        using var thread = new EngineThread(Standin.Options());
        using var rules = new RulesEngine(Standin.Options());
        using var cancel = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        Task<object?> slow = thread.Enqueue(EngineJobKind.Live, (e, token) =>
        {
            started.Set();
            return e.Call("Test.slow", token);
        }, cancel.Token);
        Assert.True(started.Wait(TimeSpan.FromSeconds(20), Ct));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Single(rules.Shown("painted-tree", """{"height":8}"""));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
        Assert.False(slow.IsCompleted);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow.WaitAsync(TimeSpan.FromSeconds(20), Ct));
    }

    [Fact]
    public void TreeAndSeedOpsAreRefused()
    {
        // They make every part again: the UI thread never runs them (EngineThread.SceneOpAsync does). Refused before
        // the engine is asked, so the stand-in, which has no sceneOp, is enough.
        using var rules = new RulesEngine(Standin.Options());
        Assert.Throws<ArgumentException>(() => rules.SceneOp(Standin.Scene, """{"kind":"tree","patch":{"height":9}}""", "low-poly", Ct));
        Assert.Throws<ArgumentException>(() => rules.SceneOp(Standin.Scene, """{"kind":"seed","seed":7}""", "low-poly", Ct));
        Assert.Throws<ArgumentException>(() => rules.SceneOp(Standin.Scene, """{"componentId":"x"}""", "low-poly", Ct));
        Assert.Throws<ArgumentException>(() => rules.SceneOp(Standin.Scene, "not json", "low-poly", Ct));
    }

    [Fact]
    public void EveryLocalSceneOpOnTheBiggestTreeIsUnder50MsWarm()
    {
        Assert.SkipUnless(EngineOptions.HasEmbeddedBundle, "Resources/engine.js is not built yet (tools/engine).");
        using var rules = new RulesEngine();

        // The painted-tree preset with the most parts (ancient on v0.4.5, 63 parts).
        (EnginePreset preset, JsonArray parts) = rules.Presets(Ct).Presets
            .Where(p => p.GeneratorId == "painted-tree")
            .Select(p => (p, JsonNode.Parse(rules.Components(p.GeneratorId, p.Parameters.GetRawText(), p.Seed))!.AsArray()))
            .MaxBy(t => t.Item2.Count);
        var scene = JsonNode.Parse(EngineFixture.SceneOf(preset))!.AsObject();
        scene["components"] = parts.DeepClone();
        string tree = scene.ToJsonString();
        string heavy = parts.First(c => (string?)c!["generatorId"] == "painted-limb"
            && System.Text.RegularExpressions.Regex.IsMatch((string?)c["parameters"]?["key"] ?? "", @"^(l\d+|c\d+s\d+)$"))!["id"]!.GetValue<string>();
        JsonNode clump = parts.First(c => (string?)c!["generatorId"] == "painted-clump")!;
        string clumpId = clump["id"]!.GetValue<string>();
        string bark = parts.First(c => (string?)c!["generatorId"] == "painted-bark")!["id"]!.GetValue<string>();
        double[] at = [.. clump["transform"]!["position"]!.AsArray().Select(v => v!.GetValue<double>())];
        at[0] += 0.25;
        string patched = rules.SceneOp(tree, Op(new { kind = "part", componentId = heavy, action = new { kind = "patch", patch = new { turn = Turn } } }), "low-poly", Ct)!;

        (string Scene, string Op)[] ops =
        [
            (tree, Op(new { kind = "part", componentId = heavy, action = new { kind = "patch", patch = new { turn = Turn } } })),
            (tree, Op(new { kind = "part", componentId = clumpId, action = new { kind = "move", position = at } })),
            (tree, Op(new { kind = "part", componentId = clumpId, action = new { kind = "remove" } })),
            (tree, Op(new { kind = "break", componentId = heavy })),
            (patched, Op(new { kind = "restore", componentId = heavy })),
            (tree, Op(new { kind = "remove", componentId = clumpId })),
            (tree, Op(new { kind = "position", componentId = clumpId, position = at })),
            (tree, Op(new { kind = "transform", componentId = bark, transform = new { position = Ahead, rotation = Spin, scale = Unscaled } })),
            (tree, Op(new { kind = "parameters", componentId = clumpId, patch = new { sizeScale = 1.2 } })),
            (tree, Op(new { kind = "copy", componentId = clumpId })),
        ];
        var report = new List<string>();
        foreach ((string before, string op) in ops)
        {
            _ = rules.SceneOp(before, op, "low-poly", Ct);
            double[] times = [.. Enumerable.Range(0, 5).Select(i =>
            {
                var watch = Stopwatch.StartNew();
                _ = rules.SceneOp(before, op, "low-poly", Ct);
                return watch.Elapsed.TotalMilliseconds;
            }).Order()];
            report.Add($"{op[..Math.Min(40, op.Length)]}: {times[2]:0.0} ms");
            Assert.True(times[2] < 50, $"{preset.Id} ({parts.Count} parts): {op} took {times[2]:0.0} ms (median of 5, warm)");
        }

        var facts = Stopwatch.StartNew();
        _ = rules.PartFacts(tree, heavy, Ct);
        Assert.True(facts.Elapsed.TotalMilliseconds < 50, $"partFacts took {facts.Elapsed.TotalMilliseconds:0.0} ms");
        TestContext.Current.TestOutputHelper?.WriteLine($"{preset.Id} ({parts.Count} parts): {string.Join("; ", report)}");

        static string Op(object op) => JsonSerializer.Serialize(op);
    }

    [Fact]
    public void CachedRuleAnswersAreKeyedByAHashAndCapped()
    {
        Assert.SkipUnless(EngineOptions.HasEmbeddedBundle, "Resources/engine.js is not built yet (tools/engine).");
        using var rules = new RulesEngine();
        string huge = new('x', 1 << 20);
        string key = RulesEngine.KeyOf("DamageSettings", ["painted-tree", huge]);
        Assert.True(key.Length < 100, key);
        Assert.NotEqual(key, RulesEngine.KeyOf("DamageSettings", ["painted-tre", "e" + huge]));

        IReadOnlyList<EngineDamageSetting> first = rules.DamageSettings("painted-tree", "{}", Ct);
        Assert.Same(first, rules.DamageSettings("painted-tree", "{}", Ct));
        for (int i = 0; i < RulesEngine.CacheCap + 10; i++)
        {
            _ = rules.ExportNames($"Prop {i}", Ct);
        }

        Assert.Equal(RulesEngine.CacheCap, rules.Cached);
        Assert.NotSame(first, rules.DamageSettings("painted-tree", "{}", Ct));
    }

    [Fact]
    public void RuleCallsFromManyThreadsAreSerialised()
    {
        using var rules = new RulesEngine(Standin.Options());
        Parallel.For(0, 64, i => Assert.Single(rules.Shown("painted-tree", $$"""{"height":{{i % 10}}}""")));
    }
}
