// Ported from easy-props frontend/src/core/export/pipeline/merge.ts (MIT, © 2026 AHS12).
using System.Collections.ObjectModel;
using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Style;

namespace PropGenerator.Nature.Export;

/// <summary>
/// Merging a prop's parts into as few meshes as it has materials. Each part's transform is baked into its vertices and
/// its material colour into its vertex colours, so what stays apart is only what has to: wood and leaves draw with
/// different materials, and a textured part cannot share with one that has nowhere to read the texture.
/// </summary>
public static class Merge
{
    /// <summary>
    /// easy-props' <c>mergeParts</c>. Proxies and parts with a non-finite (or no) position are dropped, as one NaN vertex
    /// makes a file unreadable in most viewers. The rest go, in their order, into the buckets wood, leaves, wood-plain and
    /// leaves-plain (in the order first met), each merged into one mesh named <c>{name}_{solid}</c> (<c>{name}_wood</c>
    /// unless the prop says otherwise; just <c>{name}</c> when a name other than wood already ends so, as
    /// <c>pg_granite_cliff</c> does) or <c>{name}_leaves</c> (<c>_2</c> added for the second of either), moved so
    /// <paramref name="pivot"/> is the origin. With
    /// <paramref name="wind"/>, every vertex gets its sway and flutter in uv1. <paramref name="fullRelief"/> writes a
    /// relief full size (level 0) rather than half.
    /// </summary>
    public static IReadOnlyList<MergedMesh> Parts(
        IReadOnlyList<GeneratedPart> parts,
        string name,
        Vec3d pivot,
        WindExtent? wind,
        bool fullRelief,
        string solid = Wood,
        bool comic = false)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(pivot);

        // The buckets in the order first met, as a JavaScript Map keeps them.
        var buckets = new List<(string Key, List<GeneratedPart> Parts)>();
        foreach (GeneratedPart placed in parts)
        {
            if (placed.IsProxy || !HasFinitePositions(placed.Mesh))
            {
                continue;
            }

            string key = BucketName(placed) + (placed.Surface?.MapKey is null ? "-plain" : "");
            int at = buckets.FindIndex(bucket => bucket.Key == key);
            if (at < 0)
            {
                buckets.Add((key, [placed]));
            }
            else
            {
                buckets[at].Parts.Add(placed);
            }
        }

