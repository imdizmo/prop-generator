using PropGenerator.Engine;

namespace PropGenerator.Desktop.Engine;

/// <summary>
/// The app's one easy-props engine: an <see cref="EngineThread"/> for builds, exports and pictures, a
/// <see cref="RulesEngine"/> for the panel's quick synchronous questions, the <see cref="EngineCatalog"/> read from it,
/// and the textures in a byte-capped LRU. Every window shares <see cref="Default"/>, which the app shuts down at exit.
/// Each member is made on first read, under a lock (not a <see cref="Lazy{T}"/>, which would keep a failure for the life
/// of the process): a failed load is tried again on the next read. After <see cref="Shutdown"/> every read throws
/// <see cref="ObjectDisposedException"/> instead of making an engine nobody would free.
/// </summary>
/// <param name="builds">The build engine's scripts and caps.</param>
/// <param name="rules">The rules engine's scripts and caps.</param>
internal sealed class EngineHost(EngineOptions builds, EngineOptions rules) : IDisposable
{
    /// <summary>The bytes of texture pixels kept between builds (a bark is about 1 MiB with its relief).</summary>
    internal const long TextureCapBytes = 64L << 20;

    private static readonly Lazy<EngineHost> Shared = new(() => new EngineHost(
        new EngineOptions { Log = Log },
        EngineOptions.ForRules with { Log = Log }));

    // _lock guards the fields and is never held while an engine loads or answers; _load lets one load run at a time.
    private readonly Lock _lock = new();
    private readonly Lock _load = new();
    private EngineThread? _builds;
    private RulesEngine? _rules;
    private bool _closed;

    /// <summary>Gets the app's host, made on first read; the app's exit (and the tests' assembly fixture) shuts it down.</summary>
    public static EngineHost Default => Shared.Value;

    /// <summary>Gets the build engine's thread (the engine itself loads before its first job).</summary>
    /// <exception cref="ObjectDisposedException">The host has been shut down.</exception>
    public EngineThread Builds
    {
        get
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                return _builds ??= new EngineThread(builds);
            }
        }
    }

    /// <summary>Gets a value indicating whether <see cref="Builds"/> has been made (something has asked for a build engine).</summary>
    public bool BuildsStarted
    {
        get
        {
            lock (_lock)
            {
                return _builds is not null;
            }
        }
    }

    /// <summary>Gets the rules engine, loading it on first read (on the caller's thread).</summary>
    /// <exception cref="ObjectDisposedException">The host has been shut down.</exception>
    /// <exception cref="EngineException">The bundle is missing or failed to load (tried again on the next read).</exception>
    public RulesEngine Rules
    {
        get
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                if (_rules is { } ready)
                {
                    return ready;
                }
            }

            lock (_load)
            {
                return LoadRules();
            }
        }
    }

    /// <summary>Gets the catalog: easy-props' info and presets, read once; parameters cached per generator.</summary>
    /// <exception cref="ObjectDisposedException">The host has been shut down.</exception>
    /// <exception cref="EngineException">The engine could not be loaded or read (tried again on the next read).</exception>
    public EngineCatalog Catalog
    {
        get
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                if (field is { } ready)
                {
                    return ready;
                }
            }

            lock (_load)
            {
                lock (_lock)
                {
                    ObjectDisposedException.ThrowIf(_closed, this);
                    if (field is { } loaded)
                    {
                        return loaded;
                    }
                }

                RulesEngine engine = LoadRules();
                EngineCatalog made = Loaded(() => new EngineCatalog(engine));
                lock (_lock)
                {
                    ObjectDisposedException.ThrowIf(_closed, this);
                    return field = made;
                }
            }
        }
    }

    /// <summary>Gets the textures the builds name, kept in an LRU of at most <see cref="TextureCapBytes"/>.</summary>
    /// <exception cref="ObjectDisposedException">The host has been shut down.</exception>
    public EngineTextures Textures
    {
        get
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                if (field is null)
                {
                    EngineThread thread = _builds ??= new EngineThread(builds);
                    field = new EngineTextures(TextureCapBytes, thread.TextureAsync, thread);
                }

                return field;
            }
        }
    }

    /// <summary>
    /// Gets or sets why the engine last failed to load (null when it loaded, or has not been tried). The launch also
    /// sets it when easy-props loaded but a kind's parameters could not be read, so only our own kinds are registered.
    /// </summary>
    public Exception? LoadFailure
    {
        get
        {
            lock (_lock)
            {
                return field;
            }
        }

        internal set
        {
            lock (_lock)
            {
                field = value;
            }
        }
    }

    /// <summary>
    /// Stops the engines: what is queued is cancelled and what runs is stopped. Waits at most <paramref name="wait"/>
    /// for the build thread; past it, says so in the trace and lets the exit go on. Calling it again does nothing.
    /// </summary>
    /// <param name="wait">The longest wait for the build thread to end.</param>
    public void Shutdown(TimeSpan wait)
    {
        EngineThread? thread;
        RulesEngine? engine;
        lock (_lock)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            (thread, engine) = (_builds, _rules);
            (_builds, _rules) = (null, null);
        }

        if (thread is not null && !thread.Dispose(wait))
        {
            TraceLog.Write($"engine: the build thread had not ended after {wait.TotalSeconds:0.#} s; the exit goes on");
        }

        engine?.Dispose();
    }

    /// <summary>Shuts the host down, waiting at most 5 s for the build thread.</summary>
    public void Dispose() => Shutdown(TimeSpan.FromSeconds(5));

    // Where the bundle's console goes: the trace (on the engine's thread; it only writes).
    private static void Log(string level, string text) => TraceLog.Write($"engine {level}: {text}");

    // The rules engine, loaded when there is none yet. Called under _load, never under _lock (the load takes a while).
    private RulesEngine LoadRules()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_rules is { } ready)
            {
                return ready;
            }
        }

        RulesEngine made = Loaded(() => new RulesEngine(rules));
        lock (_lock)
        {
            if (!_closed)
            {
                return _rules = made;
            }
        }

        // Shut down while it loaded: nobody else would free it.
        made.Dispose();
        throw new ObjectDisposedException(nameof(EngineHost));
    }

    // Makes one part of the engine, keeping why it failed (and clearing that once it loads).
    private T Loaded<T>(Func<T> make)
    {
        try
        {
            T made = make();
            LoadFailure = null;
            return made;
        }
        catch (Exception e) when (e is EngineException or TimeoutException or OperationCanceledException or DllNotFoundException or TypeInitializationException or IOException)
        {
            LoadFailure = e;
            TraceLog.Write($"engine: easy-props could not be loaded: {e.Message}");
            throw;
        }
    }
}
