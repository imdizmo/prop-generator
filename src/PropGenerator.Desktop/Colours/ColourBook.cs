using System.Text.Json;
using System.Text.Json.Nodes;

namespace PropGenerator.Desktop.Colours;

/// <summary>
/// The colours the user keeps, after easy-props' colour picker and palettes, in one JSON file (normally
/// ~/.config/prop-generator/colours.json): Recent (the last eight colours chosen, newest first), My colours (kept until
/// removed, at most 200) and the saved palettes (at most 500). A missing or broken file reads as nothing kept.
/// </summary>
internal sealed class ColourBook
{
    /// <summary>How many recent colours are kept.</summary>
    internal const int MaxRecent = 8;

    /// <summary>How many of My colours are kept.</summary>
    internal const int MaxMine = 200;

    /// <summary>How many palettes are kept.</summary>
    internal const int MaxPalettes = 500;

    /// <summary>
    /// The largest colours file read, in bytes: 500 palettes of their colours and 200 colours of mine come to a few
    /// hundred KB. A larger file is read as nothing kept, before it is read into memory.
    /// </summary>
    internal const long MaxBytes = 4L * 1024 * 1024;

    private readonly string _path;
    private List<string> _recent = [];
    private readonly List<string> _mine = [];
    private readonly List<SavedPalette> _palettes = [];

    /// <summary>Reads the file if there is one.</summary>
    /// <param name="path">The file.</param>
    internal ColourBook(string path)
    {
        _path = path;
        try
        {
            if (new FileInfo(path).Length > MaxBytes)
            {
                throw new IOException($"{path} is larger than {MaxBytes} bytes.");
            }

            Read(File.ReadAllBytes(path), out _recent, out _mine, out _palettes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // Nothing kept yet.
        }
    }

    /// <summary>Raised when anything kept changed.</summary>
    internal event Action? Changed;

    /// <summary>Gets the recent colours, newest first.</summary>
    internal IReadOnlyList<string> Recent => _recent;

    /// <summary>Gets My colours, in the order added.</summary>
    internal IReadOnlyList<string> Mine => _mine;

    /// <summary>Gets the saved palettes, in the order saved.</summary>
    internal IReadOnlyList<SavedPalette> Palettes => _palettes;

    /// <summary>Notes a colour just chosen: it goes first in Recent (nothing changes when it is first already).</summary>
    /// <param name="hex">The colour, any code <see cref="ColourCodes"/> reads.</param>
    internal void NoteRecent(string hex)
    {
        if (ColourCodes.ParseOne(hex) is not { } c || (_recent.Count > 0 && _recent[0] == c))
        {
            return;
        }

        _recent = [c, .. _recent.Where(x => x != c).Take(MaxRecent - 1)];
        Save();
    }

    /// <summary>Adds colours to My colours (those already there are left where they are).</summary>
    /// <param name="hexes">The colours.</param>
    /// <returns>How many were added.</returns>
    internal int AddMine(IEnumerable<string> hexes)
    {
        int added = 0;
        foreach (string? c in hexes.Select(ColourCodes.ParseOne))
        {
            if (c is not null && !_mine.Contains(c) && _mine.Count < MaxMine)
            {
                _mine.Add(c);
                added++;
            }
        }

        if (added > 0)
        {
            Save();
        }

        return added;
    }

    /// <summary>Takes a colour out of My colours.</summary>
    /// <param name="hex">The colour.</param>
    internal void RemoveMine(string hex)
    {
        if (ColourCodes.ParseOne(hex) is { } c && _mine.Remove(c))
        {
            Save();
        }
    }

    /// <summary>Saves a palette; one of the same set with the same name (case not minded) is updated instead.</summary>
    /// <param name="set">Which colours these are.</param>
    /// <param name="name">Its name.</param>
    /// <param name="colours">The colours, in the rows' order.</param>
    /// <returns>True when a new palette was saved, false when one was updated.</returns>
    internal bool SavePalette(string set, string name, IReadOnlyList<string> colours)
    {
        string n = name.Trim();
        string[] cs = [.. colours.Select(c => ColourCodes.ParseOne(c) ?? "#808080")];
        int at = _palettes.FindIndex(p => p.Set == set && string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase));
        if (at >= 0)
        {
            _palettes[at] = _palettes[at] with { Colours = cs };
            Save();
            return false;
        }

        if (_palettes.Count >= MaxPalettes)
        {
            _palettes.RemoveAt(0);
        }

        _palettes.Add(new SavedPalette("palette-" + Guid.NewGuid().ToString("N")[..10], n, set, cs));
        Save();
        return true;
    }

