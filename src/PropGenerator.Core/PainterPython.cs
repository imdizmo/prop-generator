namespace PropGenerator;

/// <summary>Which Python runs the painter's worker.</summary>
public static class PainterPython
{
    /// <summary>
    /// The Python bundled beside the app (a release package has python/bin/python3 on Linux, python\python.exe on
    /// Windows, with numpy in it), else the one on PATH (python3 on Linux, python on Windows; a checkout's build uses
    /// that one, with numpy installed by the user).
    /// </summary>
    /// <param name="baseDir">The app's folder.</param>
    /// <returns>A full path to the bundled Python, or the bare name to look up on PATH.</returns>
    public static string Find(string baseDir) => Find(baseDir, OperatingSystem.IsWindows());

    /// <summary>As <see cref="Find(string)"/>, for the platform given.</summary>
    /// <param name="baseDir">The app's folder.</param>
    /// <param name="windows">True for the Windows layout and name.</param>
    /// <returns>A full path to the bundled Python, or the bare name to look up on PATH.</returns>
    internal static string Find(string baseDir, bool windows)
    {
        ArgumentNullException.ThrowIfNull(baseDir);
        string bundled = windows ? Path.Combine(baseDir, "python", "python.exe") : Path.Combine(baseDir, "python", "bin", "python3");
        return File.Exists(bundled) ? bundled : windows ? "python" : "python3";
    }
}
