using System.Reflection;

namespace PropGenerator.Engine;

/// <summary>A script the engine runs when it loads, in order: the shims, then the bundle.</summary>
/// <param name="Name">The name script errors show (a file name).</param>
/// <param name="Source">The JavaScript.</param>
public sealed record EngineScript(string Name, string Source)
{
    /// <summary>Reads a script from a file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The script, named by the file's name.</returns>
    public static EngineScript FromFile(string path) => new(Path.GetFileName(path), File.ReadAllText(path));
}

/// <summary>
/// How an engine runs: its scripts, its memory caps and its time limit. The V8 heap is capped with
/// <c>MaxRuntimeHeapSize</c>, which throws a catchable error; the hard <c>MaxOldSpaceSize</c> is set well above it,
/// because passing that one aborts the whole process. ArrayBuffers (meshes, textures) live outside the heap and have
/// their own cap. An engine whose heap and buffers grow past <see cref="RecycleBytes"/> is rebuilt between jobs,
/// which bounds every cache inside the bundle without touching its code.
/// </summary>
public sealed record EngineOptions
{
    private static readonly Lazy<IReadOnlyList<EngineScript>> Embedded = new(ReadEmbedded);

    /// <summary>The scripts to load, in order; null for the bundle embedded in this assembly.</summary>
    public IReadOnlyList<EngineScript>? Scripts { get; init; }

    /// <summary>The V8 heap's soft cap: past it the job fails and the engine is rebuilt (512 MiB, from phase 1's measurements).</summary>
    public long MaxHeapBytes { get; init; } = 512L << 20;

    /// <summary>The V8 old space's hard cap, in MiB; kept well above <see cref="MaxHeapBytes"/>.</summary>
    public int MaxOldSpaceMiB { get; init; } = 1024;

    /// <summary>
    /// The cap on all ArrayBuffer memory (outside the heap): past it an allocation throws a RangeError. 512 MiB, because
    /// a tree piece exported at 2048 (bark, end grain and their reliefs) peaks near 290 MiB on a fresh runtime (v0.4.5);
    /// the recycle after a job (<see cref="RecycleBytes"/>) still keeps what a runtime holds between jobs at 256 MiB.
    /// </summary>
    public long MaxArrayBufferBytes { get; init; } = 512L << 20;

    /// <summary>Heap plus external memory past which the engine is rebuilt after a job.</summary>
    public long RecycleBytes { get; init; } = 256L << 20;

    /// <summary>
    /// The ArrayBuffer room an export is given (384 MiB): a runtime already holding more than
    /// <see cref="MaxArrayBufferBytes"/> less this is rebuilt before an export starts. Upstream's picture caches have
    /// no bound; one 2048 export of the biggest tree peaks at 141 MiB and of a stump near 290 MiB, about 310 MiB with a
    /// tree's pictures still cached (v0.4.5), so the recycle after a job (<see cref="RecycleBytes"/>) alone would let the
    /// next 2048 export run out of buffer room mid-way. 0 for none.
    /// </summary>
    public long ExportHeadroomBytes { get; init; } = 384L << 20;

    /// <summary>How often V8 checks the heap against <see cref="MaxHeapBytes"/>.</summary>
    public TimeSpan HeapSampleInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>The longest one call may take.</summary>
    public TimeSpan JobTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How long a cancelled call has to stop by itself before it is interrupted.</summary>
    public TimeSpan CancelGrace { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Where the bundle's console goes (level, text); null to drop it.</summary>
    public Action<string, string>? Log { get; init; }

    /// <summary>
    /// Smaller caps for the rules runtime, which only answers the panel's quick questions; a call has 1 s, so a stuck
    /// rule cannot hold the UI thread for long.
    /// </summary>
    public static EngineOptions ForRules { get; } = new()
    {
        MaxHeapBytes = 256L << 20,
        MaxOldSpaceMiB = 512,
        MaxArrayBufferBytes = 128L << 20,
        RecycleBytes = 160L << 20,
        JobTimeout = TimeSpan.FromSeconds(1),
    };

    /// <summary>Whether this assembly embeds the bundle (Resources/engine.js, written by tools/engine).</summary>
    public static bool HasEmbeddedBundle => Embedded.Value.Count > 0;

    /// <summary>The embedded scripts: Resources/shims.js when there is one, then Resources/engine.js.</summary>
    /// <returns>The scripts, or an empty list when the bundle is not embedded.</returns>
    public static IReadOnlyList<EngineScript> EmbeddedScripts() => Embedded.Value;

    private static List<EngineScript> ReadEmbedded()
    {
        var scripts = new List<EngineScript>();
        string? bundle = ReadResource("engine.js");
        if (bundle is null)
        {
            return scripts;
        }

        string? shims = ReadResource("shims.js");
        if (shims is not null)
        {
            scripts.Add(new EngineScript("shims.js", shims));
        }

        scripts.Add(new EngineScript("engine.js", bundle));
        return scripts;
    }

    /// <summary>Reads a text file embedded from Resources/.</summary>
    /// <param name="name">Its path under Resources/, e.g. engine-pin.json.</param>
    /// <returns>Its text, or null when it is not embedded.</returns>
    public static string? ReadResource(string name)
    {
        using Stream? stream = typeof(EngineOptions).Assembly.GetManifestResourceStream("PropGenerator.Engine.Resources/" + name);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Reads a file embedded from Resources/ as bytes.</summary>
    /// <param name="name">Its path under Resources/, e.g. thumbs/oak.png.</param>
    /// <returns>Its bytes, or null when it is not embedded.</returns>
    public static byte[]? ReadResourceBytes(string name)
    {
        using Stream? stream = typeof(EngineOptions).Assembly.GetManifestResourceStream("PropGenerator.Engine.Resources/" + name);
        if (stream is null)
        {
            return null;
        }

        using var bytes = new MemoryStream(stream.CanSeek ? (int)stream.Length : 0);
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    /// <summary>A preset's thumbnail as easy-props ships it (Resources/thumbs/&lt;id&gt;.png, 180 x 180).</summary>
    /// <param name="presetId">The preset's id.</param>
    /// <returns>The PNG file, or null when none is shipped for it.</returns>
    public static byte[]? Thumbnail(string presetId)
    {
        ArgumentNullException.ThrowIfNull(presetId);
        return presetId.Length == 0 || presetId.Contains('/', StringComparison.Ordinal)
            ? null
            : ReadResourceBytes("thumbs/" + presetId + ".png");
    }

    /// <summary>The names of every file embedded from Resources/ (such as the preset thumbnails).</summary>
    /// <returns>Paths under Resources/.</returns>
    public static IEnumerable<string> ResourceNames()
    {
        const string Prefix = "PropGenerator.Engine.Resources/";
        Assembly assembly = typeof(EngineOptions).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..]);
    }
}
