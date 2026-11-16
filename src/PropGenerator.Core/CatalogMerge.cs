using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PropGenerator;

/// <summary>
/// Adds kinds (and their GLB files) to a Groundwork map's catalog by Groundwork's rules: only inside the map's folder
/// (the models folder may be elsewhere if map.json says so), never through a link, old catalog kept as catalog.json.bak.
/// Everything in the catalog that is not ours is left exactly as it was.
/// </summary>
public static class CatalogMerge
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Adds or replaces the kinds, and writes the GLBs into the map's models folder.</summary>
    /// <param name="mapFolder">The map's folder (holds map.json).</param>
    /// <param name="kinds">The kinds to add; a kind with the same model name as an existing one replaces it.</param>
    /// <param name="glbs">GLB file contents by model name, written as models/name.glb. Optional.</param>
    public static void AddKinds(string mapFolder, IEnumerable<CatalogKind> kinds,
        IReadOnlyDictionary<string, byte[]>? glbs = null)
    {
        try
        {
            Merge(mapFolder, kinds, glbs);
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            throw new CatalogException($"The map's catalog could not be read or written: {e.Message}");
        }
    }

    /// <summary>Lists the kinds already in the map's catalog, to check names before writing. Changes nothing.</summary>
    /// <param name="mapFolder">The map's folder (holds map.json).</param>
    /// <returns>Each kind's model name and label (an empty list when the map has no catalog yet).</returns>
    /// <exception cref="CatalogException">The map or its catalog cannot be used.</exception>
    public static IReadOnlyList<(string Model, string Label)> ReadKinds(string mapFolder)
    {
        try
        {
            (_, _, string catalogPath) = OpenMap(mapFolder);
            if (!File.Exists(catalogPath))
            {
                return [];
            }

            JsonObject catalog = ParseObject(File.ReadAllText(catalogPath), Path.GetFileName(catalogPath));
            if (catalog["kinds"] is null)
            {
                return [];
            }

            if (catalog["kinds"] is not JsonArray array)
            {
                throw new CatalogException("The catalog's \"kinds\" is not a list.");
            }

            var result = new List<(string, string)>();
            foreach (JsonNode? node in array)
            {
                if (node is JsonObject o && o["model"] is JsonValue m && m.TryGetValue(out string? model))
                {
                    string label = (o["label"] as JsonValue)?.TryGetValue(out string? l) == true ? l! : string.Empty;
                    result.Add((model, label));
                }
            }

            return result;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            throw new CatalogException($"The map's catalog could not be read: {e.Message}");
        }
    }

    // The map folder checked, map.json read, and the catalog's path worked out (inside the map, never through a link).
    private static (string Root, JsonObject Map, string CatalogPath) OpenMap(string mapFolder)
    {
        string root = Path.GetFullPath(mapFolder);
        if (!Directory.Exists(root))
        {
            throw new CatalogException($"The map folder {root} does not exist.");
        }

        RefuseLinks(root, root);

        string mapJson = Path.Combine(root, "map.json");
        if (!File.Exists(mapJson))
        {
            throw new CatalogException("The folder has no map.json.");
        }

        RefuseLinks(root, mapJson);
        JsonObject map = ParseObject(File.ReadAllText(mapJson), "map.json");

        string catalogName = (map["catalog"] as JsonValue)?.TryGetValue(out string? c) == true && !string.IsNullOrEmpty(c)
            ? c! : "catalog.json";
        string catalogPath = InsideMap(root, catalogName, "catalog");

        return (root, map, catalogPath);
    }

    private static void Merge(string mapFolder, IEnumerable<CatalogKind> kinds, IReadOnlyDictionary<string, byte[]>? glbs)
    {
        var list = kinds.ToList();
        (string root, JsonObject map, string catalogPath) = OpenMap(mapFolder);

        string modelsName = (map["models"] as JsonValue)?.TryGetValue(out string? m) == true && !string.IsNullOrEmpty(m)
            ? m! : "models";
        string modelsDir = Path.IsPathRooted(modelsName)
            ? Path.GetFullPath(modelsName)
            : InsideMap(root, modelsName, "models folder");
        if (Path.IsPathRooted(modelsName))
        {
            // A shared folder is only used when it is real: there, not a link, models already in it (in it, not in a
            // folder below: "/home/someone" must not pass because some model lies deep inside). Never created.
            if (!Directory.Exists(modelsDir))
            {
                throw new CatalogException($"The models folder {modelsDir} does not exist.");
            }

            RefuseLinks(modelsDir, modelsDir);
            bool hasModels = Directory.EnumerateFiles(modelsDir, "*", SearchOption.TopDirectoryOnly)
                .Any(f => f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase));
            if (!hasModels)
            {
                throw new CatalogException($"The models folder {modelsDir} holds no models; not writing into it.");
            }
        }

        // Everything is read and checked before anything is written.
        JsonObject catalog;
        bool existed = File.Exists(catalogPath);
        string backupPath = catalogPath + ".bak";
        RefuseLinks(root, backupPath);
        if (existed)
        {
            catalog = ParseObject(File.ReadAllText(catalogPath), Path.GetFileName(catalogPath));
            if (catalog["kinds"] is not null and not JsonArray)
            {
                throw new CatalogException("The catalog's \"kinds\" is not a list.");
            }
        }
        else
        {
            catalog = [];
        }
        JsonArray array = catalog["kinds"] as JsonArray ?? [];
        if (catalog["kinds"] is null)
        {
            catalog["kinds"] = array;
        }

        List<(string File, byte[] Bytes)> files = [];
        if (glbs is not null)
        {
            foreach (var (name, bytes) in glbs)
            {
                if (name.Length == 0 || name.IndexOfAny(['/', '\\', '\0']) >= 0 || name.Contains(".."))
                {
                    throw new CatalogException($"\"{name}\" is not a usable model name.");
                }

                string file = Path.Combine(modelsDir, name + ".glb");
                string linkRoot = Path.IsPathRooted(modelsName) ? modelsDir : root;
                RefuseLinks(linkRoot, file);
                files.Add((file, bytes));
            }
        }

        foreach (var kind in list)
        {
            var entry = new JsonObject
            {
                ["model"] = kind.Model,
                ["label"] = kind.Label,
                ["category"] = kind.Category,
                ["scale"] = new JsonArray(kind.Scale.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()),
            };
            int at = -1;
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonObject o && o["model"] is JsonValue v && v.TryGetValue(out string? existing)
                    && existing == kind.Model) { at = i; break; }
            }
            if (at >= 0)
            {
                array[at] = entry;
            }
            else
            {
                array.Add(entry);
            }
        }

        string text = catalog.ToJsonString(Pretty);

        if (files.Count > 0)
        {
            Directory.CreateDirectory(modelsDir);
        }

        foreach (var (file, bytes) in files)
        {
            WriteAtomic(file, bytes);
        }

        if (existed)
        {
            // Through a temp file and a rename, so a link sitting at .bak is replaced, never written through.
            WriteAtomic(backupPath, File.ReadAllBytes(catalogPath));
        }

        WriteAtomic(catalogPath, System.Text.Encoding.UTF8.GetBytes(text + "\n"));
    }

    private static JsonObject ParseObject(string text, string what)
    {
        try
        {
            JsonObject node = JsonNode.Parse(text) as JsonObject ?? throw new CatalogException($"{what} is not a JSON object.");
            _ = node.ToJsonString();
            return node;
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            throw new CatalogException($"{what} cannot be read: {e.Message}");
        }
    }

    // A path named in map.json, which must stay inside the map's folder.
    private static string InsideMap(string root, string relative, string what)
    {
        if (Path.IsPathRooted(relative))
        {
            throw new CatalogException($"The {what} must be inside the map's folder.");
        }

        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new CatalogException($"The {what} must be inside the map's folder.");
        }

        RefuseLinks(root, full);
        return full;
    }

    // Refuses the path when it, or any folder from the base down to it that exists, is a link.
    private static void RefuseLinks(string baseDir, string path)
    {
        string full = Path.GetFullPath(path);
        string current = Path.GetFullPath(baseDir);
        string rest = Path.GetRelativePath(current, full);
        string[] parts = rest == "." ? [] : rest.Split(Path.DirectorySeparatorChar);
        CheckOne(current);
        foreach (string part in parts)
        {
            current = Path.Combine(current, part);
            CheckOne(current);
        }

        static void CheckOne(string p)
        {
            FileSystemInfo info = Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p);
            if (!info.Exists && info is FileInfo && info.LinkTarget is null)
            {
                return;
            }

            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new CatalogException($"{p} is a link; nothing is written through links.");
            }
        }
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        // A fresh name, created new: an existing file or link of any name is never opened.
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(bytes);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }
}
