using PropGenerator.Nature.Parameters;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class ChoiceSpecTests
{
    private static readonly ChoiceSpec Spec = new(
        "k", "K", null, null, [new ChoiceOption("a", "A"), new ChoiceOption("b", "B")], "b");

    [Fact]
    public void OfferedValueIsKept() => Assert.Equal("a", Spec.Clamp("a"));

    [Theory]
    [InlineData("A")]
    [InlineData("a ")]
    [InlineData("")]
    [InlineData("c")]
    public void UnknownStringBecomesTheDefault(string raw) => Assert.Equal("b", Spec.Clamp(raw));

    [Fact]
    public void NonStringBecomesTheDefault()
    {
        Assert.Equal("b", Spec.Clamp(3.0));
        Assert.Equal("b", Spec.Clamp(null));
        Assert.Equal("b", Spec.Clamp(true));
        Assert.Equal("b", Spec.Clamp(new List<object?> { "a" }));
    }
}
