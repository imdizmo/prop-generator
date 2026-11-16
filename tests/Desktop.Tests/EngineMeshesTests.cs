using System.Globalization;
using System.Text.Json;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Scenes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// The engine's builds and shapes as the app reads them: the view's mesh against Node's Math on the same bundle (Z-up,
/// colours by tint, parts and proxies, the atlas and bark pictures), edits, weather, and the shapes.
/// </summary>
public sealed class EngineMeshesTests(NodeMathEngine nodeMath) : IClassFixture<NodeMathEngine>
{
    private const string Style = "low-poly";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, double> Samples
    {
        get
        {
            var data = new TheoryData<string, double>();
            foreach (string preset in (string[])["oak", "coconut-palm-painted", "round-bush", "flower-clump", "long-grass", "saguaro", "boulder", "round-canopy"])
            {
                data.Add(preset, 1);
                data.Add(preset, 0.4);
                if (preset != "boulder")
                {
                    // The cut rock's far level is not among the samples measured.
                    data.Add(preset, 0.12);
                }
            }

            return data;
        }
    }

    // What V8's own Math (the app's) moves against Node's on the same bundle, measured on easy-props v0.4.5
    // (33148d06a564c8e2e261b902e9537e14d8a3553d) for each sample: how many triangles the app's build has more (+) or
    // fewer (-) than Node's, and, where both have as many vertices, how many of their position, normal, colour and
    // texture values are off by more than 1e-5, and by how much at most. Only the oak at full and middle detail moves
    // (a few dozen values, by up to 9e-3, as its limbs did on 0.4.2), and the boulder at full and middle detail is cut
    // into 6 more triangles; everything else is Node's to the bit. A new bundle measures these again: measured again on
    // v0.4.7 (480028638016383c5c41b67cbb664da09e85356b), whose bundle is 0.4.5's code with only its version strings
    // changed, and unchanged.
    private const string MeasuredTag = "v0.4.7";

