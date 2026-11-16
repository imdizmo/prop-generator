using PropGenerator.Nature.Export;
using PropGenerator.Nature.Stylized;
using PropGenerator.Nature.Textures;
using Xunit;

namespace PropGenerator.Nature.Tests;

/// <summary>What an export refuses, and how its levels are built; its files are pinned in <see cref="SharedPinsTests"/>.</summary>
public class PackageTests
{
    private static ExportSource Source => StylizedRockGenerator.Source(StylizedRockSettings.Defaults);

    // Package.Levels builds nothing until asked, one level per step, and takes the wind and the shapes from level 0 only:
    // an export of 3 levels never holds them all at once.
    [Fact]
    public void LevelsAreBuiltOneAtATimeWithWindAndShapesFromTheFirst()
    {
        int built = 0;
        ExportSource source = SharedPinsTests.Synthetic();
        ExportSource counting = source with
        {
            BuildAtDetail = detail =>
            {
                built++;
                return source.BuildAtDetail(detail);
            },
        };
        using IEnumerator<ExportLevel> levels = Package.Levels(counting, new ExportSettings("windy", "godot", 3)).GetEnumerator();
        Assert.Equal(0, built);
        Assert.True(levels.MoveNext());
        Assert.Equal(1, built);
        ExportLevel first = levels.Current;
        Assert.NotNull(first.Wind);
        Assert.NotNull(first.Shapes);
        Assert.True(levels.MoveNext());
        Assert.Equal(2, built);
        Assert.Same(first.Wind, levels.Current.Wind);
        Assert.Null(levels.Current.Shapes);
        Assert.Null(levels.Current.Joined);
        Assert.True(levels.MoveNext());
        Assert.Equal(3, built);
        Assert.False(levels.MoveNext());
        Assert.Equal(3, built);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void LevelsOutsideOneToThreeAreRefused(int levels) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Package.Build(Source, new ExportSettings("rock", "plain", levels), new TextureCache()));

    [Fact]
    public void AnUnknownEngineIsRefused() =>
        Assert.Throws<ArgumentException>(() => Package.Build(Source, new ExportSettings("rock", "blender", 1), new TextureCache()));

    [Fact]
    public void AStillPropHasNoWindEvenWhenAskedFor()
    {
        ExportLevel level = Package.Levels(Source, new ExportSettings("rock", "plain", 1)).Single();
        Assert.Null(level.Wind);
        Assert.Null(level.Merged[0].Mesh.Uv1);
    }
}
