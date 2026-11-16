using System.Text.Json;

namespace PropGenerator.Tests.Support;

/// <summary>Settings and a piece for the work file, history, settings and library tests.</summary>
internal static class Fixtures
{
    /// <summary>The settings list's defaults (the list is copied beside the tests).</summary>
    /// <returns>Every default.</returns>
    public static Settings Defaults() =>
        Settings.Defaults(ParamSchema.Load(Path.Combine(AppContext.BaseDirectory, "floor_params.json")));

    /// <summary>A floor tile with two marked faces, one with its own contrast.</summary>
    /// <returns>The piece.</returns>
    public static Piece APiece() => new("floor_tile",
    [
        new FaceMark(0, "plank_top", new Dictionary<string, JsonElement>()),
        new FaceMark(1, "cut_end", new Dictionary<string, JsonElement> { ["contrast"] = JsonSerializer.SerializeToElement(0.5) }),
    ]);
}
