using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PropGenerator;

/// <summary>
/// Talks to the painter's worker process (painter/worker.py; its module docstring is the protocol). The worker is
/// started on the first paint and kept for the next ones.
/// </summary>
/// <remarks>
/// <para>Latest wins: at most one request is with the worker at a time. A call made while one is in flight waits as
/// the pending request; a newer call replaces the pending one, and the replaced call gets null.</para>
/// <para>Failures: when the worker dies, talks nonsense, cannot start, does not finish its reply in time (20 s by
/// default) or replies with a MemoryError, <see cref="Failed"/> is raised, the worker is killed and a new one is
/// started to try the request once more. When a second failure comes less than the
/// restart window (10 s) after the one before, nothing is restarted until the next <see cref="PaintAsync"/> call.
/// Any other error reply (the worker could not paint those settings) raises <see cref="Failed"/> and the call gets null;
/// the worker is still running and is kept.</para>
/// <para>Everything runs off the caller's thread; <see cref="Failed"/> is raised on a thread-pool thread.</para>
/// </remarks>
public sealed class WorkerClient : IPainter, IAsyncDisposable
{
    /// <summary>How long a closed worker gets to finish and exit on its own before it is killed.</summary>
    private static readonly TimeSpan DisposeGrace = TimeSpan.FromSeconds(2);

    private readonly string _workerPy;
    private readonly IWorkerLauncher _launcher;
    private readonly TimeSpan _restartWindow;
    private readonly TimeSpan _replyTimeout;
    private readonly Lock _gate = new();
    private Request? _pending;
    private Worker? _worker;
    private Task _loop = Task.CompletedTask;
    private bool _loopRunning;
    private bool _stopped;
    private long? _lastFailure;
    private int _nextId;
    private volatile bool _disposed;

    /// <summary>Makes a client. Nothing starts until the first paint.</summary>
    /// <param name="workerPy">The full path of painter/worker.py.</param>
    /// <param name="launcher">Starts the worker (the app uses <see cref="WorkerLaunchers.ForThisMachine(string)"/>).</param>
    /// <param name="restartWindow">A failure this soon after the one before stops the restarts; 10 s if null.</param>
    /// <param name="replyTimeout">
    /// A worker that has not finished its reply this long after the request is stuck, and is killed and restarted; 20 s
    /// if null (a 1024 paint takes about 0.6 s, the first one a few seconds while the painting processes start).
    /// </param>
    public WorkerClient(string workerPy, IWorkerLauncher launcher, TimeSpan? restartWindow = null, TimeSpan? replyTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(workerPy);
        ArgumentNullException.ThrowIfNull(launcher);
        _workerPy = workerPy;
        _launcher = launcher;
        _restartWindow = restartWindow ?? TimeSpan.FromSeconds(10);
        _replyTimeout = replyTimeout ?? TimeSpan.FromSeconds(20);
    }

    /// <inheritdoc/>
    public event Action<string>? Failed;

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size is not 256, 512 or 1024.</exception>
    public Task<Layers?> PaintAsync(int size, Settings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (size is not (256 or 512 or 1024))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The size must be 256, 512 or 1024.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<Layers?>(ct);
        }

        var req = new Request(size, settings);
        if (ct.CanBeCanceled)
        {
            req.Registration = ct.Register(() => Cancel(req, ct));
        }

        Request? replaced;
        lock (_gate)
        {
            if (_disposed)
            {
                req.Registration.Unregister();
                throw new ObjectDisposedException(GetType().FullName);
            }

            replaced = _pending;
            _pending = req;

            // A new paint is the user's action: it may start the worker again after a stop.
            _stopped = false;
            if (!_loopRunning)
            {
                _loopRunning = true;
                _loop = Task.Run(LoopAsync, CancellationToken.None);
            }
        }

