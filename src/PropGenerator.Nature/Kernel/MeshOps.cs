// Ported from three.js r186 src/core/BufferGeometry.js, src/core/BufferAttribute.js and
// examples/jsm/utils/BufferGeometryUtils.js (MIT, © 2010-2026 three.js authors).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Kernel;

/// <summary>
/// three's mesh operations on a <see cref="PropMesh"/>, giving the same float32 values: arithmetic in doubles in three's
/// order, rounded to float32 exactly where three stores into its Float32Array attributes.
/// </summary>
public static class MeshOps
{
    /// <summary>JavaScript's Number.EPSILON (2^-52); not .NET's double.Epsilon, which is the smallest subnormal.</summary>
    private const double NumberEpsilon = 2.220446049250313e-16;

    /// <summary>
    /// three's computeVertexNormals. An indexed mesh adds each triangle's (unnormalised) face normal into its corners'
    /// float32 normals, rounding after every add; a non-indexed one writes the face normal to its three corners. Then
    /// every normal is normalised. An existing normal attribute of the right size is reset to zero and reused.
    /// </summary>
    public static void ComputeVertexNormals(PropMesh mesh)
    {
        float[] positionAttribute = mesh.Position;
        int count = positionAttribute.Length / 3;
        float[]? normalAttribute = mesh.Normal;

        if (normalAttribute is null || normalAttribute.Length / 3 != count)
        {
            normalAttribute = new float[count * 3];
            mesh.Normal = normalAttribute;
        }
        else
        {
            Array.Clear(normalAttribute);
        }

        Vec3d pA = new(), pB = new(), pC = new();
        Vec3d nA = new(), nB = new(), nC = new();
        Vec3d cb = new(), ab = new();

        if (mesh.Index is { } index)
        {
            for (int i = 0, il = index.Length; i < il; i += 3)
            {
                int vA = (int)index[i];
                int vB = (int)index[i + 1];
                int vC = (int)index[i + 2];

                pA.FromBufferAttribute(positionAttribute, vA);
                pB.FromBufferAttribute(positionAttribute, vB);
                pC.FromBufferAttribute(positionAttribute, vC);

                cb.SubVectors(pC, pB);
                ab.SubVectors(pA, pB);
                cb.Cross(ab);

                // All three are read before any is written: a triangle naming one vertex twice keeps the last write.
                nA.FromBufferAttribute(normalAttribute, vA);
                nB.FromBufferAttribute(normalAttribute, vB);
                nC.FromBufferAttribute(normalAttribute, vC);

                nA.Add(cb);
                nB.Add(cb);
                nC.Add(cb);

                SetXyz(normalAttribute, vA, nA);
                SetXyz(normalAttribute, vB, nB);
                SetXyz(normalAttribute, vC, nC);
            }
        }
        else
        {
            for (int i = 0, il = count; i < il; i += 3)
            {
                pA.FromBufferAttribute(positionAttribute, i);
                pB.FromBufferAttribute(positionAttribute, i + 1);
                pC.FromBufferAttribute(positionAttribute, i + 2);

                cb.SubVectors(pC, pB);
                ab.SubVectors(pA, pB);
                cb.Cross(ab);

                SetXyz(normalAttribute, i, cb);
                SetXyz(normalAttribute, i + 1, cb);
                SetXyz(normalAttribute, i + 2, cb);
            }
        }

        NormalizeNormals(normalAttribute);
    }

    /// <summary>
    /// three's toNonIndexed: a new mesh with every attribute copied out per index entry and the groups kept. A mesh that
    /// has no index is returned itself, as three returns <c>this</c>.
    /// </summary>
    public static PropMesh ToNonIndexed(PropMesh mesh)
    {
        if (mesh.Index is not { } indices)
        {
            return mesh;
        }

        var result = new PropMesh
        {
            Position = Gather(mesh.Position, 3, indices),
            Normal = mesh.Normal is null ? null : Gather(mesh.Normal, 3, indices),
            Color = mesh.Color is null ? null : Gather(mesh.Color, 3, indices),
            Uv = mesh.Uv is null ? null : Gather(mesh.Uv, 2, indices),
            Uv1 = mesh.Uv1 is null ? null : Gather(mesh.Uv1, 2, indices),
            Rigid = mesh.Rigid is null ? null : Gather(mesh.Rigid, 1, indices),
        };
        result.Groups.AddRange(mesh.Groups);
        return result;
    }

