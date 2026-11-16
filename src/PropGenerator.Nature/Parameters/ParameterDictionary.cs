using System.Collections;

namespace PropGenerator.Nature.Parameters;

/// <summary>
/// The values of one parameter object, in the order easy-props' schema lists them. Each value is a double or a string;
/// a component's parameters may also hold a nested parameter object (the cut rock's body holds the whole rock).
/// </summary>
public sealed class ParameterDictionary : IReadOnlyDictionary<string, object>
{
    private readonly List<string> keys = [];
    private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

    /// <summary>Adds a value at the end. A key that is already there keeps its place and takes the new value.</summary>
    internal void Set(string key, object value)
    {
        if (!values.ContainsKey(key))
        {
            keys.Add(key);
        }

        values[key] = value;
    }

    /// <summary>The keys, in order.</summary>
    public IEnumerable<string> Keys => keys;

    /// <summary>The values, in key order.</summary>
    public IEnumerable<object> Values => keys.Select(key => values[key]);

    /// <inheritdoc />
    public int Count => keys.Count;

    /// <inheritdoc />
    public object this[string key] => values[key];

    /// <summary>A number parameter. Throws if the key is missing or holds text.</summary>
    public double Number(string key) => (double)values[key];

    /// <summary>A text parameter. Throws if the key is missing or holds a number.</summary>
    public string Text(string key) => (string)values[key];

    /// <inheritdoc />
    public bool ContainsKey(string key) => values.ContainsKey(key);

    /// <inheritdoc />
    public bool TryGetValue(string key, out object value) => values.TryGetValue(key, out value!);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() =>
        keys.Select(key => new KeyValuePair<string, object>(key, values[key])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
