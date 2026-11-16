using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static PropGenerator.Tests.Support.Fixtures;

namespace PropGenerator.Tests;

public class WorkFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "prop-generator", "scratch", "wf-" + Guid.NewGuid().ToString("N"));

    public WorkFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Directory.Delete(_dir, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AFileLargerThanAnyWorkFileIsRefusedBeforeItIsRead()
    {
        var h = new History();
        h.Push("open", Defaults());
        string path = Path.Combine(_dir, WorkFile.DefaultName);
        WorkFile.Save(path, APiece(), Defaults(), h);
        using (FileStream f = File.OpenWrite(path))
        {
            // Sparse: nothing is written to the disk for it.
            f.SetLength(WorkFile.MaxBytes + 1);
        }

        WorkFileException e = Assert.Throws<WorkFileException>(() => WorkFile.Load(path));
        Assert.Contains("too large", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTripKeepsSettingsIndexAndSteps()
    {
        Settings s0 = Defaults();
        Settings s1 = s0.With("contrast", 0.7);
        Settings s2 = s1.With("colour", new Rgb(0.2f, 0.4f, 0.6f));
        var h = new History();
        h.Push("open", s0);
        h.Push("contrast", s1);
        h.Push("colour", s2);
        h.Undo();
        string path = Path.Combine(_dir, WorkFile.DefaultName);
        WorkFile.Save(path, APiece(), s1, h);

        var (piece, settings, history) = WorkFile.Load(path);
        Assert.Equal("floor_tile", piece.Recipe);
        Assert.Equal(2, piece.Faces.Count);
        Assert.Equal("cut_end", piece.Faces[1].Kind);
        Assert.Equal(0.5, piece.Faces[1].Settings["contrast"].GetDouble());
        Assert.Equal(s1, settings);
        Assert.Equal(1, history.Index);
        Assert.Equal(["open", "contrast", "colour"], history.Steps.Select(x => x.Label));
        Assert.Equal(s2, history.Steps[2].Settings);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void FileCarriesTheAppVersionFromTheVersionFile()
    {
        string path = Path.Combine(_dir, "v.propgen");
        WorkFile.Save(path, APiece(), Defaults(), new History());
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        string expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../VERSION")).Trim();
        Assert.Equal(expected, doc.RootElement.GetProperty("app").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("format").GetInt32());
    }

    [Fact]
    public void UndoAfterLoadRestoresThePreviousSettings()
    {
        Settings s0 = Defaults();
        Settings s1 = s0.With("contrast", 0.3);
        var h = new History();
        h.Push("open", s0);
        h.Push("contrast", s1);
        string path = Path.Combine(_dir, "u.propgen");
        WorkFile.Save(path, APiece(), s1, h);

        History loaded = WorkFile.Load(path).History;
        Assert.Equal(s0, loaded.Undo());
        Assert.Equal(s1, loaded.Redo());
    }

    [Fact]
    public void UnknownFormatThrows()
    {
        string path = Path.Combine(_dir, "f.propgen");
        File.WriteAllText(path, "{\"format\": 99}");
        var e = Assert.Throws<WorkFileException>(() => WorkFile.Load(path));
        Assert.Contains("99", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GarbageFileThrowsWorkFileException()
    {
        string path = Path.Combine(_dir, "g.propgen");
        File.WriteAllText(path, "not json");
        Assert.Throws<WorkFileException>(() => WorkFile.Load(path));
    }

    [Fact]
    public void CrashMidSaveLeavesTheOldFileIntact()
    {
        string path = Path.Combine(_dir, "c.propgen");
        Settings s0 = Defaults();
        var h = new History();
        h.Push("open", s0);
        WorkFile.Save(path, APiece(), s0, h);
        string before = File.ReadAllText(path);

        h.Push("more", s0.With("contrast", 0.1));
        Assert.Throws<IOException>(() =>
            WorkFile.Save(path, APiece(), s0.With("contrast", 0.1), h, () =>
            {
                Assert.True(File.Exists(path + ".tmp"));
                throw new IOException("crash");
            }));

        Assert.Equal(before, File.ReadAllText(path));
        Assert.Single(WorkFile.Load(path).History.Steps);
    }

    [Fact]
    public void NumbersKeepTheirExactJsonTextThroughASave()
    {
        Settings s = Defaults().With("seed", 1).With("contrast", 0.272).With("grain", 0.1);
        var h = new History();
        h.Push("a", s);
        string path = Path.Combine(_dir, "n.propgen");
        WorkFile.Save(path, APiece(), s, h);
        var (_, settings, history) = WorkFile.Load(path);
        Assert.Equal("1", settings.Values["seed"].GetRawText());
        Assert.Equal("0.272", settings.Values["contrast"].GetRawText());
        Assert.Equal("0.1", settings.Values["grain"].GetRawText());
        Assert.Equal("1", history.Steps[0].Settings.Values["seed"].GetRawText());
        Assert.Equal("0.272", history.Steps[0].Settings.Values["contrast"].GetRawText());
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void FailedCleanupDoesNotHideTheSaveError()
    {
        string path = Path.Combine(_dir, "d.propgen");
        Settings s = Defaults();
        var h = new History();
        h.Push("a", s);
        var e = Assert.Throws<InvalidOperationException>(() =>
            WorkFile.Save(path, APiece(), s, h, () =>
            {
                File.SetUnixFileMode(_dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                throw new InvalidOperationException("original");
            }));
        File.SetUnixFileMode(_dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Assert.Equal("original", e.Message);
    }

    [Theory]
    [InlineData("contrast", "\"high\"", "a number")]
    [InlineData("seed", "2.5", "a whole number")]
    [InlineData("broken_edge", "1", "true or false")]
    [InlineData("colour", "[0.1, 0.2]", "three numbers")]
    [InlineData("colour", "\"#ff0000\"", "three numbers")]
    public void AValueOfTheWrongTypeIsRefusedOnLoadNamingTheSettingAndWhere(string key, string json, string expected)
    {
        Settings good = Defaults();
        var h = new History();
        h.Push("open", good);
        h.Push("bad", new Settings(new Dictionary<string, JsonElement>(good.Values) { [key] = JsonDocument.Parse(json).RootElement.Clone() }));
        string path = Path.Combine(_dir, "t.propgen");
        WorkFile.Save(path, APiece(), good, h);
        IReadOnlyList<ParamSpec> specs = ParamSchema.Load(Path.Combine(AppContext.BaseDirectory, "floor_params.json"));

        var e = Assert.Throws<WorkFileException>(() => WorkFile.Load(path, specs));
        Assert.Contains($"'{key}'", e.Message, StringComparison.Ordinal);
        Assert.Contains("undo step 2", e.Message, StringComparison.Ordinal);
        Assert.Contains(expected, e.Message, StringComparison.Ordinal);
        Assert.Equal(good, WorkFile.Load(path).Settings);
    }
    private static ParamSpec Spec(string key, string type, string json, IReadOnlyList<ChoiceItem>? options = null) =>
        new(key, key, "tip", "paint", type, null, null, null, JsonDocument.Parse(json).RootElement.Clone(), null, options);

    private string SaveWith(string key, string json)
    {
        Settings s = new(new Dictionary<string, JsonElement> { [key] = JsonDocument.Parse(json).RootElement.Clone() });
        var h = new History();
        h.Push("open", s);
        string path = Path.Combine(_dir, "c.propgen");
        WorkFile.Save(path, APiece(), s, h);
        return path;
    }

    [Fact]
    public void ChoiceValueNotAnOptionIsRefused()
    {
        ParamSpec kind = Spec("kind", "choice", "\"boulder\"", [new ChoiceItem("boulder", "Boulder"), new ChoiceItem("outcrop", "Outcrop")]);
        var e = Assert.Throws<WorkFileException>(() => WorkFile.Load(SaveWith("kind", "\"blob\""), [kind]));
        Assert.Contains("'kind'", e.Message, StringComparison.Ordinal);
        Assert.Contains("one of boulder, outcrop", e.Message, StringComparison.Ordinal);
        _ = WorkFile.Load(SaveWith("kind", "\"outcrop\""), [kind]);
    }

    [Theory]
    [InlineData("-1", false)]
    [InlineData("1.5", false)]
    [InlineData("4294967296", false)]
    [InlineData("\"7\"", false)]
    [InlineData("4294967295", true)]
    [InlineData("0", true)]
    public void SeedMustBeAWholeNumberInRange(string json, bool accepted)
    {
        ParamSpec seed = Spec("rock_seed", "seed", "1");
        string path = SaveWith("rock_seed", json);
        if (accepted)
        {
            _ = WorkFile.Load(path, [seed]);
        }
        else
        {
            var e = Assert.Throws<WorkFileException>(() => WorkFile.Load(path, [seed]));
            Assert.Contains("'rock_seed'", e.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FloorFilesStillLoad()
    {
        IReadOnlyList<ParamSpec> specs = ParamSchema.Load(Path.Combine(AppContext.BaseDirectory, "floor_params.json"));
        Assert.All(specs, s =>
        {
            Assert.True(s.Group is "Wood" or "Carving" or "Planks" or "Grain" or "Edges", s.Key);
            Assert.Null(s.Options);
            Assert.Null(s.Unit);
        });
        string path = Path.Combine(_dir, "f.propgen");
        var h = new History();
        h.Push("open", Defaults());
        WorkFile.Save(path, APiece(), Defaults(), h);
        Assert.Equal(Defaults(), WorkFile.Load(path, specs).Settings);
    }

    [Fact]
    public void ASettingTheFileLacksLoadsAsItsDefaultInTheSettingsAndEveryUndoStep()
    {
        // A file saved before 'build' existed: its settings and its undo steps have only 'kind'.
        ParamSpec kind = Spec("kind", "choice", "\"boulder\"", [new ChoiceItem("boulder", "Boulder"), new ChoiceItem("outcrop", "Outcrop")]);
        ParamSpec build = Spec("build", "choice", "\"painted\"", [new ChoiceItem("painted", "Normal"), new ChoiceItem("low-poly", "Low-poly")]);
        static Settings Kind(string value) => new(new Dictionary<string, JsonElement> { ["kind"] = JsonSerializer.SerializeToElement(value) });
        var h = new History();
        h.Push("one", Kind("boulder"));
        h.Push("two", Kind("outcrop"));
        h.Undo();
        string path = Path.Combine(_dir, "old.propgen");
        WorkFile.Save(path, APiece(), Kind("boulder"), h);

        var (_, settings, history) = WorkFile.Load(path, [kind, build]);

        Assert.Equal("painted", settings.GetString("build"));
        Assert.Equal("boulder", settings.GetString("kind"));
        Assert.All(history.Steps, step => Assert.Equal("painted", step.Settings.GetString("build")));
        Assert.Equal(["boulder", "outcrop"], history.Steps.Select(step => step.Settings.GetString("kind")));
        Assert.Equal(0, history.Index);
    }

    [Fact]
    public void TheBeforeDefaultsHookFillsTheSettingsAndEveryUndoStepBeforeTheDefaults()
    {
        // An old rock: saved before 'grain' existed. The hook (upstream's earlier looks) gives it 0, which the default
        // (0.5, a new rock's) would otherwise take; a file that has the key keeps its own value.
        ParamSpec grain = Spec("grain", "float", "0.5");
        static Settings Kind(string value) => new(new Dictionary<string, JsonElement> { ["kind"] = JsonSerializer.SerializeToElement(value) });
        var seen = new List<bool>();
        Settings Earlier(Settings s)
        {
            seen.Add(s.Values.ContainsKey("grain"));
            return s.Values.ContainsKey("grain") ? s : s.With("grain", 0.0);
        }

        var h = new History();
        h.Push("one", Kind("boulder"));
        h.Push("two", Kind("outcrop").With("grain", 0.7));
        string path = Path.Combine(_dir, "old.propgen");
        WorkFile.Save(path, APiece(), Kind("outcrop"), h);

        var (_, settings, history) = WorkFile.Load(path, [grain], Earlier);

        Assert.Equal(0.0, settings.GetFloat("grain"));
        Assert.Equal([0.0f, 0.7f], history.Steps.Select(step => step.Settings.GetFloat("grain")));
        Assert.Equal([false, false, true], seen);
        Assert.Equal(0.5, WorkFile.Load(path, [grain]).Settings.GetFloat("grain"), 6);
    }

    [Fact]
    public void ASavedFileCarriesItsEngineAtTheTopAndStaysFormat1()
    {
        var h = new History();
        h.Push("open", Defaults());
        string path = Path.Combine(_dir, "stamped.propgen");
        WorkFile.Save(path, APiece(), Defaults(), h, new EngineStamp("v0.4.5", "33148d06a564c8e2e261b902e9537e14d8a3553d"));

        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(["format", "app", "engine", "piece", "settings", "history"], root.Select(kv => kv.Key));
        Assert.Equal(1, root["format"]!.GetValue<int>());
        Assert.Equal("v0.4.5", root["engine"]!["tag"]!.GetValue<string>());
        Assert.Equal("33148d06a564c8e2e261b902e9537e14d8a3553d", root["engine"]!["commit"]!.GetValue<string>());
        Assert.False(root["settings"]!.AsObject().ContainsKey("engine"));

        // What a phase 2 build reads is unchanged.
        var (piece, settings, history) = WorkFile.Load(path);
        Assert.Equal(APiece().Recipe, piece.Recipe);
        Assert.Equal(Defaults(), settings);
        Assert.Single(history.Steps);

        // A file saved without an engine (our own kinds) has no block.
        WorkFile.Save(path, APiece(), Defaults(), h);
        Assert.False(JsonNode.Parse(File.ReadAllText(path))!.AsObject().ContainsKey("engine"));
    }

    [Fact]
    public void AnUnknownTopLevelKeyIsIgnored()
    {
        var h = new History();
        h.Push("open", Defaults());
        string path = Path.Combine(_dir, "newer.propgen");
        WorkFile.Save(path, APiece(), Defaults(), h);
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["fromTheFuture"] = new JsonObject { ["anything"] = new JsonArray(1, 2, 3) };
        File.WriteAllText(path, root.ToJsonString());

        Assert.Equal(Defaults(), WorkFile.Load(path).Settings);
    }
}
