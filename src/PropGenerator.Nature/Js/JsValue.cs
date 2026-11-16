using System.Collections;

namespace PropGenerator.Nature.Js;

/// <summary>What JavaScript's <c>typeof</c>, <c>===</c> and zod's type names say about a loosely typed value.</summary>
internal static class JsValue
{
    /// <summary>
    /// True for a JavaScript number: any C# numeric type, but not a string, a bool or a char. The result is the
    /// value as a double (NaN and the infinities included).
    /// </summary>
    public static bool TryNumber(object? value, out double number)
    {
        switch (value)
        {
            case double d:
                number = d;
                return true;
            case float f:
                number = f;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            case uint ui:
                number = ui;
                return true;
            case ulong ul:
                number = ul;
                return true;
            case short s:
                number = s;
                return true;
            case ushort us:
                number = us;
                return true;
            case byte b:
                number = b;
                return true;
            case sbyte sb:
                number = sb;
                return true;
            case decimal m:
                number = (double)m;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    /// <summary>zod 4's <c>z.number()</c>: a number that is neither NaN nor infinite.</summary>
    public static bool TryFiniteNumber(object? value, out double number) =>
        TryNumber(value, out number) && double.IsFinite(number);

    /// <summary>JavaScript's <c>raw === value</c> where <c>value</c> is a number or a string.</summary>
    public static bool StrictEquals(object? raw, object value) => value switch
    {
        double expected => TryNumber(raw, out double actual) && actual == expected,
        string expected => raw is string actual && string.Equals(actual, expected, StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>The name zod 4 gives a value in "expected object, received X".</summary>
    public static string ZodTypeName(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is JsUndefined)
        {
            return "undefined";
        }

        if (TryNumber(value, out double number))
        {
            return double.IsNaN(number) ? "NaN" : "number";
        }

        return value switch
        {
            string => "string",
            bool => "boolean",
            IDictionary or IReadOnlyDictionary<string, object?> => "object",
            IEnumerable => "array",
            _ => "unknown",
        };
    }
}