    private static readonly Dictionary<(string Preset, double Detail), Noise> Measured = new()
    {
        [("oak", 1)] = new(0, true, 45, 0.0091),
        [("oak", 0.4)] = new(0, true, 28, 0.0091),
        [("oak", 0.12)] = new(0, true, 0, 1e-16),
        [("coconut-palm-painted", 1)] = new(0, true, 0, 0),
        [("coconut-palm-painted", 0.4)] = new(0, true, 0, 0),
        [("coconut-palm-painted", 0.12)] = new(0, true, 0, 0),
        [("round-bush", 1)] = new(0, true, 0, 0),
        [("round-bush", 0.4)] = new(0, true, 0, 0),
        [("round-bush", 0.12)] = new(0, true, 0, 0),
        [("flower-clump", 1)] = new(0, true, 0, 0),
        [("flower-clump", 0.4)] = new(0, true, 0, 0),
        [("flower-clump", 0.12)] = new(0, true, 0, 0),
        [("long-grass", 1)] = new(0, true, 0, 0),
        [("long-grass", 0.4)] = new(0, true, 0, 0),
        [("long-grass", 0.12)] = new(0, true, 0, 0),
        [("saguaro", 1)] = new(0, true, 0, 0),
        [("saguaro", 0.4)] = new(0, true, 0, 0),
        [("saguaro", 0.12)] = new(0, true, 0, 0),
        [("boulder", 1)] = new(6, false, 0, 0),
        [("boulder", 0.4)] = new(6, false, 0, 0),
        [("round-canopy", 1)] = new(0, true, 0, 0),
        [("round-canopy", 0.4)] = new(0, true, 0, 0),
        [("round-canopy", 0.12)] = new(0, true, 0, 0),
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void TheAppsEngineIsWithinMathNoiseOfNode(string presetId, double detail)
    {
        Assert.Equal(MeasuredTag, EngineHost.Default.Catalog.Info.Tag);
        EnginePreset preset = Preset(presetId);
        EngineRecipe recipe = EngineKit.Recipe(preset.GeneratorId);
        Settings settings = recipe.StartingPoints.Single(p => p.Id == presetId).Settings;
        bool byPart = EngineKinds.Find(preset.GeneratorId)!.Wind?.ByPart == true;
        ColouredMesh node = Build(nodeMath.Builds, nodeMath.Textures, preset, settings, byPart, detail);
        ColouredMesh app = Build(EngineHost.Default.Builds, EngineHost.Default.Textures, preset, settings, byPart, detail);
        var got = Noise.Of(node, app);
        string where = $"{presetId} at {detail.ToString(CultureInfo.InvariantCulture)}";
        Assert.True(Measured.TryGetValue((presetId, detail), out Noise? want), $"{where}: not measured; measured now {got}");
        Assert.True(got.Faces == want.Faces && got.SameVertices == want.SameVertices && got.Off <= want.Off && got.Most <= want.Most, $"{where}: {got}, measured {want}");
        if (got.Faces == 0 && got.SameVertices)
        {
            Assert.Equal(node.Parts, app.Parts);
            AssertSamePicture(node.Texture, app.Texture, $"{where} atlas");
            AssertSamePicture(node.Bark, app.Bark, $"{where} bark");
            AssertSamePicture(node.Relief, app.Relief, $"{where} relief");
        }
    }

    [Fact]
    public async Task FullFacesEqualsTheFullBuildsFaces()
    {
        // The oak has proxies (its limbs' picking tubes): they are not faces.
        EnginePreset oak = Preset("oak");
        string scene = Scene(oak);
        EngineMesh light = await EngineHost.Default.Builds.BuildAsync(scene, Style, 0.4, fullFaces: true, replaces: Key(), cancel: Ct);
        EngineMesh full = await EngineHost.Default.Builds.BuildAsync(scene, Style, 1, replaces: Key(), cancel: Ct);
        ColouredMesh drawn = EngineMeshes.ToColoured(full, EngineHost.Default.Textures, EngineJobKind.Live, Ct, new PieceFrame(4.2f, null), flutterByPart: true);
        Assert.NotNull(drawn.Proxies);
        Assert.NotNull(light.FullFaces);
        Assert.Equal(drawn.Indices.Length / 3, light.FullFaces);
        Assert.Null(full.FullFaces);
    }

    [Fact]
    public async Task ShapesComeForTheOldTreeAndNoneForGrass()
    {
        // The old tree offers no shapes of its own: the export makes them from its solid parts (ruling 4: the view shows
        // them). Grass has no solid part, so upstream makes none for it either.
        EngineCollision tree = await EngineHost.Default.Builds.CollisionAsync(Scene(Preset("round-canopy")), Style, replaces: Key(), cancel: Ct);
        IReadOnlyList<CollisionShape> shapes = EngineMeshes.ToShapes(tree);
        Assert.Empty(tree.Offered);
        Assert.NotEmpty(shapes);
        Assert.Equal(tree.Shapes.Count, shapes.Count);
        Assert.All(shapes, s => Assert.Null(s.Ring));
        Assert.All(shapes, s => Assert.True(s.R0 > 0 && s.R1 > 0 && double.IsFinite(s.Start.Y) && double.IsFinite(s.End.Y)));
        Assert.Equal((tree.Shapes[0].Start[1], tree.Shapes[0].EndRadius), (shapes[0].Start.Y, shapes[0].R1));

        EngineCollision grass = await EngineHost.Default.Builds.CollisionAsync(Scene(Preset("long-grass")), Style, replaces: Key(), cancel: Ct);
        Assert.Empty(grass.Shapes);
        Assert.Empty(EngineMeshes.ToShapes(grass));
    }

    [Fact]
    public void AnEditedBushBuildsItsEdits()
    {
        EnginePreset preset = Preset("round-bush");
        EngineRecipe bush = EngineKit.Recipe(preset.GeneratorId);
        StartingPoint point = bush.StartingPoints.Single(p => p.Id == "round-bush");
        Scene scene = bush.SceneOf(point.Settings);

        // The last part with no children (a bush's leaves): the scene without it is a whole scene.
        Assert.True(scene.Components.Count > 1);
        SceneComponent gone = scene.Components.Last(c => scene.Components.All(o => o.ParentId != c.Id));
        Scene edited = scene with { Components = [.. scene.Components.Where(c => c.Id != gone.Id)] };

        ColouredMesh before = Build(EngineHost.Default.Builds, EngineHost.Default.Textures, preset, point.Settings, bush, 1, null);
        ColouredMesh after = Build(EngineHost.Default.Builds, EngineHost.Default.Textures, preset, point.Settings, bush, 1, edited);
        Assert.Contains(before.Parts!, p => p.Id == gone.Id);
        Assert.DoesNotContain(after.Parts!, p => p.Id == gone.Id);
        Assert.True(after.Indices.Length < before.Indices.Length);

        // The same scene as components gives the generator's own mesh.
        ColouredMesh same = Build(EngineHost.Default.Builds, EngineHost.Default.Textures, preset, point.Settings, bush, 1, scene);
        Assert.Equal(before.Indices, same.Indices);
        Assert.Equal(before.Positions, same.Positions);
    }

    [Fact]
    public void TheSceneCarriesComponentsOnlyWhenEdited()
    {
        EngineRecipe bush = EngineKit.Recipe("bush");
        StartingPoint point = bush.StartingPoints.Single(p => p.Id == "round-bush");
        using var plain = JsonDocument.Parse(EngineScenes.Of("bush", """{"width":2}""", 7, null));
        Assert.Equal("bush", plain.RootElement.GetProperty("generatorId").GetString());
        Assert.Equal(7u, plain.RootElement.GetProperty("seed").GetUInt32());
        Assert.Equal(2, plain.RootElement.GetProperty("parameters").GetProperty("width").GetDouble());
        Assert.False(plain.RootElement.TryGetProperty("components", out _));
        Assert.Equal(EngineScenes.Of("bush", """{"width":2}""", 7, null), EngineScenes.Of(EngineKinds.Find("bush")!, """{"width":2}""", 7, null));

        Scene scene = bush.SceneOf(point.Settings);
        using var edited = JsonDocument.Parse(EngineScenes.Of("bush", "{}", 7, scene));
        Assert.Equal(scene.Components.Count, edited.RootElement.GetProperty("components").GetArrayLength());
        Assert.Equal(scene.Components[0].Id, edited.RootElement.GetProperty("components")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ToColouredIsRefusedOnTheEngineThread()
    {
        EngineThread builds = EngineHost.Default.Builds;
        EngineMesh mesh = await builds.BuildAsync(Scene(Preset("round-bush")), Style, 0.12, replaces: Key(), cancel: Ct);
        Exception? seen = await builds.Enqueue(EngineJobKind.Live, (_, _) =>
        {
            try
            {
                _ = EngineMeshes.ToColoured(mesh, EngineHost.Default.Textures, EngineJobKind.Live, CancellationToken.None, new PieceFrame(4.2f, null), flutterByPart: false);
                return null;
            }
            catch (InvalidOperationException e)
            {
                return e;
            }
        }, Ct).WaitAsync(TimeSpan.FromMinutes(1), Ct);
        Assert.NotNull(seen);
    }

    // Upstream's wetRoughness (weather.ts): dry * (1 - 0.55 * wet), a part with no roughness of its own being 1.
    private static float WetRoughness(double dry, double wet) => (float)(dry * (1 - (0.55 * wet)));

    [Fact]
    public async Task AWetBushHasRoughnessUnderOne()
    {
        ColouredMesh wet = await Weathered("round-bush", ("wet", 0.8));
        Assert.NotNull(wet.Roughness);
        Assert.Equal(wet.Positions.Length / 3, wet.Roughness!.Length);
        Assert.Contains(wet.Roughness, r => r < 1);

        // Every shining vertex is upstream's wet roughness of a matt part (1) or of a glossy leaf (0.55).
        float[] allowed = [1, WetRoughness(1, 0.8), WetRoughness(0.55, 0.8)];
        Assert.All(wet.Roughness, r => Assert.Contains(allowed, a => Math.Abs(a - r) < 1e-5));
        Assert.Contains(wet.Roughness, r => Math.Abs(r - WetRoughness(1, 0.8)) < 1e-5);
    }

    [Fact]
    public async Task ADryBushHasNone()
    {
        // (guard) A dry prop is fully rough: no roughness to upload.
        ColouredMesh dry = await Weathered("round-bush", ("wet", 0));
        Assert.Null(dry.Roughness);
    }

    [Fact]
    public async Task SnowDoesNotChangePositions()
    {
        // (guard) Upstream's weather is colour on the points a prop already has.
        ColouredMesh bare = await Weathered("round-bush", ("snow", 0));
        ColouredMesh snowy = await Weathered("round-bush", ("snow", 0.85));
        Assert.Equal(bare.Positions, snowy.Positions);
        Assert.Equal(bare.Indices, snowy.Indices);
        Assert.Equal(bare.Normals, snowy.Normals);
        Assert.NotEqual(bare.Colours, snowy.Colours);
        Assert.Null(snowy.Roughness);
    }

    // A preset at full detail with some of its values changed.
    private static async Task<ColouredMesh> Weathered(string presetId, params (string Key, double Value)[] changes)
    {
        EnginePreset preset = Preset(presetId);
        var values = System.Text.Json.Nodes.JsonNode.Parse(preset.Parameters.GetRawText())!.AsObject();
        foreach ((string key, double value) in changes)
        {
            values[key] = value;
        }

        string scene = EngineScenes.Of(preset.GeneratorId, values.ToJsonString(), (uint)preset.Seed, null);
        EngineMesh mesh = await EngineHost.Default.Builds.BuildAsync(scene, Style, 1, replaces: Key(), cancel: Ct);
        return await Task.Run(() => EngineMeshes.ToColoured(mesh, EngineHost.Default.Textures, EngineJobKind.Live, Ct, new PieceFrame(4.2f, null), flutterByPart: false), Ct);
    }

    private static EnginePreset Preset(string id) =>
        EngineHost.Default.Catalog.Preset(id) ?? throw new InvalidOperationException($"the engine has no preset {id}");

    // The preset's scene: its own parameters and seed.
    private static string Scene(EnginePreset preset, Scene? edited = null) =>
        EngineScenes.Of(preset.GeneratorId, preset.Parameters.GetRawText(), (uint)preset.Seed, edited);

    // A replace key of this call's own, so parallel tests on the shared host never drop each other's builds.
    private static string Key() => $"test-{Guid.NewGuid():N}";

    private static ColouredMesh Build(EngineThread builds, EngineTextures textures, EnginePreset preset, Settings settings, EngineRecipe recipe, double detail, Scene? edited = null)
    {
        EngineMesh mesh = builds.BuildAsync(Scene(preset, edited), Style, detail, replaces: Key(), cancel: Ct).GetAwaiter().GetResult();
        return EngineMeshes.ToColoured(mesh, textures, EngineJobKind.Live, Ct, PieceFrames.Of(settings), recipe.Wind?.ByPart == true);
    }

    private static ColouredMesh Build(EngineThread builds, EngineTextures textures, EnginePreset preset, Settings settings, bool byPart, double detail)
    {
        EngineMesh mesh = builds.BuildAsync(Scene(preset), Style, detail, replaces: Key(), cancel: Ct).GetAwaiter().GetResult();
        return EngineMeshes.ToColoured(mesh, textures, EngineJobKind.Live, Ct, PieceFrames.Of(settings), byPart);
    }

    // Pixels within 1 a channel, and at most 0.1 % of them off at all (V8's Math may differ from .NET's in the last bit).
    private static void AssertSamePicture(MeshTexture? want, MeshTexture? got, string where)
    {
        Assert.True((want is null) == (got is null), $"{where}: present in only one");
        if (want is null)
        {
            return;
        }

        Assert.Equal((want.Width, want.Height, want.AlphaCut), (got!.Width, got.Height, got.AlphaCut));
        AssertSamePixels(want.Rgba, got.Rgba, where);
    }

    private static void AssertSamePixels(byte[] want, byte[] got, string where)
    {
        Assert.True(want.Length == got.Length, $"{where}: {want.Length} bytes in the port's, {got.Length} in the engine's");
        int off = 0;
        for (int i = 0; i < want.Length; i += 4)
        {
            bool differs = false;
            for (int c = 0; c < 4; c++)
            {
                int d = Math.Abs(want[i + c] - got[i + c]);
                Assert.True(d <= 1, $"{where}: pixel {i / 4} channel {c}: the port {want[i + c]}, the engine {got[i + c]}");
                differs |= d != 0;
            }

            off += differs ? 1 : 0;
        }

        Assert.True(off <= want.Length / 4 / 1000, $"{where}: {off} of {want.Length / 4} pixels differ");
    }
}

/// <summary>
/// How far the app's build is from Node's: its triangles more or fewer, and, when both have the same vertices, how many
/// position, normal, colour and texture values differ by more than 1e-5, and the largest difference.
/// </summary>
/// <param name="Faces">The app's triangles less Node's.</param>
/// <param name="SameVertices">Whether both have as many vertices.</param>
/// <param name="Off">Values off by more than 1e-5 (0 when the vertices differ in number).</param>
/// <param name="Most">The largest difference (0 when the vertices differ in number).</param>
internal sealed record Noise(int Faces, bool SameVertices, int Off, double Most)
{
    /// <summary>Measures it.</summary>
    /// <param name="node">The build on Node's Math.</param>
    /// <param name="app">The app's build.</param>
    /// <returns>The noise.</returns>
    public static Noise Of(ColouredMesh node, ColouredMesh app)
    {
        int faces = (app.Indices.Length - node.Indices.Length) / 3;
        if (node.Positions.Length != app.Positions.Length)
        {
            return new Noise(faces, false, 0, 0);
        }

        int off = 0;
        double most = 0;
        foreach ((float[] a, float[] b) in new[] { (node.Positions, app.Positions), (node.Normals, app.Normals), (node.Colours, app.Colours), (node.TexCoords ?? [], app.TexCoords ?? []) })
        {
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                double d = Math.Abs(a[i] - b[i]);
                off += d > 1e-5 ? 1 : 0;
                most = Math.Max(most, d);
            }
        }

        return new Noise(faces, true, off, most);
    }

    /// <inheritdoc/>
    public override string ToString() => FormattableString.Invariant($"faces {Faces:+0;-0;0}, {(SameVertices ? $"{Off} values off, at most {Most:G3}" : "other vertices")}");
}

/// <summary>
/// A build engine on the bundle with the Math of the Node easy-props' goldens were dumped with (Fixtures/node-math.js),
/// for the checks of the conversion against the port to the bit; one for the test class, freed after it.
/// </summary>
public sealed class NodeMathEngine : IDisposable
{
    /// <summary>Loads the scripts; the engine itself loads on the first job.</summary>
    public NodeMathEngine()
    {
        var scripts = EngineOptions.EmbeddedScripts().ToList();
        string math = Path.Combine(Fixture.RepoRoot(), "tests", "Desktop.Tests", "Fixtures", "node-math.js");
        scripts.Insert(scripts.FindIndex(s => s.Name == "engine.js"), EngineScript.FromFile(math));
        // The rules engine's smaller caps (a sample prop's build fits them) and a small texture cache: the suite's
        // process holds the shared host's engines too.
        Builds = new EngineThread(EngineOptions.ForRules with { Scripts = scripts, JobTimeout = TimeSpan.FromSeconds(60) });
        Textures = new EngineTextures(16L << 20, Builds.TextureAsync, Builds);
    }

    /// <summary>Gets the build engine's thread.</summary>
    internal EngineThread Builds { get; }

    /// <summary>Gets its textures.</summary>
    internal EngineTextures Textures { get; }

    /// <summary>Stops the engine.</summary>
    public void Dispose() => Builds.Dispose(TimeSpan.FromSeconds(10));
}

/// <summary>
/// The Weather rows easy-props 0.4.5 gives its kinds, as the panel draws them from the engine's specs: in upstream's
/// order and with its labels, and "Moss grows" shown only while there is moss (upstream's shown rule).
/// </summary>
public sealed class WeatherPanelTests
{
    // Upstream's specs lists: painted-tree parameters.ts and cut-rock-generator.ts (SNOW, MOSS, MOSS_FROM, WET), the
    // bush, plant, grass and cactus generators (SNOW, WET); the old-style tree and palm have no weather.
    public static TheoryData<string, string[]> Weather => new()
    {
        { "painted-tree", ["snow", "moss", "mossFrom", "wet"] },
        { "cut-rock", ["snow", "moss", "mossFrom", "wet"] },
        { "bush", ["snow", "wet"] },
        { "plant", ["snow", "wet"] },
        { "grass", ["snow", "wet"] },
        { "cactus", ["snow", "wet"] },
        { "branching-tree", [] },
        { "palm-tree", [] },
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["snow"] = "Snow",
        ["moss"] = "Moss",
        ["mossFrom"] = "Moss grows",
        ["wet"] = "Wet",
    };

    [Theory]
    [MemberData(nameof(Weather))]
    public void WeatherGroupFollowsUpstreamsOrder(string kind, string[] keys)
    {
        IReadOnlyList<ParamSpec> specs = MeshRecipes.Find(kind)!.Specs;
        ParamSpec[] weather = [.. specs.Where(s => s.Group == "Weather")];
        Assert.Equal(keys, weather.Select(s => s.Key));
        Assert.All(weather, s => Assert.Equal(Labels[s.Key], s.Label));

        // The rows sit together, one group, as upstream's panel draws them.
        if (weather.Length > 0)
        {
            int first = specs.ToList().IndexOf(weather[0]);
            Assert.Equal(weather, specs.Skip(first).Take(weather.Length));
        }

        if (keys.Contains("mossFrom"))
        {
            ParamSpec from = weather.Single(s => s.Key == "mossFrom");
            Assert.Equal(["sky", "shade"], from.Options!.Select(o => o.Value));
            Assert.Equal(["On top", "On one side"], from.Options!.Select(o => o.Label));
            Assert.Equal("sky", from.Default.GetString());
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData("painted-tree", "oak")]
    [InlineData("cut-rock", "boulder")]
    public void MossFromHidesWhileMossIsZero(string kind, string presetId)
    {
        MainWindow w = Ui.Open(new FakePainter());
        StartingPoint point = MeshRecipes.Find(kind)!.StartingPoints.Single(p => p.Id == presetId);
        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(point), 30000));
        Ui.Pump();

        PanelShownRowsTests.Set(w, "moss", 0);
        Assert.True(w.Panel.RowFor("moss")?.IsVisible, "moss hidden");
        Assert.False(w.Panel.RowFor("mossFrom")!.IsVisible, "Moss grows shown with no moss");

        PanelShownRowsTests.Set(w, "moss", 0.5);
        Assert.True(w.Panel.RowFor("mossFrom")!.IsVisible, "Moss grows hidden with moss");
        Assert.True(w.Panel.RowFor("snow")?.IsVisible, "snow hidden");
        Assert.True(w.Panel.RowFor("wet")?.IsVisible, "wet hidden");

        PanelShownRowsTests.Set(w, "moss", 0);
        Assert.False(w.Panel.RowFor("mossFrom")!.IsVisible, "Moss grows shown again with no moss");
        w.Close();
    }
}
