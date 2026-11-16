using System.Text.Json;
using System.Text.Json.Nodes;
using PropGenerator.Engine.Tests.Support;
using Xunit;

namespace PropGenerator.Engine.Tests;

/// <summary>
/// The host's phase 3 calls on easy-props' own bundle: upstream's scene operations (the cheap ones on the rules
/// engine, tree and seed on the engine thread), earlier looks, pieces, Make it fit, set colours and damaged versions.
/// </summary>
public sealed class EngineSceneOpsTests(BundleCalls bundle) : IClassFixture<BundleCalls>
{
    private const string Style = "low-poly";

    /// <summary>The seven rock settings upstream fills at 0 in a file saved before they existed.</summary>
    private static readonly string[] Seven = ["grain", "rounded", "blend", "dents", "layers", "overhang", "lichen"];

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(120);

    private static readonly int[] Turn = [0, 20, 0];

    private static readonly double[] Ahead = [0.5, 0, 0];

    private static readonly double[] Spin = [0, 0.3, 0];

    private static readonly int[] Unscaled = [1, 1, 1];

    private static readonly double[] Aside = [0.4, 0, 0];

    private static readonly double[] HalfSpin = [0, 0.5, 0];

    private static readonly string[] MineIds = ["mine-1"];

    private static readonly int[] Up = [0, 1, 0];

    private static readonly string[] TwoTrees = ["oak", "beech"];

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryOpReturnsAScene()
    {
        // Oak: its parts are painted, so they are edited through part actions and break; copy and position are
        // refused (ARefusedOpIsNull). A bush and a boulder take the plain ops.
        string oak = bundle.Scene("oak");
        JsonArray parts = bundle.Components("oak");
        string heavy = Id(parts, c => Gen(c) == "painted-limb" && IsHeavyKey(c["parameters"]?["key"]?.GetValue<string>()));
        string clump = Id(parts, c => Gen(c) == "painted-clump");
        string bark = Id(parts, c => Gen(c) == "painted-bark");
        JsonNode clumpAt = parts.Single(c => Id(c!) == clump)!["transform"]!["position"]!;
        double[] moved = [clumpAt[0]!.GetValue<double>() + 0.25, clumpAt[1]!.GetValue<double>(), clumpAt[2]!.GetValue<double>()];

        string patched = Op(oak, new { kind = "part", componentId = heavy, action = new { kind = "patch", patch = new { turn = Turn } } });
        Op(oak, new { kind = "part", componentId = clump, action = new { kind = "move", position = moved } });
        Op(oak, new { kind = "part", componentId = clump, action = new { kind = "remove" } });
        Op(oak, new { kind = "part", componentId = heavy, action = new { kind = "break" } });
        Op(patched, new { kind = "part", componentId = heavy, action = new { kind = "restore" } });
        Op(oak, new { kind = "break", componentId = heavy });
        Op(patched, new { kind = "restore", componentId = heavy });
        Op(oak, new { kind = "remove", componentId = clump });
        Op(oak, new { kind = "position", componentId = clump, position = moved });
        Op(oak, new { kind = "transform", componentId = bark, transform = new { position = Ahead, rotation = Spin, scale = Unscaled } });
        await HeavyOps(oak, "painted-tree");

        foreach (string id in new[] { "round-bush", "boulder" })
        {
            string scene = bundle.Scene(id);
            string first = Id(bundle.Components(id)[0]!);
            string edited = Op(scene, new { kind = "parameters", componentId = first, patch = new { seed = 5 } });
            Op(scene, new { kind = "copy", componentId = first });
            Op(scene, new { kind = "copy", componentId = first, ids = MineIds });
            Op(scene, new { kind = "remove", componentId = first });
            Op(edited, new { kind = "restore", componentId = first });
            Op(scene, new { kind = "position", componentId = first, position = Aside });
            Op(scene, new { kind = "transform", componentId = first, transform = new { position = Aside, rotation = HalfSpin, scale = Unscaled } });
            await HeavyOps(scene, JsonNode.Parse(scene)!["generatorId"]!.GetValue<string>());
        }

        string Op(string scene, object op)
        {
            string? after = bundle.Rules.SceneOp(scene, JsonSerializer.Serialize(op), Style, Ct);
            Assert.True(after is not null, $"refused: {JsonSerializer.Serialize(op)}");
            Assert.NotEqual(scene, after);
            Assert.Equal(JsonNode.Parse(scene)!["generatorId"]!.GetValue<string>(), JsonNode.Parse(after)!["generatorId"]!.GetValue<string>());
            return after;
        }
    }

