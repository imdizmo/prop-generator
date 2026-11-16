using Xunit;
using static PropGenerator.Tests.Support.Fixtures;

namespace PropGenerator.Tests;

public class SettingsTests
{
    [Fact]
    public void SettingsWithKeepsTheOriginalAndEqualsByValue()
    {
        Settings a = Defaults();
        Settings b = a.With("contrast", 0.9);
        Assert.NotEqual(a, b);
        Assert.Equal(0.9f, b.GetFloat("contrast"));
        Assert.Equal(a, Defaults());
        Assert.Equal(a.GetHashCode(), Defaults().GetHashCode());
        Assert.Equal(new Rgb(1, 0, 0.5f), a.With("colour", new Rgb(1, 0, 0.5f)).GetColour("colour"));
    }

    [Fact]
    public void GetStringAndGetSeedReadTheirValues()
    {
        Settings s = Defaults().With("kind", "outcrop").With("rock_seed", 4294967295L);
        Assert.Equal("outcrop", s.GetString("kind"));
        Assert.Equal(4294967295u, s.GetSeed("rock_seed"));
    }

    [Fact]
    public void DefaultsTakeChoiceAndSeedDefaults()
    {
        ParamSpec[] specs =
        [
            new("kind", "Kind", "t", "paint", "choice", null, null, null, System.Text.Json.JsonSerializer.SerializeToElement("boulder"), "Shape", [new ChoiceItem("boulder", "Boulder")]),
            new("rock_seed", "Seed", "t", "paint", "seed", null, null, null, System.Text.Json.JsonSerializer.SerializeToElement(42)),
        ];
        var d = Settings.Defaults(specs);
        Assert.Equal("boulder", d.GetString("kind"));
        Assert.Equal(42u, d.GetSeed("rock_seed"));
    }
}
