namespace PropGenerator.Desktop.Props;

/// <summary>A small cache that keeps at most a set number of entries, letting the one used longest ago go first.</summary>
/// <typeparam name="TKey">The key.</typeparam>
/// <typeparam name="TValue">The value.</typeparam>
/// <param name="cap">The most entries kept.</param>
internal sealed class LruCache<TKey, TValue>(int cap)
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = [];
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();

    /// <summary>Gets how many entries are kept now.</summary>
    internal int Count => _map.Count;

    /// <summary>Finds an entry and marks it used.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, when found.</param>
    /// <returns>True when found.</returns>
    internal bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out LinkedListNode<(TKey Key, TValue Value)>? node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Keeps an entry (in place of one with the same key), letting the oldest go when full.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void Put(TKey key, TValue value)
    {
        if (_map.Remove(key, out LinkedListNode<(TKey Key, TValue Value)>? old))
        {
            _order.Remove(old);
        }

        _map[key] = _order.AddFirst((key, value));
        while (_map.Count > cap && _order.Last is { } last)
        {
            _order.RemoveLast();
            _map.Remove(last.Value.Key);
        }
    }
}
