using Xunit;

namespace PropGenerator.Tests;

public class ParamSchemaTests
{
    private static string SchemaPath => Path.Combine(AppContext.BaseDirectory, "floor_params.json");

    [Fact]
    public void EveryParamHasARealTooltip()
    {
        foreach (var s in ParamSchema.Load(SchemaPath))
        {
            Assert.True(s.Tooltip.Length >= 40, s.Key);
            Assert.NotEqual(s.Label, s.Tooltip, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void LoadsEveryEntryWithItsFields()
    {
        var specs = ParamSchema.Load(SchemaPath);
        Assert.Equal(18, specs.Count);
        Assert.Equal(specs.Count, specs.Select(s => s.Key).Distinct().Count());
        var colour = specs[0];
        Assert.Equal("colour", colour.Key);
        Assert.Equal("tone", colour.Kind);
        Assert.Equal("colour", colour.Type);
        Assert.Equal(3, colour.Default.GetArrayLength());
        var contrast = specs.Single(s => s.Key == "contrast");
        Assert.Equal(0.2, contrast.Min);
        Assert.Equal(1.2, contrast.Max);
        Assert.Equal(0.65, contrast.Default.GetDouble());
        var broken = specs.Single(s => s.Key == "broken_edge");
        Assert.Null(broken.Min);
        Assert.True(broken.Default.GetBoolean());
        Assert.All(specs, s => Assert.True(s.Kind is "tone" or "paint", s.Key));
    }
}
