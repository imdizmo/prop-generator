// Adapted from easy-props frontend/src/core/export/set-colours.ts, set-list.ts and the set part of
// frontend/src/store/editor-store.ts (startBatch) (MIT, © 2026 AHS12).
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Engine;

namespace PropGenerator.Desktop.Batch;

/// <summary>One prop of a set: what it is called, its kind and its settings (its saved colours, before the set's).</summary>
/// <param name="Name">Its name: before the export, the prop's own (a file's, the open prop's); at the export, upstream's numbered item name (oak_01).</param>
/// <param name="Recipe">Its kind (an easy-props kind: only those can be in a set).</param>
/// <param name="Settings">Its settings as saved.</param>
internal sealed record SetProp(string Name, EngineRecipe Recipe, Settings Settings);

/// <summary>A set as chosen: its name as typed, its colour groups and the choice made for each group.</summary>
/// <param name="Name">The set's name as typed (at most <see cref="ExportSet.MaxName"/> characters).</param>
/// <param name="Groups">The colour settings its props share (upstream's <c>colourGroups</c>).</param>
/// <param name="Colours">The choice for each group, by the group's id.</param>
internal sealed record SetChoice(string Name, IReadOnlyList<EngineColourGroup> Groups, IReadOnlyDictionary<string, string> Colours)
{
    /// <summary>The colours as the engine takes them: <c>{groupId: value}</c>.</summary>
    /// <returns>JSON.</returns>
    internal string ColoursJson() => ExportSet.ColoursJson(Colours);

    /// <summary>The groups as the engine gave them.</summary>
    /// <returns>JSON.</returns>
    internal string GroupsJson() => JsonSerializer.Serialize(Groups, ExportSet.Json);
}

/// <summary>
/// Export as a set (decisions E7 and E8): several props matched to the same colours, written flat into one folder
/// with materials an engine can share, and a list of what went in. The rules (the colour groups, the names) are
/// upstream's, asked of easy-props' engine.
/// </summary>
internal static class ExportSet
{
    /// <summary>The longest set name (upstream's field's <c>maxLength</c>).</summary>
    internal const int MaxName = 60;

    /// <summary>The name a set starts with, and the one an empty name stands for (upstream's).</summary>
    internal const string DefaultName = "My set";

    /// <summary>The most props whose small pictures are drawn (upstream's <c>PREVIEWS</c>).</summary>
    internal const int Previews = 8;

    /// <summary>How large each small picture is, in pixels (upstream's <c>PREVIEW_SIZE</c>).</summary>
    internal const int PreviewSize = 72;

    /// <summary>JSON as the engine reads it (camelCase).</summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // The settings a set's colours are made of (upstream's COLOUR_KEYS).
    private static readonly string[] ColourKeys = ["palette", "barkTone", "flowerColour"];

    /// <summary>A typed set name, cut at <see cref="MaxName"/> characters.</summary>
    /// <param name="typed">The name typed.</param>
    /// <returns>The name kept.</returns>
    internal static string Cut(string? typed) => typed is null ? string.Empty : typed.Length > MaxName ? typed[..MaxName] : typed;

    /// <summary>The name the set is exported under: the name typed, or "My set" when nothing is (upstream's <c>name.trim() || 'My set'</c>).</summary>
    /// <param name="typed">The name typed.</param>
    /// <returns>The name.</returns>
    internal static string NameOf(string? typed) => Cut(typed).Trim() is { Length: > 0 } name ? name : DefaultName;

    /// <summary>The colour settings these props share, each starting on the choice most of them use (upstream's <c>colourGroups</c>, from the rules engine).</summary>
    /// <param name="props">The set's props.</param>
    /// <returns>The groups, in upstream's order.</returns>
    /// <exception cref="EngineException">The rules engine failed.</exception>
    internal static IReadOnlyList<EngineColourGroup> Groups(IReadOnlyList<SetProp> props)
    {
        ArgumentNullException.ThrowIfNull(props);
        var list = new JsonArray();
        foreach (SetProp prop in props)
        {
            list.Add(new JsonObject
            {
                ["generatorId"] = prop.Recipe.Id,
                ["parameters"] = JsonNode.Parse(prop.Recipe.ValuesJson(prop.Settings)),
            });
        }

        return EngineHost.Default.Rules.ColourGroups(list.ToJsonString());
    }

    /// <summary>The set's starting colours: each group's usual choice (upstream's <c>usualColours</c>).</summary>
    /// <param name="groups">The groups.</param>
    /// <returns>The choice for each group, by its id.</returns>
    internal static Dictionary<string, string> Usual(IReadOnlyList<EngineColourGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var colours = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (EngineColourGroup group in groups)
        {
            colours[group.Id] = group.Usual;
        }

        return colours;
    }

