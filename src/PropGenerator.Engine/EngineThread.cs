namespace PropGenerator.Engine;

/// <summary>What a job is for, which sets its turn: lower runs first.</summary>
public enum EngineJobKind
{
    /// <summary>The live view: what the user is looking at now.</summary>
    Live = 0,

    /// <summary>An export the user asked for.</summary>
    Export = 1,

    /// <summary>The Show me more cards.</summary>
    ShowMore = 2,

    /// <summary>Drawer thumbnails.</summary>
    Thumbnail = 3,
}

/// <summary>
/// The build engine on its own thread, so no caller (the UI least of all) ever waits on V8. Jobs queue by kind
/// (<see cref="EngineJobKind"/>: live, then export, then show-more, then thumbnails; first come first served
/// within a kind). A new live build drops the queued live build with the same key (one key per view and purpose): only
/// the latest of each matters. A job's token cancels it in the queue, or stops it while it runs (see
/// <see cref="EasyPropsEngine"/>). Disposing cancels what is queued and stops what runs.
/// </summary>
public sealed class EngineThread : IDisposable
{
    private const string LiveBuild = "live-build";

    private readonly Lock _lock = new();
    private readonly PriorityQueue<Job, (EngineJobKind Kind, long Order)> _queue = new();
    private readonly Dictionary<string, Job> _replaceable = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _closing = new();
    private readonly EngineOptions _options;
    private readonly Thread _thread;
    private readonly long[] _queued = new long[Enum.GetValues<EngineJobKind>().Length];
    private long _order;
    private bool _closed;
    private int _freed;

    /// <summary>Starts the thread; it loads the engine before its first job.</summary>
    /// <param name="options">The engine's scripts, caps and limits; null for the embedded bundle.</param>
    public EngineThread(EngineOptions? options = null)
    {
        _options = options ?? new EngineOptions();
        _thread = new Thread(Loop) { IsBackground = true, Name = "easy-props engine" };
        _thread.Start();
    }

    /// <summary>Queues work for the engine.</summary>
    /// <typeparam name="T">The work's result.</typeparam>
    /// <param name="kind">Its turn.</param>
    /// <param name="work">The work: given the engine and the job's stop token, on the engine's thread.</param>
    /// <param name="cancel">Cancels the job, queued or running.</param>
    /// <returns>The work's result; cancelled when the job is cancelled.</returns>
    public Task<T> Enqueue<T>(EngineJobKind kind, Func<EasyPropsEngine, CancellationToken, T> work, CancellationToken cancel = default) =>
        Enqueue(kind, null, work, cancel);

    /// <summary>Queues work for the engine, dropping the queued job it replaces.</summary>
    /// <typeparam name="T">The work's result.</typeparam>
    /// <param name="kind">Its turn.</param>
    /// <param name="replaces">A key: a queued job with the same key is dropped (cancelled) for this one; null for none.</param>
    /// <param name="work">The work: given the engine and the job's stop token, on the engine's thread.</param>
    /// <param name="cancel">Cancels the job, queued or running.</param>
    /// <returns>The work's result; cancelled when the job is cancelled or dropped.</returns>
    public Task<T> Enqueue<T>(EngineJobKind kind, string? replaces, Func<EasyPropsEngine, CancellationToken, T> work,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (cancel.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancel);
        }

        var job = new Job<T>(work, cancel);

        // Watched before it is queued: once queued, the engine thread may run it (and dispose the registration)
        // at any moment. A token cancelled in between drops the job, and the thread skips it.
        job.Watch();
        lock (_lock)
        {
            if (_closed)
            {
                job.Drop();
                ObjectDisposedException.ThrowIf(_closed, this);
            }

            if (replaces is not null)
            {
                if (_replaceable.TryGetValue(replaces, out Job? stale))
                {
                    stale.Drop();
                }

                _replaceable[replaces] = job;
                job.ReplaceKey = replaces;
            }

            _queue.Enqueue(job, (kind, _order++));
            Interlocked.Increment(ref _queued[(int)kind]);
        }

