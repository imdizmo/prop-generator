// Ported from easy-props frontend/src/core/export/engine-kits.ts (MIT, © 2026 AHS12).
using System.Text;

namespace PropGenerator.Nature.Export;

/// <summary>
/// The files that set a prop up in an engine: its materials and how to use them. The kits are easy-props' own files,
/// copied byte for byte into this library, so saving a kit needs nothing but a folder.
/// </summary>
public static class EngineKits
{
    private const string Prefix = "PropGenerator.Nature.EngineKits/";

    // The Godot materials a look needs: wood and leaves that move in the wind, and lines for the toon looks.
    private static readonly Dictionary<string, string[]> GodotMaterials = new(StringComparer.Ordinal)
    {
        [ExportLook.Lit] = ["wood.gdshader", "leaves.gdshader"],
        [ExportLook.Cartoon] = ["wood_cartoon.gdshader", "leaves_cartoon.gdshader", "outline.gdshader"],
        [ExportLook.Cel] = ["wood_comic.gdshader", "leaves_comic.gdshader", "outline.gdshader"],
    };

    /// <summary>Gets what each kit holds, in the user's words.</summary>
    public static IReadOnlyDictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["godot"] = "Wind materials, Cartoon and Comic materials, and setup steps for Godot.",
        ["unreal"] = "A script that sets up your props in Unreal, and the steps to do it by hand.",
    };

    /// <summary>The folder a kit is saved in.</summary>
    /// <param name="engine"><c>godot</c> or <c>unreal</c>.</param>
    public static string Folder(string engine) => $"easy-props-{engine}-kit";

    /// <summary>The Godot materials a look needs, as files.</summary>
    /// <param name="look">One of <see cref="ExportLook.All"/>.</param>
    public static IReadOnlyList<ExportFile> GodotMaterialFiles(string look)
    {
        var kit = Kit("godot");
        return [.. GodotMaterials[look].Select(name => new ExportFile(name, kit[name], null))];
    }

    /// <summary>Every file of an engine's kit, in easy-props' order.</summary>
    /// <param name="engine"><c>godot</c> or <c>unreal</c>.</param>
    public static IReadOnlyList<ExportFile> KitFiles(string engine) =>
        [.. Kit(engine).Select(file => new ExportFile(file.Key, file.Value, null))];

    /// <summary>A kit file's text, by its path under the kits (<c>godot/wood.gdshader</c>).</summary>
    /// <param name="path">The path.</param>
    public static string Text(string path) => Encoding.UTF8.GetString(Bytes(path));

    private static OrderedDictionary<string, byte[]> Kit(string engine) => engine switch
    {
        "godot" => new(StringComparer.Ordinal)
        {
            ["README.md"] = Bytes("godot/README.md"),
            ["wood.gdshader"] = Bytes("godot/wood.gdshader"),
            ["leaves.gdshader"] = Bytes("godot/leaves.gdshader"),
            ["wood_cartoon.gdshader"] = Encoding.UTF8.GetBytes(GodotToon.Shader(ExportLook.Cartoon, "wood")),
            ["leaves_cartoon.gdshader"] = Encoding.UTF8.GetBytes(GodotToon.Shader(ExportLook.Cartoon, "leaves")),
            ["wood_comic.gdshader"] = Encoding.UTF8.GetBytes(GodotToon.Shader(ExportLook.Cel, "wood")),
            ["leaves_comic.gdshader"] = Encoding.UTF8.GetBytes(GodotToon.Shader(ExportLook.Cel, "leaves")),
            ["outline.gdshader"] = Encoding.UTF8.GetBytes(GodotToon.OutlineShader()),
        },
        "unreal" => new(StringComparer.Ordinal)
        {
            ["README.md"] = Bytes("unreal/README.md"),
            ["setup_props.py"] = Bytes("unreal/setup_props.py"),
        },
        _ => throw new ArgumentException($"No kit for '{engine}': godot or unreal", nameof(engine)),
    };

    private static byte[] Bytes(string path)
    {
        using Stream stream = typeof(EngineKits).Assembly.GetManifestResourceStream(Prefix + path)
            ?? throw new InvalidOperationException($"The engine kit file {path} is not in this library");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
