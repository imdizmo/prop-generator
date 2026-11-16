using System.Diagnostics;
using System.Text.Json;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using PropGenerator.Nature.Export;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// easy-props' kinds as the engine describes them (settings, starting points, hide rules, own colours, far card), and the rules the panel asks on the UI thread stay off the build thread, quick, bounded and safe when the
/// engine fails. They run alone: they count the build thread's jobs, time the rules and put a recipe double in place.
/// </summary>
[Collection(MeshRecipesAlone.Name)]
public sealed class EngineKindDataTests
{
    public static TheoryData<string> Kinds => [.. EngineKinds.All.Select(k => k.Id)];

    private static EngineKindData Data(string id) => new(EngineKinds.Find(id)!, EngineHost.Default.Catalog, EngineHost.Default.Rules);

    // The engine's own settings (the port's are 0.4.2's): upstream's specs in its parameters() order, then its colour
    // rows, then the seed, each with upstream's label and group; the Weather and Damage groups where upstream has them.
    [Theory]
    [MemberData(nameof(Kinds))]
    public void SpecsFollowUpstreamOrderThenColoursThenSeed(string id)
    {
        EngineParameters parameters = EngineHost.Default.Catalog.Parameters(id);
        JsonElement[] drawn = [.. parameters.Specs.Concat(parameters.ColourRows)
            .Where(s => s.GetProperty("kind").GetString() is "number" or "choice" or "colour")];
        IReadOnlyList<ParamSpec> specs = Data(id).Specs;
        Assert.Equal([.. drawn.Select(s => s.GetProperty("key").GetString()!), "seed"], specs.Select(s => s.Key));
        foreach ((JsonElement want, ParamSpec got) in drawn.Zip(specs))
        {
            Assert.Equal(want.TryGetProperty("label", out JsonElement label) ? label.GetString() : got.Key, got.Label);
            Assert.Equal(want.TryGetProperty("group", out JsonElement group) ? group.GetString() : null, got.Group);
        }

        Assert.Equal(EngineKinds.Find(id)!.SeedGroup, specs[^1].Group);
        string[] groups = [.. specs.Select(s => s.Group).OfType<string>().Distinct()];
        bool weather = id is not ("branching-tree" or "palm-tree");
        bool damage = id is "cut-rock" or "cactus" or EngineKinds.PaintedTree;
        Assert.Equal(weather, groups.Contains("Weather"));
        Assert.Equal(damage, groups.Contains("Damage"));
    }

    // The own-colour groups the 0.4.2 port's wrappers had, by kind (as the app saved and read them); a tree's pieces
    // paint the tree's colours (upstream's paintsOwnColours) and the old-style tree and palm have none.
    private static readonly (string Kind, string Set, string Title, string ChoiceKey, string Keys)[] PortsOwnColourGroups =
    [
        ("cut-rock", "stone", "Rocks", "palette", "stoneShadow,stoneDark,stoneLight,stoneHighlight"),
        ("grass", "grass", "Grass", "palette", "grassRoot,grassLow,grassHigh,grassTip"),
        ("grass", "flowers", "Flowers", "flowerColour", "flowerDeep,flowerMiddle,flowerLight"),
        ("plant", "flowers", "Flowers", "palette", "flowerDeep,flowerMiddle,flowerLight"),
        ("bush", "bush-leaves", "Bush leaves", "palette", "leafShadow,leafDark,leafLight,leafHighlight"),
        ("bush", "flowers", "Flowers", "flowerColour", "flowerDeep,flowerMiddle,flowerLight"),
        ("cactus", "flowers", "Flowers", "flowerColour", "flowerDeep,flowerMiddle,flowerLight"),
        ("cactus", "cactus", "Cacti", "palette", "cactusShadow,cactusDark,cactusLight,cactusHighlight,spineColour"),
        ("painted-tree", "tree", "Trees", "palette", "barkDeep,barkLight,leafDeep,leafMid,leafLight"),
    ];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void OwnColourGroupsEqualThePorts(string id)
    {
        IEnumerable<(string, string, string, string)> want = PortsOwnColourGroups.Where(g => g.Kind == id).Select(g => (g.Set, g.Title, g.ChoiceKey, g.Keys));
        IReadOnlyList<OwnColourGroup> got = Data(id).OwnColourGroups;
        Assert.Equal(want, got.Select(g => (g.Set, g.Title, g.ChoiceKey, string.Join(",", g.Keys))));
    }

