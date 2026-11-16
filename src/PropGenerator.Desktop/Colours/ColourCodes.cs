using System.Globalization;
using System.Text.RegularExpressions;

namespace PropGenerator.Desktop.Colours;

/// <summary>
/// Colour codes as people paste them (easy-props' colour-code): #rgb, #rrggbb, either without the #, and rgb() or rgba()
/// with commas or spaces and an alpha that is let go. Every colour comes out as lower-case #rrggbb.
/// </summary>
internal static partial class ColourCodes
{
    /// <summary>
    /// The most colours a pasted list gives: My colours keeps 200 and a palette a handful, so a list longer than this is
    /// not one anybody means; the rest of it is let go.
    /// </summary>
    internal const int MaxListed = 1000;

    /// <summary>
    /// How much of a pasted text is read, in characters: 1000 codes with their separators take about 10 000; the rest
    /// of a longer paste (a whole file by mistake) is not looked at.
    /// </summary>
    internal const int MaxPasteChars = 64 * 1024;

    /// <summary>Reads one colour code.</summary>
    /// <param name="text">The code.</param>
    /// <returns>The colour as #rrggbb, or null when it is not a colour code.</returns>
    internal static string? ParseOne(string? text)
    {
        string t = (text ?? string.Empty).Trim();
        if (Rgb().Match(t) is { Success: true } m)
        {
            int[] c = [.. Enumerable.Range(1, 3).Select(i => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture))];
            return c.All(x => x <= 255) ? $"#{c[0]:x2}{c[1]:x2}{c[2]:x2}" : null;
        }

        string h = t.StartsWith('#') ? t[1..] : t;
        if (!h.All(Uri.IsHexDigit))
        {
            return null;
        }

        return h.Length switch
        {
            6 => "#" + h.ToLowerInvariant(),
            3 => string.Concat("#", h[0], h[0], h[1], h[1], h[2], h[2]).ToLowerInvariant(),
            _ => null,
        };
    }

    /// <summary>
    /// Reads a pasted list: codes split by spaces, new lines, commas or semicolons, in order, repeats dropped; only the
    /// first <see cref="MaxPasteChars"/> characters are read and at most <see cref="MaxListed"/> colours given.
    /// </summary>
    /// <param name="text">The text pasted.</param>
    /// <returns>The colours, and how many pieces were not colours.</returns>
    internal static (IReadOnlyList<string> Codes, int NotColours) ParseList(string? text)
    {
        string t = text ?? string.Empty;
        var codes = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int bad = 0;
        foreach (Match m in Token().Matches(t.Length > MaxPasteChars ? t[..MaxPasteChars] : t))
        {
            if (ParseOne(m.Value) is { } hex)
            {
                if (codes.Count < MaxListed && seen.Add(hex))
                {
                    codes.Add(hex);
                }
            }
            else
            {
                bad++;
            }
        }

        return (codes, bad);
    }

    [GeneratedRegex(@"^rgba?\(\s*(\d{1,3})\s*[,\s]\s*(\d{1,3})\s*[,\s]\s*(\d{1,3})\s*(?:[,/\s]\s*[\d.]+%?\s*)?\)$", RegexOptions.IgnoreCase)]
    private static partial Regex Rgb();

    [GeneratedRegex(@"rgba?\([^)]*\)|[^\s,;]+", RegexOptions.IgnoreCase)]
    private static partial Regex Token();
}
