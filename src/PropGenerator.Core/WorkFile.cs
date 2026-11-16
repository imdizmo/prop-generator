using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PropGenerator;

/// <summary>A piece saved as one JSON file with its settings and its undo steps inside.</summary>
public static class WorkFile
{
    /// <summary>The file format this code writes and reads.</summary>
    public const int Format = 1;

    /// <summary>The name a new piece is saved under.</summary>
    public const string DefaultName = "untitled.propgen";

    /// <summary>
    /// The largest work file read, in bytes: a file keeps at most 200 undo steps, and even 200 steps each holding a tree's
    /// largest own edits (256 KB) come to about 50 MB; a real one is a few hundred KB. A larger file (a shared one, made to
    /// freeze or exhaust the app) is refused before it is read.
    /// </summary>
    public const long MaxBytes = 128L * 1024 * 1024;

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    // The app version comes from the VERSION file, through the build's Version property.
    private static string AppVersion { get; } = (typeof(WorkFile).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0").Split('+')[0];

    /// <summary>Saves a piece. The old file stays whole until the new one is complete.</summary>
    /// <param name="path">The work file.</param>
    /// <param name="piece">The piece.</param>
    /// <param name="current">The settings now.</param>
    /// <param name="h">The undo history.</param>
    /// <param name="engine">
    /// The engine that made the prop (easy-props' kinds), written as an <c>engine</c> block at the top of the file beside
    /// the settings (older builds leave it alone); null for our own kinds, which have none.
    /// </param>
    public static void Save(string path, Piece piece, Settings current, History h, EngineStamp? engine = null) =>
        Save(path, piece, current, h, beforeMove: null, engine);

    /// <summary>Loads a work file.</summary>
    /// <param name="path">The work file.</param>
    /// <returns>The piece, its current settings and its history.</returns>
    public static (Piece Piece, Settings Settings, History History) Load(string path)
    {
        if (new FileInfo(path) is { Exists: true, Length: > MaxBytes } info)
        {
            throw new WorkFileException($"'{path}' is too large to be a work file ({info.Length / (1024 * 1024)} MB; a work file is at most {MaxBytes / (1024 * 1024)} MB).");
        }

        try
        {
            JsonNode root = JsonNode.Parse(File.ReadAllText(path)) ?? throw new WorkFileException("The file is empty.");
            int format = root["format"]?.GetValue<int>() ?? throw new WorkFileException("The file has no format number.");
            if (format != Format)
            {
                throw new WorkFileException($"This file is format {format}; this app reads format {Format}.");
            }

            JsonNode piece = root["piece"] ?? throw new WorkFileException("The file has no piece.");
            var faces = new List<FaceMark>();
            foreach (JsonNode? f in piece["faces"]!.AsArray())
            {
                faces.Add(new FaceMark(f!["faceId"]!.GetValue<int>(), f["kind"]!.GetValue<string>(), ReadValues(f["settings"])));
            }

            Settings current = new(ReadValues(root["settings"]));
            JsonNode hist = root["history"]!;
            var steps = hist["steps"]!.AsArray()
                .Select(s => new HistoryStep(s!["label"]!.GetValue<string>(), new Settings(ReadValues(s["settings"]))));
            var h = History.Restore(steps, hist["index"]!.GetValue<int>());
            return (new Piece(piece["recipe"]!.GetValue<string>(), faces), current, h);
        }
        catch (Exception e) when (e is JsonException or NullReferenceException or InvalidOperationException
                                      or FormatException or KeyNotFoundException)
        {
            throw new WorkFileException($"'{path}' is not a readable work file: {e.Message}", e);
        }
    }

    /// <summary>
    /// Loads a work file and checks every value it holds (the settings, each undo step's, each face's) against the
    /// settings list: a number where a number goes, a whole number for whole-number settings, true or false for
    /// on/off ones, three numbers for a colour, one of the options for a choice, a whole number from 0 to 4294967295 for a seed. Keys the list does not have are left alone.
    /// </summary>
    /// <param name="path">The work file.</param>
    /// <param name="specs">The settings list.</param>
    /// <returns>The piece, its current settings and its history.</returns>
    /// <exception cref="WorkFileException">The file cannot be read, or a value has the wrong type (the message says which).</exception>
    public static (Piece Piece, Settings Settings, History History) Load(string path, IReadOnlyList<ParamSpec> specs) =>
        Load(path, specs, null);

    /// <summary>
    /// Loads a work file as <see cref="Load(string, IReadOnlyList{ParamSpec})"/> does, first giving the settings and every
    /// undo step to <paramref name="beforeDefaults"/>, so a setting the file was saved without can take the value the
    /// prop had when it was saved (easy-props' earlier looks) instead of a new prop's default.
    /// </summary>
    /// <param name="path">The work file.</param>
    /// <param name="specs">The settings list.</param>
    /// <param name="beforeDefaults">Fills what an old file lacks, before the defaults; null for nothing.</param>
    /// <returns>The piece, its current settings and its history.</returns>
    /// <exception cref="WorkFileException">The file cannot be read, or a value has the wrong type (the message says which).</exception>
    public static (Piece Piece, Settings Settings, History History) Load(string path, IReadOnlyList<ParamSpec> specs, Func<Settings, Settings>? beforeDefaults)
    {
        ArgumentNullException.ThrowIfNull(specs);
        (Piece Piece, Settings Settings, History History) loaded = Load(path);
        var byKey = specs.ToDictionary(x => x.Key);
        Check(loaded.Settings.Values, byKey, "the settings");
        for (int i = 0; i < loaded.History.Steps.Count; i++)
        {
            Check(loaded.History.Steps[i].Settings.Values, byKey, $"undo step {i + 1}");
        }

        foreach (FaceMark f in loaded.Piece.Faces)
        {
            Check(f.Settings, byKey, $"face {f.FaceId}");
        }

        // A setting the file was saved without (it came in a later version) takes its default, in the settings and in
        // every undo step, so an old file opens as it was made and undo walks back through it.
        Func<Settings, Settings> earlier = beforeDefaults ?? (s => s);
        Settings current = WithDefaults(earlier(loaded.Settings), specs);
        var steps = loaded.History.Steps.Select(step => step with { Settings = WithDefaults(earlier(step.Settings), specs) }).ToList();
        return (loaded.Piece, current, History.Restore(steps, loaded.History.Index));
    }

    private static Settings WithDefaults(Settings values, IReadOnlyList<ParamSpec> specs)
    {
        Settings s = values;
        foreach (ParamSpec spec in specs.Where(spec => !values.Values.ContainsKey(spec.Key)))
        {
            s = s.With(spec.Key, spec.Default);
        }

        return s;
    }

    private static void Check(IReadOnlyDictionary<string, JsonElement> values, Dictionary<string, ParamSpec> specs, string where)
    {
        foreach (KeyValuePair<string, JsonElement> kv in values)
        {
            if (!specs.TryGetValue(kv.Key, out ParamSpec? spec))
            {
                continue;
            }

            JsonElement v = kv.Value;
            (bool ok, string wanted) = spec.Type switch
            {
                "float" => (v.ValueKind == JsonValueKind.Number, "a number"),
                "int" => (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && d == Math.Round(d)
                    && d >= int.MinValue && d <= int.MaxValue, "a whole number"),
                "bool" => (v.ValueKind is JsonValueKind.True or JsonValueKind.False, "true or false"),
                "colour" => (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3
                    && v.EnumerateArray().All(c => c.ValueKind == JsonValueKind.Number), "three numbers (red, green, blue)"),
                "seed" => (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double sd) && sd == Math.Round(sd)
                    && sd >= 0 && sd <= uint.MaxValue, "a whole number from 0 to 4294967295"),
                "choice" => (v.ValueKind == JsonValueKind.String
                    && (spec.Options is null || spec.Options.Any(o => o.Value == v.GetString())),
                    "one of " + string.Join(", ", spec.Options?.Select(o => o.Value) ?? [])),
                _ => (true, string.Empty),
            };
            if (!ok)
            {
                throw new WorkFileException($"'{kv.Key}' ({spec.Label}) in {where} should be {wanted}, but is {v.GetRawText()}.");
            }
        }
    }

