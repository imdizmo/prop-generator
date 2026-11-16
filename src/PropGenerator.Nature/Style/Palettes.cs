// Ported from easy-props frontend/src/assets/style/palettes.ts (ROCK_RAMPS) (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Style;

/// <summary>
/// The rock colour ramps cliffs and stylized rocks are painted in, as <c>#rrggbb</c> text: easy-props' own, so a cliff
/// and an easy-props rock on the same palette belong to the same game. The order is easy-props' (the order of the
/// inspector's choices). easy-props' other palettes live in its engine.
/// </summary>
public static class Palettes
{
    /// <summary>Rock colour ramps by rock palette id, shadow to highlight.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> RockRamps { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        ["lavender"] = ["#3f4180", "#6467a8", "#9598d4", "#d9daf6"],
        ["slate"] = ["#27354f", "#4f6585", "#83a0bd", "#cfe0ee"],
        ["granite"] = ["#35364a", "#676a7c", "#9fa2ad", "#dfdfe2"],
        ["sandstone"] = ["#5a3f4a", "#a0785f", "#d2ad80", "#f6e3bd"],
        ["redrock"] = ["#4a2438", "#94483f", "#cb7c58", "#f2c49a"],
        ["basalt"] = ["#14162a", "#30334e", "#565a78", "#9599b4"],
        ["frost"] = ["#3c5583", "#7c9fc9", "#bcd6ec", "#f4fbff"],
    };
}
