using System.Runtime.CompilerServices;

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>
/// Keeps every test away from the real Groundwork inbox: before any test runs, XDG_DATA_HOME points at a scratch
/// folder of the run (removed when it ends), so a window that is not given a data folder of its own still sends there.
/// </summary>
internal static class GroundworkGuard
{
    /// <summary>Gets the run's stand-in for XDG_DATA_HOME.</summary>
    internal static string DataHome { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "prop-generator", "scratch", "desktop-xdg-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "The test run must never reach the user's Groundwork inbox.")]
    internal static void Point()
    {
        Directory.CreateDirectory(DataHome);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", DataHome);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(DataHome, recursive: true);
            }
            catch (IOException)
            {
                // Left in scratch.
            }
        };
    }
}
