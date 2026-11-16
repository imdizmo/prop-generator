// Ported from easy-props frontend/src/core/export/pipeline/tidy.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Js;
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Export;

/// <summary>
/// Tidying wood before it is written, so an engine drawing faces one-sided shows it whole. Each generator means its
/// faces to point out, and the normals at the corners say where out is; a few faces still come out the wrong way, in
/// tight folds of sculpted wood and where simplifying crushes a thin branch flat. Leaves are left alone, as their cards
/// are meant to be seen from both sides.
/// </summary>
public static class Tidy
{
    /// <summary>
    /// easy-props' <c>tidyWood</c>, in place: turns round any face pointing against all three of its corners' normals, and
    /// drops a face that repeats an earlier one on the same three points facing the same way (two back to back are
    /// kept). The new index is 32-bit, as easy-props builds it. A mesh without an index is left as it is.
    /// </summary>
    public static void Wood(PropMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.Index is not { } index)
        {
            return;
        }

        float[] position = mesh.Position;
        float[]? normal = mesh.Normal;
        uint[] corners = (uint[])index.Clone();
        Vec3d a = new(), b = new(), c = new(), corner = new(), facing = new();
        var facesAt = new OrderedDictionary<string, List<int>>(StringComparer.Ordinal);
        var facings = new List<Vec3d>(corners.Length / 3);
        string[] places = new string[3];

        for (int face = 0; face < corners.Length / 3; face++)
        {
            int i = (int)corners[face * 3];
            int j = (int)corners[(face * 3) + 1];
            int k = (int)corners[(face * 3) + 2];

            a.FromBufferAttribute(position, i);
            b.FromBufferAttribute(position, j);
            c.FromBufferAttribute(position, k);
            facing.SubVectors(c, b).Cross(corner.SubVectors(a, b));

            if (normal is not null
                && corner.FromBufferAttribute(normal, i).Dot(facing) < 0
                && corner.FromBufferAttribute(normal, j).Dot(facing) < 0
                && corner.FromBufferAttribute(normal, k).Dot(facing) < 0)
            {
                corners[(face * 3) + 1] = (uint)k;
                corners[(face * 3) + 2] = (uint)j;
                facing.Negate();
            }

            facings.Add(facing.Clone());

            places[0] = Place(position, i);
            places[1] = Place(position, j);
            places[2] = Place(position, k);
            Array.Sort(places, StringComparer.Ordinal);
            string key = string.Join('|', places);
            if (!facesAt.TryGetValue(key, out List<int>? faces))
            {
                faces = [];
                facesAt.Add(key, faces);
            }

            faces.Add(face);
        }

        // A copy of a face on the same three points, facing the same way, adds nothing. Two back to back are kept:
        // they are a thin branch simplified flat, and still part of the outline, seen from either side.
        var copies = new HashSet<int>();
        foreach (List<int> faces in facesAt.Values)
        {
            for (int at = 1; at < faces.Count; at++)
            {
                for (int earlier = 0; earlier < at; earlier++)
                {
                    if (facings[faces[earlier]].Dot(facings[faces[at]]) > 0)
                    {
                        copies.Add(faces[at]);
                        break;
                    }
                }
            }
        }

        uint[] kept = copies.Count == 0
            ? corners
            : [.. corners.Where((_, at) => !copies.Contains(at / 3))];

        // easy-props sets a Uint32Array, whatever its numbers.
        mesh.SetIndex(kept, wide: true);
    }

    /// <summary>A corner's place as tidyWood keys it: each coordinate (a float32 read as a double) with five decimals.</summary>
    private static string Place(float[] position, int vertex) =>
        string.Join(
            ',',
            JsNumber.ToFixed(position[vertex * 3], 5),
            JsNumber.ToFixed(position[(vertex * 3) + 1], 5),
            JsNumber.ToFixed(position[(vertex * 3) + 2], 5));
}
