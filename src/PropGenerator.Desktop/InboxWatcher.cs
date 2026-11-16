namespace PropGenerator.Desktop;

/// <summary>
/// Watches the inbox folder for GLB files Blender sends. A file is read once changes to it have been still for half a
/// second (Blender writes in several steps), off the UI thread; the result, or the reason it could not be read, is
/// raised on the UI thread. A file that cannot be read is reported and never crashes the app. Files already in the inbox
/// when the app starts are not loaded.
/// </summary>
public sealed class InboxWatcher : IDisposable
{
    /// <summary>How long a file must stay unchanged before it is read.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(500);

    private readonly FileSystemWatcher? _watcher;
    private readonly Debounce _debounce;
    private string? _pending;
    private int _disposed;

    /// <summary>Starts watching (the folder is made if it is missing).</summary>
    /// <param name="inbox">The inbox folder.</param>
    public InboxWatcher(string inbox)
    {
        ArgumentException.ThrowIfNullOrEmpty(inbox);
        Folder = inbox;
        _debounce = new Debounce(Quiet, OnQuiet);
        try
        {
            Directory.CreateDirectory(inbox);
            _watcher = new FileSystemWatcher(inbox) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            _watcher.Created += OnTouched;
            _watcher.Changed += OnTouched;
            _watcher.Renamed += OnTouched;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            _watcher?.Dispose();
            _watcher = null;
            StartError = $"The Blender inbox {inbox} cannot be watched: {e.Message}";
        }
    }

    /// <summary>A GLB was read: its name (without .glb), the result, and the warning (or null).</summary>
    public event Action<InboxLoad>? Loaded;

    /// <summary>A GLB could not be read: a sentence for the status line.</summary>
    public event Action<string>? Failed;

    /// <summary>Gets the inbox folder.</summary>
    public string Folder { get; }

    /// <summary>Gets why watching could not start, or null.</summary>
    public string? StartError { get; }

    /// <summary>Gets the piece of the newest file read, or null.</summary>
    public Piece? Piece { get; private set; }

    /// <summary>Gets a value indicating whether the watcher was disposed.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _debounce.Cancel();
        _watcher?.Dispose();
    }

    private void OnTouched(object sender, FileSystemEventArgs e)
    {
        if (IsDisposed || !e.FullPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Volatile.Write(ref _pending, e.FullPath);
        _debounce.Restart();
    }

    private void OnQuiet()
    {
        string? path = Interlocked.Exchange(ref _pending, null);
        if (path is null || IsDisposed)
        {
            return;
        }

        _ = ReadAsync(path);
    }

    private async Task ReadAsync(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        try
        {
            GlbImport.Result result = await Task.Run(() => GlbImport.Read(path));
            if (IsDisposed)
            {
                return;
            }

            Piece = result.Piece;
            Loaded?.Invoke(new InboxLoad(name, result));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            if (!IsDisposed)
            {
                Failed?.Invoke($"{Path.GetFileName(path)} from Blender could not be loaded: {e.Message}");
            }
        }
    }
}