        var merged = new List<MergedMesh>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, bucket) in buckets)
        {
            PaintedSurface? surface = bucket[0].Surface;
            var pieces = bucket.Select(placed => Normalised(placed, pivot, wind, comic)).ToList();
            PropMesh geometry = MeshOps.Merge(pieces, false)
                ?? throw new InvalidOperationException("The parts of this prop could not be merged.");

            MeshOps.Translate(geometry, -pivot.X, -pivot.Y, -pivot.Z);

            string bucketName = BucketName(bucket[0]);
            string baseName = bucketName == Wood ? SolidName(name, solid) : $"{name}_{bucketName}";
            string meshName = used.Contains(baseName) ? $"{baseName}_2" : baseName;
            used.Add(baseName);

            merged.Add(new MergedMesh(
                meshName,
                geometry,
                new MergedMaterial(
                    meshName,
                    surface is null ? Roughness : 1,
                    surface?.MapKey ?? "white",
                    surface?.ReliefKey,
                    fullRelief,
                    surface?.AlphaTest ?? 0),
                geometry.Index!.Length / 3));
        }

        return merged;
    }

    /// <summary>
    /// easy-props' <c>joinMeshes</c>: the merged meshes made one mesh named <paramref name="name"/>, one draw group per
    /// former mesh, each keeping its material. Empty for no meshes.
    /// </summary>
    public static IReadOnlyList<JoinedMesh> Join(IReadOnlyList<MergedMesh> meshes, string name)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        if (meshes.Count == 0)
        {
            return [];
        }

        PropMesh geometry = MeshOps.Merge([.. meshes.Select(mesh => mesh.Mesh)], true)
            ?? throw new InvalidOperationException("The parts of this prop could not be joined.");
        return [new JoinedMesh(name, geometry, [.. meshes.Select(mesh => mesh.Material)], geometry.Index!.Length / 3)];
    }

    /// <summary>A merged mesh's attributes in three's order: position, color, normal, uv, then uv1 when it has wind.</summary>
    // Bends a leaf card's normals toward the round shape of its whole clump, as the Comic view lights them, so an
    // engine's light falls across a crown in one band rather than speckling card by card.
    private static void RoundLeafLight(PropMesh geometry)
    {
        // three's computeBoundingSphere takes its centre from the bounding box.
        var box = new Box3d().SetFromBufferAttribute(geometry.Position);
        var centre = box.IsEmpty() ? new Vec3d(0, 0, 0) : new Vec3d(box.Min.X, box.Min.Y, box.Min.Z).Add(box.Max).MultiplyScalar(0.5);
        float[] position = geometry.Position;
        float[] normal = geometry.Normal!;
        var own = new Vec3d();
        var round = new Vec3d();

        for (int i = 0; i < geometry.VertexCount; i++)
        {
            own.Set(normal[i * 3], normal[(i * 3) + 1], normal[(i * 3) + 2]);
            round.Set(position[i * 3], position[(i * 3) + 1], position[(i * 3) + 2]).Sub(centre).Normalize();
            own.Lerp(round, ToonLooks.ComicRounding).Normalize();
            normal[i * 3] = (float)own.X;
            normal[(i * 3) + 1] = (float)own.Y;
            normal[(i * 3) + 2] = (float)own.Z;
        }
    }

    internal static IReadOnlyList<string> AttributeOrderOf(PropMesh mesh) =>
        mesh.Uv1 is null ? WithoutWind : WithWind;

    /// <summary>The name easy-props gives a prop's solid (not foliage) parts' mesh: a tree's wood.</summary>
    public const string Wood = "wood";

    /// <summary>The roughness of a part that does not paint itself.</summary>
    private const double Roughness = 0.85;

    private static readonly ReadOnlyCollection<string> WithoutWind = new(["position", "color", "normal", "uv"]);

    private static readonly ReadOnlyCollection<string> WithWind = new(["position", "color", "normal", "uv", "uv1"]);

    /// <summary>
    /// The solid mesh's name: <c>{name}_{solid}</c>, or the name alone where it already is, or ends in, a solid other
    /// than wood (so a cliff named "cliff" is not "cliff_cliff").
    /// </summary>
    private static string SolidName(string name, string solid) =>
        solid != Wood && (name == solid || name.EndsWith($"_{solid}", StringComparison.Ordinal)) ? name : $"{name}_{solid}";

    private static string BucketName(GeneratedPart part) => part.RoleOfPart == "foliage" ? "leaves" : Wood;

    private static bool HasFinitePositions(PropMesh mesh)
    {
        if (mesh.Position.Length < 3)
        {
            return false;
        }

        foreach (float value in mesh.Position)
        {
            if (!float.IsFinite(value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The same shape with the attributes every part in a mesh must share (easy-props' <c>normalised</c>): its vertex
    /// colours times its tint, normals (computed per face for a faceted part), texture coordinates (zero without a map),
    /// an index, all placed by its matrix; wood tidied; wind when there is any.
    /// </summary>
    private static PropMesh Normalised(GeneratedPart placed, Vec3d pivot, WindExtent? wind, bool comic)
    {
        // A faceted part has one normal per face, which takes its own vertices.
        bool faceted = placed.FlatShading && placed.Surface is null;
        PropMesh source = faceted && placed.Mesh.Index is not null ? MeshOps.ToNonIndexed(placed.Mesh) : placed.Mesh;
        int count = source.VertexCount;
        float[] colors = new float[count * 3];
        // A Comic export carries the flat colours the view shows, so any engine draws them.
        float[]? vertexColors = source.Color is { } painted && comic
            ? FlatColours.Flatten(Array.ConvertAll(painted, value => (double)value))
            : source.Color;
        Color3d tint = placed.Tint;

        for (int i = 0; i < count; i++)
        {
            colors[i * 3] = (float)((vertexColors is null ? 1 : vertexColors[i * 3]) * tint.R);
            colors[(i * 3) + 1] = (float)((vertexColors is null ? 1 : vertexColors[(i * 3) + 1]) * tint.G);
            colors[(i * 3) + 2] = (float)((vertexColors is null ? 1 : vertexColors[(i * 3) + 2]) * tint.B);
        }

        var geometry = new PropMesh { Position = (float[])source.Position.Clone(), Color = colors };

        if (faceted || source.Normal is null)
        {
            geometry.SetIndex((uint[]?)source.Index?.Clone());
            MeshOps.ComputeVertexNormals(geometry);
        }
        else
        {
            geometry.Normal = (float[])source.Normal.Clone();
        }

        // Without a texture of its own, every vertex reads the one white colour.
        geometry.Uv = placed.Surface?.MapKey is not null
            ? (float[])(source.Uv ?? throw new InvalidOperationException("A textured part has no texture coordinates.")).Clone()
            : new float[count * 2];

        // Every part is indexed, so parts can merge whichever way they were built (a Uint32Array either way).
        uint[] index = source.Index is { } own ? (uint[])own.Clone() : [.. Enumerable.Range(0, count).Select(i => (uint)i)];
        geometry.SetIndex(index, wide: true);

        if (comic && placed.Surface?.MapKey is not null && (placed.Surface.AlphaTest ?? 0) > 0)
        {
            RoundLeafLight(geometry);
        }

        MeshOps.ApplyMatrix4(geometry, new Mat4d().FromArray(placed.Matrix.Elements));

        if (BucketName(placed) == Wood)
        {
            Tidy.Wood(geometry);
        }

        if (wind is not null)
        {
            geometry.Uv1 = Wind.Uv1(geometry, placed, pivot, wind, source.Rigid);
        }

        return geometry;
    }
}
