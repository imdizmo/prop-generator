using PropGenerator.Nature.Js;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class JsJsonTests
{
    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(0.000001, "0.000001")]
    [InlineData(1e21, "1e+21")]
    [InlineData(123456789012345680000.0, "123456789012345680000")]
    [InlineData(0.30000000000000004, "0.30000000000000004")]
    [InlineData(double.NaN, "null")]
    [InlineData(double.NegativeInfinity, "null")]
    [InlineData(-2.5, "-2.5")]
    public void NumbersAreWrittenAsJavaScriptWritesThem(double value, string json) => Assert.Equal(json, JsJson.Stringify(value));

    [Fact]
    public void MinusZeroIsWrittenAsZero() => Assert.Equal("0", JsJson.Stringify(-0.0));

    [Fact]
    public void OtherNumberTypesAndLiteralsAreWritten()
    {
        Assert.Equal("[1,2.5,3,true,false,null]", JsJson.Stringify(new object?[] { 1, 2.5f, 3L, true, false, null }));
    }

    [Fact]
    public void StringsAreEscapedAsJsonStringifyEscapesThem()
    {
        Assert.Equal("\"a\\\"b\\\\c/\\n\\t\\r\\f\\b\\u0000\\u001f\u007f é 🌲 \u2028\"", JsJson.Stringify("a\"b\\c/\n\t\r\f\b\u0000\u001f\u007f é 🌲 \u2028"));
        // A lone surrogate is escaped (well-formed JSON.stringify); a pair is kept.
        Assert.Equal("\"\\ud800x\\udc00\"", JsJson.Stringify("\ud800x\udc00"));
    }

    [Fact]
    public void ArrayIndexKeysComeFirstAscendingThenTheRestInOrder()
    {
        var value = new List<KeyValuePair<string, object?>>
        {
            new("zebra", 1.0),
            new("10", "ten"),
            new("2", "two"),
            new("4294967295", "not an index"),
            new("4294967294", "the last index"),
            new("01", "not canonical"),
            new("-1", "negative"),
        };
        Assert.Equal(
            "{\"2\":\"two\",\"10\":\"ten\",\"4294967294\":\"the last index\",\"zebra\":1,\"4294967295\":\"not an index\",\"01\":\"not canonical\",\"-1\":\"negative\"}",
            JsJson.Stringify(value));
    }

    [Fact]
    public void UndefinedMembersAreLeftOutAndUndefinedItemsAreNull()
    {
        var value = new List<KeyValuePair<string, object?>> { new("a", JsUndefined.Value), new("b", new object?[] { JsUndefined.Value }) };
        Assert.Equal("{\"b\":[null]}", JsJson.Stringify(value));
        Assert.Null(JsJson.Stringify(JsUndefined.Value));
    }

    [Fact]
    public void IndentLaysOutAsJavaScriptDoes()
    {
        var value = new List<KeyValuePair<string, object?>>
        {
            new("a", new object?[] { 1.0, new List<KeyValuePair<string, object?>>(), Array.Empty<object>() }),
            new("b", new List<KeyValuePair<string, object?>> { new("c", null) }),
        };
        Assert.Equal("{\n  \"a\": [\n    1,\n    {},\n    []\n  ],\n  \"b\": {\n    \"c\": null\n  }\n}", JsJson.Stringify(value, 2));
    }
}