        replaced?.Complete(null);
        return req.Task;
    }

    /// <summary>
    /// Stops the worker: closes its stdin (it then stops its painting processes and exits), waits up to 2 s, then
    /// kills it and everything it started by PID. A paint still waiting or in flight gets null.
    /// </summary>
    /// <returns>A task that ends once the worker is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        Request? pending;
        Worker? worker;
        Task loop;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pending = _pending;
            _pending = null;
            worker = _worker;
            _worker = null;
            loop = _loop;
        }

        pending?.Complete(null);
        if (worker is not null)
        {
            await worker.StopAsync(DisposeGrace, closeInput: true).ConfigureAwait(false);
        }

        await loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        worker?.Dispose();
    }

    private void Cancel(Request req, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_pending == req)
            {
                _pending = null;
            }
        }

        req.Cancel(ct);
    }

    private async Task LoopAsync()
    {
        bool clean = false;
        try
        {
            while (true)
            {
                Request req;
                lock (_gate)
                {
                    if (_pending is null || _disposed)
                    {
                        _loopRunning = false;
                        clean = true;
                        return;
                    }

                    req = _pending;
                    _pending = null;
                }

                Layers? result = null;
                try
                {
                    result = await ServeAsync(req).ConfigureAwait(false);
                }
                finally
                {
                    req.Complete(result);
                }
            }
        }
        finally
        {
            // Only reached without `clean` when a Failed handler threw: let the next paint start a new loop.
            if (!clean)
            {
                lock (_gate)
                {
                    _loopRunning = false;
                }
            }
        }
    }

    /// <summary>Sends one request, with one restart and retry if the worker fails.</summary>
    private async Task<Layers?> ServeAsync(Request req)
    {
        bool retried = false;
        while (true)
        {
            Worker? worker;
            lock (_gate)
            {
                if (_disposed || _stopped || req.Task.IsCompleted)
                {
                    return null;
                }

                worker = _worker;
            }

            string failure;
            try
            {
                worker ??= await StartWorkerAsync().ConfigureAwait(false);
                if (worker is null)
                {
                    return null;
                }

                Reply reply = await worker.PaintAsync(Interlocked.Increment(ref _nextId), req.Size, req.Settings, _replyTimeout)
                    .ConfigureAwait(false);
                if (_disposed)
                {
                    return null;
                }

                if (reply.Error is null)
                {
                    return reply.Layers;
                }

                // Out of memory: the worker has grown near its cap, so it is replaced like one that died.
                // Any other error is about these settings: the worker is fine and is kept.
                if (!reply.Error.Contains("MemoryError", StringComparison.Ordinal))
                {
                    Failed?.Invoke($"The painter could not paint these settings: {reply.Error}");
                    return null;
                }

                failure = $"it ran out of memory ({reply.Error})";
            }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or InvalidOperationException
                or OperationCanceledException or ObjectDisposedException or System.ComponentModel.Win32Exception)
            {
                failure = e is OperationCanceledException
                    ? $"it did not finish its reply within {_replyTimeout.TotalSeconds:0.#} s"
                    : e.Message;
            }

            if (_disposed)
            {
                return null;
            }

            (string message, string tail) = await EndFailedWorkerAsync(worker, failure).ConfigureAwait(false);
            bool restart = NoteFailure();
            string next = restart
                ? "It is started again."
                : $"It failed twice within {_restartWindow.TotalSeconds:0} s, so it is not started again until the next change.";
            Failed?.Invoke(tail.Length > 0 ? $"{message} {next} Its last messages: {tail}" : $"{message} {next}");
            if (!restart || retried)
            {
                return null;
            }

            lock (_gate)
            {
                // A newer request is waiting: it goes next (on a new worker) and this one is dropped.
                if (_pending is not null)
                {
                    return null;
                }
            }

            retried = true;
        }
    }

    /// <summary>Starts a worker, or kills it again at once if the client was disposed meanwhile.</summary>
    private async Task<Worker?> StartWorkerAsync()
    {
        Process process = _launcher.Start(_workerPy);
        Worker worker;
        try
        {
            worker = new Worker(process);
        }
        catch (InvalidOperationException)
        {
            // Its streams were not redirected: it is of no use, so it must not be left running.
            process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }

        lock (_gate)
        {
            if (!_disposed)
            {
                _worker = worker;
                return worker;
            }
        }

        await worker.StopAsync(TimeSpan.Zero, closeInput: true).ConfigureAwait(false);
        worker.Dispose();
        return null;
    }

    /// <summary>Kills a worker that failed; says what happened, and gives the end of its stderr.</summary>
    private async Task<(string Message, string Tail)> EndFailedWorkerAsync(Worker? worker, string failure)
    {
        if (worker is null)
        {
            return ($"The painter could not start: {failure.TrimEnd('.')}.", "");
        }

        lock (_gate)
        {
            if (_worker == worker)
            {
                _worker = null;
            }
        }

        // Its output ended or made no sense: give it a moment to exit by itself, then kill it.
        int? exitCode = await worker.StopAsync(TimeSpan.FromMilliseconds(500), closeInput: true).ConfigureAwait(false);
        string tail = await worker.ErrorTailAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        worker.Dispose();
        var sb = new StringBuilder("The painter stopped");
        if (exitCode is int code)
        {
            sb.Append(" (exit code ").Append(code).Append(')');
        }

        sb.Append(": ").Append(failure.TrimEnd('.')).Append('.');
        return (sb.ToString(), tail);
    }

    /// <summary>Notes a failure now. False when it came within the restart window of the one before: stop.</summary>
    private bool NoteFailure()
    {
        long now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            bool tooSoon = _lastFailure is long last && Stopwatch.GetElapsedTime(last, now) < _restartWindow;
            _lastFailure = now;
            if (tooSoon)
            {
                _stopped = true;
            }

            return !tooSoon;
        }
    }

    private sealed class Request(int size, Settings settings)
    {
        private readonly TaskCompletionSource<Layers?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Size => size;

        public Settings Settings => settings;

        public CancellationTokenRegistration Registration { get; set; }

        public Task<Layers?> Task => _tcs.Task;

        public void Complete(Layers? layers)
        {
            Registration.Unregister();
            _tcs.TrySetResult(layers);
        }

        public void Cancel(CancellationToken ct) => _tcs.TrySetCanceled(ct);
    }

    private readonly record struct Reply(Layers? Layers, string? Error);

    /// <summary>One running worker process: its pipes, and the last 4 KB of what it wrote to stderr.</summary>
    private sealed class Worker : IDisposable
    {
        private const int TailBytes = 4096;
        private const int MaxHeadBytes = 64 * 1024;

        private readonly Process _process;
        private readonly Stream _input;
        private readonly BufferedStream _output;
        private readonly Task _drain;
        private readonly byte[] _tail = new byte[TailBytes];
        private readonly Lock _tailGate = new();
        private int _tailLength;

        public Worker(Process process)
        {
            _process = process;
            _input = process.StandardInput.BaseStream;
            _output = new BufferedStream(process.StandardOutput.BaseStream, 64 * 1024);

            // Keep reading stderr: a full pipe would stop the worker in its tracks.
            _drain = Task.Run(() => DrainAsync(process.StandardError.BaseStream));
        }

        public async Task<Reply> PaintAsync(int id, int size, Settings settings, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            CancellationToken ct = cts.Token;
            await _input.WriteAsync(RequestLine(id, size, settings), ct).ConfigureAwait(false);
            await _input.FlushAsync(ct).ConfigureAwait(false);

            byte[] head = await ReadHeadAsync(ct).ConfigureAwait(false);
            (Reply? error, long expected) = ReadHead(head, id, size);
            if (error is Reply e)
            {
                return e;
            }

            byte[] data = new byte[expected];
            try
            {
                await _output.ReadExactlyAsync(data, ct).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                throw new EndOfStreamException("its output ended in the middle of a reply");
            }

            return new Reply(Layers.FromWorkerBytes(size, data), null);
        }

        /// <summary>Closes stdin (optionally), waits up to grace for the exit, then kills the process tree.</summary>
        /// <returns>The exit code, or null if it could not be read.</returns>
        public async Task<int?> StopAsync(TimeSpan grace, bool closeInput)
        {
            if (closeInput)
            {
                try
                {
                    _input.Dispose();
                }
                catch (IOException)
                {
                    // Already closed at the other end: the worker has gone.
                }
            }

            if (!await WaitForExitAsync(grace).ConfigureAwait(false))
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It exited between the wait and the kill.
                }

                await WaitForExitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }

            try
            {
                return _process.HasExited ? _process.ExitCode : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>The last of what the worker wrote to stderr, once stderr has closed (or after the wait).</summary>
        public async Task<string> ErrorTailAsync(TimeSpan wait)
        {
            await Task.WhenAny(_drain, Task.Delay(wait)).ConfigureAwait(false);
            lock (_tailGate)
            {
                return Encoding.UTF8.GetString(_tail, 0, _tailLength).Trim();
            }
        }

        public void Dispose()
        {
            _output.Dispose();
            _process.Dispose();
        }

        /// <summary>
        /// Checks a reply header. Gives the error reply, or the payload's byte count. A header that cannot be read
        /// (not JSON, a field of the wrong kind, 1.5 where a whole number belongs) is an InvalidDataException, so the
        /// worker, now out of step, is killed and restarted.
        /// </summary>
        private static (Reply? Error, long Bytes) ReadHead(byte[] head, int id, int size)
        {
            try
            {
                using var doc = JsonDocument.Parse(head);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("it sent a reply that is not a JSON object");
                }

                if (root.TryGetProperty("id", out JsonElement gotId) && gotId.ValueKind == JsonValueKind.Number
                    && gotId.GetInt64() != id)
                {
                    throw new InvalidDataException($"it answered request {gotId.GetInt64()} instead of {id}");
                }

                if (root.TryGetProperty("error", out JsonElement error))
                {
                    return (new Reply(null, error.ValueKind == JsonValueKind.String ? error.GetString() : error.GetRawText()), 0);
                }

                long expected = (long)Layers.Count * size * size * 2;
                if (!root.TryGetProperty("size", out JsonElement gotSize) || gotSize.GetInt32() != size
                    || !root.TryGetProperty("dtype", out JsonElement dtype) || dtype.GetString() != "float16"
                    || !root.TryGetProperty("bytes", out JsonElement bytes) || bytes.GetInt64() != expected)
                {
                    throw new InvalidDataException($"its reply does not describe {size} x {size} float16 layers");
                }

                return (null, expected);
            }
            catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or OverflowException)
            {
                throw new InvalidDataException($"it sent a reply header that cannot be read ({e.Message})", e);
            }
        }

        private static byte[] RequestLine(int id, int size, Settings settings)
        {
            var buffer = new ArrayBufferWriter<byte>(1024);
            using (var w = new Utf8JsonWriter(buffer))
            {
                w.WriteStartObject();
                w.WriteNumber("id", id);
                w.WriteNumber("size", size);
                w.WriteStartObject("settings");
                foreach (KeyValuePair<string, JsonElement> kv in settings.Values)
                {
                    w.WritePropertyName(kv.Key);
                    kv.Value.WriteTo(w);
                }

                w.WriteEndObject();
                w.WriteEndObject();
            }

            buffer.Write("\n"u8);
            return buffer.WrittenSpan.ToArray();
        }

        private async Task<bool> WaitForExitAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return _process.HasExited;
            }
        }

        private async Task<byte[]> ReadHeadAsync(CancellationToken ct)
        {
            var line = new ArrayBufferWriter<byte>(256);
            byte[] one = new byte[1];
            while (true)
            {
                if (await _output.ReadAsync(one, ct).ConfigureAwait(false) == 0)
                {
                    throw new EndOfStreamException("its output ended");
                }

                if (one[0] == (byte)'\n')
                {
                    return line.WrittenSpan.ToArray();
                }

                if (line.WrittenCount >= MaxHeadBytes)
                {
                    throw new InvalidDataException("it sent a reply line longer than 64 KB");
                }

                line.Write(one);
            }
        }

        private async Task DrainAsync(Stream stderr)
        {
            byte[] buffer = new byte[1024];
            try
            {
                int n;
                while ((n = await stderr.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    lock (_tailGate)
                    {
                        int keep = Math.Min(_tailLength, TailBytes - n);
                        if (keep < _tailLength)
                        {
                            Buffer.BlockCopy(_tail, _tailLength - keep, _tail, 0, keep);
                        }

                        Buffer.BlockCopy(buffer, 0, _tail, keep, n);
                        _tailLength = keep + n;
                    }
                }
            }
            catch (IOException)
            {
                // The pipe broke: the worker is gone.
            }
            catch (ObjectDisposedException)
            {
                // Disposed while reading.
            }
        }
    }
}
