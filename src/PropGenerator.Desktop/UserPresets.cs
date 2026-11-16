using System.Text.Json;
using System.Text.Json.Nodes;
using PropGenerator.Desktop.Recipes;

namespace PropGenerator.Desktop;

/// <summary>
/// My presets, as easy-props' own starting points: the settings of a piece (its seed too) saved under a name, shown in
/// the drawer's My presets tab and started from like any other starting point. Kept in one JSON file (normally
/// ~/.config/prop-generator/presets.json), at most 200; a missing or broken file reads as none.
/// </summary>
internal sealed class UserPresets
{
    /// <summary>The drawer group they are shown under.</summary>
    internal const string Group = "My presets";

    /// <summary>How many are kept.</summary>
    internal const int Max = 200;

    /// <summary>
    /// The largest presets file read, in bytes: 200 presets, each a piece's settings (a tree's own edits among them, at
    /// most 256 KB), come to about 50 MB at the very most. A larger file is read as none, before it is read into memory.
    /// </summary>
    internal const long MaxBytes = 64L * 1024 * 1024;

    private readonly string _path;

    // Every preset in the file's order: a shown one, or (Held) one of easy-props' kinds read while its engine could not
    // be loaded, not shown but kept in its place and written back. Both count against Max.
    private readonly List<Entry> _entries = [];

    // The shown ones, in order.
    private List<StartingPoint> _all = [];

    /// <summary>Reads the file if there is one.</summary>
    /// <param name="path">The file.</param>
    internal UserPresets(string path)
    {
        _path = path;
        try
        {
            if (new FileInfo(path).Length > MaxBytes)
            {
                throw new IOException($"{path} is larger than {MaxBytes} bytes.");
            }

            _entries.AddRange(Read(File.ReadAllBytes(path), held: true));
            Shown();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // None kept yet.
        }
    }

    /// <summary>Raised when one was saved, renamed or removed.</summary>
    internal event Action? Changed;

    /// <summary>Gets every preset of the user's, in the order saved.</summary>
    internal IReadOnlyList<StartingPoint> All => _all;

    /// <summary>
    /// Gets why some presets are not shown: they are of easy-props' kinds and its engine could not be loaded (they are
    /// kept in the file, and come back once it loads); null when every preset is shown.
    /// </summary>
    internal string? Hidden
    {
        get
        {
            int held = _entries.Count - _all.Count;
            return held == 0 || MeshRecipes.EngineFailure is not { } failure
                ? null
                : $"{(held == 1 ? "1 of My presets is" : $"{held} of My presets are")} not shown: {MeshRecipes.EngineDown(failure)}.";
        }
    }

    /// <summary>Saves settings as a new preset; when there are Max, the oldest goes (a held one too).</summary>
    /// <param name="name">Its name.</param>
    /// <param name="recipe">The recipe it makes.</param>
    /// <param name="settings">Its settings, the seed with them.</param>
    /// <returns>The preset.</returns>
    internal StartingPoint Add(string name, string recipe, Settings settings)
    {
        var p = new StartingPoint("mine-" + Guid.NewGuid().ToString("N")[..12], name.Trim(), Group, recipe, settings);
        if (_entries.Count >= Max)
        {
            _entries.RemoveAt(0);
        }

        _entries.Add(new Entry(p, null));
        Save();
        return p;
    }

    /// <summary>Renames a preset.</summary>
    /// <param name="id">Its id.</param>
    /// <param name="name">The new name.</param>
    internal void Rename(string id, string name)
    {
        int i = _entries.FindIndex(e => e.Shown?.Id == id);
        if (i >= 0 && name.Trim().Length > 0)
        {
            _entries[i] = new Entry(_entries[i].Shown! with { Name = name.Trim() }, null);
            Save();
        }
    }

    /// <summary>Removes a preset (pieces started from it keep their settings).</summary>
    /// <param name="id">Its id.</param>
    internal void Remove(string id)
    {
        if (_entries.RemoveAll(e => e.Shown?.Id == id) > 0)
        {
            Save();
        }
    }

    /// <summary>
    /// Merges a backup's presets: one with an id not here is added while there are fewer than Max (held ones counted);
    /// the rest is left as it is.
    /// </summary>
    /// <param name="json">The backup's presets.json.</param>
    internal void MergeFrom(byte[] json)
    {
        try
        {
            HashSet<string> ids = [.. _entries.Select(e => e.Id)];
            int before = _entries.Count;
            _entries.AddRange(Read(json, held: false).Where(e => ids.Add(e.Id)).Take(Max - _entries.Count));
            if (_entries.Count != before)
            {
                Save();
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            // Not a presets file: nothing merged.
        }
    }

    // The presets a file holds, in order; those of easy-props' kinds while its engine is down are held (or left out).
    private static List<Entry> Read(byte[] json, bool held)
    {
        var list = new List<Entry>();
        foreach (JsonObject p in (JsonNode.Parse(json)?["presets"] as JsonArray ?? []).OfType<JsonObject>().Take(Max))
        {
            string? id = p["id"]?.GetValue<string>();
            string? name = p["name"]?.GetValue<string>();
            string? recipe = p["recipe"]?.GetValue<string>();
            if (id is null || name is null || recipe is null || p["settings"] is not JsonObject values)
            {
                continue;
            }

            if (recipe != FloorRecipe.Id && MeshRecipes.Find(recipe) is null)
            {
                if (held && MeshRecipes.Unavailable(recipe) is not null)
                {
                    list.Add(new Entry(null, (JsonObject)p.DeepClone()));
                }

                continue;
            }

            var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach ((string key, JsonNode? v) in values)
            {
                settings[key] = JsonSerializer.SerializeToElement(v);
            }

            list.Add(new Entry(new StartingPoint(id, name, Group, recipe, Loaded(recipe, new Settings(settings))), null));
        }

        return list;
    }

    // A preset saved by an earlier version opens as it looked then (an old rock's earlier looks), as a work file does.
    // When the engine cannot say, the preset is kept as saved: it is the user's, and the file is written back whole.
    private static Settings Loaded(string recipe, Settings saved)
    {
        try
        {
            return MeshRecipes.Loaded(recipe, saved);
        }
        catch (WorkFileException e)
        {
            TraceLog.Write($"My presets: a {recipe} kept as saved ({e.Message})");
            return saved;
        }
    }

    private void Save()
    {
        try
        {
            var root = new JsonObject
            {
                ["presets"] = new JsonArray([.. _entries.Select(e =>
                {
                    if (e.Held is { } held)
                    {
                        return (JsonNode?)held.DeepClone();
                    }

                    StartingPoint p = e.Shown!;
                    var values = new JsonObject();
                    foreach ((string key, JsonElement v) in p.Settings.Values)
                    {
                        values[key] = JsonNode.Parse(v.GetRawText());
                    }

                    return (JsonNode?)new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["recipe"] = p.Recipe, ["settings"] = values };
                })]),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, root.ToJsonString());
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TraceLog.Write($"My presets not kept: {e.Message}");
        }

        Shown();
        Changed?.Invoke();
    }

    private void Shown() => _all = [.. _entries.Select(e => e.Shown).OfType<StartingPoint>()];

    // One preset: shown, or held as read.
    private readonly record struct Entry(StartingPoint? Shown, JsonObject? Held)
    {
        internal string Id => Shown?.Id ?? Held!["id"]!.GetValue<string>();
    }
}
