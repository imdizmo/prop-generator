using PropGenerator.Engine;

namespace PropGenerator.Desktop.Engine;

/// <summary>
/// The textures easy-props' parts name (the leaf and dab atlases, barks and their reliefs), as the view reads them:
/// fetched from the build engine on a miss and kept in an LRU bounded by bytes. Two callers missing one key share one
/// fetch. The lock is never held while a fetch is waited on. The pixels are used as the engine gives them: three's
/// <c>image.data</c> has the row at v = 0 first, as <see cref="MeshTexture"/> does, so no row is flipped.
/// </summary>
internal sealed class EngineTextures
{
    private readonly long _capBytes;
    private readonly Func<string, EngineJobKind, CancellationToken, Task<EngineTexture>> _fetch;
    private readonly EngineThread? _thread;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, MeshTexture Texture)>> _kept = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, MeshTexture Texture)> _order = new();
    private readonly Dictionary<string, Task<MeshTexture>> _fetching = new(StringComparer.Ordinal);
    private long _bytes;
    private long _fetches;

    /// <summary>Makes an empty cache.</summary>
    /// <param name="capBytes">The most pixel bytes kept (the newest texture is kept even when it alone is larger).</param>
    /// <param name="fetch">Reads a texture by key, with the caller's turn and token; null for <see cref="EngineHost.Default"/>'s build engine.</param>
    /// <param name="thread">The build engine's thread, where <see cref="Get"/> must never run; null for the default host's.</param>
    public EngineTextures(long capBytes, Func<string, EngineJobKind, CancellationToken, Task<EngineTexture>>? fetch = null, EngineThread? thread = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capBytes);
        _capBytes = capBytes;
        _thread = thread ?? (fetch is null ? EngineHost.Default.Builds : null);
        _fetch = fetch ?? ((key, kind, cancel) => EngineHost.Default.Builds.TextureAsync(key, kind, cancel));
    }

    /// <summary>Gets the pixel bytes kept now.</summary>
    public long Bytes
    {
        get
        {
            lock (_lock)
            {
                return _bytes;
            }
        }
    }

    /// <summary>Gets how many times a texture was fetched from the engine (each miss shared by its callers counts once).</summary>
    public long Fetches => Interlocked.Read(ref _fetches);

    /// <summary>A texture by its key, kept or fetched (waiting for the engine on a miss).</summary>
    /// <param name="key">The key a part names, such as "leaf" or "bark:2:0.5".</param>
    /// <param name="kind">The fetch's turn on the engine: the caller's own.</param>
    /// <param name="cancel">Stops the wait (and the fetch, when no other caller shares it).</param>
    /// <returns>The texture, with no alpha cut (the part that prints it sets its own).</returns>
    /// <exception cref="InvalidOperationException">Called on the engine's own thread, which is the only one that could fetch it.</exception>
    /// <exception cref="EngineException">The engine failed to read it.</exception>
    /// <exception cref="OperationCanceledException">The wait was stopped.</exception>
    public MeshTexture Get(string key, EngineJobKind kind, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_thread?.IsEngineThread == true)
        {
            throw new InvalidOperationException("A texture is waited for on the engine's own thread, which is the one that would fetch it.");
        }

        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            Task<MeshTexture> fetching;
            TaskCompletionSource<MeshTexture>? mine = null;
            lock (_lock)
            {
                if (_kept.TryGetValue(key, out LinkedListNode<(string Key, MeshTexture Texture)>? node))
                {
                    _order.Remove(node);
                    _order.AddLast(node);
                    return node.Value.Texture;
                }

                if (!_fetching.TryGetValue(key, out fetching!))
                {
                    mine = new TaskCompletionSource<MeshTexture>(TaskCreationOptions.RunContinuationsAsynchronously);
                    fetching = mine.Task;
                    _fetching[key] = fetching;
                }
            }

            if (mine is not null)
            {
                Fetch(key, kind, mine, cancel);
            }

            try
            {
                return fetching.WaitAsync(cancel).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested && mine is null)
            {
                // The caller whose fetch this was gave up; this one still wants it, so it asks again.
            }
        }
    }

    // Fetches one texture outside the lock, keeps it, and lets every caller waiting on it go.
    private void Fetch(string key, EngineJobKind kind, TaskCompletionSource<MeshTexture> done, CancellationToken cancel)
    {
        Interlocked.Increment(ref _fetches);
        Task<EngineTexture> fetch;
        try
        {
            fetch = _fetch(key, kind, cancel);
        }
        catch (Exception e)
        {
            fetch = Task.FromException<EngineTexture>(e);
        }

        _ = fetch.ContinueWith(
            t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    MeshTexture texture = Convert(key, t.Result);
                    Keep(key, texture);
                    done.TrySetResult(texture);
                    return;
                }

                lock (_lock)
                {
                    _fetching.Remove(key);
                }

                if (t.IsCanceled)
                {
                    done.TrySetCanceled();
                }
                else
                {
                    done.TrySetException(t.Exception!.InnerExceptions);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // Keeps a fetched texture as the newest, letting the oldest go past the cap.
    private void Keep(string key, MeshTexture texture)
    {
        lock (_lock)
        {
            _fetching.Remove(key);
            if (_kept.Remove(key, out LinkedListNode<(string Key, MeshTexture Texture)>? stale))
            {
                _order.Remove(stale);
                _bytes -= stale.Value.Texture.Rgba.Length;
            }

            _kept[key] = _order.AddLast((key, texture));
            _bytes += texture.Rgba.Length;
            while (_bytes > _capBytes && _order.First is { } oldest && oldest != _order.Last)
            {
                _order.RemoveFirst();
                _kept.Remove(oldest.Value.Key);
                _bytes -= oldest.Value.Texture.Rgba.Length;
            }
        }
    }

    // The view's texture, once. Bark is sRGB and repeats, its relief is linear: a texture that says otherwise is noted.
    private static MeshTexture Convert(string key, EngineTexture texture)
    {
        bool bark = key.StartsWith("bark:", StringComparison.Ordinal);
        bool relief = key.StartsWith("relief:", StringComparison.Ordinal);
        if ((bark && (texture.ColorSpace != "srgb" || !texture.Repeat)) || (relief && texture.ColorSpace.Length != 0))
        {
            TraceLog.Write($"engine: texture {key} is {(texture.ColorSpace.Length == 0 ? "linear" : texture.ColorSpace)}{(texture.Repeat ? ", repeating" : string.Empty)}, not as its slot reads it");
        }

        return new MeshTexture(texture.Width, texture.Height, texture.Rgba, 0);
    }
}
