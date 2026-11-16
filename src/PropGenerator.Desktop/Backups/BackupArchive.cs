using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PropGenerator.Desktop.Backups;

/// <summary>
/// One backup as one zip, after easy-props' (format 1): backup.json lists each prop (its name in the zip, where it was,
/// its SHA-256 and when it was saved), props/ holds the work files and extras/ the app's own lists (colours, palettes,
/// My presets). A restore merges and never replaces: a prop already here as it is in the backup is left alone, a missing
/// one comes back where it was, and one that differs comes back beside it as "&lt;name&gt; (from backup)".
/// Sizes are checked as the zip gives them, before anything is written, and the bytes written are counted too.
/// </summary>
internal static class BackupArchive
{
    /// <summary>The format this code writes and reads.</summary>
    internal const int Format = 1;

    /// <summary>
    /// The largest backup.json a restore reads, in bytes: a prop's line in it is about 250 bytes, so this holds far
    /// more than <see cref="MaxProps"/> props. A larger one is refused before it is parsed.
    /// </summary>
    internal const long MaxManifest = 16L * 1024 * 1024;

    /// <summary>The most props one backup may list: far more than anyone's library holds (hundreds).</summary>
    internal const int MaxProps = 20_000;

    /// <summary>
    /// The most bytes a restore writes, all props together: a work file is a few megabytes at most, so a real backup
    /// is far below this; a zip whose props come to more (as their sizes in the zip say) is refused before any is written,
    /// and a restore stops once the bytes it has written come to more.
    /// </summary>
    internal const long MaxTotal = 4L * 1024 * 1024 * 1024;

    private const long MaxEntry = 64L * 1024 * 1024;
    private const long MaxExtra = 4L * 1024 * 1024;

