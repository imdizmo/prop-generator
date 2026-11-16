using System.Globalization;
using System.Text.Json;
using PropGenerator.Nature.Cliffs;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Textures;

// CliffExport <settings.json> <out.glb>: the settings file holds the parameter keys of the slab cliff
// ("width", "top-tilt", "palette" ...) and optionally "seed"; keys left out take their defaults.
// CliffExport --preset <id> [--seed <n>] <out.glb>: a starting point by its id ("long-cliff" ...), at its own seed
// unless one is given.
const string Usage = "usage: CliffExport <settings.json> <out.glb>\n       CliffExport --preset <id> [--seed <n>] <out.glb>";
CliffSettings settings;
string output;
if (args.Length is 3 or 5 && args[0] == "--preset")
{
    var preset = CliffPresets.All.Where(p => p.Id == args[1]).Select(p => p.Settings).FirstOrDefault();
    if (preset is null)
    {
        Console.Error.WriteLine($"no starting point '{args[1]}'; there are {string.Join(", ", CliffPresets.All.Select(p => p.Id))}");
        return 2;
    }

    if (args.Length == 5)
    {
        if (args[2] != "--seed" || !uint.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint seed))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        preset = preset with { Seed = seed };
    }

    settings = preset;
    output = args[^1];
}
else if (args.Length == 2)
{
    var raw = new Dictionary<string, object?>(StringComparer.Ordinal);
    uint seed = 1;
    using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(args[0])))
    {
        foreach (JsonProperty p in doc.RootElement.EnumerateObject())
        {
            if (p.Name == "seed")
            {
                seed = (uint)p.Value.GetDouble();
            }
            else
            {
                raw[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetDouble();
            }
        }
    }

    settings = CliffSettings.From(CliffParameterSet.Instance.Validate(raw).Parameters, seed);
    output = args[1];
}
else
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var export = new ExportSettings("cliff", "plain", 1, Collision: false);
IReadOnlyList<ExportFile> files = Package.Build(CliffExport.Source(settings), export, new TextureCache());
var glb = files.Single(f => f.Name.EndsWith(".glb", StringComparison.Ordinal));
File.WriteAllBytes(output, glb.Bytes);
Console.WriteLine($"{output}: {glb.Faces} faces, seed {settings.Seed}");
return 0;
