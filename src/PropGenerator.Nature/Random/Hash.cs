// Ported from easy-props frontend/src/assets/shared/random.ts (hashSeed) and
// frontend/src/assets/trees/painted/plan.ts (hashKey) (MIT, © 2026 AHS12).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Random;

/// <summary>The two hashes that turn names and numbers into seeds, bit for bit as easy-props computes them.</summary>
public static class Hash
{
    /// <summary>
    /// hashSeed: combines numbers into one seed. Each part is truncated and wrapped to 32 bits as JavaScript's
    /// bitwise operators do, so fractions, negatives and values above 2^32 behave as they do there.
    /// </summary>
    public static uint Seed(params double[] parts)
    {
        unchecked
        {
            uint hash = 0x811c9dc5u;
            foreach (double part in parts)
            {
                uint value = SeededRandom.Imul(JsNumber.ToUint32(Math.Truncate(part)) ^ 0x9e3779b9u, 0x85ebca6bu);
                value ^= value >> 13;
                value = SeededRandom.Imul(value, 0xc2b2ae35u);
                value ^= value >> 16;
                hash = SeededRandom.Imul(hash ^ value, 0x27d4eb2fu);
            }

            return hash;
        }
    }

    /// <summary>hashKey: FNV-1a over the UTF-16 code units of a part's name.</summary>
    public static uint Key(string key)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char c in key)
            {
                hash = SeededRandom.Imul(hash ^ c, 16777619u);
            }

            return hash;
        }
    }
}
