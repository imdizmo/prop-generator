using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

/// <summary>The kernel sample holds all eight families, each with its sampled cases.</summary>
public class KernelGoldensTests
{
    [Fact]
    public void TheIndexListsExactlyTheEightFamilies() =>
        Assert.Equal(
            KernelGoldens.FamilyNames.Order(StringComparer.Ordinal),
            KernelGoldens.IndexedFamilyNames().Order(StringComparer.Ordinal));

    [Theory]
    [InlineData("compose")]
    [InlineData("applyMatrix4")]
    [InlineData("setFromEuler")]
    [InlineData("setFromUnitVectors")]
    [InlineData("setFromQuaternion")]
    [InlineData("color")]
    [InlineData("vector")]
    [InlineData("quaternion")]
    public void EveryFamilyHasItsSampledCases(string name) =>
        Assert.Equal(KernelGoldens.CasesPerFamily, KernelGoldens.Family(name).Records);
}
