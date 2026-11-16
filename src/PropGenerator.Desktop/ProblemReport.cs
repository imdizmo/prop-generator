using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PropGenerator.Desktop;

/// <summary>
/// Report a problem: a plain-text report of what is needed to look into one (the app's version, the system, the open
/// prop's kind, starting point, seed and every setting, the last failure said). It is only shown, copied or saved:
/// nothing is ever sent (our repo has no place to send it to).
/// </summary>
internal static class ProblemReport
{
    /// <summary>Writes the report for the window as it is now.</summary>
    /// <param name="w">The window.</param>
    /// <returns>The report.</returns>
    internal static string Make(MainWindow w)
    {
        ArgumentNullException.ThrowIfNull(w);
        var sb = new StringBuilder();
        sb.AppendLine("Prop Generator problem report");
        sb.AppendLine();
        sb.AppendLine("What happened:");
        sb.AppendLine("(say what you did, what you expected and what you got instead)");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"App: Prop Generator {BuildInfo.Version}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"System: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");
        foreach (string line in EngineLines(Engine.EngineHost.Default.LoadFailure))
        {
            sb.AppendLine(line);
        }

        string kind = Props.YourProps.KindOf(w.Files.Recipe);
        string from = w.Files.StartedFrom is { } id && w.AllStartingPoints.FirstOrDefault(p => p.Id == id) is { } point
            ? $", from {point.Name}"
            : w.Files.FilePath is null ? string.Empty : ", opened from a file";
        string lowPoly = w.Panel.Current.Values.TryGetValue("build", out JsonElement b) && b.ValueKind == JsonValueKind.String && b.GetString() == "low-poly"
            ? ", Low-poly"
            : string.Empty;
        sb.AppendLine(CultureInfo.InvariantCulture, $"Prop: {kind}{from}, seed {w.Panel.SeedValue}{lowPoly}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"File: {w.Files.Name}{(w.Files.Dirty ? " (unsaved changes)" : string.Empty)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Export look: {w.Files.Prefs.ExportLook}, pictures {w.Files.Prefs.ExportPictureSize}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Last failure shown: {w.LastError ?? "none"}");
        sb.AppendLine("Settings:");
        foreach ((string key, JsonElement value) in w.Panel.Current.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {key}: {value.GetRawText()}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// The report's lines about easy-props' engine: the bundle's tag and commit, read from its pin file (not from the
    /// engine, which cannot answer exactly when it failed to load), and why it failed to load, when it did.
    /// </summary>
    /// <param name="failure">Why the engine failed to load, or null.</param>
    /// <returns>The lines.</returns>
    internal static IReadOnlyList<string> EngineLines(Exception? failure)
    {
        string bundle = "unknown (no engine-pin.json)";
        if (PropGenerator.Engine.EngineOptions.ReadResource("engine-pin.json") is string pin)
        {
            try
            {
                using var doc = JsonDocument.Parse(pin);
                string tag = doc.RootElement.TryGetProperty("tag", out JsonElement t) ? t.GetString() ?? "?" : "?";
                string commit = doc.RootElement.TryGetProperty("commit", out JsonElement c) ? c.GetString() ?? "?" : "?";
                bundle = $"{tag} ({commit})";
            }
            catch (JsonException e)
            {
                bundle = $"unknown (engine-pin.json unreadable: {e.Message})";
            }
        }

        List<string> lines = [$"easy-props engine: {bundle}"];
        if (failure is not null)
        {
            lines.Add($"easy-props engine failed to load, only slab cliffs and stylized rocks are offered: {failure.GetType().Name}: {failure.Message}");
        }

        return lines;
    }
}