    [Fact]
    public void FarAwayFollowsUpstream()
    {
        static bool Far(string id, string? key = null, string? value = null)
        {
            EngineKindData data = Data(id);
            var s = Settings.Defaults(data.Specs);
            return data.FarAway(key is null ? s : s.With(key, value!));
        }

        Assert.True(Far("palm-tree"));
        Assert.True(Far("branching-tree"));
        Assert.True(Far("painted-tree"));
        Assert.True(Far("bush"));
        Assert.True(Far("cactus", "kind", "column"));
        Assert.False(Far("cactus", "kind", "barrel"));
        Assert.False(Far("grass"));
        Assert.False(Far("plant"));
        Assert.False(Far("cut-rock"));
    }

    [Fact]
    public void ShownRowsRelabelAPalmFormTree()
    {
        EngineKindData data = Data("painted-tree");
        var crown = Settings.Defaults(data.Specs).With("form", "crown");
        Assert.Null(data.ShownRows(crown));

        IReadOnlyList<ShownRow> palm = data.ShownRows(crown.With("form", "palm"))!;
        Assert.NotNull(palm);
        // Fronds keeps its group under its palm name; droop, a branch row, shapes the fronds under Leaves.
        ShownRow fronds = palm.Single(r => r.Key == "clumpCount");
        Assert.Equal("Fronds", fronds.Label);
        Assert.Equal(data.Specs.Single(s => s.Key == "clumpCount").Group, fronds.Group);
        ShownRow arch = palm.Single(r => r.Key == "droop");
        Assert.Equal("Frond arch", arch.Label);
        Assert.Equal("Branches", data.Specs.Single(s => s.Key == "droop").Group);
        Assert.Equal("Leaves", arch.Group);
        Assert.DoesNotContain(palm, r => r.Key == "limbCount");
    }

    [Fact]
    public void RuleCallsNeverQueueOnTheBuildThread()
    {
        EngineThread builds = EngineHost.Default.Builds;
        EngineJobKind[] kinds = Enum.GetValues<EngineJobKind>();
        long[] before = [.. kinds.Select(builds.JobsQueued)];
        foreach (EngineKind kind in EngineKinds.All)
        {
            EngineKindData data = Data(kind.Id);
            var s = Settings.Defaults(data.Specs);
            _ = data.Specs;
            _ = data.StartingPoints;
            _ = data.DetailShares;
            _ = data.LowPoly;
            _ = data.Version;
            _ = data.OwnColourGroups;
            _ = data.HiddenKeys(s);
            _ = data.ShownRows(s);
            _ = data.FarAway(s);
            _ = data.SwaysEvenly(s);
            ParamSpec? choice = data.Specs.FirstOrDefault(p => p.Type == "choice" && p.Options!.Any(o => o.Value == "custom"));
            _ = choice is null ? s : data.Adjust(s, s.With(choice.Key, "custom"));
        }

        Assert.Equal(before, kinds.Select(builds.JobsQueued));
    }