    /// <summary>Saves, running a hook between writing the temporary file and moving it into place (for tests).</summary>
    /// <param name="path">The work file.</param>
    /// <param name="piece">The piece.</param>
    /// <param name="current">The settings now.</param>
    /// <param name="h">The undo history.</param>
    /// <param name="beforeMove">Called once the temporary file is written; may throw.</param>
    /// <param name="engine">The engine that made the prop, or null.</param>
    internal static void Save(string path, Piece piece, Settings current, History h, Action? beforeMove, EngineStamp? engine = null)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(h);
        var root = new JsonObject
        {
            ["format"] = Format,
            ["app"] = AppVersion,
        };
        if (engine is not null)
        {
            root["engine"] = new JsonObject { ["tag"] = engine.Tag, ["commit"] = engine.Commit };
        }

        root["piece"] = new JsonObject
        {
            ["recipe"] = piece.Recipe,
            ["faces"] = new JsonArray(piece.Faces.Select(f => (JsonNode)new JsonObject
            {
                ["faceId"] = f.FaceId,
                ["kind"] = f.Kind,
                ["settings"] = WriteValues(f.Settings),
            }).ToArray()),
        };
        root["settings"] = WriteValues(current.Values);
        root["history"] = new JsonObject
        {
            ["index"] = h.Index,
            ["steps"] = new JsonArray(h.Steps.Select(s => (JsonNode)new JsonObject
            {
                ["label"] = s.Label,
                ["settings"] = WriteValues(s.Settings.Values),
            }).ToArray()),
        };

        string tmp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });
                root.WriteTo(w, Pretty);
                w.Flush();
                fs.Flush(true);
            }

            beforeMove?.Invoke();
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(tmp);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The save error is the one worth reporting; a leftover .tmp is harmless.
            }

            throw;
        }
    }

    private static JsonObject WriteValues(IReadOnlyDictionary<string, JsonElement> values)
    {
        var o = new JsonObject();
        foreach (KeyValuePair<string, JsonElement> kv in values)
        {
            o[kv.Key] = JsonNode.Parse(kv.Value.GetRawText());
        }

        return o;
    }

    private static Dictionary<string, JsonElement> ReadValues(JsonNode? node)
    {
        var d = new Dictionary<string, JsonElement>();
        foreach (KeyValuePair<string, JsonNode?> kv in node!.AsObject())
        {
            d[kv.Key] = JsonSerializer.SerializeToElement(kv.Value);
        }

        return d;
    }
}
