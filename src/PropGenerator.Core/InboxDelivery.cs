namespace PropGenerator;

/// <summary>
/// One delivery to Groundwork's inbox while it is written: each GLB goes into the hidden partial folder as it is added
/// (so one model at a time is held in memory), then <see cref="Complete"/> writes send.json and renames the folder,
/// which is when Groundwork sees it. Disposed without completing, the partial folder is removed.
/// </summary>
public sealed class InboxDelivery : IDisposable
{
    private readonly GroundworkInbox _inbox;
    private readonly long _maxFileBytes;
    private readonly List<InboxKind> _kinds = [];
    private bool _done;

    internal InboxDelivery(GroundworkInbox inbox, string id, string partialFolder, long maxFileBytes)
    {
        _inbox = inbox;
        Id = id;
        PartialFolder = partialFolder;
        _maxFileBytes = maxFileBytes;
    }

    /// <summary>Gets the delivery's id: the name its folder gets in the inbox.</summary>
    public string Id { get; }

    /// <summary>Gets the hidden folder the delivery is written into.</summary>
    public string PartialFolder { get; }

    /// <summary>Gets the models added so far.</summary>
    public IReadOnlyList<InboxKind> Kinds => _kinds;

    /// <summary>
    /// Adds a model: checks it against the inbox's rules (a plain model name not sent twice, a known group, a size range,
    /// a self-contained GLB of at most 512 MB, at most 200 models), then writes its GLB into the partial folder.
    /// </summary>
    /// <param name="kind">The model and how the drawer shows it.</param>
    /// <param name="glb">The GLB's bytes, textures embedded.</param>
    /// <exception cref="InboxException">The model breaks a rule; nothing was written for it.</exception>
    /// <exception cref="IOException">The file could not be written.</exception>
    public void Add(InboxKind kind, byte[] glb)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(glb);
        ObjectDisposedException.ThrowIf(_done, this);
        if (_kinds.Count >= GroundworkInbox.MaxKinds)
        {
            throw new InboxException($"Groundwork takes at most {GroundworkInbox.MaxKinds} models in one delivery.");
        }

        if (!GroundworkInbox.Plain(kind.Model) || !GroundworkInbox.Plain(kind.Model + ".glb"))
        {
            throw new InboxException($"\"{kind.Model}\" is not a plain file name (no folders, not hidden), so it cannot be sent to Groundwork.");
        }

        if (_kinds.Any(k => k.Model.Equals(kind.Model, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InboxException($"{kind.Model} is in the delivery twice.");
        }

        if (!GroundworkInbox.Categories.Contains(kind.Category, StringComparer.Ordinal))
        {
            throw new InboxException($"\"{kind.Category}\" is not one of Groundwork's groups ({string.Join(", ", GroundworkInbox.Categories)}).");
        }

        if (kind.Scale is { } s && (s.Length != 2 || !float.IsFinite(s[0]) || !float.IsFinite(s[1]) || s[0] <= 0 || s[1] < s[0] || s[1] > 100))
        {
            throw new InboxException($"{kind.Model}: the size range must be two sizes, 0 < min <= max <= 100.");
        }

        if (glb.Length > _maxFileBytes)
        {
            throw new InboxException($"{kind.Model}.glb is {glb.Length / (1024.0 * 1024.0):0.#} MB; Groundwork takes at most {_maxFileBytes / (1024 * 1024)} MB.");
        }

        if (glb.Length < 12 || !glb.AsSpan(0, 4).SequenceEqual("glTF"u8))
        {
            throw new InboxException($"{kind.Model}.glb is not a glTF binary.");
        }

        if (GlbContents.NotSelfContained(glb) is { } loose)
        {
            throw new InboxException($"{kind.Model}.glb does not stand alone: {loose}.");
        }

        string label = kind.Label.Trim();
        if (label.Length > GroundworkInbox.MaxLabel)
        {
            label = label[..GroundworkInbox.MaxLabel].TrimEnd();
        }

        File.WriteAllBytes(Path.Combine(PartialFolder, kind.Model + ".glb"), glb);
        _kinds.Add(kind with { Label = label.Length > 0 ? label : kind.Model });
    }

    /// <summary>Writes send.json and renames the folder to its id, which hands the delivery to Groundwork.</summary>
    /// <returns>The delivery's folder in the inbox.</returns>
    /// <exception cref="InboxException">Nothing was added.</exception>
    /// <exception cref="IOException">The folder could not be written or renamed.</exception>
    public string Complete()
    {
        ObjectDisposedException.ThrowIf(_done, this);
        if (_kinds.Count == 0)
        {
            throw new InboxException("Nothing to send: a delivery needs at least one model.");
        }

        File.WriteAllText(Path.Combine(PartialFolder, "send.json"), GroundworkInbox.SendJson(_kinds));
        string final = Path.Combine(_inbox.Folder, Id);
        Directory.Move(PartialFolder, final);
        _done = true;
        return final;
    }

    /// <summary>Removes the partial folder of a delivery never completed.</summary>
    public void Dispose()
    {
        if (_done)
        {
            return;
        }

        _done = true;
        try
        {
            if (Directory.Exists(PartialFolder))
            {
                Directory.Delete(PartialFolder, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Hidden from Groundwork either way; left behind.
        }
    }
}
