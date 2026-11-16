namespace PropGenerator.Nature.Textures;

/// <summary>
/// The textures built so far, by key, keeping the <see cref="Capacity"/> most recently used. Easy-props keeps every
/// bark print, relief and file relief it makes (54 + 6 + 12 at up to 1 MB each); this keeps 8 and builds again when an
/// evicted one is asked for, which gives the same bytes.
/// Not thread safe: callers are single threaded for now. Guard it with a lock before sharing it between threads. (The
/// texture builders themselves keep no state, so they may run on any thread.)
/// </summary>
public sealed class TextureCache
{
    /// <summary>How many textures are kept.</summary>
    public const int Capacity = 8;

    private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, RgbaImage>>> _byKey = new(StringComparer.Ordinal);

    /// <summary>Most recently used first.</summary>
    private readonly LinkedList<KeyValuePair<string, RgbaImage>> _order = new();

    /// <summary>How many textures are held, never more than <see cref="Capacity"/>.</summary>
    public int Count => _byKey.Count;

    /// <summary>
    /// The texture kept under <paramref name="key"/>, the same instance every time while it is kept; otherwise it is
    /// made with <paramref name="make"/> and kept, dropping the least recently used one when the cache is full.
    /// </summary>
    public RgbaImage Get(string key, Func<RgbaImage> make)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(make);

        if (_byKey.TryGetValue(key, out LinkedListNode<KeyValuePair<string, RgbaImage>>? node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Value;
        }

        RgbaImage made = make() ?? throw new InvalidOperationException($"Making texture '{key}' gave nothing");

        if (_byKey.Count >= Capacity)
        {
            LinkedListNode<KeyValuePair<string, RgbaImage>> oldest = _order.Last!;
            _order.RemoveLast();
            _byKey.Remove(oldest.Value.Key);
        }

        _byKey.Add(key, _order.AddFirst(new KeyValuePair<string, RgbaImage>(key, made)));
        return made;
    }
}