        _signal.Release();
        return job.Task;
    }

    /// <summary>Whether the caller runs on the engine's own thread (inside a job): it must never wait on another job.</summary>
    public bool IsEngineThread => Thread.CurrentThread == _thread;

    /// <summary>How many jobs of a kind have been queued since the thread started (dropped ones included).</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The count.</returns>
    public long JobsQueued(EngineJobKind kind) => Interlocked.Read(ref _queued[(int)kind]);

    /// <summary>The bundle's version and generators.</summary>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The info.</returns>
    public Task<EngineInfo> InfoAsync(CancellationToken cancel = default) =>
        Enqueue(EngineJobKind.Live, static (e, t) => e.Info(t), cancel);

    /// <summary>Every preset.</summary>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The presets.</returns>
    public Task<EnginePresets> PresetsAsync(CancellationToken cancel = default) =>
        Enqueue(EngineJobKind.Live, static (e, t) => e.Presets(t), cancel);

    /// <summary>A generator's parameter definitions.</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>Its specs and colour rows.</returns>
    public Task<EngineParameters> ParametersAsync(string generatorId, CancellationToken cancel = default) =>
        Enqueue(EngineJobKind.Live, (e, t) => e.Parameters(generatorId, t), cancel);

    /// <summary>
    /// Builds a scene at one detail. A build drops the queued job with the same replace key; a live build with no key
    /// of its own has the one shared key, so it drops any keyless live build still queued.
    /// </summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="detail">The detail.</param>
    /// <param name="kind">Its turn (live for the view).</param>
    /// <param name="fullFaces">Also count the faces at full detail.</param>
    /// <param name="replaces">Its replace key (such as view-build:3); null for the shared live key, or none for other kinds.</param>
    /// <param name="cancel">Cancels the build.</param>
    /// <returns>The parts.</returns>
    public Task<EngineMesh> BuildAsync(string sceneJson, string style, double detail,
        EngineJobKind kind = EngineJobKind.Live, bool fullFaces = false, string? replaces = null, CancellationToken cancel = default) =>
        Enqueue(kind, replaces ?? (kind == EngineJobKind.Live ? LiveBuild : null),
            (e, t) => e.Build(sceneJson, style, detail, fullFaces, t), cancel);

    /// <summary>A texture's pixels by its key.</summary>
    /// <param name="key">The key.</param>
    /// <param name="kind">Its turn.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The pixels.</returns>
    public Task<EngineTexture> TextureAsync(string key, EngineJobKind kind = EngineJobKind.Live, CancellationToken cancel = default) =>
        Enqueue(kind, (e, t) => e.Texture(key, t), cancel);

    /// <summary>Upstream's export package.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="settingsJson">The export settings.</param>
    /// <param name="progress">
    /// Gets the export's progress (0 to 1, ending at 1). It is called on the engine's thread, so it may only report on
    /// (a <see cref="Progress{T}"/>, or a post to the UI thread), never wait for another thread.
    /// </param>
    /// <param name="cancel">Cancels the export.</param>
    /// <returns>The files.</returns>
    public Task<IReadOnlyList<EngineFile>> ExportAsync(string sceneJson, string style, string settingsJson,
        IProgress<double>? progress = null, CancellationToken cancel = default) =>
        Enqueue(EngineJobKind.Export, (e, t) => e.ExportPackage(sceneJson, style, settingsJson, progress, t), cancel);

    /// <summary>The scene's components, in compact form.</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="valuesJson">The values.</param>
    /// <param name="seed">The seed.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The components, as JSON.</returns>
    public Task<string> ComponentsAsync(string generatorId, string valuesJson, long seed, CancellationToken cancel = default) =>
        Enqueue(EngineJobKind.Live, (e, t) => e.Components(generatorId, valuesJson, seed, t), cancel);

    /// <summary>The scene's collision: what the generator offers, and what the export writes.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="kind">Its turn.</param>
    /// <param name="replaces">Its replace key: a queued job with the same key is dropped for this one; null for none.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The capsules.</returns>
    public Task<EngineCollision> CollisionAsync(string sceneJson, string style,
        EngineJobKind kind = EngineJobKind.Live, string? replaces = null, CancellationToken cancel = default) =>
        Enqueue(kind, replaces, (e, t) => e.Collision(sceneJson, style, t), cancel);

    /// <summary>The scene's pivot.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="kind">Its turn.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>x, y, z.</returns>
    public Task<IReadOnlyList<double>> PivotAsync(string sceneJson, EngineJobKind kind = EngineJobKind.Live, CancellationToken cancel = default) =>
        Enqueue(kind, (e, t) => e.Pivot(sceneJson, t), cancel);

    /// <summary>The traits of a set of values (on the build engine; the panel asks <see cref="RulesEngine"/>).</summary>
    /// <param name="generatorId">The generator.</param>
    /// <param name="valuesJson">The values.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The traits.</returns>
    public Task<EngineTraits> TraitsAsync(string generatorId, string valuesJson, CancellationToken cancel = default) =>
        Enqueue(EngineJobKind.Live, (e, t) => e.Traits(generatorId, valuesJson, t), cancel);

    /// <summary>
    /// One of upstream's scene operations on the engine's thread: the tree and seed ops, which make every part of the
    /// prop again (the cheap part edits run on <see cref="RulesEngine.SceneOp"/>). A live job.
    /// </summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="opJson">The operation: <c>{kind: tree, patch}</c> or <c>{kind: seed, seed}</c> (any op is taken).</param>
    /// <param name="style">The style profile.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The scene after it, or null when upstream refused.</returns>
    public Task<string?> SceneOpAsync(string sceneJson, string opJson, string style, CancellationToken cancel = default) =>
        Enqueue(EngineJobKind.Live, (e, t) => e.SceneOp(sceneJson, opJson, style, t), cancel);

    /// <summary>The faces at full detail, as upstream's status bar counts them.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>Wood and leaf faces.</returns>
    public Task<EngineFaceCounts> CountFacesAsync(string sceneJson, string style, CancellationToken cancel = default) =>
        Enqueue(EngineJobKind.Live, (e, t) => e.CountFaces(sceneJson, style, t), cancel);

    /// <summary>Upstream's Make it fit.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="budget">The faces it may have.</param>
    /// <param name="kind">Its turn.</param>
    /// <param name="deadline">How long it may try: null for upstream's 5 s, <see cref="Timeout.InfiniteTimeSpan"/> for no limit.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The scene and what changed.</returns>
    public Task<EngineFit> MakeItFitAsync(string sceneJson, string style, int budget, EngineJobKind kind = EngineJobKind.Live,
        TimeSpan? deadline = null, CancellationToken cancel = default) =>
        Enqueue(kind, (e, t) => e.MakeItFit(sceneJson, style, budget, deadline, t), cancel);

    /// <summary>The pieces a tree can make (plans the tree up to three times). A live job.</summary>
    /// <param name="sceneJson">The tree's scene.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The kinds, in upstream's order; none for anything but a painted tree.</returns>
    public Task<IReadOnlyList<string>> PiecesOfferedAsync(string sceneJson, CancellationToken cancel = default) =>
        Enqueue(EngineJobKind.Live, (e, t) => e.PiecesOffered(sceneJson, t), cancel);

    /// <summary>
    /// One piece of a tree, as a prop's scene: one kind per job, so pieces are made one at a time and a cancel stops
    /// the kinds still queued (and a recycle can fall between them). An export job.
    /// </summary>
    /// <param name="sceneJson">The tree's scene.</param>
    /// <param name="treeName">The tree's name.</param>
    /// <param name="kind">stump, log, branch or litter.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The piece.</returns>
    public Task<EnginePiece> MakePieceAsync(string sceneJson, string treeName, string kind, string style, CancellationToken cancel = default) =>
        Enqueue(EngineJobKind.Export, (e, t) =>
        {
            IReadOnlyList<EnginePiece> made = e.MakePieces(sceneJson, treeName, System.Text.Json.JsonSerializer.Serialize(new[] { kind }), style, t);
            return made.Count == 1 ? made[0] : throw new EngineScriptException($"makePieces made {made.Count} pieces for one kind ({kind}).");
        }, cancel);

    /// <summary>A batch's names, as upstream numbers them (names only). An export job.</summary>
    /// <param name="choicesJson">The choices.</param>
    /// <param name="variations">Variations of each preset or scene.</param>
    /// <param name="contextJson">The context: <c>{style}</c>.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The items.</returns>
    public Task<IReadOnlyList<EngineBatchItem>> BatchItemsAsync(string choicesJson, int variations, string contextJson, CancellationToken cancel = default) =>
        Enqueue(EngineJobKind.Export, (e, t) => e.BatchItems(choicesJson, variations, contextJson, t), cancel);

    /// <summary>The prop in a set's colours (made again).</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="coloursJson">The set's colours, by group id.</param>
    /// <param name="style">The style profile.</param>
    /// <param name="kind">Its turn (export by default; a preview may queue as show-more).</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The scene.</returns>
    public Task<string> InSetColoursAsync(string sceneJson, string coloursJson, string style,
        EngineJobKind kind = EngineJobKind.Export, CancellationToken cancel = default) =>
        Enqueue(kind, (e, t) => e.InSetColours(sceneJson, coloursJson, style, t), cancel);

    /// <summary>The prop in one version: on its seed, damaged as it says.</summary>
    /// <param name="sceneJson">The scene.</param>
    /// <param name="versionJson">The version (<see cref="EngineVersion.ToJson"/>).</param>
    /// <param name="style">The style profile.</param>
    /// <param name="kind">Its turn (export by default; Show me more's cards queue as show-more).</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The version's scene.</returns>
    public Task<string> VersionSceneAsync(string sceneJson, string versionJson, string style,
        EngineJobKind kind = EngineJobKind.Export, CancellationToken cancel = default) =>
        Enqueue(kind, (e, t) => e.VersionScene(sceneJson, versionJson, style, t), cancel);

    /// <summary>The list a set ends with. An export job.</summary>
    /// <param name="name">The set's export name.</param>
    /// <param name="entriesJson">The props and their files (each GLB as its JSON chunk).</param>
    /// <param name="groupsJson">The colour groups.</param>
    /// <param name="coloursJson">The set's colours.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <returns>The file.</returns>
    public Task<EngineFile> SetListAsync(string name, string entriesJson, string groupsJson, string coloursJson, CancellationToken cancel = default) =>
        Enqueue(EngineJobKind.Export, (e, t) => e.SetList(name, entriesJson, groupsJson, coloursJson, t), cancel);

    /// <summary>Whether a scene op makes every part again (tree, seed): those run here, never on the rules engine.</summary>
    /// <param name="kind">The op's kind.</param>
    /// <returns>True for tree and seed.</returns>
    internal static bool IsHeavyOp(string kind) => kind is "tree" or "seed";

    /// <summary>The engine's memory, after the jobs queued before this one.</summary>
    /// <param name="kind">Its turn.</param>
    /// <returns>Heap and external sizes.</returns>
    public Task<EngineHeap> HeapAsync(EngineJobKind kind = EngineJobKind.Live) =>
        Enqueue(kind, static (e, _) => e.Heap());

    /// <summary>
    /// How many runtimes the engine has loaded (<see cref="EasyPropsEngine.Generation"/>: 1, plus one per recovery or
    /// recycle), after the jobs queued before this one. Loads the engine if no job has yet.
    /// </summary>
    /// <param name="kind">Its turn.</param>
    /// <returns>The generation.</returns>
    public Task<int> GenerationAsync(EngineJobKind kind = EngineJobKind.Live) =>
        Enqueue(kind, static (e, _) => e.Generation);

    private void Loop()
    {
        EasyPropsEngine? engine = null;
        try
        {
            while (true)
            {
                _signal.Wait();
                Job? job;
                lock (_lock)
                {
                    if (!_queue.TryDequeue(out job, out _))
                    {
                        if (_closed)
                        {
                            return;
                        }

                        continue;
                    }

                    if (job.ReplaceKey is not null && _replaceable.TryGetValue(job.ReplaceKey, out Job? latest) && latest == job)
                    {
                        _replaceable.Remove(job.ReplaceKey);
                    }

                    if (!job.Start())
                    {
                        continue;
                    }
                }

                try
                {
                    engine ??= new EasyPropsEngine(_options);
                }
                catch (Exception e) when (e is EngineException or IOException)
                {
                    job.Fail(e);
                    continue;
                }

                job.Run(engine, _closing.Token);
            }
        }
        finally
        {
            engine?.Dispose();
        }
    }

    /// <summary>Cancels what is queued, stops what runs, and waits for the thread to end.</summary>
    public void Dispose() => Close(Timeout.InfiniteTimeSpan);

    /// <summary>
    /// Cancels what is queued and stops what runs, as <see cref="Dispose()"/> does, but waits at most
    /// <paramref name="wait"/> for the thread to end (a job stuck in a host callback cannot hold up the app's exit).
    /// Calling it again (or <see cref="Dispose()"/>) waits again.
    /// </summary>
    /// <param name="wait">The longest wait for the thread.</param>
    /// <returns>True when the thread has ended; false when it was still running at the bound.</returns>
    public bool Dispose(TimeSpan wait) => Close(wait);

    private bool Close(TimeSpan wait)
    {
        bool first;
        lock (_lock)
        {
            first = !_closed;
            if (first)
            {
                _closed = true;
                foreach ((Job job, _) in _queue.UnorderedItems)
                {
                    job.Drop();
                }
            }
        }

        if (first)
        {
            _closing.Cancel();
            _signal.Release();
        }

        if (!_thread.Join(wait))
        {
            return false;
        }

        // Freed once the thread has ended, and only then: a thread still running reads both.
        if (Interlocked.Exchange(ref _freed, 1) == 0)
        {
            _closing.Dispose();
            _signal.Dispose();
        }

        return true;
    }

    private abstract class Job
    {
        private int _state; // 0 queued, 1 running or done, 2 dropped

        public string? ReplaceKey { get; set; }

        /// <summary>Marks the job as running; false when it was dropped or cancelled first.</summary>
        public bool Start() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

        /// <summary>Cancels the job if it has not started.</summary>
        public void Drop()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                Cancelled();
            }
        }

        public abstract void Watch();

        public abstract void Run(EasyPropsEngine engine, CancellationToken closing);

        public abstract void Fail(Exception error);

        protected abstract void Cancelled();
    }

    private sealed class Job<T>(Func<EasyPropsEngine, CancellationToken, T> work, CancellationToken cancel) : Job
    {
        private readonly TaskCompletionSource<T> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _watch;

        public Task<T> Task => _done.Task;

        /// <summary>Cancels the job while it is queued, the moment its token is cancelled. Called once, before it is queued.</summary>
        public override void Watch() => _watch = cancel.Register(Drop);

        public override void Run(EasyPropsEngine engine, CancellationToken closing)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel, closing);
            try
            {
                _done.TrySetResult(work(engine, stop.Token));
            }
            catch (OperationCanceledException e)
            {
                _done.TrySetCanceled(cancel.IsCancellationRequested ? cancel : e.CancellationToken);
            }
            catch (Exception e)
            {
                _done.TrySetException(e);
            }
            finally
            {
                _watch.Dispose();
            }
        }

        public override void Fail(Exception error)
        {
            _watch.Dispose();
            _done.TrySetException(error);
        }

        /// <summary>
        /// A dropped job lets go of its token: a long-lived token (a window's) would otherwise hold every dropped
        /// job, its work and what that captured, until the token's source ends.
        /// </summary>
        protected override void Cancelled()
        {
            _watch.Dispose();
            _done.TrySetCanceled(cancel.IsCancellationRequested ? cancel : CancellationToken.None);
        }
    }
}
