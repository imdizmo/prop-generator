// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Text.RegularExpressions;

namespace PropGenerator.Desktop.Searching;

/// <summary>
/// How the palette ranks what you type, as easy-props does it. Pure, so it can be tested without a screen. Enter runs
/// the top row, which is why a typo-tolerant match always ranks below any exact one, and why the start of a word
/// beats a match inside one: "move" must never put "Remove part" first.
/// </summary>
internal static partial class PaletteSearch
{
    /// <summary>The score of an entry that does not answer the query.</summary>
    internal const double NoMatch = -1;

    /// <summary>The most uses that count towards the tie-break.</summary>
    internal const int MostUses = 20;

    /// <summary>Words people type for what the app calls something else.</summary>
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.Ordinal)
    {
        ["lod"] = "detail level",
        ["poly"] = "detail faces",
        ["triangles"] = "faces detail",
        ["tris"] = "faces detail",
        ["delete"] = "remove",
        ["reset"] = "restore",
        ["colour"] = "color",
        ["color"] = "colour",
        ["fps"] = "frame limit",
        ["preferences"] = "settings",
        ["options"] = "settings",
    };

    /// <summary>The sections a leading character limits the search to.</summary>
    private static readonly Dictionary<char, string> Prefixes = new()
    {
        ['>'] = PaletteSection.Actions,
        ['#'] = PaletteSection.Controls,
        ['@'] = PaletteSection.Presets,
    };

    /// <summary>
    /// How well an entry answers the query: the sum, over the words typed, of the best way each word matches. Every
    /// word must match somewhere or the entry scores <see cref="NoMatch"/>.
    /// </summary>
    /// <param name="query">What was typed.</param>
    /// <param name="label">The entry's name.</param>
    /// <param name="keywords">More words it matches on; may be empty.</param>
    /// <returns>The score; negative for not at all.</returns>
    internal static double Score(string query, string label, string keywords)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(label);
        string name = label.ToLowerInvariant();
        string[] parts = Split(name);
        string haystack = $"{name} {(keywords ?? string.Empty).ToLowerInvariant()}";
        string[] all = Split(haystack);
        string[] words = query.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        double total = 0;
        foreach (string word in words)
        {
            double score = ScoreWord(word, name, parts, all, haystack);
            if (Synonyms.TryGetValue(word, out string? synonym))
            {
                // Only the name counts here: a group called "Bark detail" is not about LODs. The word means this
                // feature, so it beats a passing mention elsewhere.
                bool viaSynonym = synonym.Split(' ').Any(other => parts.Any(part => part.StartsWith(other, StringComparison.Ordinal)));
                score = Math.Max(score, viaSynonym ? 6.5 : NoMatch);
            }

            if (score < 0)
            {
                return NoMatch;
            }

            total += score;
        }

        return total;
    }

    /// <summary>
    /// The entries that match, best first. Use only breaks near-ties: a command you run a lot rises above a similar
    /// one, and never above a clearly better match.
    /// </summary>
    /// <typeparam name="T">The kind of entry.</typeparam>
    /// <param name="entries">The candidates.</param>
    /// <param name="query">What was typed (the prefix already taken off).</param>
    /// <param name="label">Reads an entry's name.</param>
    /// <param name="keywords">Reads an entry's other words.</param>
    /// <param name="usageOf">How many times the user ran an entry.</param>
    /// <returns>The matches, best first (equal scores keep their order).</returns>
    internal static IReadOnlyList<T> Rank<T>(
        IEnumerable<T> entries, string query, Func<T, string> label, Func<T, string> keywords, Func<T, int>? usageOf = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(keywords);
        List<(T Entry, double Score)> scored =
            [.. entries.Select(e => (Entry: e, Score: Score(query, label(e), keywords(e)))).Where(x => x.Score >= 0)];

        // Typo tolerance is for when nothing else answers. Beside a real match it is noise.
        bool anyReal = scored.Any(x => x.Score >= 1);
        return
        [
            .. (anyReal ? scored.Where(x => x.Score >= 1) : scored)
                .Select(x => (x.Entry, Score: x.Score + (Math.Min(usageOf?.Invoke(x.Entry) ?? 0, MostUses) * 0.01)))
                .OrderByDescending(x => x.Score)
                .Select(x => x.Entry),
        ];
    }

    /// <summary>Takes a leading <c>&gt;</c>, <c>#</c> or <c>@</c> off the query, and says which section it limits the search to.</summary>
    /// <param name="query">What was typed.</param>
    /// <returns>The section (null for none) and the rest of the text.</returns>
    internal static (string? Section, string Text) ParsePrefix(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Length > 0 && Prefixes.TryGetValue(query[0], out string? section) ? (section, query[1..]) : (null, query);
    }

    [GeneratedRegex(@"[\s:()…\-]+")]
    private static partial Regex Separators();

    private static string[] Split(string text) => [.. Separators().Split(text).Where(s => s.Length > 0)];

    private static double ScoreWord(string word, string label, string[] parts, string[] all, string haystack)
    {
        if (label.StartsWith(word, StringComparison.Ordinal))
        {
            return 8;
        }

        if (parts.Any(p => p.StartsWith(word, StringComparison.Ordinal)))
        {
            return 6;
        }

        if (all.Any(p => p.StartsWith(word, StringComparison.Ordinal)))
        {
            return 3;
        }

        if (haystack.Contains(word, StringComparison.Ordinal))
        {
            return 2;
        }

        return all.Any(p => CloseTo(word, p)) ? 0.5 : NoMatch;
    }

    // True when the word is within one typing slip of the target (needs four letters or more, and the same length ±1).
    private static bool CloseTo(string word, string target)
    {
        if (word.Length < 4 || Math.Abs(word.Length - target.Length) > 1)
        {
            return false;
        }

        int i = 0;
        int j = 0;
        int slips = 0;
        while (i < word.Length && j < target.Length)
        {
            if (word[i] == target[j])
            {
                i++;
                j++;
                continue;
            }

            slips++;
            if (slips > 1)
            {
                return false;
            }

            // A swapped pair is one slip.
            if (j + 1 < target.Length && i + 1 < word.Length && word[i] == target[j + 1] && word[i + 1] == target[j])
            {
                i += 2;
                j += 2;
            }
            else if (word.Length > target.Length)
            {
                i++;
            }
            else if (word.Length < target.Length)
            {
                j++;
            }
            else
            {
                i++;
                j++;
            }
        }

        return slips + (word.Length - i) + (target.Length - j) <= 1;
    }
}
