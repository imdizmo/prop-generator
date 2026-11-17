using System.IO.Compression;
using System.Text;

namespace PropGenerator.Desktop.Crash;

/// <summary>
/// A crash report as the team server takes it: one zip with report.txt (the tool, its version, the OS, the memory cap
/// and the last memory line and errors of the run) and the run's whole log. <see cref="Shown"/> is exactly those
/// files' text, for the person to read before Send.
/// </summary>
/// <param name="Zip">The zip.</param>
/// <param name="Shown">What is in it, as text.</param>
internal sealed record CrashReport(byte[] Zip, string Shown)
{
    /// <summary>The largest report the server takes.</summary>
    internal const long MaxZip = 20L * 1024 * 1024;

    /// <summary>Makes the report of a run.</summary>
    /// <param name="log">The run's log.</param>
    /// <returns>The report.</returns>
    internal static CrashReport Of(string log)
    {
        string text = File.ReadAllText(log);
        string[] lines = text.Split('\n');
        string summary = new StringBuilder()
            .AppendLine("Tool: prop-generator")
            .AppendLine($"Version: {RunLog.Version}")
            .AppendLine($"OS: {RunLog.Os} ({System.Runtime.InteropServices.RuntimeInformation.OSDescription})")
            .AppendLine($"Memory cap: {RunLog.Cap()}")
            .AppendLine($"Last memory line: {lines.LastOrDefault(l => l.Contains(" memory: ", StringComparison.Ordinal))?.Trim() ?? "(none: the run was shorter than a minute)"}")
            .AppendLine($"Errors in the log: {lines.Count(l => l.Contains(" error (", StringComparison.Ordinal))}")
            .AppendLine("How it ended: no clean exit was written (an error, a crash or a kill, such as by the memory cap)")
            .ToString();
        string name = Path.GetFileName(log);
        using var m = new MemoryStream();
        using (var zip = new ZipArchive(m, ZipArchiveMode.Create, leaveOpen: true))
        {
            Put(zip, "report.txt", summary);
            Put(zip, name, text);
        }

        return new CrashReport(m.ToArray(), $"report.txt\n{summary}\n{name}\n{text}");
    }

    private static void Put(ZipArchive zip, string name, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        w.Write(text);
    }
}
