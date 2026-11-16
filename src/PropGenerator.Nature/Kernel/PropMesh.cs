namespace PropGenerator.Nature.Kernel;

/// <summary>
/// A mesh as easy-props builds it with three's BufferGeometry, Y-up: float32 attributes, an optional index and draw
/// groups. Positions are always there; the other attributes are null when the geometry does not have them, which
/// matters to <see cref="MeshOps.Merge"/> and <see cref="MeshOps.MergeVertices"/> as it does to three.
/// </summary>
public sealed class PropMesh
{
    /// <summary>Positions, three floats per vertex (three's 'position').</summary>
    public required float[] Position { get; set; }

    /// <summary>Normals, three floats per vertex (three's 'normal'), or null.</summary>
    public float[]? Normal { get; set; }

    /// <summary>Linear vertex colours, three floats per vertex (three's 'color'), or null.</summary>
    public float[]? Color { get; set; }

    /// <summary>Texture coordinates, two floats per vertex (three's 'uv'), or null.</summary>
    public float[]? Uv { get; set; }

    /// <summary>Second texture coordinates, two floats per vertex (three's 'uv1'; easy-props keeps wind in it), or null.</summary>
    public float[]? Uv1 { get; set; }

    /// <summary>
    /// easy-props 0.4.2's <c>rigid</c> attribute, one float per vertex, or null: a point marked 1 sways with the rest but
    /// never flutters, as a fruit swings whole. It is read by the wind and never written to a file.
    /// </summary>
    public float[]? Rigid { get; set; }

    /// <summary>
    /// Triangle corners as vertex numbers, or null for a non-indexed mesh (every three vertices a triangle). Only
    /// <see cref="SetIndex(uint[])"/> and <see cref="SetIndex(uint[], bool)"/> set it, so its width always travels with it.
    /// </summary>
    public uint[]? Index { get; private set; }

    /// <summary>
    /// True when three would hold the index as 32-bit numbers, false for 16-bit. GLTFExporter writes that width, so an
    /// export needs it. It cannot always be told from the numbers: easy-props builds some indices as Uint32Array
    /// whatever they hold, so the width is set with the index and travels with it.
    /// </summary>
    public bool IndexIsUint32 { get; private set; }

    /// <summary>three's <c>setIndex(array)</c> for a plain array: 32-bit if any number is 65535 or more, else 16-bit.</summary>
    public void SetIndex(uint[]? index)
    {
        Index = index;
        IndexIsUint32 = false;
        if (index is null)
        {
            return;
        }

        // three's arrayNeedsUint32 looks from the end, where large numbers usually are; 65535 itself is kept for
        // primitive restart, so it already needs 32 bits.
        for (int i = index.Length - 1; i >= 0; i--)
        {
            if (index[i] >= 65535)
            {
                IndexIsUint32 = true;
                return;
            }
        }
    }

    /// <summary>three's <c>setIndex(attribute)</c> for an index already held at a width: <paramref name="wide"/> (true for 32-bit).</summary>
    public void SetIndex(uint[] index, bool wide)
    {
        Index = index;
        IndexIsUint32 = wide;
    }

    /// <summary>Draw groups: a range of the index (or of the vertices, when not indexed) and its material number.</summary>
    public List<(int Start, int Count, int Material)> Groups { get; init; } = [];

    /// <summary>The number of vertices.</summary>
    public int VertexCount => Position.Length / 3;

    /// <summary>A deep copy, as three's geometry.clone() for these attributes.</summary>
    public PropMesh Clone() => new()
    {
        Position = (float[])Position.Clone(),
        Normal = (float[]?)Normal?.Clone(),
        Color = (float[]?)Color?.Clone(),
        Uv = (float[]?)Uv?.Clone(),
        Uv1 = (float[]?)Uv1?.Clone(),
        Rigid = (float[]?)Rigid?.Clone(),
        Index = (uint[]?)Index?.Clone(),
        IndexIsUint32 = IndexIsUint32,
        Groups = [.. Groups],
    };
}
