using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using PropGenerator.Engine;

namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// easy-props' parameter definitions as the panel's settings, and the panel's settings as the values easy-props reads:
/// the same rules the port's <c>MakeSpecs</c> follows, so saved work keeps its keys, order, types and defaults.
/// </summary>
internal static class EngineSpecs
{
    // Spec kinds already written to the trace as skipped, by kind and spec kind (a handful at most).
    private static readonly ConcurrentDictionary<string, byte> Skipped = new(StringComparer.Ordinal);

    /// <summary>
    /// The panel's settings from a generator's parameter definitions: its specs, then its colour rows, then the seed. A
    /// number is whole ("int") when its step is 1 and its bounds are whole; the unit "count" is dropped; a colour's
    /// default is three numbers and its swatches are value and name. A spec of a kind the panel cannot draw is left out
    /// and written once to the trace.
    /// </summary>
    /// <param name="p">The generator's parameter definitions.</param>
    /// <param name="k">The kind (its tooltips, seed and seed group).</param>
    /// <returns>The settings in panel order, the seed last.</returns>
    internal static IReadOnlyList<ParamSpec> From(EngineParameters p, EngineKind k)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(k);
        var list = new List<ParamSpec>();
        foreach (JsonElement spec in p.Specs.Concat(p.ColourRows))
        {
            string kind = Text(spec, "kind") ?? string.Empty;
            string key = Text(spec, "key") ?? string.Empty;
            string label = Text(spec, "label") ?? key;
            string tooltip = k.Tooltip(key) ?? Text(spec, "help") ?? label;
            string? group = Text(spec, "group");
            switch (kind)
            {
                case "number":
                    double min = spec.GetProperty("min").GetDouble();
                    double max = spec.GetProperty("max").GetDouble();
                    double step = spec.GetProperty("step").GetDouble();
                    string? unit = Text(spec, "unit");
                    list.Add(new ParamSpec(
                        key, label, tooltip, "paint",
                        step == 1 && min == Math.Floor(min) && max == Math.Floor(max) ? "int" : "float",
                        min, max, step, JsonSerializer.SerializeToElement(spec.GetProperty("default").GetDouble()), group, null,
                        unit is null or "count" ? null : unit));
                    break;
                case "choice":
                    list.Add(new ParamSpec(
                        key, label, tooltip, "paint", "choice", null, null, null,
                        JsonSerializer.SerializeToElement(spec.GetProperty("default").GetString()), group,
                        [.. spec.GetProperty("options").EnumerateArray().Select(o => new ChoiceItem(Text(o, "value") ?? string.Empty, Text(o, "label") ?? string.Empty))]));
                    break;
                case "colour":
                    var c = Rgb.FromHex(spec.GetProperty("default").GetString()!);
                    list.Add(new ParamSpec(
                        key, label, tooltip, "paint", "colour", null, null, null,
                        JsonSerializer.SerializeToElement(new[] { c.R, c.G, c.B }), group, null, null,
                        spec.TryGetProperty("swatches", out JsonElement swatches) && swatches.ValueKind == JsonValueKind.Array
                            ? [.. swatches.EnumerateArray().Select(w => new ChoiceItem(Text(w, "value") ?? string.Empty, Text(w, "name") ?? string.Empty))]
                            : []));
                    break;
                default:
                    if (Skipped.TryAdd(k.Id + ":" + kind, 0))
                    {
                        TraceLog.Write($"engine specs: {k.Id} setting '{key}' of kind '{kind}' is not drawn (the panel has no row for that kind)");
                    }

                    break;
            }
        }

