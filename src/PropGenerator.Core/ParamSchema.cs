using System.Text.Json;

namespace PropGenerator;

/// <summary>Reads the painter's list of settings.</summary>
public static class ParamSchema
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Reads floor_params.json (or a file of the same shape) in file order.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>One entry per setting.</returns>
    public static IReadOnlyList<ParamSpec> Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        List<ParamSpec> specs = JsonSerializer.Deserialize<List<ParamSpec>>(stream, Options)
            ?? throw new InvalidDataException($"{path} holds no settings list.");
        return specs;
    }
}
