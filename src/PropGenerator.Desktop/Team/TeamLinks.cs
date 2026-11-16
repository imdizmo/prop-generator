using System.Text.Json;
using System.Text.Json.Nodes;

namespace PropGenerator.Desktop.Team;

/// <summary>
/// Which of your work files are which prop on the team, and the version you last published or took of each, so the next
/// publish is a new version of the same prop (and is refused when someone published since). Kept in a small JSON file
/// (team-links.json in the config folder); a missing or broken file reads as no links.
/// </summary>
internal sealed class TeamLinks
{
    private readonly string _path;
    private readonly Dictionary<string, (string Id, int Version)> _links = new(StringComparer.Ordinal);

    /// <summary>Reads the file if there is one.</summary>
    /// <param name="path">The file.</param>
    internal TeamLinks(string path)
    {
        _path = path;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject o)
            {
                foreach ((string file, JsonNode? v) in o)
                {
                    if (v?["id"]?.GetValue<string>() is { } id && v["version"]?.GetValue<int>() is int version)
                    {
                        _links[file] = (id, version);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            // No links.
        }
    }

    /// <summary>The team prop a work file is, if it is one.</summary>
    /// <param name="workFile">The work file.</param>
    /// <returns>Its id and version, or null.</returns>
    internal (string Id, int Version)? Of(string workFile) =>
        _links.TryGetValue(Path.GetFullPath(workFile), out var l) ? l : null;

    /// <summary>Remembers that a work file is a version of a team prop.</summary>
    /// <param name="workFile">The work file.</param>
    /// <param name="id">The prop's id.</param>
    /// <param name="version">The version.</param>
    internal void Set(string workFile, string id, int version)
    {
        _links[Path.GetFullPath(workFile)] = (id, version);
        Save();
    }

    /// <summary>Follows a work file to its new name.</summary>
    /// <param name="from">The old path.</param>
    /// <param name="to">The new path.</param>
    internal void Move(string from, string to)
    {
        if (_links.Remove(Path.GetFullPath(from), out var l))
        {
            _links[Path.GetFullPath(to)] = l;
            Save();
        }
    }

    // ponytail: rewritten whole; a few hundred links at most.
    private void Save()
    {
        var o = new JsonObject();
        foreach ((string file, (string id, int version)) in _links)
        {
            o[file] = new JsonObject { ["id"] = id, ["version"] = version };
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(_path + ".tmp", o.ToJsonString());
            File.Move(_path + ".tmp", _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // ponytail: the next publish of this prop then goes up as a second team prop; no work is lost.
        }
    }
}