    /// <summary>Colours as the engine takes them.</summary>
    /// <param name="colours">The choice for each group, by its id.</param>
    /// <returns>JSON: <c>{groupId: value}</c>, in id order so the same colours give the same text.</returns>
    internal static string ColoursJson(IReadOnlyDictionary<string, string> colours)
    {
        ArgumentNullException.ThrowIfNull(colours);
        var o = new JsonObject();
        foreach (KeyValuePair<string, string> pick in colours.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            o[pick.Key] = pick.Value;
        }

        return o.ToJsonString();
    }

    /// <summary>
    /// The prop in the set's colours: upstream's <c>inSetColours</c> on the engine (its colour settings changed where the
    /// set says, which keeps its own light and dark), read back as the prop's settings. The saved prop is not touched.
    /// </summary>
    /// <param name="prop">The prop.</param>
    /// <param name="coloursJson">The set's colours (<see cref="ColoursJson"/>).</param>
    /// <param name="kind">The engine's turn: export for the export, show-more for the small pictures.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The settings in the set's colours.</returns>
    /// <exception cref="EngineException">The engine failed.</exception>
    internal static async Task<Settings> InSetColoursAsync(SetProp prop, string coloursJson, EngineJobKind kind, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(prop);
        string scene = await Task.Run(() => prop.Recipe.SceneJson(prop.Settings), cancel).ConfigureAwait(false);
        string coloured = await EngineHost.Default.Builds.InSetColoursAsync(scene, coloursJson, "low-poly", kind, cancel).ConfigureAwait(false);
        Settings s = prop.Settings;
        if (JsonNode.Parse(coloured)?["parameters"] is not JsonObject parameters)
        {
            return s;
        }

        foreach (string key in ColourKeys)
        {
            if (parameters[key] is JsonValue v && v.TryGetValue(out string? value) && prop.Recipe.Specs.Any(p => p.Key == key)
                && !(s.Values.TryGetValue(key, out JsonElement now) && now.ValueKind == JsonValueKind.String && now.GetString() == value))
            {
                s = s.With(key, value);
            }
        }

        return s;
    }

    /// <summary>
    /// The props named as upstream's <c>batchItems</c> names a batch of saved props, one version each: each name made
    /// export-safe and numbered (oak_01, oak_02 for two props both called oak). One job; names only.
    /// </summary>
    /// <param name="props">The props, by their own names.</param>
    /// <param name="cancel">Stops the naming.</param>
    /// <returns>The props, renamed, in order.</returns>
    /// <exception cref="EngineException">The engine could not name them.</exception>
    internal static async Task<IReadOnlyList<SetProp>> NamedAsync(IReadOnlyList<SetProp> props, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(props);
        var choices = new JsonArray([.. props.Select(p => (JsonNode)new JsonObject { ["kind"] = "scene", ["name"] = p.Name })]);
        IReadOnlyList<EngineBatchItem> named = await EngineHost.Default.Builds
            .BatchItemsAsync(choices.ToJsonString(), 1, new JsonObject { ["style"] = "low-poly" }.ToJsonString(), cancel)
            .ConfigureAwait(true);
        if (named.Count != props.Count)
        {
            throw new EngineScriptException(string.Create(CultureInfo.InvariantCulture, $"batchItems named {named.Count} props for {props.Count}."));
        }

        return [.. named.Zip(props, static (item, prop) => prop with { Name = item.Name })];
    }

    /// <summary>One prop's files as the set's list reads them: each .glb as its JSON chunk (where its materials are named).</summary>
    /// <param name="files">The prop's files.</param>
    /// <returns>JSON: <c>[{name, json?}]</c>.</returns>
    internal static JsonArray EntryFiles(IReadOnlyList<EngineFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var list = new JsonArray();
        foreach (EngineFile file in files)
        {
            var entry = new JsonObject { ["name"] = file.Name };
            if (file.Name.EndsWith(".glb", StringComparison.Ordinal) && JsonChunk(file.Bytes) is string json)
            {
                entry["json"] = json;
            }

            list.Add(entry);
        }

        return list;
    }

    // A GLB's JSON chunk: 'glTF', then the chunk's length at 12 and its text from 20.
    private static string? JsonChunk(byte[] bytes)
    {
        if (bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != 0x46546c67)
        {
            return null;
        }

        int length = (int)Math.Min(BitConverter.ToUInt32(bytes, 12), (uint)(bytes.Length - 20));
        return Encoding.UTF8.GetString(bytes, 20, length);
    }
}
