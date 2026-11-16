using System.Text.Json;

namespace PropGenerator;

/// <summary>The value of every setting for one piece, as the JSON the painter is sent.</summary>
/// <param name="Values">Setting key to JSON value.</param>
public sealed record Settings(IReadOnlyDictionary<string, JsonElement> Values)
{
    /// <summary>The starting settings: every entry's default.</summary>
    /// <param name="specs">The settings list.</param>
    /// <returns>Settings holding each default.</returns>
    public static Settings Defaults(IReadOnlyList<ParamSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        var d = new Dictionary<string, JsonElement>();
        foreach (ParamSpec s in specs)
        {
            d[s.Key] = s.Default.Clone();
        }

        return new Settings(d);
    }

    /// <summary>Reads a decimal setting.</summary>
    /// <param name="key">The setting name.</param>
    /// <returns>The value.</returns>
    public float GetFloat(string key) => Values[key].GetSingle();

    /// <summary>Reads a whole-number setting.</summary>
    /// <param name="key">The setting name.</param>
    /// <returns>The value.</returns>
    public int GetInt(string key) => (int)Math.Round(Values[key].GetDouble());

    /// <summary>Reads an on/off setting.</summary>
    /// <param name="key">The setting name.</param>
    /// <returns>The value.</returns>
    public bool GetBool(string key) => Values[key].GetBoolean();

    /// <summary>Reads a choice setting.</summary>
    /// <param name="key">The setting name.</param>
    /// <returns>The chosen option's value.</returns>
    public string GetString(string key) => Values[key].GetString() ?? string.Empty;

    /// <summary>Reads a seed setting.</summary>
    /// <param name="key">The setting name.</param>
    /// <returns>The seed.</returns>
    public uint GetSeed(string key) => (uint)Values[key].GetDouble();

    /// <summary>Reads a colour setting (a list of three numbers).</summary>
    /// <param name="key">The setting name.</param>
    /// <returns>The colour.</returns>
    public Rgb GetColour(string key)
    {
        JsonElement e = Values[key];
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3)
        {
            throw new FormatException($"Setting '{key}' is not a colour of three numbers.");
        }

        return new Rgb(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
    }

    /// <summary>Gives a copy with one setting changed. This object stays as it was.</summary>
    /// <param name="key">The setting name.</param>
    /// <param name="value">The new value: a number, a bool, a list of numbers, an <see cref="Rgb"/> or a JsonElement.</param>
    /// <returns>The new settings.</returns>
    public Settings With(string key, object value)
    {
        var d = new Dictionary<string, JsonElement>(Values.Count + 1);
        foreach (KeyValuePair<string, JsonElement> kv in Values)
        {
            d[kv.Key] = kv.Value;
        }

        d[key] = value switch
        {
            JsonElement e => e.Clone(),
            Rgb c => JsonSerializer.SerializeToElement(new[] { c.R, c.G, c.B }),
            _ => JsonSerializer.SerializeToElement(value),
        };
        return new Settings(d);
    }

    /// <summary>Gives a copy without one setting. This object stays as it was.</summary>
    /// <param name="key">The setting name.</param>
    /// <returns>The new settings; this object when it has no such setting.</returns>
    public Settings Without(string key)
    {
        if (!Values.ContainsKey(key))
        {
            return this;
        }

        return new Settings(Values.Where(kv => kv.Key != key).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    /// <summary>Two settings are equal when they hold the same keys with the same JSON values.</summary>
    /// <param name="other">The other settings.</param>
    /// <returns>True when equal.</returns>
    public bool Equals(Settings? other)
    {
        if (other is null || Values.Count != other.Values.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, JsonElement> kv in Values)
        {
            if (!other.Values.TryGetValue(kv.Key, out JsonElement o) || !JsonSame(kv.Value, o))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A hash that agrees with <see cref="Equals(Settings?)"/>.</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode()
    {
        int h = Values.Count;
        foreach (KeyValuePair<string, JsonElement> kv in Values)
        {
            h ^= HashCode.Combine(kv.Key, kv.Value.ValueKind);
        }

        return h;
    }

    /// <summary>Compares two JSON values by content (1 and 1.0 are the same).</summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns>True when the same.</returns>
    internal static bool JsonSame(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Number:
                return a.GetDouble() == b.GetDouble();
            case JsonValueKind.String:
                return a.GetString() == b.GetString();
            case JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength())
                {
                    return false;
                }

                return a.EnumerateArray().Zip(b.EnumerateArray()).All(p => JsonSame(p.First, p.Second));
            case JsonValueKind.Object:
                var props = a.EnumerateObject().ToList();
                if (props.Count != b.EnumerateObject().Count())
                {
                    return false;
                }

                return props.All(p => b.TryGetProperty(p.Name, out JsonElement o) && JsonSame(p.Value, o));
            default:
                return true;
        }
    }
}
