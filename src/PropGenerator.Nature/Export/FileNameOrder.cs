// Ported from easy-props frontend/src/core/export/pipeline/package.ts (the files' sort) (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Export;

/// <summary>
/// The order easy-props lists an export's files in: <c>a.name.localeCompare(b.name)</c>, which V8 answers with ICU's
/// root collation. Nature runs with invariant globalization, where a culture compare is ordinal, so this reproduces ICU
/// for the characters an export's file names have: letters, digits, space, <c>_</c>, <c>-</c> and <c>.</c>. ICU compares
/// the whole names letter by letter ignoring case first (space, then <c>_ - .</c>, then digits, then letters; a name
/// that ends first comes first), and only when that ties, by case, lower case first.
/// </summary>
public sealed class FileNameOrder : IComparer<string>
{
    /// <summary>Every character this order knows, in ICU's root order ignoring case.</summary>
    private const string Primary = " _-.0123456789abcdefghijklmnopqrstuvwxyz";

    private FileNameOrder()
    {
    }

    /// <summary>The one instance.</summary>
    public static FileNameOrder Instance { get; } = new();

    /// <summary>
    /// Less than zero when <paramref name="x"/> comes first, zero for the same name, more than zero otherwise.
    /// </summary>
    /// <exception cref="ArgumentException">A name has a character no export's file name has.</exception>
    public int Compare(string? x, string? y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        foreach (char c in x.Concat(y))
        {
            _ = Weight(c);
        }

        int shorter = Math.Min(x.Length, y.Length);
        for (int i = 0; i < shorter; i++)
        {
            int difference = Weight(x[i]) - Weight(y[i]);
            if (difference != 0)
            {
                return difference;
            }
        }

        if (x.Length != y.Length)
        {
            return x.Length - y.Length;
        }

        // The same letters: the first difference of case decides, lower case first.
        for (int i = 0; i < x.Length; i++)
        {
            if (x[i] != y[i])
            {
                return char.IsLower(x[i]) ? -1 : 1;
            }
        }

        return 0;
    }

    private static int Weight(char c)
    {
        int at = Primary.IndexOf(char.ToLowerInvariant(c), StringComparison.Ordinal);
        return at >= 0 && c < 128
            ? at
            : throw new ArgumentException($"'{c}' is not a character of an export's file name, whose order is known here");
    }
}