    [Fact]
    public void ARefusedOpIsNull()
    {
        string oak = bundle.Scene("oak");
        JsonArray parts = bundle.Components("oak");
        string heavy = Id(parts, c => Gen(c) == "painted-limb" && IsHeavyKey(c["parameters"]?["key"]?.GetValue<string>()));

        Assert.Null(bundle.Rules.SceneOp(oak, JsonSerializer.Serialize(new { kind = "position", componentId = heavy, position = Up }), Style, Ct));
        Assert.Null(bundle.Rules.SceneOp(oak, JsonSerializer.Serialize(new { kind = "copy", componentId = heavy }), Style, Ct));
        Assert.Null(bundle.Rules.SceneOp(oak, """{"kind":"remove","componentId":"no-such-part"}""", Style, Ct));
        string boulder = bundle.Scene("boulder");
        string rock = Id(bundle.Components("boulder")[0]!);
        Assert.Null(bundle.Rules.SceneOp(boulder, JsonSerializer.Serialize(new { kind = "break", componentId = rock }), Style, Ct));

        // A broken limb cannot break again: upstream rebuilds it rather than refusing, so the facts say so.
        string broken = bundle.Rules.SceneOp(oak, JsonSerializer.Serialize(new { kind = "break", componentId = heavy }), Style, Ct)!;
        Assert.Equal(new EnginePartFacts { Painted = true, Heavy = true, CanBreak = true }, bundle.Rules.PartFacts(oak, heavy, Ct));
        Assert.False(bundle.Rules.PartFacts(broken, heavy, Ct).CanBreak);
        Assert.Equal(new EnginePartFacts(), bundle.Rules.PartFacts(oak, "no-such-part", Ct));
    }

    [Fact]
    public void EarlierLooksFillsAnOldRock()
    {
        string old = OldRock("boulder");
        using var filled = JsonDocument.Parse(bundle.Rules.EarlierLooks(old, Ct));
        JsonElement parameters = filled.RootElement.GetProperty("parameters");
        Assert.All(Seven, k => Assert.Equal(0, parameters.GetProperty(k).GetDouble()));
        JsonElement[] rocks = [.. filled.RootElement.GetProperty("components").EnumerateArray()
            .Select(c => c.GetProperty("parameters"))
            .Where(p => p.TryGetProperty("rock", out JsonElement r) && r.ValueKind == JsonValueKind.Object)
            .Select(p => p.GetProperty("rock"))];
        Assert.NotEmpty(rocks);
        Assert.All(rocks, r => Assert.All(Seven, k => Assert.Equal(0, r.GetProperty(k).GetDouble())));

        // A rock saved with a grain keeps it.
        JsonObject kept = JsonNode.Parse(bundle.Scene("boulder"))!.AsObject();
        kept["parameters"]!["grain"] = 0.6;
        using var after = JsonDocument.Parse(bundle.Rules.EarlierLooks(kept.ToJsonString(), Ct));
        Assert.Equal(0.6, after.RootElement.GetProperty("parameters").GetProperty("grain").GetDouble());
    }

