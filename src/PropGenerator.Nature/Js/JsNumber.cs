using System.Globalization;
using System.Numerics;

namespace PropGenerator.Nature.Js;

/// <summary>JavaScript's number conversions (ECMAScript), where .NET's own differ.</summary>
public static class JsNumber
{
    private const double Two32 = 4294967296.0;

    /// <summary>
    /// ECMAScript ToInt32, as <c>x | 0</c> and the other bitwise operators apply it: drop the fraction, wrap modulo
    /// 2^32 into the signed range. NaN and the infinities give 0. A C# cast saturates or is undefined instead.
    /// </summary>
    public static int ToInt32(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        // Exact: the remainder of two doubles is always representable, and |m| < 2^32 fits a long.
        double m = Math.Truncate(value) % Two32;
        return unchecked((int)(long)m);
    }

    /// <summary>ECMAScript ToUint32, as <c>x &gt;&gt;&gt; 0</c> applies it: ToInt32's bits read unsigned.</summary>
    public static uint ToUint32(double value) => unchecked((uint)ToInt32(value));

    /// <summary>
    /// ECMAScript ToUint8, as a store into a <c>Uint8Array</c> applies it: drop the fraction and wrap modulo 256, so 256
    /// gives 0 and -1 gives 255. NaN and the infinities give 0. (A <c>Uint8ClampedArray</c> would round and clamp.)
    /// </summary>
    public static byte ToUint8(double value) => unchecked((byte)ToInt32(value));

    /// <summary>
    /// Number.prototype.toFixed: <paramref name="digits"/> decimals from the exact binary value, halves rounded up in
    /// magnitude. -0 gives "0.00…" (.NET's "F" format gives "-0.00…"); 1e21 and above use <see cref="ToJsString"/>.
    /// </summary>
    public static string ToFixed(double value, int digits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(digits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, 100);
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (Math.Abs(value) >= 1e21)
        {
            return ToJsString(value);
        }

        // value = mantissa * 2^exponent exactly; n = round(|value| * 10^digits), ties up.
        long bits = BitConverter.DoubleToInt64Bits(Math.Abs(value));
        int exponent = (int)(bits >> 52);
        long mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0)
        {
            exponent = 1;
        }
        else
        {
            mantissa |= 1L << 52;
        }

        exponent -= 1075;
        BigInteger n = mantissa * BigInteger.Pow(10, digits);
        n = exponent >= 0 ? n << exponent : (n + (BigInteger.One << (-exponent - 1))) >> -exponent;

        string s = n.ToString(CultureInfo.InvariantCulture);
        if (digits > 0)
        {
            if (s.Length <= digits)
            {
                s = new string('0', digits + 1 - s.Length) + s;
            }

            s = string.Concat(s.AsSpan(0, s.Length - digits), ".", s.AsSpan(s.Length - digits));
        }

        return value < 0 ? "-" + s : s;
    }

    /// <summary>
    /// Number.prototype.toString(): the shortest digits that read back as the same double, laid out as JavaScript
    /// does (plain from 1e-6 up to below 1e21, otherwise "1.5e-7" or "1e+21"). -0 gives "0".
    /// </summary>
    public static string ToJsString(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (value == 0)
        {
            return "0";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Infinity" : "-Infinity";
        }

        if (value < 0)
        {
            return "-" + ToJsString(-value);
        }

        // .NET's "R" gives the shortest round-trip digits; take its digits and decimal exponent, whatever its layout.
        string r = value.ToString("R", CultureInfo.InvariantCulture);
        int e = r.IndexOf('E', StringComparison.Ordinal);
        int exp10 = e < 0 ? 0 : int.Parse(r.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        string mant = e < 0 ? r : r[..e];
        int point = mant.IndexOf('.', StringComparison.Ordinal);
        int intDigits = point < 0 ? mant.Length : point;
        string digits = point < 0 ? mant : mant.Remove(point, 1);
        int lead = 0;
        while (digits[lead] == '0')
        {
            lead++;
        }

        digits = digits[lead..].TrimEnd('0');

        // value = 0.digits * 10^n, k digits.
        int n = intDigits - lead + exp10;
        int k = digits.Length;
        if (k <= n && n <= 21)
        {
            return digits + new string('0', n - k);
        }

        if (n is > 0 and <= 21)
        {
            return string.Concat(digits.AsSpan(0, n), ".", digits.AsSpan(n));
        }

        if (n is > -6 and <= 0)
        {
            return "0." + new string('0', -n) + digits;
        }

        int shown = n - 1;
        string sign = shown < 0 ? "-" : "+";
        string abs = Math.Abs(shown).ToString(CultureInfo.InvariantCulture);
        return k == 1
            ? $"{digits}e{sign}{abs}"
            : $"{digits[0]}.{digits.AsSpan(1)}e{sign}{abs}";
    }
}
