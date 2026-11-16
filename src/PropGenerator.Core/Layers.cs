namespace PropGenerator;

/// <summary>
/// The painter's seven tone layers for one square texture. Every array holds size x size values, row-major, rows
/// bottom-up (row 0 is the bottom of the texture), as the painter's worker sends them.
/// </summary>
/// <param name="size">Width and height in pixels.</param>
/// <param name="marks">The part of the light/dark value that contrast scales.</param>
/// <param name="rest">The rest of the light/dark value.</param>
/// <param name="drift">The raw red/grey colour drift.</param>
/// <param name="region">0 plank or long side, 1 cut end, 2 seam.</param>
/// <param name="baseMul">Multiplier on the region's base colour.</param>
/// <param name="seam">How deep the seam is, 0 to 1.</param>
/// <param name="aa">How far a plank pixel next to a seam blends toward the seam colour.</param>
public sealed class Layers(
    int size,
    float[] marks,
    float[] rest,
    float[] drift,
    float[] region,
    float[] baseMul,
    float[] seam,
    float[] aa)
{
    /// <summary>The layers in the order the worker sends them.</summary>
    public const int Count = 7;

    /// <summary>Gets the width and height in pixels.</summary>
    public int Size { get; } = size;

    /// <summary>Gets the part of the value that contrast scales.</summary>
    public float[] Marks { get; } = marks;

    /// <summary>Gets the rest of the value.</summary>
    public float[] Rest { get; } = rest;

    /// <summary>Gets the raw red/grey drift.</summary>
    public float[] Drift { get; } = drift;

    /// <summary>Gets the region: 0 plank or long side, 1 cut end, 2 seam.</summary>
    public float[] Region { get; } = region;

    /// <summary>Gets the multiplier on the region's base colour.</summary>
    public float[] BaseMul { get; } = baseMul;

    /// <summary>Gets the seam depth, 0 to 1.</summary>
    public float[] Seam { get; } = seam;

    /// <summary>Gets the blend of plank pixels toward the seam colour.</summary>
    public float[] Aa { get; } = aa;

    /// <summary>Reads the worker's payload: seven float16 layers, little-endian, one after the other.</summary>
    /// <param name="size">Width and height in pixels.</param>
    /// <param name="f16">The raw bytes.</param>
    /// <returns>The layers as floats.</returns>
    /// <exception cref="ArgumentException">The byte count is not 7 x size x size x 2.</exception>
    public static Layers FromWorkerBytes(int size, ReadOnlySpan<byte> f16)
    {
        long pixels = (long)size * size;
        if (size <= 0 || f16.Length != Count * pixels * 2)
        {
            throw new ArgumentException($"Expected {Count * pixels * 2} bytes for size {size}, got {f16.Length}.", nameof(f16));
        }

        float[][] arrays = new float[Count][];
        for (int i = 0; i < Count; i++)
        {
            float[] layer = new float[pixels];
            ReadOnlySpan<byte> src = f16.Slice((int)(i * pixels * 2), (int)(pixels * 2));
            for (int k = 0; k < layer.Length; k++)
            {
                layer[k] = (float)System.Buffers.Binary.BinaryPrimitives.ReadHalfLittleEndian(src.Slice(k * 2, 2));
            }

            arrays[i] = layer;
        }

        return new Layers(size, arrays[0], arrays[1], arrays[2], arrays[3], arrays[4], arrays[5], arrays[6]);
    }
}
