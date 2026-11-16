using PropGenerator.Desktop.Recipes;
using PropGenerator.Nature.Export;

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>
/// A recipe that builds as another does, only slower, counting how many of its builds run at once and which seeds it
/// built, and noting every call with its purpose, count wish, replace key and cancel token: to see whether a caller
/// lets builds overlap, which queue it asks for and when it stops them. It can also play an engine's queue dropping a
/// call (<see cref="Drop"/>), and answer the per-settings rules (<see cref="Far"/>, <see cref="Shown"/>).
/// </summary>
/// <param name="inner">The recipe it builds as.</param>
/// <param name="delay">How long each build takes, besides the real build.</param>
internal sealed class SlowRecipe(IMeshRecipe inner, TimeSpan delay) : IMeshRecipe
{
    private readonly Lock _gate = new();
    private readonly List<Call> _calls = [];
    private int _running;

    /// <summary>Gets the most builds that ran at once.</summary>
    public int MostAtOnce { get; private set; }

    /// <summary>Gets the seeds built, in the order the builds started.</summary>
    public List<uint> Built { get; } = [];

    /// <summary>Gets or sets how long each build (and each shapes call) takes, besides the real work.</summary>
    public TimeSpan Delay { get; set; } = delay;

    /// <summary>Gets or sets which calls are dropped as an engine's queue drops a replaced job: they throw <see cref="OperationCanceledException"/> with their token not cancelled.</summary>
    public Func<Call, bool>? Drop { get; set; }

    /// <summary>Gets or sets the far-away rule by settings; null answers as the inner recipe.</summary>
    public Func<Settings, bool>? Far { get; set; }

    /// <summary>Gets or sets the shown rows by settings; null answers as the inner recipe.</summary>
    public Func<Settings, IReadOnlyList<ShownRow>?>? Shown { get; set; }

    /// <summary>Gets every call so far, in the order they started.</summary>
    public IReadOnlyList<Call> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <inheritdoc/>
    public string Id => inner.Id;

    /// <inheritdoc/>
    public string Label => inner.Label;

    /// <inheritdoc/>
    public string ExportName => inner.ExportName;

    /// <inheritdoc/>
    public int Version => inner.Version;

    /// <inheritdoc/>
    public IReadOnlyList<ParamSpec> Specs => inner.Specs;

    /// <inheritdoc/>
    public IReadOnlyList<StartingPoint> StartingPoints => inner.StartingPoints;

    /// <inheritdoc/>
    public IReadOnlyList<double> DetailShares => inner.DetailShares;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> HiddenKeys(Settings s) => inner.HiddenKeys(s);

    /// <inheritdoc/>
    public Settings Adjust(Settings before, Settings after) => inner.Adjust(before, after);

    /// <inheritdoc/>
    public bool FarAway(Settings s) => Far is { } far ? far(s) : inner.FarAway(s);

    /// <inheritdoc/>
    public IReadOnlyList<ShownRow>? ShownRows(Settings s) => Shown is { } shown ? shown(s) : inner.ShownRows(s);

    /// <inheritdoc/>
    public ColouredMesh Build(Settings s, double detail, BuildPurpose purpose = BuildPurpose.View, CancellationToken cancel = default, string? replaces = null)
    {
        Start(new Call("build", purpose, detail, null, replaces, s, cancel));
        try
        {
            return inner.Build(s, detail, purpose, cancel, replaces);
        }
        finally
        {
            End();
        }
    }

    /// <inheritdoc/>
    public (ColouredMesh Mesh, int? FullFaces) BuildCounted(Settings s, double detail, bool countFull, BuildPurpose purpose, CancellationToken cancel, string? replaces = null)
    {
        Start(new Call("counted", purpose, detail, countFull, replaces, s, cancel));
        try
        {
            ColouredMesh mesh = inner.Build(s, detail, purpose, cancel, replaces);
            if (!countFull)
            {
                return (mesh, null);
            }

            ColouredMesh full = detail >= 1 ? mesh : inner.Build(s, 1, purpose, cancel, replaces);
            return (mesh, full.Indices.Length / 3);
        }
        finally
        {
            End();
        }
    }

    /// <inheritdoc/>
    public byte[] Glb(Settings s, string modelName, ExportOptions? options = null, CancellationToken cancel = default) => inner.Glb(s, modelName, options, cancel);

    /// <inheritdoc/>
    public IReadOnlyList<CollisionShape> Collision(Settings s, CancellationToken cancel = default, string? replaces = null)
    {
        var call = new Call("collision", BuildPurpose.Shapes, 1, null, replaces, s, cancel);
        lock (_gate)
        {
            _calls.Add(call);
        }

        Wait(call);
        return inner.Collision(s, cancel, replaces);
    }

    // Notes a build starting, waits its delay and drops it when told to.
    private void Start(Call call)
    {
        lock (_gate)
        {
            _calls.Add(call);
            _running++;
            MostAtOnce = Math.Max(MostAtOnce, _running);
            Built.Add(call.Settings.GetSeed("seed"));
        }

        try
        {
            Wait(call);
        }
        catch
        {
            End();
            throw;
        }
    }

    private void End()
    {
        lock (_gate)
        {
            _running--;
        }
    }

    // The delay, cut short when the call is cancelled; then a cancelled or dropped call throws.
    private void Wait(Call call)
    {
        if (Delay > TimeSpan.Zero)
        {
            _ = call.Cancel.WaitHandle.WaitOne(Delay);
        }

        call.Cancel.ThrowIfCancellationRequested();
        if (Drop?.Invoke(call) == true)
        {
            throw new OperationCanceledException("dropped by a newer job with the same key");
        }
    }

    /// <summary>One call to the recipe.</summary>
    /// <param name="Method">"build", "counted" (BuildCounted) or "collision".</param>
    /// <param name="Purpose">What it was for (Shapes for a collision call).</param>
    /// <param name="Detail">The share of full detail asked for.</param>
    /// <param name="CountFull">Whether a full count was asked for; null for a plain build or a collision call.</param>
    /// <param name="Replaces">The caller's replace key.</param>
    /// <param name="Settings">The settings.</param>
    /// <param name="Cancel">The caller's token.</param>
    internal sealed record Call(string Method, BuildPurpose Purpose, double Detail, bool? CountFull, string? Replaces, Settings Settings, CancellationToken Cancel);
}
