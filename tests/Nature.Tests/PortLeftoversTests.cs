using System.Reflection;
using PropGenerator.Nature.Parameters;
using PropGenerator.Nature.Style;
using Xunit;

namespace PropGenerator.Nature.Tests;

/// <summary>
/// Phase 4 deleted the C# port of easy-props' kinds and kept only what cliffs and stylized rocks share: what only the port
/// used (the painted and branching trees' parameter kinds, the style profiles, the painted tree's swatches) is gone too.
/// </summary>
public class PortLeftoversTests
{
    [Theory]
    [InlineData("PropGenerator.Nature.Parameters.ColourSpec")]
    [InlineData("PropGenerator.Nature.Parameters.TextSpec")]
    [InlineData("PropGenerator.Nature.Parameters.RangeSpec")]
    [InlineData("PropGenerator.Nature.Parameters.OwnColourSpec")]
    [InlineData("PropGenerator.Nature.Parameters.ColourSwatch")]
    [InlineData("PropGenerator.Nature.Style.StyleProfile")]
    public void APortOnlyTypeIsGone(string name) => Assert.Null(typeof(ParameterSet).Assembly.GetType(name));

    [Theory]
    [InlineData(typeof(Materials), "BarkColours")]
    [InlineData(typeof(Materials), "LeafColours")]
    [InlineData(typeof(Materials), "BarkLightColour")]
    [InlineData(typeof(Materials), "LeavesDarkColour")]
    [InlineData(typeof(ParameterSet), "PanelColours")]
    public void APortOnlyMemberIsGone(Type type, string member) =>
        Assert.Empty(type.GetMember(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance));
}