    [Fact]
    public void ADragOf100TicksStaysUnder100MsOfRuleCalls()
    {
        EngineKindData data = Data("cut-rock");
        var s = Settings.Defaults(data.Specs);
        _ = data.HiddenKeys(s.With("width", 0.5));
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            _ = data.HiddenKeys(s.With("width", 1 + (i * 0.05)));
        }

        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(100), $"100 ticks took {clock.Elapsed.TotalMilliseconds:0.0} ms");
    }

    [Fact]
    public void AnUnknownSpecKindIsSkipped()
    {
        var kind = EngineKinds.Find("grass")!;
        using var specs = JsonDocument.Parse("""
            [
              {"kind": "number", "key": "height", "label": "Height", "group": "Shape", "min": 0.1, "max": 2, "step": 0.05, "unit": "m", "default": 0.5},
              {"kind": "toggle", "key": "glow", "label": "Glow", "default": true},
              {"kind": "choice", "key": "build", "label": "Build", "options": [{"value": "normal", "label": "Normal"}], "default": "normal"}
            ]
            """);
        var written = new List<string>();
        void Note(string text) => written.Add(text);
        TraceLog.Written += Note;
        try
        {
            IReadOnlyList<ParamSpec> got = EngineSpecs.From(new EngineParameters { Specs = [.. specs.RootElement.EnumerateArray()] }, kind);
            Assert.Equal(["height", "build", "seed"], got.Select(s => s.Key));
            _ = EngineSpecs.From(new EngineParameters { Specs = [.. specs.RootElement.EnumerateArray()] }, kind);
        }
        finally
        {
            TraceLog.Written -= Note;
        }

        Assert.True(written.Count(t => t.Contains("'toggle'", StringComparison.Ordinal)) <= 1);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AThrowingRulesEngineLeavesThePanelUsable()
    {
        using var broken = new RulesEngine(EngineOptions.ForRules with
        {
            Scripts = [new EngineScript("broken.js", """
                var EasyProps = {};
                for (const name of ['shown', 'adjust', 'traits', 'validate', 'components', 'parameters', 'info', 'presets', 'preset']) {
                    EasyProps[name] = function () { throw new Error('broken ' + name); };
                }
                """)],
        });
        var data = new EngineKindData(EngineKinds.Find("cut-rock")!, EngineHost.Default.Catalog, broken);
        Assert.Throws<EngineScriptException>(() => broken.Shown("cut-rock", "{}"));
        using IDisposable _ = MeshRecipes.Override(new KindRules(RockRecipe.Engine, data));
        MainWindow w = PanelShownRowsTests.Rock();

        // Every row shows (the rule that hides the stone colours said nothing), and nothing escaped the setter.
        foreach (ParamSpec spec in w.Panel.Specs.Where(s => !SettingsPanel.IsHidden(s)))
        {
            Assert.True(w.Panel.RowFor(spec.Key)?.IsVisible, $"{spec.Key} hidden");
        }

        Assert.False(data.FarAway(w.Panel.Current));
        Settings was = w.Panel.Current;
        Assert.Same(was, data.Adjust(was, was));
        Ui.Drag(w, w.Panel.NumberRowFor("width")!.Slider, 5);
        Ui.Pump();
        Assert.NotEqual(was.GetFloat("width"), w.Panel.Current.GetFloat("width"));
        PanelShownRowsTests.Set(w, "chunks", 3);
        Assert.Equal(3, w.Panel.Current.GetInt("chunks"));
        Assert.Null(data.ShownRows(w.Panel.Current));
        w.Close();
    }

    [Fact]
    public async Task HiddenKeysFromTwoThreadsKeepTheCap()
    {
        EngineKindData data = Data("cut-rock");
        var s = Settings.Defaults(data.Specs);
        void Ask(double from)
        {
            for (int i = 0; i < 500; i++)
            {
                _ = data.HiddenKeys(s.With("width", from + (i * 0.01)));
                Assert.InRange(data.Cached, 0, EngineKindData.CacheCap);
            }
        }

        var first = Task.Run(() => Ask(1));
        var second = Task.Run(() => Ask(10));
        await Task.WhenAll(first, second);
        Assert.InRange(data.Cached, 1, EngineKindData.CacheCap);
    }

    // A port recipe whose panel rules come from the engine's kind data.
    private sealed class KindRules(IMeshRecipe inner, EngineKindData data) : IMeshRecipe
    {
        public string Id => inner.Id;

        public string Label => inner.Label;

        public string ExportName => inner.ExportName;

        public int Version => inner.Version;

        public IReadOnlyList<ParamSpec> Specs => inner.Specs;

        public IReadOnlyList<StartingPoint> StartingPoints => inner.StartingPoints;

        public IReadOnlyList<double> DetailShares => inner.DetailShares;

        public IReadOnlyCollection<string> HiddenKeys(Settings s) => data.HiddenKeys(s);

        public IReadOnlyList<ShownRow>? ShownRows(Settings s) => data.ShownRows(s);

        public Settings Adjust(Settings before, Settings after) => data.Adjust(before, after);

        public bool FarAway(Settings s) => data.FarAway(s);

        public ColouredMesh Build(Settings s, double detail, BuildPurpose purpose = BuildPurpose.View, CancellationToken cancel = default, string? replaces = null) =>
            inner.Build(s, detail, purpose, cancel, replaces);

        public byte[] Glb(Settings s, string modelName, ExportOptions? options = null, CancellationToken cancel = default) => inner.Glb(s, modelName, options, cancel);

        public IReadOnlyList<CollisionShape> Collision(Settings s, CancellationToken cancel = default, string? replaces = null) => inner.Collision(s, cancel, replaces);
    }
}