    [Fact]
    public void EarlierLooksOfACutRockIsTheSevenAtZero()
    {
        EngineEarlierLooks rock = bundle.Rules.EarlierLooksOf("cut-rock", Ct);
        Assert.Equal(Seven, rock.Asset.Keys);
        Assert.Equal(Seven, rock.Rock.Keys);
        Assert.All(rock.Asset.Values.Concat(rock.Rock.Values), v => Assert.Equal(0, v.GetDouble()));
        foreach (EngineGenerator generator in bundle.Rules.Info(Ct).Generators.Where(g => g.Id != "cut-rock"))
        {
            Assert.True(bundle.Rules.EarlierLooksOf(generator.Id, Ct).IsEmpty, generator.Id);
        }
    }

    [Fact]
    public void TheCFillEqualsEarlierLooksOnSampleScenes()
    {
        // The fill the app does in C# (once per generator, no engine call per history step) against upstream's
        // parseProject on the same scene: old rocks with and without parts, a rock with some of the seven, a part
        // whose rock is "$asset", a new rock, and kinds upstream leaves alone.
        JsonObject partial = JsonNode.Parse(OldRock("mossy-boulder"))!.AsObject();
        partial["parameters"]!["grain"] = 0.4;
        partial["parameters"]!["lichen"] = 0.7;
        JsonObject noParts = JsonNode.Parse(OldRock("split-boulder"))!.AsObject();
        _ = noParts.Remove("components");
        JsonObject assetRock = JsonNode.Parse(OldRock("boulder"))!.AsObject();
        assetRock["components"]![0]!["parameters"]!["rock"] = "$asset";
        string[] scenes =
        [
            OldRock("boulder"), OldRock("cliff-face"), partial.ToJsonString(), noParts.ToJsonString(), assetRock.ToJsonString(),
            bundle.SceneWithParts("boulder"), bundle.SceneWithParts("oak"), bundle.SceneWithParts("round-bush"), bundle.Scene("saguaro"),
        ];
        foreach (string scene in scenes)
        {
            string generator = JsonNode.Parse(scene)!["generatorId"]!.GetValue<string>();
            JsonNode ours = JsonNode.Parse(bundle.Rules.EarlierLooksOf(generator, Ct).FillScene(scene))!;
            JsonNode theirs = JsonNode.Parse(bundle.Rules.EarlierLooks(scene, Ct))!;
            Assert.True(JsonNode.DeepEquals(ours["parameters"], theirs["parameters"]), $"{generator} parameters: {ours["parameters"]!.ToJsonString()} vs {theirs["parameters"]!.ToJsonString()}");
            JsonNode?[] ourRocks = Rocks(ours);
            JsonNode?[] theirRocks = Rocks(theirs);
            Assert.Equal(theirRocks.Length, ourRocks.Length);
            for (int i = 0; i < ourRocks.Length; i++)
            {
                Assert.True(JsonNode.DeepEquals(ourRocks[i], theirRocks[i]), $"{generator} part {i}: {ourRocks[i]?.ToJsonString()} vs {theirRocks[i]?.ToJsonString()}");
            }
        }

        static JsonNode?[] Rocks(JsonNode scene) =>
            scene["components"] is JsonArray parts ? [.. parts.Select(c => c?["parameters"]?["rock"])] : [];
    }

    [Fact]
    public async Task PiecesOfOakAreFourAndOfAPalmThree()
    {
        IReadOnlyList<string> oak = await bundle.Thread.PiecesOfferedAsync(bundle.Scene("oak"), Ct).WaitAsync(Wait, Ct);
        Assert.Equal(["stump", "log", "branch", "litter"], oak);
        IReadOnlyList<string> palm = await bundle.Thread.PiecesOfferedAsync(bundle.Scene("coconut-palm-painted"), Ct).WaitAsync(Wait, Ct);
        Assert.Equal(3, palm.Count);
        Assert.DoesNotContain("branch", palm);
        Assert.Empty(await bundle.Thread.PiecesOfferedAsync(bundle.Scene("boulder"), Ct).WaitAsync(Wait, Ct));

        EnginePieceLabels labels = bundle.Rules.PieceLabels(Ct);
        Assert.Equal(["stump", "log", "branch", "litter"], labels.Kinds.Select(k => k.Kind));
        Assert.Equal("Stump", labels.Of("stump")?.Name);
        Assert.All(labels.Kinds, k => Assert.False(string.IsNullOrWhiteSpace(k.Help)));

        EnginePiece stump = await bundle.Thread.MakePieceAsync(bundle.Scene("oak"), "Oak", "stump", Style, Ct).WaitAsync(Wait, Ct);
        Assert.Equal("stump", stump.Kind);
        Assert.Contains("Oak", stump.Name, StringComparison.Ordinal);
        Assert.Equal("tree-stump", JsonNode.Parse(stump.Scene)!["generatorId"]!.GetValue<string>());
    }

