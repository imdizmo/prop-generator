using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PropGenerator;

/// <summary>
/// Groundwork's inbox, version 1: the folder other programs send models to, where Groundwork's open map takes them in.
/// One delivery is one folder, written hidden as <c>inbox/.&lt;id&gt;.partial/</c> and renamed to <c>inbox/&lt;id&gt;/</c>
/// once whole: <c>send.json</c> and the self-contained <c>.glb</c> files, flat. Groundwork answers in
/// <c>inbox/.results/&lt;id&gt;.json</c>.
/// </summary>
public sealed class GroundworkInbox
{
    /// <summary>The source every delivery is sent as: Groundwork keeps its models in a folder of this name.</summary>
    public const string Source = "Prop Generator";

    /// <summary>The most models one delivery may hold.</summary>
    public const int MaxKinds = 200;

    /// <summary>The largest GLB Groundwork takes: 512 MB.</summary>
    public const long MaxFileBytes = 512L * 1024 * 1024;

    /// <summary>The longest label Groundwork shows; a longer one would be replaced by the model's name.</summary>
    public const int MaxLabel = 80;

    /// <summary>What is said when no answer came in time: the delivery waits for a map to be opened.</summary>
    public const string WaitingText = "Waiting in Groundwork's inbox (open a map in Groundwork)";

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly long _maxFileBytes;

