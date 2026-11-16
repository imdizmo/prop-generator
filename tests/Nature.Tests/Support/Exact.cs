using PropGenerator.Nature.Kernel;
using Xunit;

namespace PropGenerator.Nature.Tests.Support;

/// <summary>Bit-exact comparisons: doubles and floats by their bits (NaN matches any NaN), meshes array by array.</summary>
internal static class Exact
{
    public static bool Same(double expected, double actual) =>
        double.IsNaN(expected)
            ? double.IsNaN(actual)
            : BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual);

    public static bool Same(float expected, float actual) =>
        float.IsNaN(expected)
            ? float.IsNaN(actual)
            : BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual);

    /// <summary>Fails unless both meshes have the same attributes, the same bits in each, the same index and groups.</summary>
    public static void Mesh(PropMesh expected, PropMesh actual, string name)
    {
        Floats(expected.Position, actual.Position, $"{name} position");
        Floats(expected.Normal, actual.Normal, $"{name} normal");
        Floats(expected.Color, actual.Color, $"{name} color");
        Floats(expected.Uv, actual.Uv, $"{name} uv");
        Floats(expected.Uv1, actual.Uv1, $"{name} uv1");
        Assert.True((expected.Index is null) == (actual.Index is null), $"{name}: index present in one only");
        if (expected.Index is not null)
        {
            Assert.Equal(expected.Index, actual.Index!);
        }

        Assert.Equal(expected.Groups, actual.Groups);
    }

    private static void Floats(float[]? expected, float[]? actual, string what)
    {
        Assert.True((expected is null) == (actual is null), $"{what}: present in {(expected is null ? "the port" : "three")} only");
        if (expected is null || actual is null)
        {
            return;
        }

        Assert.True(expected.Length == actual.Length, $"{what}: three has {expected.Length} values, the port {actual.Length}");
        int differing = 0;
        string first = "";
        for (int i = 0; i < expected.Length; i++)
        {
            if (!Same(expected[i], actual[i]))
            {
                if (differing == 0)
                {
                    first = $"value {i}: three {expected[i]:R}, port {actual[i]:R}";
                }

                differing++;
            }
        }

        Assert.True(differing == 0, $"{what}: {differing} of {expected.Length} values differ; first {first}");
    }
}