    [Fact]
    public async Task MakeItFitBringsOakUnderBudget()
    {
        string oak = bundle.Scene("oak");
        EngineFaceCounts counts = await bundle.Thread.CountFacesAsync(oak, Style, Ct).WaitAsync(Wait, Ct);
        Assert.True(counts.Wood > 0 && counts.Leaves > 0, $"{counts}");
        IReadOnlyList<EngineFitControl> controls = bundle.Rules.FitControls(oak, Ct);
        Assert.NotEmpty(controls);
        Assert.All(controls, c => Assert.True(c.Min <= c.Limit && c.Limit <= c.Max && c.Label.Length > 0, $"{c}"));

        int budget = (int)Math.Round(counts.Total * 0.6);
        EngineFit fit = await bundle.Thread.MakeItFitAsync(oak, Style, budget, deadline: Timeout.InfiniteTimeSpan, cancel: Ct).WaitAsync(Wait, Ct);
        Assert.True(fit.Fits, $"{fit.Faces} faces for {budget}");
        Assert.True(fit.Faces <= budget);
        Assert.Equal(budget, fit.Budget);
        Assert.False(fit.Stopped);
        Assert.NotEmpty(fit.Changes);
        Assert.All(fit.Changes, c => Assert.True(c.To < c.From, $"{c}"));
        Assert.False(string.IsNullOrWhiteSpace(fit.Description));
        EngineFaceCounts after = await bundle.Thread.CountFacesAsync(fit.Scene, Style, Ct).WaitAsync(Wait, Ct);
        Assert.True(after.Total <= budget, $"{after.Total} faces after, budget {budget}");
    }

    [Fact]
    public async Task ColourGroupsOfTwoTreesShareThePalette()
    {
        string props = "[" + string.Join(",", TwoTrees.Select(id =>
            $$"""{"generatorId":"painted-tree","parameters":{{JsonNode.Parse(bundle.Scene(id))!["parameters"]!.ToJsonString()}}}""")) + "]";
        IReadOnlyList<EngineColourGroup> groups = bundle.Rules.ColourGroups(props, Ct);
        EngineColourGroup palette = Assert.Single(groups, g => g.Key == "palette");
        Assert.Equal(2, palette.Props);
        Assert.StartsWith("Tree: ", palette.Label, StringComparison.Ordinal);
        Assert.Contains(palette.Options, o => o.Value == palette.Usual);

        string groupsJson = JsonSerializer.Serialize(groups, Web);
        using var usual = JsonDocument.Parse(bundle.Rules.UsualColours(groupsJson, Ct));
        Assert.Equal(palette.Usual, usual.RootElement.GetProperty(palette.Id).GetString());

        string other = palette.Options.First(o => o.Value != palette.Usual).Value;
        string recoloured = await bundle.Thread.InSetColoursAsync(bundle.Scene("oak"), JsonSerializer.Serialize(new Dictionary<string, string> { [palette.Id] = other }), Style, cancel: Ct)
            .WaitAsync(Wait, Ct);
        Assert.Equal(other, JsonNode.Parse(recoloured)!["parameters"]!["palette"]!.GetValue<string>());
    }

