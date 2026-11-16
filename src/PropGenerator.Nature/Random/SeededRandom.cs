// Ported from easy-props frontend/src/assets/shared/random.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Random;

/// <summary>
/// The deterministic random source every generator draws from: mulberry32, drawing the same numbers as easy-props'
/// createSeededRandom for the same seed. Not thread safe; use one per generator run.
/// </summary>
public sealed class SeededRandom
{
    private const double Uint32Range = 4294967296.0;

    private uint _state;

    /// <summary>Starts the stream. The seed is read as JavaScript's <c>seed &gt;&gt;&gt; 0</c> reads it.</summary>
    public SeededRandom(double seed)
    {
        _state = JsNumber.ToUint32(seed);
    }

    /// <summary>The next number in [0, 1).</summary>
    public double Next()
    {
        unchecked
        {
            _state += 0x6d2b79f5u;
            uint t = _state;
            t = Imul(t ^ (t >> 15), t | 1u);
            t ^= t + Imul(t ^ (t >> 7), t | 61u);
            return (t ^ (t >> 14)) / Uint32Range;
        }
    }

    /// <summary>A number in [min, max).</summary>
    public double Range(double min, double max) => min + (Next() * (max - min));

    /// <summary>An integer in [min, max], both bounds included.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Mirrors randomInt in easy-props.")]
    public int Int(int min, int max) => (int)Math.Floor(Range(min, (double)max + 1));

    /// <summary>One element chosen uniformly; uses one draw.</summary>
    public T Pick<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Cannot pick from an empty collection");
        }

        return items[(int)Math.Floor(Next() * items.Count)];
    }

    /// <summary>
    /// A shuffled copy (Fisher-Yates from the last index down, one draw per index above 0); the input is not changed.
    /// </summary>
    public T[] Shuffled<T>(IReadOnlyList<T> items)
    {
        var result = new T[items.Count];
        for (int k = 0; k < result.Length; k++)
        {
            result[k] = items[k];
        }

        for (int i = result.Length - 1; i > 0; i--)
        {
            int j = (int)Math.Floor(Next() * (i + 1));
            (result[i], result[j]) = (result[j], result[i]);
        }

        return result;
    }

    /// <summary>Math.imul on the bits of two unsigned values, read back as unsigned.</summary>
    internal static uint Imul(uint a, uint b) => unchecked(a * b);
}
