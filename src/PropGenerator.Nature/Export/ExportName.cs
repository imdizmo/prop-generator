// Ported from easy-props frontend/src/core/export/pipeline/types.ts (exportNameOf) (MIT, © 2026 AHS12).
using System.Text.RegularExpressions;

namespace PropGenerator.Nature.Export;

/// <summary>A prop's name made safe for every file its export writes (easy-props' <c>exportNameOf</c>).</summary>
public static partial class ExportName
{
    /// <summary>
    /// The name trimmed and lower-cased, each run of anything but <c>a-z</c> and <c>0-9</c> made one underscore, and
    /// underscores at either end dropped; "prop" when nothing is left.
    /// </summary>
    /// <param name="name">The name as the user gave it.</param>
    /// <returns>The safe name.</returns>
    public static string Of(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string safe = Edges().Replace(Unsafe().Replace(name.Trim().ToLowerInvariant(), "_"), "");
        return safe.Length > 0 ? safe : "prop";
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex Unsafe();

    [GeneratedRegex("^_+|_+$")]
    private static partial Regex Edges();
}