    /// <summary>
    /// three's geometry.applyMatrix4, in place: each position goes through the matrix with the w divide and is stored as
    /// float32; each normal goes through the normal matrix, is normalised and is stored as float32. Returns the mesh.
    /// </summary>
    public static PropMesh ApplyMatrix4(PropMesh mesh, Mat4d matrix)
    {
        var vector = new Vec3d();
        float[] position = mesh.Position;
        for (int i = 0, l = position.Length / 3; i < l; i++)
        {
            vector.FromBufferAttribute(position, i).ApplyMatrix4(matrix);
            SetXyz(position, i, vector);
        }

        if (mesh.Normal is { } normal)
        {
            var normalMatrix = new Mat3d().GetNormalMatrix(matrix);
            for (int i = 0, l = normal.Length / 3; i < l; i++)
            {
                vector.FromBufferAttribute(normal, i).ApplyNormalMatrix(normalMatrix);
                SetXyz(normal, i, vector);
            }
        }

        return mesh;
    }

    /// <summary>three's geometry.translate: <see cref="ApplyMatrix4"/> with a translation, so normals are renormalised too.</summary>
    public static PropMesh Translate(PropMesh mesh, double x, double y, double z) =>
        ApplyMatrix4(mesh, new Mat4d().MakeTranslation(x, y, z));

    /// <summary>three's geometry.scale: <see cref="ApplyMatrix4"/> with a scale.</summary>
    public static PropMesh Scale(PropMesh mesh, double x, double y, double z) =>
        ApplyMatrix4(mesh, new Mat4d().MakeScale(x, y, z));

    /// <summary>
    /// three's BufferGeometryUtils.mergeGeometries: attributes concatenated, indices offset by the vertices before them.
    /// With <paramref name="useGroups"/>, one group per input (its index count, or vertex count when not indexed) with
    /// the input's position as its material; the inputs' own groups are dropped either way. Null, as three, when the
    /// inputs are not all indexed or all not, or do not have the same attributes.
    /// </summary>
    public static PropMesh? Merge(IReadOnlyList<PropMesh> meshes, bool useGroups)
    {
        ArgumentOutOfRangeException.ThrowIfZero(meshes.Count);

        PropMesh first = meshes[0];
        bool isIndexed = first.Index is not null;
        var merged = new PropMesh { Position = [] };
        int offset = 0;

        for (int i = 0; i < meshes.Count; i++)
        {
            PropMesh mesh = meshes[i];
            if (isIndexed != (mesh.Index is not null) || !SameAttributes(first, mesh))
            {
                return null;
            }

            if (useGroups)
            {
                int count = isIndexed ? mesh.Index!.Length : mesh.VertexCount;
                merged.Groups.Add((offset, count, i));
                offset += count;
            }
        }

        if (isIndexed)
        {
            var mergedIndex = new List<uint>();
            uint indexOffset = 0;
            foreach (PropMesh mesh in meshes)
            {
                foreach (uint index in mesh.Index!)
                {
                    mergedIndex.Add(index + indexOffset);
                }

                indexOffset += (uint)mesh.VertexCount;
            }

            // three's setIndex of a plain array, which also picks the index's width.
            merged.SetIndex([.. mergedIndex]);
        }

        merged.Position = Concat(meshes, m => m.Position)!;
        merged.Normal = Concat(meshes, m => m.Normal);
        merged.Color = Concat(meshes, m => m.Color);
        merged.Uv = Concat(meshes, m => m.Uv);
        merged.Uv1 = Concat(meshes, m => m.Uv1);
        return merged;
    }

