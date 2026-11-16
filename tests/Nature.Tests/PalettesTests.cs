using System.Text.Json;
using PropGenerator.Nature.Style;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class PalettesTests
{
    [Fact]
    public void TheRockRampsMatchEasyProps()
    {
        using var doc = Fixture.Json("params-palettes.json");
        var golden = doc.RootElement.GetProperty("palettes");

        Same(golden.GetProperty("ROCK_RAMPS"), Palettes.RockRamps, r => r);
    }

    private static IEnumerable<string> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!);

    private static void Same<T>(JsonElement golden, IReadOnlyDictionary<string, T> actual, Func<T, IEnumerable<string>> colours)
    {
        var properties = golden.EnumerateObject().ToList();
        Assert.Equal(properties.Select(p => p.Name), actual.Keys);
        foreach (var property in properties)
        {
            List<string> expected = property.Value.ValueKind == JsonValueKind.Array
                ? [.. Strings(property.Value)]
                : [.. property.Value.EnumerateObject().SelectMany(f => f.Value.ValueKind == JsonValueKind.Array ? Strings(f.Value) : [f.Value.GetString()!])];
            Assert.Equal(expected, colours(actual[property.Name]));
        }
    }
}