    /// <summary>Uses the inbox of this Groundwork data folder.</summary>
    /// <param name="dataDir">Groundwork's data folder (see <see cref="DefaultDataDir()"/>); the inbox is its <c>inbox</c>.</param>
    /// <param name="maxFileBytes">The largest GLB taken (the tests make it small).</param>
    public GroundworkInbox(string dataDir, long maxFileBytes = MaxFileBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDir);
        DataDir = Path.GetFullPath(dataDir);
        _maxFileBytes = maxFileBytes;
    }

    /// <summary>Gets the groups a kind can be sent in, as Groundwork's drawer has them.</summary>
    public static IReadOnlyList<string> Categories { get; } = ["Trees", "Rocks", "Bushes", "Plants", "Props", "Buildings", "Markers", "Other"];

    /// <summary>Gets Groundwork's data folder.</summary>
    public string DataDir { get; }

    /// <summary>Gets the inbox folder.</summary>
    public string Folder => Path.Combine(DataDir, "inbox");

    /// <summary>
    /// Groundwork's data folder on this machine: on Windows %LOCALAPPDATA%\Groundwork; elsewhere
    /// $XDG_DATA_HOME/Groundwork, or ~/.local/share/Groundwork when XDG_DATA_HOME is unset (or not absolute).
    /// </summary>
    /// <returns>The folder (not made).</returns>
    public static string DefaultDataDir() => DefaultDataDir(
        Environment.GetEnvironmentVariable,
        OperatingSystem.IsWindows(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>Groundwork's data folder from these facts (for the tests).</summary>
    /// <param name="env">Reads an environment variable.</param>
    /// <param name="windows">Whether the machine runs Windows.</param>
    /// <param name="home">The user's home folder.</param>
    /// <param name="localAppData">Windows' local application data folder.</param>
    /// <returns>The folder.</returns>
    public static string DefaultDataDir(Func<string, string?> env, bool windows, string home, string localAppData)
    {
        ArgumentNullException.ThrowIfNull(env);
        if (windows)
        {
            return Path.Combine(localAppData, "Groundwork");
        }

        string? xdg = env("XDG_DATA_HOME");
        return !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
            ? Path.Combine(xdg, "Groundwork")
            : Path.Combine(home, ".local", "share", "Groundwork");
    }

    /// <summary>A new delivery id: <c>propgen-&lt;yyyyMMdd&gt;-&lt;HHmmss&gt;-&lt;4 hex&gt;</c>.</summary>
    /// <param name="now">The time it is sent.</param>
    /// <returns>The id, a plain name.</returns>
    public static string NewId(DateTime now) =>
        string.Create(CultureInfo.InvariantCulture, $"propgen-{now:yyyyMMdd}-{now:HHmmss}-{Random.Shared.Next(0x10000):x4}");

    /// <summary>Starts a delivery: makes the inbox if missing and the hidden partial folder in it.</summary>
    /// <returns>The delivery; dispose it, and complete it to send it.</returns>
    /// <exception cref="IOException">The inbox could not be written.</exception>
    public InboxDelivery Begin()
    {
        Directory.CreateDirectory(Folder);
        for (int tries = 0; ; tries++)
        {
            string id = NewId(DateTime.Now);
            string partial = Path.Combine(Folder, "." + id + ".partial");
            if (!Directory.Exists(partial) && !Directory.Exists(Path.Combine(Folder, id)) && !File.Exists(ResultPath(id)))
            {
                Directory.CreateDirectory(partial);
                return new InboxDelivery(this, id, partial, _maxFileBytes);
            }

            if (tries > 50)
            {
                throw new IOException("No free delivery name could be found in Groundwork's inbox.");
            }
        }
    }

    /// <summary>Reads Groundwork's answer to a delivery.</summary>
    /// <param name="json">The text of <c>inbox/.results/&lt;id&gt;.json</c>.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="InboxException">The text is not an answer.</exception>
    public static InboxAnswer ReadAnswer(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o || o["ok"] is not JsonValue okValue || !okValue.TryGetValue(out bool ok))
            {
                throw new InboxException("Groundwork's answer is not an object with \"ok\".");
            }

            string? map = (o["map"] as JsonValue)?.TryGetValue(out string? m) == true ? m : null;
            string[] models = o["models"] is JsonArray list
                ? [.. list.OfType<JsonValue>().Select(v => v.TryGetValue(out string? s) ? s : null).OfType<string>()]
                : [];
            string? error = (o["error"] as JsonValue)?.TryGetValue(out string? e) == true ? e : null;
            return new InboxAnswer(ok, Waiting: false, map, models, ok ? null : error ?? "Groundwork gave no reason.");
        }
        catch (JsonException e)
        {
            throw new InboxException($"Groundwork's answer could not be read: {e.Message}");
        }
    }

    /// <summary>
    /// Waits for Groundwork's answer to a delivery: looks for <c>inbox/.results/&lt;id&gt;.json</c> every tenth of a
    /// second until the timeout. An answer still being written is read again at the next look.
    /// </summary>
    /// <param name="id">The delivery's id.</param>
    /// <param name="timeout">How long to wait (a few seconds).</param>
    /// <param name="cancel">Stops the wait.</param>
    /// <returns>The answer, or a waiting answer (<see cref="InboxAnswer.Waiting"/>) when none came in time.</returns>
    /// <exception cref="OperationCanceledException">The wait was stopped.</exception>
    public async Task<InboxAnswer> WaitForAnswerAsync(string id, TimeSpan timeout, CancellationToken cancel = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        string path = ResultPath(id);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            if (TryRead(path) is { } answer)
            {
                return answer;
            }

            TimeSpan left = timeout - clock.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                return InboxAnswer.StillWaiting;
            }

            await Task.Delay(left < TimeSpan.FromMilliseconds(100) ? left : TimeSpan.FromMilliseconds(100), cancel).ConfigureAwait(false);
        }
    }

    /// <summary>Whether a name is one Groundwork takes for a file: plain, no folder in it, not hidden, no device name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>True when plain.</returns>
    internal static bool Plain(string name) =>
        name.Length is > 0 and <= 120 && name.Trim() == name && !name.StartsWith('.')
        && name.IndexOfAny([.. Path.GetInvalidFileNameChars(), '/', '\\', ':', '*', '?', '"', '<', '>', '|']) < 0
        && !IsDeviceName(name);

    /// <summary>Writes send.json for these kinds.</summary>
    /// <param name="kinds">The kinds, in order.</param>
    /// <returns>The text.</returns>
    internal static string SendJson(IEnumerable<InboxKind> kinds)
    {
        var list = new JsonArray();
        foreach (InboxKind k in kinds)
        {
            float[] scale = k.Scale ?? [1f, 1f];
            list.Add(new JsonObject
            {
                ["file"] = k.Model + ".glb",
                ["model"] = k.Model,
                ["label"] = k.Label,
                ["category"] = k.Category,
                ["scale"] = new JsonArray(scale[0], scale[1]),
            });
        }

        var send = new JsonObject { ["version"] = 1, ["source"] = Source, ["kinds"] = list };
        return send.ToJsonString(Pretty);
    }

    private string ResultPath(string id) => Path.Combine(Folder, ".results", id + ".json");

    private static InboxAnswer? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? ReadAnswer(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is InboxException or IOException or UnauthorizedAccessException)
        {
            // Not whole yet, or held by the writer: looked at again.
            return null;
        }
    }

    // CON, PRN, AUX, NUL, COM1-9 and LPT1-9, with or without an extension: names Windows keeps for devices.
    private static bool IsDeviceName(string name)
    {
        string stem = name.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL"
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9');
    }
}
