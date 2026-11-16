using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

/// <summary>The committed fixtures stay small, and the tests kept past the port read them, never the goldens folder.</summary>
public class FixturesTests
{
    // The tests of shared code, which outlive the port and its goldens.
    private static readonly string[] Kept =
    [
        "HashTests.cs", "SeededRandomTests.cs", "RampTests.cs", "FlatColoursTests.cs", "PalettesTests.cs",
        "VariationsTests.cs", "EngineKitsTests.cs", "JsMathTests.cs", "KernelGoldensTests.cs", "Box3dTests.cs", "Color3dTests.cs",
        "EulerTests.cs", "Mat3dTests.cs", "Mat4dTests.cs", "QuatdTests.cs", "Vec3dTests.cs", "MeshOpsTests.cs", "MaterialsTests.cs",
        "SharedPinsTests.cs", "BannedSymbolsTests.cs", "Support/Fixture.cs", "Support/KernelGoldens.cs", "Support/KernelFamily.cs",
    ];

    [Fact]
    public void FixturesStayUnder2MB()
    {
        long total = Directory.EnumerateFiles(Fixture.Dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        Assert.True(total <= 2L * 1024 * 1024, $"tests/Nature.Tests/Fixtures holds {total} bytes, more than 2 MB");
    }

    [Fact]
    public void NoKeptTestReadsTheGoldensFolder()
    {
        string root = Path.Combine(Fixture.RepoRoot(), "tests", "Nature.Tests");
        foreach (string name in Kept)
        {
            string text = File.ReadAllText(Path.Combine(root, name));
            Assert.False(text.Contains("goldens/", StringComparison.Ordinal), $"{name} names the goldens folder");
            Assert.False(text.Contains("Goldens.Json(", StringComparison.Ordinal) || text.Contains("Goldens.Read(", StringComparison.Ordinal) || text.Contains("Goldens.Dir", StringComparison.Ordinal), $"{name} reads the goldens");
        }
    }
}
