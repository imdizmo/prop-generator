namespace PropGenerator.Desktop.Tests.Support;

/// <summary>A painter that paints nothing: it notes every request and answers with blank layers of that size.</summary>
internal sealed class FakePainter : IPainter
{
    private readonly Lock _gate = new();
    private readonly List<(int Size, Settings Settings)> _requests = [];

    /// <inheritdoc/>
    public event Action<string>? Failed;

    /// <summary>Gets or sets the answer to a request; blank layers of the asked size when null.</summary>
    public Func<int, Settings, Task<Layers?>>? Reply { get; set; }

    /// <summary>Gets a copy of every request so far, oldest first.</summary>
    public IReadOnlyList<(int Size, Settings Settings)> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Makes blank layers.</summary>
    /// <param name="size">Width and height.</param>
    /// <returns>Seven layers of zeros.</returns>
    public static Layers Blank(int size)
    {
        float[] Layer() => new float[size * size];
        return new Layers(size, Layer(), Layer(), Layer(), Layer(), Layer(), Layer(), Layer());
    }

    /// <inheritdoc/>
    public Task<Layers?> PaintAsync(int size, Settings settings, CancellationToken ct)
    {
        lock (_gate)
        {
            _requests.Add((size, settings));
        }

        return Reply?.Invoke(size, settings) ?? Task.FromResult<Layers?>(Blank(size));
    }

    /// <summary>Raises <see cref="Failed"/> on a thread-pool thread, as the worker client does.</summary>
    /// <param name="message">The message.</param>
    public void FailFromThreadPool(string message) => Task.Run(() => Failed?.Invoke(message)).Wait();
}