    /// <summary>
    /// three's BufferGeometryUtils.mergeVertices: vertices whose every attribute value agrees to within
    /// <paramref name="tolerance"/> become one, in first-seen order, and the result is indexed. three keys a vertex by
    /// the string of <c>Math.trunc(value * 10^log10(1 / tolerance) + tolerance / 2 * that)</c> per value; here each
    /// truncated value keys by its bits as a long, with -0 read as 0 and every NaN as one NaN, which is exactly when
    /// JavaScript prints two numbers the same. Groups are kept.
    /// </summary>
    public static PropMesh MergeVertices(PropMesh mesh, double tolerance = 1e-4)
    {
        tolerance = Math.Max(tolerance, NumberEpsilon);

        var hashToIndex = new Dictionary<long[], uint>(VertexKeyComparer.Instance);
        uint[]? indices = mesh.Index;
        int vertexCount = indices?.Length ?? mesh.VertexCount;
        uint nextIndex = 0;

        // The attributes, each with its item size, in a fixed order (equal keys do not depend on the order).
        var attributes = new List<(float[] Source, int ItemSize)> { (mesh.Position, 3) };
        AddIfPresent(attributes, mesh.Normal, 3);
        AddIfPresent(attributes, mesh.Color, 3);
        AddIfPresent(attributes, mesh.Uv, 2);
        AddIfPresent(attributes, mesh.Uv1, 2);
        int keyLength = attributes.Sum(a => a.ItemSize);
        float[][] tmpAttributes = [.. attributes.Select(a => new float[a.Source.Length])];
        uint[] newIndices = new uint[vertexCount];

        double halfTolerance = tolerance * 0.5;
        double exponent = JsMath.Log10(1 / tolerance);
        double hashMultiplier = JsMath.Pow(10, exponent);
        double hashAdditive = halfTolerance * hashMultiplier;

        for (int i = 0; i < vertexCount; i++)
        {
            int index = indices is null ? i : (int)indices[i];

            long[] hash = new long[keyLength];
            int k = 0;
            foreach ((float[] source, int itemSize) in attributes)
            {
                for (int c = 0; c < itemSize; c++)
                {
                    hash[k++] = KeyOf(Math.Truncate((source[(index * itemSize) + c] * hashMultiplier) + hashAdditive));
                }
            }

            if (hashToIndex.TryGetValue(hash, out uint existing))
            {
                newIndices[i] = existing;
            }
            else
            {
                for (int j = 0; j < attributes.Count; j++)
                {
                    int itemSize = attributes[j].ItemSize;
                    Array.Copy(attributes[j].Source, index * itemSize, tmpAttributes[j], (int)nextIndex * itemSize, itemSize);
                }

                hashToIndex.Add(hash, nextIndex);
                newIndices[i] = nextIndex;
                nextIndex++;
            }
        }

        float[] Trimmed(int j) => tmpAttributes[j][..((int)nextIndex * attributes[j].ItemSize)];
        float[]? TrimmedIf(float[]? source) => source is null ? null : Trimmed(attributes.FindIndex(a => a.Source == source));

        var result = new PropMesh
        {
            Position = Trimmed(0),
            Normal = TrimmedIf(mesh.Normal),
            Color = TrimmedIf(mesh.Color),
            Uv = TrimmedIf(mesh.Uv),
            Uv1 = TrimmedIf(mesh.Uv1),
        };

        // three's setIndex of a plain array, which also picks the index's width.
        result.SetIndex(newIndices);
        result.Groups.AddRange(mesh.Groups);
        return result;
    }

    /// <summary>three's normalizeNormals: each normal read as doubles, normalised, stored as float32.</summary>
    private static void NormalizeNormals(float[] normals)
    {
        var vector = new Vec3d();
        for (int i = 0, il = normals.Length / 3; i < il; i++)
        {
            vector.FromBufferAttribute(normals, i).Normalize();
            SetXyz(normals, i, vector);
        }
    }

    /// <summary>BufferAttribute.setXYZ on a Float32Array: each double rounds to the nearest float32.</summary>
    private static void SetXyz(float[] attribute, int index, Vec3d v)
    {
        attribute[index * 3] = (float)v.X;
        attribute[(index * 3) + 1] = (float)v.Y;
        attribute[(index * 3) + 2] = (float)v.Z;
    }

    private static float[] Gather(float[] attribute, int itemSize, uint[] indices)
    {
        float[] array2 = new float[indices.Length * itemSize];
        int index2 = 0;
        foreach (uint i in indices)
        {
            int index = (int)i * itemSize;
            for (int j = 0; j < itemSize; j++)
            {
                array2[index2++] = attribute[index++];
            }
        }

        return array2;
    }

    private static bool SameAttributes(PropMesh a, PropMesh b) =>
        (a.Normal is null) == (b.Normal is null)
        && (a.Color is null) == (b.Color is null)
        && (a.Uv is null) == (b.Uv is null)
        && (a.Uv1 is null) == (b.Uv1 is null);

    private static float[]? Concat(IReadOnlyList<PropMesh> meshes, Func<PropMesh, float[]?> attribute)
    {
        if (attribute(meshes[0]) is null)
        {
            return null;
        }

        float[] array = new float[meshes.Sum(m => attribute(m)!.Length)];
        int offset = 0;
        foreach (PropMesh mesh in meshes)
        {
            float[] source = attribute(mesh)!;
            source.CopyTo(array, offset);
            offset += source.Length;
        }

        return array;
    }

    private static void AddIfPresent(List<(float[] Source, int ItemSize)> attributes, float[]? source, int itemSize)
    {
        if (source is not null)
        {
            attributes.Add((source, itemSize));
        }
    }

    /// <summary>A truncated value's key: its bits, with -0 as 0 and every NaN as one NaN, as JavaScript prints them.</summary>
    private static long KeyOf(double truncated) =>
        double.IsNaN(truncated) ? BitConverter.DoubleToInt64Bits(double.NaN)
        : truncated == 0 ? 0
        : BitConverter.DoubleToInt64Bits(truncated);
}
