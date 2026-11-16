using System.Globalization;

namespace PropGenerator.Desktop.Props;

/// <summary>Names in the order people expect: case not minded, and "Rock 2" before "Rock 10".</summary>
internal sealed class NaturalOrder : IComparer<string>
{
    /// <summary>Gets the one comparer.</summary>
    internal static NaturalOrder Instance { get; } = new();

    /// <inheritdoc/>
    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return x is null ? (y is null ? 0 : -1) : 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsAsciiDigit(y[j]))
                {
                    j++;
                }

                string a = x[si..i].TrimStart('0'), b = y[sj..j].TrimStart('0');
                int byLength = a.Length.CompareTo(b.Length);
                int c = byLength != 0 ? byLength : string.CompareOrdinal(a, b);
                if (c != 0)
                {
                    return c;
                }

                continue;
            }

            int ch = string.Compare(x[i].ToString(), y[j].ToString(), CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);
            if (ch != 0)
            {
                return ch;
            }

            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}
