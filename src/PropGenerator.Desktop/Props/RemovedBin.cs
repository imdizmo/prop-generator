using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PropGenerator.Desktop.Props;

/// <summary>
/// Recently removed, as easy-props' bin: a removed prop's work file waits in a folder of its own (normally
/// ~/.config/prop-generator/removed/) as &lt;id&gt;.propgen beside &lt;id&gt;.json, which says where it was, its name and when it
/// was removed. It can be put back for 30 days; the app forgets older ones when it starts.
/// </summary>
internal sealed class RemovedBin
{
    private readonly Func<DateTimeOffset> _now;

    /// <summary>Uses a bin folder (made on the first remove).</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="now">The clock (the tests give theirs).</param>
    internal RemovedBin(string folder, Func<DateTimeOffset>? now = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        Folder = folder;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Gets how long a removed prop waits before it is gone for good.</summary>
    internal static TimeSpan KeepFor { get; } = TimeSpan.FromDays(30);

    /// <summary>Raised when what the bin holds changed.</summary>
    internal event Action? Changed;

    /// <summary>Gets the bin's folder.</summary>
    internal string Folder { get; }

    /// <summary>Moves a work file into the bin.</summary>
    /// <param name="path">The work file.</param>
    /// <returns>The prop as the bin holds it.</returns>
    internal RemovedProp Remove(string path)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Folder);
        string id = Guid.NewGuid().ToString("N")[..16];
        var removed = new RemovedProp(id, Path.GetFileNameWithoutExtension(full), full, _now());
        File.Move(full, FileOf(id));
        var meta = new JsonObject
        {
            ["name"] = removed.Name,
            ["originalPath"] = removed.OriginalPath,
            ["removedAt"] = removed.RemovedAt.ToString("o", CultureInfo.InvariantCulture),
        };
        File.WriteAllText(MetaOf(id), meta.ToJsonString());
        Changed?.Invoke();
        return removed;
    }

    /// <summary>Lists what the bin holds, the one removed last first. A file without its note is left out.</summary>
    /// <returns>The props.</returns>
    internal IReadOnlyList<RemovedProp> List()
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var list = new List<RemovedProp>();
        foreach (string meta in Directory.EnumerateFiles(Folder, "*.json"))
        {
            string id = Path.GetFileNameWithoutExtension(meta);
            if (Read(id, meta) is { } r && File.Exists(FileOf(id)))
            {
                list.Add(r);
            }
        }

        return [.. list.OrderByDescending(r => r.RemovedAt)];
    }

    /// <summary>Puts a prop back where it was (its folder made again if it went).</summary>
    /// <param name="id">The prop's id in the bin.</param>
    /// <returns>The work file's path.</returns>
    /// <exception cref="IOException">Another file is in its place, or it is no longer in the bin.</exception>
    internal string PutBack(string id)
    {
        RemovedProp r = List().FirstOrDefault(x => x.Id == id)
            ?? throw new IOException("Couldn't put that prop back: it is no longer in Recently removed.");
        if (File.Exists(r.OriginalPath) || Directory.Exists(r.OriginalPath))
        {
            throw new IOException($"Couldn't put {r.Name} back: a saved prop already uses its place ({r.OriginalPath}).");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(r.OriginalPath)!);
        File.Move(FileOf(id), r.OriginalPath);
        File.Delete(MetaOf(id));
        Changed?.Invoke();
        return r.OriginalPath;
    }

    /// <summary>Removes a prop for good.</summary>
    /// <param name="id">Its id in the bin.</param>
    internal void Forget(string id)
    {
        File.Delete(FileOf(id));
        File.Delete(MetaOf(id));
        Changed?.Invoke();
    }

    /// <summary>Removes everything in the bin for good.</summary>
    /// <returns>How many props went.</returns>
    internal int Empty()
    {
        IReadOnlyList<RemovedProp> all = List();
        foreach (RemovedProp r in all)
        {
            File.Delete(FileOf(r.Id));
            File.Delete(MetaOf(r.Id));
        }

        if (Directory.Exists(Folder))
        {
            // Leftovers (a note without its file, a half-made move) go too.
            foreach (string f in Directory.EnumerateFiles(Folder))
            {
                File.Delete(f);
            }
        }

        Changed?.Invoke();
        return all.Count;
    }

    /// <summary>Removes for good what was removed more than 30 days ago (run when the app starts).</summary>
    /// <returns>How many props went.</returns>
    internal int ForgetOld()
    {
        DateTimeOffset now = _now();
        int gone = 0;
        foreach (RemovedProp r in List().Where(r => now - r.RemovedAt > KeepFor))
        {
            File.Delete(FileOf(r.Id));
            File.Delete(MetaOf(r.Id));
            gone++;
        }

        if (gone > 0)
        {
            Changed?.Invoke();
        }

        return gone;
    }

    private string FileOf(string id) => Path.Combine(Folder, id + ".propgen");

    private string MetaOf(string id) => Path.Combine(Folder, id + ".json");

    // A damaged note falls back on the file's own time, as easy-props does.
    private static RemovedProp? Read(string id, string meta)
    {
        try
        {
            var n = JsonNode.Parse(File.ReadAllText(meta));
            string? original = n?["originalPath"]?.GetValue<string>();
            if (original is null)
            {
                return null;
            }

            string name = n!["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(original);
            DateTimeOffset when = DateTimeOffset.TryParse(
                n["removedAt"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset at)
                ? at
                : File.GetLastWriteTimeUtc(meta);
            return new RemovedProp(id, name, original, when);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
