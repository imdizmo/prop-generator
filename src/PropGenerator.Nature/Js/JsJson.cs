using System.Collections;
using System.Globalization;
using System.Text;

namespace PropGenerator.Nature.Js;

/// <summary>
/// JavaScript's <c>JSON.stringify(value, null, indent)</c> for the values a port holds: null, <see cref="JsUndefined"/>,
/// booleans, numbers (double, float, int, long), strings, objects (any sequence of string-keyed pairs, such as a
/// <see cref="Parameters.ParameterDictionary"/>) and arrays (any other sequence).
/// </summary>
public static class JsJson
{
    /// <summary>
    /// The JSON text, laid out as JavaScript lays it out: numbers in their shortest form (<c>1e-7</c>, <c>1e+21</c>; -0 as
    /// <c>0</c>, NaN and the infinities as <c>null</c>), object keys that are array indices first and ascending, the rest
    /// in their order, undefined members left out (and <c>null</c> in arrays), strings escaped as JSON.stringify does.
    /// Null for <see cref="JsUndefined"/> itself, as JavaScript gives undefined.
    /// </summary>
    public static string? Stringify(object? value, int indent = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(indent);
        if (value is JsUndefined)
        {
            return null;
        }

        var text = new StringBuilder();
        Write(text, value, new string(' ', Math.Min(indent, 10)), "");
        return text.ToString();
    }

    private static void Write(StringBuilder text, object? value, string gap, string indent)
    {
        switch (value)
        {
            case null:
                text.Append("null");
                break;
            case bool flag:
                text.Append(flag ? "true" : "false");
                break;
            case double number:
                text.Append(double.IsFinite(number) ? JsNumber.ToJsString(number) : "null");
                break;
            case float number:
                Write(text, (double)number, gap, indent);
                break;
            case int number:
                Write(text, (double)number, gap, indent);
                break;
            case long number:
                Write(text, (double)number, gap, indent);
                break;
            case string s:
                Quote(text, s);
                break;
            case IEnumerable<KeyValuePair<string, object?>> members:
                WriteObject(text, members, gap, indent);
                break;
            case IEnumerable items:
                WriteArray(text, items, gap, indent);
                break;
            default:
                throw new ArgumentException($"JSON has no form for a {value.GetType().Name}", nameof(value));
        }
    }

    private static void WriteObject(StringBuilder text, IEnumerable<KeyValuePair<string, object?>> members, string gap, string indent)
    {
        // JavaScript's own key order: array indices ascending, then the other keys as they were added.
        var ordered = members.Where(member => member.Value is not JsUndefined).ToList();
        var indices = ordered.Where(member => IsArrayIndex(member.Key)).OrderBy(member => uint.Parse(member.Key, CultureInfo.InvariantCulture));
        var rest = ordered.Where(member => !IsArrayIndex(member.Key));
        var all = indices.Concat(rest).ToList();
        if (all.Count == 0)
        {
            text.Append("{}");
            return;
        }

        string inner = indent + gap;
        text.Append('{');
        for (int i = 0; i < all.Count; i++)
        {
            text.Append(i == 0 ? "" : ",");
            if (gap.Length > 0)
            {
                text.Append('\n').Append(inner);
            }

            Quote(text, all[i].Key);
            text.Append(gap.Length > 0 ? ": " : ":");
            Write(text, all[i].Value, gap, inner);
        }

        if (gap.Length > 0)
        {
            text.Append('\n').Append(indent);
        }

        text.Append('}');
    }

    private static void WriteArray(StringBuilder text, IEnumerable items, string gap, string indent)
    {
        var all = items.Cast<object?>().ToList();
        if (all.Count == 0)
        {
            text.Append("[]");
            return;
        }

        string inner = indent + gap;
        text.Append('[');
        for (int i = 0; i < all.Count; i++)
        {
            text.Append(i == 0 ? "" : ",");
            if (gap.Length > 0)
            {
                text.Append('\n').Append(inner);
            }

            Write(text, all[i] is JsUndefined ? null : all[i], gap, inner);
        }

        if (gap.Length > 0)
        {
            text.Append('\n').Append(indent);
        }

        text.Append(']');
    }

    /// <summary>
    /// ECMAScript's QuoteJSONString: <c>"</c> and <c>\</c> escaped, the five short escapes, other controls as
    /// <c>\u00xx</c>, a lone surrogate as <c>\udxxx</c>; everything else, U+2028 and U+2029 included, as it is.
    /// </summary>
    private static void Quote(StringBuilder text, string s)
    {
        text.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\b':
                    text.Append("\\b");
                    break;
                case '\f':
                    text.Append("\\f");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        text.Append(c).Append(s[++i]);
                    }
                    else if (char.IsSurrogate(c))
                    {
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        text.Append('"');
    }

    /// <summary>An array index: the canonical text of a whole number from 0 to 2^32 - 2.</summary>
    private static bool IsArrayIndex(string key) =>
        uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out uint index)
        && index != uint.MaxValue
        && index.ToString(CultureInfo.InvariantCulture) == key;
}
