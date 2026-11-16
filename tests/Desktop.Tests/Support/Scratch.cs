namespace PropGenerator.Desktop.Tests.Support;

/// <summary>A scratch folder on disk (never /tmp, which is RAM), removed when the test is done.</summary>
internal sealed class Scratch : IDisposable
{
    /// <summary>Makes a new empty folder.</summary>
    public Scratch()
    {
        Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "prop-generator", "scratch", "desktop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
    }

    /// <summary>Gets the folder.</summary>
    public string Dir { get; }

    /// <summary>A path inside the folder.</summary>
    /// <param name="parts">Names below the folder.</param>
    /// <returns>The path.</returns>
    public string P(params string[] parts) => Path.Combine([Dir, .. parts]);

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(Dir, recursive: true);
}