    [Fact]
    public async Task PageDamagesOfOakAreEightVersions()
    {
        string oak = bundle.Scene("oak");
        JsonNode node = JsonNode.Parse(oak)!;
        uint seed = node["seed"]!.GetValue<uint>();
        IReadOnlyList<EngineDamageSetting> settings = bundle.Rules.DamageSettings("painted-tree", node["parameters"]!.ToJsonString(), Ct);
        Assert.NotEmpty(settings);
        Assert.All(settings, s => Assert.Equal("Damage", s.Group));
        Assert.Empty(bundle.Rules.DamageSettings("grass", "{}", Ct));

        string settingsJson = EngineDamageSetting.ToJson(settings);
        IReadOnlyList<EngineVersion> damages = bundle.Rules.PageDamages(seed, 0, settingsJson, Ct);
        Assert.Equal(8, damages.Count);
        Assert.All(damages, d => Assert.Equal(seed, d.Seed));
        Assert.All(damages, d => Assert.NotNull(d.Damage));
        Assert.Equal(8, damages.Select(d => bundle.Rules.VersionKey(d.ToJson(), Ct)).Distinct().Count());
        Assert.False(string.IsNullOrWhiteSpace(bundle.Rules.DamageNames(damages[0].ToJson(), settingsJson, Ct)));
        Assert.Equal("5", bundle.Rules.VersionKey(new EngineVersion { Seed = 5 }.ToJson(), Ct));

        IReadOnlyList<uint> seeds = bundle.Rules.PageSeeds(seed, 0, Ct);
        Assert.Equal(8, seeds.Distinct().Count());

        string damaged = await bundle.Thread.VersionSceneAsync(oak, damages[0].ToJson(), Style, cancel: Ct).WaitAsync(Wait, Ct);
        JsonNode values = JsonNode.Parse(damaged)!["parameters"]!;
        Assert.All(damages[0].Damage!, d => Assert.Equal(d.Value, values[d.Key]!.GetValue<double>()));
        string reseeded = await bundle.Thread.VersionSceneAsync(oak, new EngineVersion { Seed = seeds[0] }.ToJson(), Style, cancel: Ct).WaitAsync(Wait, Ct);
        Assert.Equal(seeds[0], JsonNode.Parse(reseeded)!["seed"]!.GetValue<uint>());

        string choices = JsonSerializer.Serialize(new[] { new { kind = "versions", name = "Old oak", versions = new[] { new { seed = 1 }, new { seed = 2 }, new { seed = 3 } } } });
        IReadOnlyList<EngineBatchItem> items = await bundle.Thread.BatchItemsAsync(choices, 1, """{"style":"low-poly"}""", Ct).WaitAsync(Wait, Ct);
        Assert.Equal(["old_oak_01", "old_oak_02", "old_oak_03"], items.Select(i => i.Name));
        Assert.Equal(new EngineExportNames { Folder = "Old Oak #1", Export = "old_oak_1" }, bundle.Rules.ExportNames("Old Oak #1", Ct));
    }

