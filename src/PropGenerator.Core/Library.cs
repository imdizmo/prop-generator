using System.Text.Json;
using System.Text.Json.Nodes;

namespace PropGenerator;

/// <summary>The list of work files opened before, newest first, kept in one JSON file.</summary>
public sealed class Library
{
    private readonly int _cap;

    /// <summary>Opens (or starts) the library.</summary>
    /// <param name="path">The library file, normally ~/.config/prop-generator/library.json.</param>
    /// <param name="cap">The most entries kept.</param>
    public Library(string path, int cap = 500)
    {
        FilePath = path;
        _cap = cap;
    }

    /// <summary>The default library file, in the user's config folder.</summary>
    /// <returns>The path.</returns>
    public static string DefaultPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "prop-generator", "library.json");

    /// <summary>Notes that a file was opened (or saved): it moves to the top of the list.</summary>
    /// <param name="path">The work file.</param>
    public void Add(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        List<LibraryEntry> all = Read();
        all.RemoveAll(e => e.Path == full);
        all.Insert(0, new LibraryEntry(
            full, System.IO.Path.GetFileNameWithoutExtension(full), DateTimeOffset.UtcNow, ReadColourHex(full)));
        if (all.Count > _cap)
        {
            all.RemoveRange(_cap, all.Count - _cap);
        }

        Write(all);
    }

    /// <summary>
    /// Notes that a file was renamed (moved) on disk: its entry takes the new path and name and keeps its place, its
    /// date and its colour. A file the library did not have is added at the top, as <see cref="Add"/> does.
    /// </summary>
    /// <param name="oldPath">The work file's path before.</param>
    /// <param name="newPath">The work file's path now.</param>
    public void Rename(string oldPath, string newPath)
    {
        string from = System.IO.Path.GetFullPath(oldPath);
        string to = System.IO.Path.GetFullPath(newPath);
        Renamed?.Invoke(from, to);
        List<LibraryEntry> all = Read();
        int i = all.FindIndex(e => e.Path == from);
        if (i < 0)
        {
            Add(to);
            return;
        }

        all.RemoveAll(e => e.Path == to);
        i = all.FindIndex(e => e.Path == from);
        all[i] = all[i] with { Path = to, Name = System.IO.Path.GetFileNameWithoutExtension(to) };
        Write(all);
    }

    /// <summary>Raised when a work file is renamed (old full path, new full path), so what is kept by path can follow it.</summary>
    public event Action<string, string>? Renamed;

    /// <summary>Gets the library file.</summary>
    public string FilePath { get; }

    /// <summary>Forgets a file (it was removed); a file the library does not have changes nothing.</summary>
    /// <param name="path">The work file.</param>
    public void Forget(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        List<LibraryEntry> all = Read();
        if (all.RemoveAll(e => e.Path == full) > 0)
        {
            Write(all);
        }
    }

    /// <summary>Lists the files, newest first. Files that no longer exist are left out and forgotten.</summary>
    /// <returns>The entries.</returns>
    public IReadOnlyList<LibraryEntry> List()
    {
        List<LibraryEntry> all = Read();
        int before = all.Count;
        all.RemoveAll(e => !File.Exists(e.Path));
        if (all.Count != before)
        {
            try
            {
                Write(all);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Listing still works from memory; the cleanup is tried again next time.
            }
        }

        return all;
    }

    private static string? ReadColourHex(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement c = doc.RootElement.GetProperty("settings").GetProperty("colour");
            static string B(JsonElement e) => ((int)Math.Round(Math.Clamp(e.GetDouble(), 0, 1) * 255)).ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
            return $"#{B(c[0])}{B(c[1])}{B(c[2])}";
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException
                                      or UnauthorizedAccessException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private List<LibraryEntry> Read()
    {
        try
        {
            var list = new List<LibraryEntry>();
            foreach (JsonNode? n in JsonNode.Parse(File.ReadAllText(FilePath))!["entries"]!.AsArray())
            {
                list.Add(new LibraryEntry(
                    n!["path"]!.GetValue<string>(),
                    n["name"]!.GetValue<string>(),
                    DateTimeOffset.Parse(n["lastOpened"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture),
                    n["colourHex"]?.GetValue<string>()));
            }

            return list;
        }
        catch (Exception e) when (e is IOException or JsonException or NullReferenceException
                                      or InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Write(List<LibraryEntry> all)
    {
        string? dir = System.IO.Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var root = new JsonObject
        {
            ["entries"] = new JsonArray(all.Select(e => (JsonNode)new JsonObject
            {
                ["path"] = e.Path,
                ["name"] = e.Name,
                ["lastOpened"] = e.LastOpened.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                ["colourHex"] = e.ColourHex,
            }).ToArray()),
        };
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
