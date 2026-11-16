using System.Reflection;
using System.Text.RegularExpressions;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public partial class BannedSymbolsTests
{
    private static readonly string Nature = Path.Combine(Fixture.RepoRoot(), "src", "PropGenerator.Nature");

    /// <summary>
    /// The folders from the repository root down to Nature: MSBuild reads a Directory.Build.* and the compiler an
    /// .editorconfig or .globalconfig from any of them.
    /// </summary>
    private static readonly string[] FoldersAbove = [Fixture.RepoRoot(), Path.Combine(Fixture.RepoRoot(), "src")];

    private static IEnumerable<string> Banned =>
        File.ReadAllLines(Path.Combine(Nature, "BannedSymbols.txt"))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split(';')[0]);

    [Fact]
    public void EveryBannedSymbolNamesARealMember()
    {
        foreach (string id in Banned)
        {
            Assert.True(Resolves(id), $"BannedSymbols.txt: {id} names nothing in .NET {Environment.Version}");
        }
    }

    [Fact]
    public void TheMathsThatDiffersFromV8IsBanned()
    {
        var banned = Banned.ToHashSet(StringComparer.Ordinal);
        string[] required =
        [
            "M:System.Math.FusedMultiplyAdd(System.Double,System.Double,System.Double)",
            "M:System.Math.ReciprocalEstimate(System.Double)",
            "M:System.Math.ReciprocalSqrtEstimate(System.Double)",
            "M:System.Math.Cbrt(System.Double)",
            "M:System.Math.Log2(System.Double)",
            "M:System.Math.Sinh(System.Double)",
            "M:System.Math.Cosh(System.Double)",
            "M:System.Math.Tanh(System.Double)",
            "M:System.Math.Asinh(System.Double)",
            "M:System.Math.Acosh(System.Double)",
            "M:System.Math.Atanh(System.Double)",
            "M:System.Double.Exp2(System.Double)",
            "M:System.Double.Exp10(System.Double)",
            "M:System.Double.ExpM1(System.Double)",
            "M:System.Double.LogP1(System.Double)",
            "M:System.Double.Cbrt(System.Double)",
            "M:System.Double.SinPi(System.Double)",
            "M:System.Double.CosPi(System.Double)",
            "M:System.Double.TanPi(System.Double)",
            "M:System.Single.Sin(System.Single)",
            "M:System.Single.Pow(System.Single,System.Single)",
            "M:System.Single.Round(System.Single)",
            "M:System.Single.FusedMultiplyAdd(System.Single,System.Single,System.Single)",
            "T:System.MathF",
        ];
        Assert.All(required, id => Assert.Contains(id, banned));
    }

    [Fact]
    public void OnlyJsMaySuppressTheBan()
    {
        string js = Path.Combine(Nature, "Js") + Path.DirectorySeparatorChar;
        var sources = Directory.EnumerateFiles(Nature, "*", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".targets" or ".editorconfig" or ".globalconfig")
            .Concat(FoldersAbove.SelectMany(folder => Directory.EnumerateFiles(folder).Where(IsBuildFile)))
            .ToList();
        Assert.Contains(Path.Combine(Fixture.RepoRoot(), "Directory.Build.props"), sources);
        Assert.Contains(Path.Combine(Fixture.RepoRoot(), ".editorconfig"), sources);
        foreach (string path in sources)
        {
            string text = File.ReadAllText(path);
            if (!path.EndsWith(".cs", StringComparison.Ordinal))
            {
                // A build file switches a rule off by NoWarn, WarningsNotAsErrors or a severity (its own, its category's or
                // every analyzer's), or switches the analyzers off; naming the rule in a comment does nothing.
                Assert.False(BuildSuppression().IsMatch(text), $"{path} suppresses RS0030; only the V8 ports in Js/ may use the banned maths");
                continue;
            }

            Assert.False(BareDisable().IsMatch(text), $"{path} has a #pragma warning disable without ids, which switches RS0030 off too");
            if (!text.Contains("RS0030", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.True(path.StartsWith(js, StringComparison.Ordinal),
                $"{path} suppresses RS0030; only the V8 ports in src/PropGenerator.Nature/Js/ may use the banned maths");
            AssertJustified(path, File.ReadAllLines(path));
        }
    }

    [Fact]
    public void TheBanIsWiredIntoTheBuild()
    {
        string project = File.ReadAllText(Path.Combine(Nature, "PropGenerator.Nature.csproj"));
        Assert.Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers", project, StringComparison.Ordinal);
        Assert.Contains("<AdditionalFiles Include=\"BannedSymbols.txt\" />", project, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<NoWarn>$(NoWarn);RS0030</NoWarn>")]
    [InlineData("<WarningsNotAsErrors>RS0030</WarningsNotAsErrors>")]
    [InlineData("dotnet_diagnostic.RS0030.severity = none")]
    [InlineData("dotnet_analyzer_diagnostic.category-ApiDesign.severity = silent")]
    [InlineData("dotnet_analyzer_diagnostic.severity = none")]
    [InlineData("<RunAnalyzers>false</RunAnalyzers>")]
    [InlineData("<RunAnalyzersDuringBuild> false </RunAnalyzersDuringBuild>")]
    public void TheScanFindsEachWayABuildFileSwitchesTheBanOff(string line) =>
        Assert.Matches(BuildSuppression(), line);

    [Theory]
    [InlineData("#pragma warning disable", true)]
    [InlineData("    #pragma warning disable // anything", true)]
    [InlineData("#pragma warning disable CA1822", false)]
    [InlineData("#pragma warning disable RS0030 // V8's own", false)]
    public void TheScanFindsABareDisable(string line, bool bare) =>
        Assert.Equal(bare, BareDisable().IsMatch(line));

    [GeneratedRegex(@"(NoWarn|WarningsNotAsErrors)[^\n]*RS0030|RS0030[^\n]*severity|severity[^\n]*RS0030|category-ApiDesign[^\n]*severity|dotnet_analyzer_diagnostic\.severity|<RunAnalyzers(DuringBuild)?>\s*false", RegexOptions.IgnoreCase)]
    private static partial Regex BuildSuppression();

    [GeneratedRegex(@"^\s*#pragma\s+warning\s+disable\s*(//.*)?$", RegexOptions.Multiline)]
    private static partial Regex BareDisable();

    /// <summary>A file in a folder above Nature that MSBuild or the compiler reads for every project below it.</summary>
    private static bool IsBuildFile(string path)
    {
        string name = Path.GetFileName(path);
        return name.StartsWith("Directory.Build.", StringComparison.Ordinal)
            || name.StartsWith("Directory.Packages.", StringComparison.Ordinal)
            || name is ".editorconfig" or ".globalconfig"
            || name.EndsWith(".globalconfig", StringComparison.Ordinal);
    }

    /// <summary>Each suppression in Js/ is a pragma pair with a comment just above it saying why.</summary>
    private static void AssertJustified(string path, string[] lines)
    {
        int open = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (!line.Contains("RS0030", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("#pragma warning disable", StringComparison.Ordinal))
            {
                Assert.True(i > 0 && lines[i - 1].Trim().StartsWith("//", StringComparison.Ordinal),
                    $"{path}:{i + 1}: the RS0030 suppression needs a comment just above it saying why");
                open++;
            }
            else if (line.StartsWith("#pragma warning restore", StringComparison.Ordinal))
            {
                open--;
            }
            else
            {
                Assert.Fail($"{path}:{i + 1}: RS0030 may only be suppressed by a #pragma pair");
            }
        }

        Assert.True(open == 0, $"{path}: an RS0030 suppression is never restored");
    }

    private static bool IsBuildOutput(string path)
    {
        string relative = Path.GetRelativePath(Nature, path);
        return relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>Whether a documentation id (<c>T:</c> or <c>M:</c>, as BannedSymbols.txt uses them) names a public member.</summary>
    private static bool Resolves(string id)
    {
        if (id.StartsWith("T:", StringComparison.Ordinal))
        {
            return typeof(object).Assembly.GetType(id[2..]) is not null;
        }

        if (!id.StartsWith("M:", StringComparison.Ordinal))
        {
            return false;
        }

        string signature = id[2..];
        int paren = signature.IndexOf('(', StringComparison.Ordinal);
        string qualified = paren < 0 ? signature : signature[..paren];
        int dot = qualified.LastIndexOf('.');
        Type? type = typeof(object).Assembly.GetType(qualified[..dot]);
        string name = qualified[(dot + 1)..];
        Type?[] parameters = paren < 0
            ? []
            : [.. signature[(paren + 1)..^1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => typeof(object).Assembly.GetType(p))];
        if (type is null || parameters.Any(p => p is null))
        {
            return false;
        }

        return type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, [.. parameters!]) is { } method
            && method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters!);
    }
}