    [Fact]
    public async Task ASetListNamesItsMaterials()
    {
        string[] boulders = [bundle.Scene("boulder"), JsonNode.Parse(bundle.Scene("boulder"))!.AsObject().Also(o => o["seed"] = 402).ToJsonString()];
        string props = "[" + string.Join(",", boulders.Select(b => $$"""{"generatorId":"cut-rock","parameters":{{JsonNode.Parse(b)!["parameters"]!.ToJsonString()}}}""")) + "]";
        string groups = JsonSerializer.Serialize(bundle.Rules.ColourGroups(props, Ct), Web);
        string colours = bundle.Rules.UsualColours(groups, Ct);
        string set = bundle.Rules.ExportNames("My set", Ct).Export;
        var entries = new JsonArray();
        for (int i = 0; i < boulders.Length; i++)
        {
            string name = $"boulder_0{i + 1}";
            string settings = $$"""{"name":"{{name}}","engine":"godot","levels":3,"collision":true,"wind":true,"far":false,"look":"lit","materials":false,"setName":"{{set}}"}""";
            IReadOnlyList<EngineFile> files = await bundle.Thread.ExportAsync(boulders[i], Style, settings, cancel: Ct).WaitAsync(Wait, Ct);
            var list = new JsonArray();
            foreach (EngineFile f in files)
            {
                var file = new JsonObject { ["name"] = f.Name };
                if (f.Name.EndsWith(".glb", StringComparison.Ordinal))
                {
                    file["json"] = GlbJson(f.Bytes);
                }

                list.Add(file);
            }

            entries.Add(new JsonObject { ["name"] = name, ["files"] = list });
        }

        EngineFile setList = await bundle.Thread.SetListAsync(set, entries.ToJsonString(), groups, colours, Ct).WaitAsync(Wait, Ct);
        Assert.Equal("my_set.set.json", setList.Name);
        using var text = JsonDocument.Parse(setList.Bytes);
        Assert.Equal("my_set", text.RootElement.GetProperty("set").GetString());
        Assert.All(text.RootElement.GetProperty("materials").EnumerateArray(), m => Assert.StartsWith("my_set_", m.GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task AWetSnowyBoulderHasItsOwnRoughness()
    {
        JsonObject wet = JsonNode.Parse(bundle.Scene("snowy-boulder"))!.AsObject();
        wet["parameters"]!["wet"] = 0.6;
        EngineMesh mesh = await bundle.Thread.BuildAsync(wet.ToJsonString(), Style, 1, cancel: Ct).WaitAsync(Wait, Ct);
        Assert.Contains(mesh.Parts, p => p.Surface.Roughness is < 1);
        EngineMesh dry = await bundle.Thread.BuildAsync(bundle.Scene("boulder"), Style, 1, cancel: Ct).WaitAsync(Wait, Ct);
        Assert.All(dry.Parts, p => Assert.Null(p.Surface.Roughness));
    }

    private async Task HeavyOps(string scene, string generatorId)
    {
        JsonElement spec = bundle.Rules.Parameters(generatorId).Specs.First(s => s.GetProperty("kind").GetString() == "number" && s.GetProperty("key").GetString() != "seed");
        string key = spec.GetProperty("key").GetString()!;
        double to = spec.GetProperty("default").GetDouble() == spec.GetProperty("min").GetDouble() ? spec.GetProperty("max").GetDouble() : spec.GetProperty("min").GetDouble();
        string tree = JsonSerializer.Serialize(new Dictionary<string, object> { ["kind"] = "tree", ["patch"] = new Dictionary<string, double> { [key] = to } });
        string? grown = await bundle.Thread.SceneOpAsync(scene, tree, Style, Ct).WaitAsync(Wait, Ct);
        Assert.True(grown is not null, $"{generatorId}: tree {key} = {to} refused");
        Assert.Equal(to, JsonNode.Parse(grown)!["parameters"]![key]!.GetValue<double>());
        string? seeded = await bundle.Thread.SceneOpAsync(scene, """{"kind":"seed","seed":777}""", Style, Ct).WaitAsync(Wait, Ct);
        Assert.True(seeded is not null, $"{generatorId}: seed refused");
        Assert.Equal(777, JsonNode.Parse(seeded)!["seed"]!.GetValue<int>());
    }

    /// <summary>A rock as a file saved on 0.4.2 has it: none of the seven keys, in the asset or in its parts' rock.</summary>
    private string OldRock(string presetId)
    {
        JsonObject scene = JsonNode.Parse(bundle.SceneWithParts(presetId))!.AsObject();
        JsonObject validated = JsonNode.Parse(bundle.Rules.Validate("cut-rock", scene["parameters"]!.ToJsonString()).Parameters.GetRawText())!.AsObject();
        foreach (string key in Seven)
        {
            _ = scene["parameters"]!.AsObject().Remove(key);
            _ = validated.Remove(key);
        }

        foreach (JsonNode? part in scene["components"]!.AsArray())
        {
            JsonObject parameters = part!["parameters"]!.AsObject();
            if (parameters["rock"] is JsonValue v && v.GetValue<string>() == "$asset")
            {
                parameters["rock"] = validated.DeepClone();
            }
            else if (parameters["rock"] is JsonObject rock)
            {
                foreach (string key in Seven)
                {
                    _ = rock.Remove(key);
                }
            }
        }

        return scene.ToJsonString();
    }

    private static string GlbJson(byte[] glb)
    {
        int length = BitConverter.ToInt32(glb, 12);
        return System.Text.Encoding.UTF8.GetString(glb, 20, length).TrimEnd(' ', '\0');
    }

    private static string Id(JsonNode c) => c["id"]!.GetValue<string>();

    private static string Id(JsonArray parts, Func<JsonNode, bool> where) => Id(parts.First(c => where(c!))!);

    private static string? Gen(JsonNode c) => c["generatorId"]?.GetValue<string>();

    /// <summary>Upstream's heavy limbs: the trunk's own limbs (l0) and their big branches (c0s1).</summary>
    private static bool IsHeavyKey(string? key) => key is not null && System.Text.RegularExpressions.Regex.IsMatch(key, @"^(l\d+|c\d+s\d+)$");
}

/// <summary>One rules engine and one engine thread on the embedded bundle for a test class; skips while it is not built.</summary>
public sealed class BundleCalls : IDisposable
{
    private readonly Lazy<RulesEngine> _rules = new(() => new RulesEngine());
    private readonly Lazy<EngineThread> _thread = new(() => new EngineThread());
    private readonly Lazy<EnginePresets> _presets;

    /// <summary>Loads nothing until a test asks.</summary>
    public BundleCalls() => _presets = new(() => Rules.Presets());

    /// <summary>The rules engine; skips the test when the bundle is not embedded.</summary>
    public RulesEngine Rules
    {
        get
        {
            Assert.SkipUnless(EngineOptions.HasEmbeddedBundle, "Resources/engine.js is not built yet (tools/engine).");
            return _rules.Value;
        }
    }

    /// <summary>The engine thread; skips the test when the bundle is not embedded.</summary>
    public EngineThread Thread
    {
        get
        {
            Assert.SkipUnless(EngineOptions.HasEmbeddedBundle, "Resources/engine.js is not built yet (tools/engine).");
            return _thread.Value;
        }
    }

    /// <summary>A preset's scene, with no components (the engine makes them).</summary>
    public string Scene(string presetId) => EngineFixture.SceneOf(Preset(presetId));

    /// <summary>A preset's components as the engine makes them (compact).</summary>
    public JsonArray Components(string presetId)
    {
        EnginePreset p = Preset(presetId);
        return JsonNode.Parse(Rules.Components(p.GeneratorId, p.Parameters.GetRawText(), p.Seed))!.AsArray();
    }

    /// <summary>A preset's scene with its components written in.</summary>
    public string SceneWithParts(string presetId)
    {
        JsonObject scene = JsonNode.Parse(Scene(presetId))!.AsObject();
        scene["components"] = Components(presetId);
        return scene.ToJsonString();
    }

    /// <summary>A preset by id.</summary>
    public EnginePreset Preset(string id) => _presets.Value.Presets.Single(p => p.Id == id);

    /// <summary>Frees both runtimes.</summary>
    public void Dispose()
    {
        if (_thread.IsValueCreated)
        {
            _thread.Value.Dispose();
        }

        if (_rules.IsValueCreated)
        {
            _rules.Value.Dispose();
        }
    }
}

/// <summary>Small helpers for the tests' JSON.</summary>
internal static class JsonTestExtensions
{
    /// <summary>Does something to a node and returns it.</summary>
    public static T Also<T>(this T node, Action<T> act)
    {
        act(node);
        return node;
    }
}