    /// <summary>Removes a palette (props using its colours keep them).</summary>
    /// <param name="id">The palette's id.</param>
    internal void RemovePalette(string id)
    {
        if (_palettes.RemoveAll(p => p.Id == id) > 0)
        {
            Save();
        }
    }

    /// <summary>
    /// Merges a backup's colours: My colours are added; a palette of the same set and name with the same colours is
    /// skipped, one with other colours is added as "&lt;name&gt; (from backup)". Recent is left as it is. A file that
    /// cannot be read changes nothing.
    /// </summary>
    /// <param name="json">The backup's colours.json.</param>
    internal void MergeFrom(byte[] json)
    {
        List<string> mine;
        List<SavedPalette> palettes;
        try
        {
            Read(json, out _, out mine, out palettes);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return;
        }

        foreach (string c in mine.Where(c => !_mine.Contains(c) && _mine.Count < MaxMine))
        {
            _mine.Add(c);
        }

        foreach (SavedPalette p in palettes)
        {
            SavedPalette? same = _palettes.FirstOrDefault(x => x.Set == p.Set && string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            if (same is not null && same.Colours.SequenceEqual(p.Colours))
            {
                continue;
            }

            if (_palettes.Any(x => x.Set == p.Set && x.Colours.SequenceEqual(p.Colours) && x.Name.StartsWith(p.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string name = same is null ? p.Name : p.Name + " (from backup)";
            if (_palettes.Count < MaxPalettes)
            {
                _palettes.Add(p with { Id = "palette-" + Guid.NewGuid().ToString("N")[..10], Name = name });
            }
        }

        Save();
    }

    private static void Read(byte[] json, out List<string> recent, out List<string> mine, out List<SavedPalette> palettes)
    {
        JsonObject o = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Not a colours file.");
        recent = [.. Codes(o["recent"]).Distinct(StringComparer.Ordinal).Take(MaxRecent)];
        mine = [.. Codes(o["mine"]).Distinct(StringComparer.Ordinal).Take(MaxMine)];
        palettes = [];
        foreach (JsonObject p in (o["palettes"] as JsonArray ?? []).OfType<JsonObject>().Take(MaxPalettes))
        {
            string? id = p["id"]?.GetValue<string>();
            string? name = p["name"]?.GetValue<string>();
            string? set = p["set"]?.GetValue<string>();
            if (id is not null && name is not null && set is not null)
            {
                palettes.Add(new SavedPalette(id, name, set, [.. Codes(p["colours"])]));
            }
        }
    }

    // The colour codes of a list, in order; a palette keeps its repeats (each is a row), Recent and My colours do not.
    private static IEnumerable<string> Codes(JsonNode? n) =>
        (n as JsonArray ?? []).OfType<JsonValue>()
            .Select(v => v.TryGetValue(out string? s) ? ColourCodes.ParseOne(s) : null)
            .OfType<string>();

    private void Save()
    {
        try
        {
            var o = new JsonObject
            {
                ["recent"] = new JsonArray([.. _recent.Select(c => (JsonNode?)c)]),
                ["mine"] = new JsonArray([.. _mine.Select(c => (JsonNode?)c)]),
                ["palettes"] = new JsonArray([.. _palettes.Select(p => (JsonNode?)new JsonObject
                {
                    ["id"] = p.Id,
                    ["name"] = p.Name,
                    ["set"] = p.Set,
                    ["colours"] = new JsonArray([.. p.Colours.Select(c => (JsonNode?)c)]),
                })]),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, o.ToJsonString());
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TraceLog.Write($"colours not kept: {e.Message}");
        }

        Changed?.Invoke();
    }
}
