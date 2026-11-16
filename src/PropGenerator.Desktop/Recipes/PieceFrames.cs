using System.Text.Json;

namespace PropGenerator.Desktop.Recipes;

/// <summary>How a new piece of an easy-props prop is framed, for the engine's builds.</summary>
internal static class PieceFrames
{
    /// <summary>easy-props frames a new piece by its Height (4.2 m without one) and its Width, when it has them.</summary>
    /// <param name="s">The settings.</param>
    /// <returns>The frame.</returns>
    public static PieceFrame Of(Settings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new PieceFrame(Number(s, "height") ?? 4.2f, Number(s, "width"));
    }

    private static float? Number(Settings s, string key) =>
        s.Values.TryGetValue(key, out JsonElement e) && e.ValueKind == JsonValueKind.Number ? (float)e.GetDouble() : null;
}
