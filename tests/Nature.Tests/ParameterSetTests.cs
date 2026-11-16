using PropGenerator.Nature.Js;
using PropGenerator.Nature.Parameters;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class ParameterSetTests
{
    private static readonly ParameterSet Set = new(
        "demo",
        [
            new NumberSpec("w", "W", null, null, 1, 10, 1, 4),
            new ChoiceSpec("c", "C", null, null, [new ChoiceOption("a", "A"), new ChoiceOption("b", "B")], "a"),
        ],
        ["c", "w"]);

    [Fact]
    public void MissingKeysTakeTheDefaultsWithoutAnIssue()
    {
        var result = Set.Validate(new Dictionary<string, object?>());
        Assert.True(result.Valid);
        Assert.Empty(result.Issues);
        Assert.Equal(["w", "c"], result.Parameters.Keys);
        Assert.Equal(4.0, result.Parameters.Number("w"));
    }

    [Fact]
    public void ChangesAreReportedWithJavaScriptNumberText()
    {
        var result = Set.Validate(new Dictionary<string, object?> { ["w"] = 1e21, ["c"] = "zzz", ["extra"] = 1.0 });
        Assert.False(result.Valid);
        Assert.Equal(
            [new ValidationIssue("w", "w was adjusted to 10"), new ValidationIssue("c", "c was reset to a")],
            result.Issues);
        Assert.False(result.Parameters.ContainsKey("extra"));
    }

    [Fact]
    public void NullAndWrongTypesAreReported()
    {
        var result = Set.Validate(new Dictionary<string, object?> { ["w"] = null, ["c"] = 3.0 });
        Assert.Equal(["w", "c"], result.Issues.Select(issue => issue.Key));
    }

    [Fact]
    public void UndefinedCountsAsMissing()
    {
        var result = Set.Validate(new Dictionary<string, object?> { ["w"] = JsUndefined.Value });
        Assert.True(result.Valid);
    }

    [Fact]
    public void NegativeZeroInRangeIsNotAChange()
    {
        var set = new ParameterSet("demo", [new NumberSpec("w", "W", null, null, 0, 1, 0.1, 0.5)], ["w"]);
        Assert.True(set.Validate(new Dictionary<string, object?> { ["w"] = -0.0 }).Valid);
    }

    [Theory]
    [InlineData(null, "null")]
    [InlineData("text", "string")]
    [InlineData(5.0, "number")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(true, "boolean")]
    public void NonObjectsGiveTheDefaultsAndZodsMessage(object? raw, string received)
    {
        var result = Set.Validate(raw);
        Assert.False(result.Valid);
        Assert.Equal([new ValidationIssue("parameters", $"Invalid input: expected object, received {received}")], result.Issues);
        Assert.Equal(4.0, result.Parameters.Number("w"));
    }

    [Fact]
    public void ArraysAndUndefinedAreNamedLikeZod()
    {
        Assert.EndsWith("received array", Set.Validate(new List<object?>()).Issues[0].Message);
        Assert.EndsWith("received undefined", Set.Validate(JsUndefined.Value).Issues[0].Message);
    }

    [Fact]
    public void ControlsFollowTheInspectorOrderAndValuesTheSchemaOrder()
    {
        Assert.Equal(["c", "w"], Set.Controls.Select(spec => spec.Key));
        Assert.Equal(["w", "c"], Set.Schema.Select(spec => spec.Key));
    }
}
