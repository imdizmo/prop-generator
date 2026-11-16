using System.Diagnostics;
using System.Globalization;

namespace PropGenerator.Desktop;

/// <summary>
/// The timing lines written to stderr when the app runs with PROPGEN_TRACE=1, each starting with the milliseconds
/// since the app started, so a slider change can be followed to the frame that shows it.
/// </summary>
internal static class TraceLog
{
    private static readonly long Start = Stopwatch.GetTimestamp();

    /// <summary>Gets a value indicating whether PROPGEN_TRACE=1 is set.</summary>
    public static bool On { get; } = Environment.GetEnvironmentVariable("PROPGEN_TRACE") == "1";

    /// <summary>Every line written, tracing on or off (the tests listen for what must never be written).</summary>
    internal static event Action<string>? Written;

    /// <summary>Writes one line, if tracing is on.</summary>
    /// <param name="text">The line, without the time.</param>
    public static void Write(string text)
    {
        Written?.Invoke(text);
        if (On)
        {
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"t={Stopwatch.GetElapsedTime(Start).TotalMilliseconds:0.0} {text}"));
        }
    }
}
