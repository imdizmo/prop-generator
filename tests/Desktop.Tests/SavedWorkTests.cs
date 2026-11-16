using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PropGenerator.Desktop.Backups;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Props;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// Saved work opens as it looked (easy-props' <c>withEarlierLooks</c>, ruling 3): a rock saved before easy-props 0.4.5's
/// seven rock settings opens with them at 0 through every load path (Open, a restored backup, Your props' Export…, My
/// presets), in its settings, every undo step and its moved parts; files record the engine they were saved with.
/// </summary>
public sealed partial class SavedWorkTests
{
    // The settings a rock gained after easy-props 0.4.2, which upstream's EARLIER_LOOKS sets to 0 in an old file.
    private static readonly string[] Seven = ["grain", "rounded", "blend", "dents", "layers", "overhang", "lichen"];

    // A boulder as the app saved it on easy-props 0.4.2: none of the seven, the lavender palette of its day.
    private const string OldBoulder = """
        {"build":"painted","kind":"boulder","width":3,"height":2,"depth":2.6,"chunks":4,"faces":0.6,"cuts":0.65,"edges":0.7,
         "bend":0.5,"moss":0.5,"variation":0.5,"palette":"lavender","seed":401}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EngineRecipe Rock => EngineKit.Recipe("cut-rock");

    private static Settings Old() => new(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(OldBoulder)!);

    private static string SaveOld(string path, Settings current, params Settings[] steps)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var history = new History();
        foreach (Settings step in steps.Length == 0 ? [current] : steps)
        {
            history.Push("Boulder", step);
        }

        WorkFile.Save(path, new Piece("cut-rock", []), current, history);
        return path;
    }

    private static void AssertEarlierLook(Settings s, string where)
    {
        foreach (string key in Seven)
        {
            Assert.True(s.Values.ContainsKey(key), $"{where}: no {key}");
            Assert.True(s.GetFloat(key) == 0, $"{where}: {key} is {s.GetFloat(key)}, not 0");
        }
    }

    private static Settings WithoutTheSeven(Settings s)
    {
        foreach (string key in Seven)
        {
            s = s.Without(key);
        }

        return s;
    }

    [Fact]
    public void ANewRockHasTheSevenOnSoTheEarlierLookIsADifferentLook()
    {
        // What the tests below would not tell apart otherwise: the seven are not all 0 for a new rock.
        var fresh = Settings.Defaults(Rock.Specs);
        Assert.All(Seven, key => Assert.Contains(Rock.Specs, spec => spec.Key == key));
        Assert.Contains(Seven, key => fresh.GetFloat(key) != 0);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnOldRockOpensWithItsEarlierLook()
    {
        var (w, _, _, _, s) = ClaudeKit.Make();
        string path = SaveOld(s.P("old-boulder.propgen"), Old());

        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));

        AssertEarlierLook(w.Panel.Current, "the settings");
        Assert.Equal("lavender", w.Panel.Current.GetString("palette"));
        w.Close();
    }

    public static TheoryData<string> OldRockPresets
    {
        get
        {
            var data = new TheoryData<string>();
            using JsonDocument summaries = PortFixtures.Json("cut-rock-summaries.json");
            foreach (JsonElement p in summaries.RootElement.GetProperty("presets").EnumerateArray())
            {
                data.Add(p.GetProperty("preset").GetString()!);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(OldRockPresets))]
    public async Task AnOldRockMatchesItsV042Fixture(string preset)
    {
        // Each cut-rock preset of easy-props 0.4.2 as easy-props dumped it (its validated settings at its own seed),
        // saved by the app without the seven and opened, is built by the app's engine into the 0.4.2 golden's stones at
        // every detail level: the faces, the bounds and the positions (to 1e-4) the fixture keeps of the golden, and for
        // the two presets kept whole, every position, index and colour. The one difference is V8's own Math, which
        // splits one triangle of the boulder's full detail another way (912 faces against easy-props' 913; the
        // vertices are the same). The plan allowed mossy stones' colours to differ (0.4.5 changed the moss ramp);
        // measured, they do not: at its earlier looks (blend and lichen at 0) a mossy rock keeps 0.4.2's colours.
        using JsonDocument summaries = PortFixtures.Json("cut-rock-summaries.json");
        JsonElement fixture = summaries.RootElement.GetProperty("presets").EnumerateArray().Single(p => p.GetProperty("preset").GetString() == preset);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty p in fixture.GetProperty("parameters").EnumerateObject())
        {
            values[p.Name] = p.Value.Clone();
        }

        values["seed"] = JsonSerializer.SerializeToElement(fixture.GetProperty("seed").GetUInt32());
        Assert.DoesNotContain(Seven, values.ContainsKey);
        using var s = new Scratch();
        string path = SaveOld(s.P("old.propgen"), new Settings(values));

        Settings opened = WorkFile.Load(path, Rock.Specs, x => MeshRecipes.Loaded("cut-rock", x)).Settings;
        AssertEarlierLook(opened, preset);
        string scene = Rock.SceneJson(opened);

        JsonDocument? whole = File.Exists(PortFixtures.PathOf($"cut-rock-{preset}.json.gz")) ? PortFixtures.Json($"cut-rock-{preset}.json.gz") : null;
        using (whole)
        {
            double[] levels = [1, 0.4, 0.12];
            for (int level = 0; level < levels.Length; level++)
            {
                EngineMesh mesh = await Task.Run(() => EngineHost.Default.Builds.BuildAsync(scene, "low-poly", levels[level], EngineJobKind.Live, replaces: $"saved-{Guid.NewGuid():N}", cancel: Ct), Ct);
                JsonNode want = JsonNode.Parse(fixture.GetProperty("levels")[level].GetRawText())!;
                JsonObject got = PortFixtures.Summary(mesh.Parts.Select(p => (p.Positions, p.Index!.Length / 3)));
                if (preset == "boulder" && level == 0)
                {
                    Assert.Equal(913, want["faces"]!.GetValue<int>());
                    Assert.Equal(912, got["faces"]!.GetValue<int>());
                    got["faces"] = 913;
                }

                Assert.True(JsonNode.DeepEquals(want, got), $"{preset} L{level}: the golden's {want.ToJsonString()}, the engine's {got.ToJsonString()}");

                if (whole is not null)
                {
                    JsonElement[] parts = [.. whole.RootElement.GetProperty("levels")[level].GetProperty("parts").EnumerateArray()];
                    Assert.Equal(parts.Length, mesh.Parts.Count);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        EnginePart part = mesh.Parts[i];
                        EngineKit.Close([.. parts[i].GetProperty("position").EnumerateArray().Select(v => v.GetSingle())], part.Positions);
                        Assert.Equal(parts[i].GetProperty("index").EnumerateArray().Select(v => v.GetUInt32()), part.Index!);
                        EngineKit.Close([.. parts[i].GetProperty("color").EnumerateArray().Select(v => v.GetSingle())], part.Colors!);
                    }
                }
            }
        }
    }

    // The port's keys that easy-props 0.4.5's kinds no longer have, by kind: a port-made file opens without them.
    private static readonly Dictionary<string, string[]> DroppedSince042 = new(StringComparer.Ordinal)
    {
        ["branching-tree"] = ["asymmetry", "branchAngle", "branchLevels"],
    };

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EveryPortMadeFileOpensWithTheSameSettings()
    {
        // A work file of each of easy-props' kinds as the 0.4.2 C# port saved it (its first starting point; the palm
        // from its defaults) opens on the engine with every value it saved, and nothing said.
        var (w, _, _, _, s) = ClaudeKit.Make();
        var droppedNow = new Dictionary<string, string[]>(StringComparer.Ordinal);
        using JsonDocument kinds = PortFixtures.Json("kinds.json");
        foreach (JsonElement k in kinds.RootElement.GetProperty("files").EnumerateArray())
        {
            string file = k.GetProperty("file").GetString()!;
            string recipe = k.GetProperty("recipe").GetString()!;
            string path = PortFixtures.Copy(file, s.P("port", file));
            Settings saved = WorkFile.Load(path).Settings;

            Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)), file);
            Assert.Equal(recipe, w.Files.Recipe);
            Assert.IsType<EngineRecipe>(MeshRecipes.Find(w.Files.Recipe));
            // Every value the engine's kind still has keeps its value; the port's own keys that 0.4.5 dropped go, as on
            // master (named here so a change shows).
            var gone = new List<string>();
            foreach ((string key, JsonElement v) in saved.Values)
            {
                if (!w.Panel.Current.Values.TryGetValue(key, out JsonElement now))
                {
                    gone.Add(key);
                    continue;
                }

                Assert.True(JsonElement.DeepEquals(v, now), $"{file}: {key} was {v.GetRawText()}, opens as {now.GetRawText()}");
            }

            droppedNow[recipe] = [.. gone.Order(StringComparer.Ordinal)];

            if (recipe == "cut-rock")
            {
                AssertEarlierLook(w.Panel.Current, file);
            }

            Assert.Null(w.LastError);
        }

        static string Show(IEnumerable<KeyValuePair<string, string[]>> d) => string.Join("; ", d.Where(x => x.Value.Length > 0).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}: {string.Join(",", x.Value)}"));
        Assert.Equal(Show(DroppedSince042), Show(droppedNow));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnOldRockWithAMovedPartKeepsTheMoveAndTheLook()
    {
        var (w, _, _, dialogs, s) = ClaudeKit.Make();
        dialogs.Choice = SaveChoice.Discard;
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(YourPropsTests.Point("boulder"))));
        string id = w.Editing.Scene!.Components[0].Id;
        w.Editing.Select(id);
        w.Editing.MoveBy(new Vector3(0.5f, 0, 0));
        Settings moved = w.Panel.Current;
        Assert.True(moved.Values.ContainsKey(SettingsPanel.PartEditsKey));

        // As the app saved it on 0.4.2: the seven neither in the settings nor in the moved part's rock (the engine's kept
        // part carries the rock's values written out, the seven among them, so they are taken off here).
        JsonObject edits = JsonNode.Parse(moved.Values[SettingsPanel.PartEditsKey].GetRawText())!.AsObject();
        JsonObject rock = edits["kept"]![0]!["parameters"]!["rock"]!.AsObject();
        foreach (string key in Seven)
        {
            _ = rock.Remove(key);
        }

        Settings old = WithoutTheSeven(moved).With(SettingsPanel.PartEditsKey, JsonSerializer.SerializeToElement(edits));
        string path = SaveOld(s.P("moved.propgen"), old);
        Assert.True(CommandListTests.RunAsync(w.Files.NewAsync()));

        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));

        AssertEarlierLook(w.Panel.Current, "the settings");
        JsonObject opened = JsonNode.Parse(w.Panel.Current.Values[SettingsPanel.PartEditsKey].GetRawText())!.AsObject();
        JsonObject keptRock = opened["kept"]![0]!["parameters"]!["rock"]!.AsObject();
        Assert.All(Seven, key => Assert.Equal(0.0, keptRock[key]!.GetValue<double>()));
        Assert.Equal(0.5, w.Editing.Scene!.Components.Single(c => c.Id == id).Transform.Position.X, 5);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EveryHistoryStepGetsTheEarlierLook()
    {
        var (w, _, _, _, s) = ClaudeKit.Make();
        Settings first = Old(), second = Old().With("width", 4.0), third = Old().With("width", 5.0);
        string path = SaveOld(s.P("steps.propgen"), third, first, second, third);

        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));
        foreach (HistoryStep step in w.Files.History.Steps)
        {
            AssertEarlierLook(step.Settings, $"step {step.Label}");
        }

        w.Files.Undo();
        w.Files.Undo();
        Assert.Equal(3.0, w.Panel.Current.GetFloat("width"));
        AssertEarlierLook(w.Panel.Current, "the first step");
        w.Close();
    }

    [Fact]
    public void OpeningA200StepRockFileAsksTheEngineOnce()
    {
        using var s = new Scratch();
        Settings[] steps = [.. Enumerable.Range(0, 200).Select(i => Old().With("width", 1 + (i / 100.0)))];
        string path = SaveOld(s.P("long.propgen"), steps[^1], steps);
        _ = EngineHost.Default.Rules.Info(Ct);

        // A recipe of its own, so no earlier open has asked for it already.
        var rock = new EngineRecipe(EngineKinds.Find("cut-rock")!, EngineHost.Default);
        var (_, settings, history) = WorkFile.Load(path, rock.Specs, rock.EarlierLooks);

        Assert.Equal(200, history.Steps.Count);
        AssertEarlierLook(settings, "the settings");
        Assert.All(history.Steps, step => AssertEarlierLook(step.Settings, step.Label));
        Assert.Equal(1, rock.EarlierLooksAsked);
        Assert.True(rock.EarlierLooksTime < TimeSpan.FromMilliseconds(100), $"{rock.EarlierLooksTime.TotalMilliseconds} ms of engine time");
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnOldRockExportedFromYourPropsKeepsItsEarlierLook()
    {
        var (w, _, _, _, s) = ClaudeKit.Make();
        string path = SaveOld(s.P("props", "Old boulder.propgen"), Old());
        string map = s.P("map");
        Directory.CreateDirectory(map);
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        w.Files.Prefs.MapFolder = map;

        Assert.Equal(1, ClaudeKit.Run(w.Files.ExportFilesAsync([path]), 120000));

        string glb = Assert.Single(Directory.GetFiles(map, "*.glb", SearchOption.AllDirectories));
        string model = Path.GetFileNameWithoutExtension(glb);
        ExportOptions look = w.Files.ExportOptionsNow;
        var earlier = Settings.Defaults(Rock.Specs);
        foreach ((string key, JsonElement v) in Old().Values)
        {
            earlier = earlier.With(key, v);
        }

        Settings defaults = earlier;
        foreach (string key in Seven)
        {
            earlier = earlier.With(key, 0.0);
        }

        byte[] want = EngineKit.OnThePool(() => Rock.Glb(earlier, model, look, Ct));
        byte[] fresh = EngineKit.OnThePool(() => Rock.Glb(defaults, model, look, Ct));
        Assert.False(want.AsSpan().SequenceEqual(fresh), "the earlier look exports as the new one");
        Assert.True(File.ReadAllBytes(glb).AsSpan().SequenceEqual(want), "the export is not the earlier look");
        w.Close();
    }

    [Fact]
    public void AnOldRockPresetInMyPresetsKeepsItsEarlierLook()
    {
        using var s = new Scratch();
        var values = new JsonObject();
        foreach ((string key, JsonElement v) in Old().Values)
        {
            values[key] = JsonNode.Parse(v.GetRawText());
        }

        var root = new JsonObject
        {
            ["presets"] = new JsonArray(new JsonObject { ["id"] = "mine-0123456789ab", ["name"] = "Old boulder", ["recipe"] = "cut-rock", ["settings"] = values }),
        };
        File.WriteAllText(s.P("presets.json"), root.ToJsonString());

        StartingPoint mine = Assert.Single(new UserPresets(s.P("presets.json")).All);

        AssertEarlierLook(mine.Settings, "the preset");
        Assert.Equal("lavender", mine.Settings.GetString("palette"));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ARestoredBackupOfAnOldRockKeepsItsEarlierLook()
    {
        var (w, _, _, _, s) = ClaudeKit.Make();
        var library = new Library(s.P("library.json"));
        string path = SaveOld(s.P("props", "Old boulder.propgen"), Old());
        library.Add(path);
        string zip = s.P("backup.zip");
        Assert.Equal(1, BackupArchive.Write(zip, [path], new Dictionary<string, byte[]>(), DateTimeOffset.UnixEpoch));
        File.Delete(path);
        library.Forget(path);

        BackupRestore r = BackupArchive.Restore(zip, library, s.P("restored"));
        Assert.Equal(1, r.Added);
        string back = Assert.Single(library.List()).Path;

        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(back)));
        AssertEarlierLook(w.Panel.Current, "the restored rock");
        w.Close();
    }

    [Fact]
    public void NoLoadPathSkipsLoaded()
    {
        // Every load of a work file's values in the app (Load with a settings list) goes through MeshRecipes.Loaded,
        // so no path opens an old rock with the new look. Load(path) alone reads only the recipe or the summary.
        string src = Path.Combine(Nature.Tests.Support.Fixture.RepoRoot(), "src", "PropGenerator.Desktop");
        var calls = new List<string>();
        foreach (string file in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/obj/", StringComparison.Ordinal)))
        {
            string text = File.ReadAllText(file);
            foreach (Match m in LoadCall().Matches(text))
            {
                calls.Add($"{Path.GetFileName(file)}: {m.Value}");
            }
        }

        List<string> withSpecs = [.. calls.Where(c => c.Contains(',', StringComparison.Ordinal))];
        Assert.True(withSpecs.Count >= 2, string.Join("\n", calls));
        Assert.All(withSpecs, c => Assert.Contains("MeshRecipes.Loaded", c, StringComparison.Ordinal));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ARockSavedWithLavenderStaysLavender()
    {
        var (w, _, _, _, s) = ClaudeKit.Make();
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(YourPropsTests.Point("boulder"))));
        Assert.Null(w.Panel.SetFromOutside("palette", JsonSerializer.SerializeToElement("lavender")));
        string now = s.P("lavender.propgen");
        Assert.Null(w.Files.SaveWithoutAsking(now, overwrite: true));
        string old = SaveOld(s.P("old.propgen"), Old());

        foreach (string path in (string[])[now, old])
        {
            Assert.True(CommandListTests.RunAsync(w.Files.NewAsync()));
            Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));
            Assert.Equal("lavender", w.Panel.Current.GetString("palette"));
        }

        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ANewRockStartsGreystone()
    {
        var (w, _, _, _, _) = ClaudeKit.Make();
        CommandListTests.Run(w, "new-rock");
        Assert.Equal("cut-rock", w.Files.Recipe);
        Assert.Equal("greystone", w.Panel.Current.GetString("palette"));
        Assert.Contains(Seven, key => w.Panel.Current.GetFloat(key) != 0);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnOldFruitTreeFollowsUpstream()
    {
        // A painted tree saved before 'fruitHang' existed takes upstream's default (0.3), as it does in easy-props:
        // upstream fills only the rocks' settings from earlier looks (ruling 3).
        var (w, _, _, _, s) = ClaudeKit.Make();
        StartingPoint oak = YourPropsTests.Point("oak");
        Settings old = oak.Settings.Without("fruitHang");
        var history = new History();
        history.Push(oak.Name, old);
        string path = s.P("old-oak.propgen");
        WorkFile.Save(path, new Piece("painted-tree", []), old, history);

        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));
        Assert.Equal(0.3, w.Panel.Current.GetFloat("fruitHang"), 5);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ASavedFileCarriesTheEngineTag()
    {
        var (w, _, _, _, s) = ClaudeKit.Make();
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(YourPropsTests.Point("boulder"))));
        string rock = s.P("rock.propgen");
        Assert.Null(w.Files.SaveWithoutAsking(rock, overwrite: true));
        Assert.True(CommandListTests.RunAsync(w.Files.NewAsync()));
        string floor = s.P("floor.propgen");
        Assert.Null(w.Files.SaveWithoutAsking(floor, overwrite: true));

        JsonObject root = JsonNode.Parse(File.ReadAllText(rock))!.AsObject();
        EngineInfo info = EngineHost.Default.Catalog.Info;
        Assert.Equal(info.Tag, root["engine"]?["tag"]?.GetValue<string>());
        Assert.Equal(info.Commit, root["engine"]?["commit"]?.GetValue<string>());
        Assert.Equal(EnginePin.Tag, info.Tag);
        Assert.False(JsonNode.Parse(File.ReadAllText(floor))!.AsObject().ContainsKey("engine"));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ASavedFileStaysFormat1WithTheEngineBlockAtTheTop()
    {
        var (w, _, _, _, s) = ClaudeKit.Make();
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(YourPropsTests.Point("boulder"))));
        string path = s.P("rock.propgen");
        Assert.Null(w.Files.SaveWithoutAsking(path, overwrite: true));

        // What a phase 2 build reads: format 1, the piece, the settings and the history as they were; the engine is
        // one more key at the top, beside them, never inside the settings.
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(["format", "app", "engine", "piece", "settings", "history"], root.Select(kv => kv.Key));
        Assert.Equal(1, root["format"]!.GetValue<int>());
        Assert.Equal("cut-rock", root["piece"]!["recipe"]!.GetValue<string>());
        Assert.False(root["settings"]!.AsObject().ContainsKey("engine"));
        var (piece, settings, history) = WorkFile.Load(path);
        Assert.Equal("cut-rock", piece.Recipe);
        Assert.Equal(w.Panel.Current, settings);
        Assert.Equal(w.Files.History.Steps.Count, history.Steps.Count);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnUnknownKindOpensWithANote()
    {
        // A prop of a kind this build does not know (a newer build's): Your props lists it by its id, and opening it says
        // why it cannot, with the piece on screen left as it was.
        var (w, _, _, dialogs, s) = ClaudeKit.Make();
        string path = s.P("props", "Shroom.propgen");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var history = new History();
        history.Push("Shroom", Old());
        WorkFile.Save(path, new Piece("mushroom", []), Old(), history);
        var library = new Library(s.P("library.json"));
        library.Add(path);

        PropSummary listed = Assert.Single(new YourProps(library, new RemovedBin(s.P("removed"), () => DateTimeOffset.UnixEpoch)).List());
        Assert.Equal("Unknown kind (mushroom)", listed.KindLabel);
        Assert.Equal("Unknown kind (mushroom)", YourProps.KindOf("mushroom"));

        string before = w.Files.Recipe;
        Assert.False(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));
        Assert.Equal(before, w.Files.Recipe);
        string told = Assert.Single(dialogs.Told);
        Assert.Contains("Unknown kind (mushroom)", told, StringComparison.Ordinal);
        Assert.Contains("cannot open it", told, StringComparison.Ordinal);
        w.Close();
    }

    [GeneratedRegex(@"WorkFile\.Load\([^;]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex LoadCall();
}
