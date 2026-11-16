using System.Text.Json;

namespace PropGenerator;

/// <summary>One setting of the painter, as floor_params.json lists it.</summary>
/// <param name="Key">The name the painter knows the setting by.</param>
/// <param name="Label">The short name shown beside the control.</param>
/// <param name="Tooltip">What the setting does to the wood, in plain words.</param>
/// <param name="Kind">"tone" (recolours instantly) or "paint" (repaints the tile).</param>
/// <param name="Type">"colour", "float", "int", "bool", "choice" (one of the options) or "seed" (a whole number from 0 to 4294967295).</param>
/// <param name="Min">The smallest value; null for colour and bool.</param>
/// <param name="Max">The largest value; null for bool.</param>
/// <param name="Step">The size of one slider step; null for bool.</param>
/// <param name="Default">The starting value: a number, a bool, a list of three numbers or, for a choice, an option's value.</param>
/// <param name="Group">The heading the setting sits under in the panel; null for none.</param>
/// <param name="Options">The options of a "choice" setting; null for every other type.</param>
/// <param name="Unit">The unit shown after a number, such as "m"; null for none (a count, or a plain amount).</param>
/// <param name="Swatches">For a colour: the named colours offered as swatches (value "#rrggbb", label its name); null for the wood colours.</param>
public sealed record ParamSpec(
    string Key,
    string Label,
    string Tooltip,
    string Kind,
    string Type,
    double? Min,
    double? Max,
    double? Step,
    JsonElement Default,
    string? Group = null,
    IReadOnlyList<ChoiceItem>? Options = null,
    string? Unit = null,
    IReadOnlyList<ChoiceItem>? Swatches = null);