        list.Add(new ParamSpec(
            "seed", "Seed", k.Tooltip("seed") ?? "A whole number that picks which random one you get from the other settings.", "paint", "seed", 0, uint.MaxValue, 1,
            JsonSerializer.SerializeToElement((double)k.DefaultSeed), k.SeedGroup));
        return list;
    }

    /// <summary>
    /// The settings as the values easy-props reads, as JSON: each setting's key (the seed and the part edits never),
    /// numbers as numbers, choices as strings, colours as <c>#rrggbb</c>; a painted tree's own edits too, and what the
    /// kind carries undrawn (<see cref="EngineKind.CarriedKeys"/>: a piece's tree). The same settings give the same text,
    /// so its hash keys the rule caches.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="k">The kind.</param>
    /// <param name="specs">The kind's settings (<see cref="From"/>).</param>
    /// <returns>A JSON object.</returns>
    internal static string ValuesJson(Settings s, EngineKind k, IReadOnlyList<ParamSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(k);
        ArgumentNullException.ThrowIfNull(specs);
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            foreach (ParamSpec spec in specs)
            {
                if (SettingsPanel.IsHidden(spec) || !s.Values.TryGetValue(spec.Key, out JsonElement e))
                {
                    continue;
                }

                switch (e.ValueKind)
                {
                    case JsonValueKind.Array when spec.Type == "colour" && e.GetArrayLength() == 3:
                        w.WriteString(spec.Key, HexOf(s.GetColour(spec.Key)));
                        break;
                    case JsonValueKind.Number:
                        w.WriteNumber(spec.Key, e.GetDouble());
                        break;
                    case JsonValueKind.String:
                        w.WriteString(spec.Key, e.GetString());
                        break;
                    default:
                        // Anything else is not a value easy-props has: its validation takes the default.
                        break;
                }
            }

            if (k.Id == EngineKinds.PaintedTree && s.Values.TryGetValue(SettingsPanel.TreeEditsKey, out JsonElement edits) && edits.ValueKind == JsonValueKind.String)
            {
                w.WriteString(SettingsPanel.TreeEditsKey, edits.GetString());
            }

            // What a piece carries and the panel does not draw (E12): its tree, as it is; a tree text past any tree's is
            // never sent, so the engine makes the piece from its default tree (opening such a file says so).
            foreach (string key in k.CarriedKeys)
            {
                if (!s.Values.TryGetValue(key, out JsonElement carried))
                {
                    continue;
                }

                switch (carried.ValueKind)
                {
                    case JsonValueKind.String when key != EngineKinds.TreeKey || carried.GetString()!.Length <= EngineKinds.MaxTreeChars:
                        w.WriteString(key, carried.GetString());
                        break;
                    case JsonValueKind.Number:
                        w.WriteNumber(key, carried.GetDouble());
                        break;
                    default:
                        break;
                }
            }

            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>The settings with easy-props' patch (its <c>adjust</c>) put in: a <c>#rrggbb</c> as a colour, anything else as it is.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="patch">Key to new value.</param>
    /// <returns>The new settings.</returns>
    internal static Settings ApplyPatch(Settings s, IReadOnlyDictionary<string, JsonElement> patch)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(patch);
        foreach ((string key, JsonElement value) in patch)
        {
            s = value.ValueKind == JsonValueKind.String && IsHex(value.GetString()!) ? s.With(key, Rgb.FromHex(value.GetString()!)) : s.With(key, value);
        }

        return s;
    }

    /// <summary>The settings with a preset's parameters put in (as upstream has them): colours as colours, the rest as they are.</summary>
    /// <param name="s">The settings to start from.</param>
    /// <param name="parameters">The preset's parameters, a JSON object.</param>
    /// <param name="specs">The kind's settings, which say which keys are colours.</param>
    /// <returns>The new settings.</returns>
    internal static Settings WithValues(Settings s, JsonElement parameters, IReadOnlyList<ParamSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(specs);
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return s;
        }

        foreach (JsonProperty p in parameters.EnumerateObject())
        {
            bool colour = p.Value.ValueKind == JsonValueKind.String && specs.Any(spec => spec.Key == p.Name && spec.Type == "colour");
            s = colour ? s.With(p.Name, Rgb.FromHex(p.Value.GetString()!)) : s.With(p.Name, p.Value);
        }

        return s;
    }

    // An app colour as easy-props reads one: #rrggbb, each channel rounded to a byte.
    private static string HexOf(Rgb c)
    {
        static int Byte(float v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return string.Create(CultureInfo.InvariantCulture, $"#{Byte(c.R):x2}{Byte(c.G):x2}{Byte(c.B):x2}");
    }

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    private static bool IsHex(string text) =>
        text.Length == 7 && text[0] == '#' && !text.AsSpan(1).ContainsAnyExcept(HexDigits);

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