    private const string TooLarge = "That backup is too large to restore (more props or bytes than a Prop Generator backup holds).";

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>Writes a backup (to a file beside it first, then moved into place).</summary>
    /// <param name="zipPath">The zip to write.</param>
    /// <param name="props">The work files.</param>
    /// <param name="extras">The app's lists to keep with them, by file name.</param>
    /// <param name="now">When the backup is made.</param>
    /// <returns>How many props it holds (a file that went missing meanwhile is left out).</returns>
    internal static int Write(string zipPath, IReadOnlyList<string> props, IReadOnlyDictionary<string, byte[]> extras, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(extras);
        string temp = zipPath + ".tmp";
        var files = new JsonArray();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (ZipArchive zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (string path in props)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(path);
                string inZip = $"props/{name}.propgen";
                for (int n = 2; !used.Add(inZip); n++)
                {
                    inZip = $"props/{name} {n}.propgen";
                }

                ZipArchiveEntry entry = zip.CreateEntry(inZip, CompressionLevel.Optimal);
                entry.LastWriteTime = File.GetLastWriteTime(path);
                using (Stream to = entry.Open())
                {
                    using FileStream from = File.OpenRead(path);
                    from.CopyTo(to);
                }

                files.Add(new JsonObject
                {
                    ["path"] = inZip,
                    ["original"] = Path.GetFullPath(path),
                    ["sha256"] = Sha(path),
                    ["savedAt"] = File.GetLastWriteTimeUtc(path).ToString("o", CultureInfo.InvariantCulture),
                });
            }

            foreach ((string name, byte[] bytes) in extras)
            {
                using Stream to = zip.CreateEntry("extras/" + Path.GetFileName(name)).Open();
                to.Write(bytes);
            }

            var manifest = new JsonObject
            {
                ["format"] = Format,
                ["app"] = BuildInfo.Version,
                ["madeAt"] = now.ToString("o", CultureInfo.InvariantCulture),
                ["files"] = files,
            };
            using Stream m = zip.CreateEntry("backup.json").Open();
            m.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, Pretty));
        }

        File.Move(temp, zipPath, overwrite: true);
        return files.Count;
    }

    /// <summary>Restores a backup into the library, merging (see the class).</summary>
    /// <param name="zipPath">The zip.</param>
    /// <param name="library">The library the props go back into.</param>
    /// <param name="restoredFolder">Where a prop goes whose old folder cannot be made here (another computer's).</param>
    /// <param name="trusted">The folders a prop may go back into (those of the library's props and the save folder); null for the library's own.</param>
    /// <returns>What it did.</returns>
    /// <exception cref="BackupException">The file is not a backup, one made by a newer app, or too large.</exception>
    internal static BackupRestore Restore(string zipPath, Library library, string restoredFolder, IReadOnlyCollection<string>? trusted = null) =>
        Restore(zipPath, library, restoredFolder, trusted, MaxTotal);

    /// <summary>Restores a backup into the library, writing at most so many bytes of props (the tests' smaller limit).</summary>
    /// <param name="zipPath">The zip.</param>
    /// <param name="library">The library the props go back into.</param>
    /// <param name="restoredFolder">Where a prop goes whose old folder cannot be made here (another computer's).</param>
    /// <param name="trusted">The folders a prop may go back into; null for the library's own.</param>
    /// <param name="maxTotal">The most bytes the props may come to, together.</param>
    /// <returns>What it did.</returns>
    /// <exception cref="BackupException">The file is not a backup, one made by a newer app, or too large.</exception>
    internal static BackupRestore Restore(string zipPath, Library library, string restoredFolder, IReadOnlyCollection<string>? trusted, long maxTotal)
    {
        ArgumentNullException.ThrowIfNull(library);
        string[] known = [.. (trusted ?? [.. library.List().Select(e => Path.GetDirectoryName(e.Path)!)])
            .Where(Path.IsPathFullyQualified).Select(d => Path.TrimEndingDirectorySeparator(Path.GetFullPath(d))).Distinct(StringComparer.Ordinal)];
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            JsonNode manifest = ReadManifest(zip);
            JsonArray files = manifest["files"]?.AsArray() ?? [];
            if (files.Count > MaxProps || SaysMoreThan(zip, maxTotal))
            {
                throw new BackupException(TooLarge);
            }

            int added = 0, same = 0, copies = 0, refused = 0;
            long written = 0;

            // Each entry comes back once: a manifest naming one entry again (under another name) is refused there, so
            // the props written come to no more than the zip holds.
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonNode? f in files)
            {
                string? inZip = f?["path"]?.GetValue<string>();
                string? original = f?["original"]?.GetValue<string>();
                ZipArchiveEntry? entry = inZip is null ? null : zip.GetEntry(inZip);
                string name = original is null ? string.Empty : Path.GetFileName(original.Replace('\\', '/'));
                if (inZip is null || entry is null || !Safe(inZip) || entry.Length > MaxEntry || !Safe("props/" + name)
                    || !name.EndsWith(".propgen", StringComparison.OrdinalIgnoreCase) || !used.Add(inZip))
                {
                    refused++;
                    continue;
                }

                // The manifest's place is only a hint: used when it is a plain path into a folder that is already
                // here and already one of the user's own; else the prop comes back in the restored folder.
                string sha = f!["sha256"]?.GetValue<string>() ?? string.Empty;
                string target;
                if (Trusted(original!, known) is string place)
                {
                    target = place;
                }
                else
                {
                    Directory.CreateDirectory(restoredFolder);
                    target = Path.Combine(restoredFolder, name);
                }

                if (File.Exists(target) && Sha(target) == sha)
                {
                    same++;
                    library.Add(target);
                    continue;
                }

                bool copy = File.Exists(target);
                if (copy)
                {
                    target = FreeCopyName(target, sha, out bool found);
                    if (found)
                    {
                        same++;
                        library.Add(target);
                        continue;
                    }
                }

                written += Extract(entry, target, maxTotal - written);
                library.Add(target);
                if (copy)
                {
                    copies++;
                }
                else
                {
                    added++;
                }
            }

            var extras = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (ZipArchiveEntry e in zip.Entries.Where(e => e.FullName.StartsWith("extras/", StringComparison.Ordinal)))
            {
                string name = e.FullName["extras/".Length..];
                if (BackupActions.ExtraFiles.Contains(name, StringComparer.Ordinal) && e.Length <= MaxExtra)
                {
                    using Stream s = e.Open();
                    using var m = new MemoryStream();
                    s.CopyTo(m);
                    extras[name] = m.ToArray();
                }
            }

            return new BackupRestore(added, same, copies, refused, extras);
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or InvalidOperationException)
        {
            throw new BackupException("That file isn't a Prop Generator backup. Pick a .zip made with Back up to a file.", e);
        }
    }

    // The manifest, checked: a zip without one is not a backup; a newer format is refused.
    private static JsonNode ReadManifest(ZipArchive zip)
    {
        ZipArchiveEntry entry = zip.GetEntry("backup.json")
            ?? throw new BackupException("That file isn't a Prop Generator backup. Pick a .zip made with Back up to a file.");
        if (entry.Length > MaxManifest)
        {
            throw new BackupException(TooLarge);
        }

        using Stream s = entry.Open();
        JsonNode manifest = JsonNode.Parse(s) ?? throw new BackupException("That file isn't a Prop Generator backup. Pick a .zip made with Back up to a file.");
        int format = manifest["format"]?.GetValue<int>() ?? 0;
        if (format > Format)
        {
            throw new BackupException("This backup was made by a newer Prop Generator. Update the app, then try again.");
        }

        if (format < 1)
        {
            throw new BackupException("That file isn't a Prop Generator backup. Pick a .zip made with Back up to a file.");
        }

        return manifest;
    }

    // Only props/<one file>.propgen: no folders, no way out, no backslashes.
    private static bool Safe(string inZip)
    {
        if (!inZip.StartsWith("props/", StringComparison.Ordinal))
        {
            return false;
        }

        string name = inZip["props/".Length..];
        return name.Length > 0 && !name.Contains('/', StringComparison.Ordinal) && !name.Contains('\\', StringComparison.Ordinal)
            && name is not "." and not ".." && !name.StartsWith('.') && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    // The manifest's path, when it is absolute, plain (no "..", ".", UNC or device form, no backslash) and its folder is
    // already there and is, or is inside, a known folder; else null. No folder is ever made from it.
    private static string? Trusted(string original, string[] known)
    {
        if (!Path.IsPathFullyQualified(original) || original.Contains('\\', StringComparison.Ordinal)
            || original.StartsWith("//", StringComparison.Ordinal) || original.Split('/').Any(p => p is ".." or "."))
        {
            return null;
        }

        string full = Path.GetFullPath(original);
        string? folder = Path.GetDirectoryName(full);
        if (folder is null || full != original || !Directory.Exists(folder))
        {
            return null;
        }

        folder = Path.TrimEndingDirectorySeparator(folder);
        return known.Any(k => folder == k || folder.StartsWith(k + Path.DirectorySeparatorChar, StringComparison.Ordinal)) ? full : null;
    }

    // "<name> (from backup)", then " 2", " 3"…; a copy already holding the backup's contents is found instead.
    private static string FreeCopyName(string target, string sha, out bool found)
    {
        string folder = Path.GetDirectoryName(target)!;
        string name = Path.GetFileNameWithoutExtension(target) + " (from backup)";
        string candidate = Path.Combine(folder, name + ".propgen");
        for (int n = 2; File.Exists(candidate); n++)
        {
            if (Sha(candidate) == sha)
            {
                found = true;
                return candidate;
            }

            candidate = Path.Combine(folder, $"{name} {n}.propgen");
        }

        found = false;
        return candidate;
    }

    // Whether the props in the zip, as their headers say, come to more than the limit (counted without overflowing).
    private static bool SaysMoreThan(ZipArchive zip, long limit)
    {
        long total = 0;
        foreach (ZipArchiveEntry e in zip.Entries.Where(e => e.FullName.StartsWith("props/", StringComparison.Ordinal)))
        {
            total += Math.Min(e.Length, limit + 1);
            if (total > limit)
            {
                return true;
            }
        }

        return false;
    }

    // Writes the entry to the target through a file beside it, counting the bytes as they are written; past the
    // limit nothing is kept and the restore stops.
    private static long Extract(ZipArchiveEntry entry, string target, long limit)
    {
        if (entry.Length > limit)
        {
            throw new BackupException(TooLarge);
        }

        string temp = target + ".restoring";
        long total = 0;
        using (FileStream to = File.Create(temp))
        {
            using Stream from = entry.Open();
            byte[] buffer = new byte[81920];
            int n;
            while ((n = from.Read(buffer)) > 0)
            {
                total += n;
                if (total > limit)
                {
                    break;
                }

                to.Write(buffer, 0, n);
            }
        }

        if (total > limit)
        {
            File.Delete(temp);
            throw new BackupException(TooLarge);
        }

        File.Move(temp, target);
        File.SetLastWriteTime(target, entry.LastWriteTime.LocalDateTime);
        return total;
    }

    private static string Sha(string path)
    {
        using FileStream s = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }
}
