using System.Numerics;
using System.Text.Json;
using SharpGLTF.Schema2;

namespace PropGenerator;

/// <summary>
/// Reads a mesh sent from Blender (a GLB in the inbox) back into the app's own frame. The face numbers (Blender's
/// <c>pg_face_id</c>) ride in the GLB's second texture-coordinate set, <c>TEXCOORD_1</c>: u is the face number and v is
/// 0 in Blender (1 in glTF, which counts v downwards). Blender's glTF exporter averages ordinary per-face attributes
/// onto the vertices, but it keeps every corner of a texture-coordinate set, splitting vertices where they differ, so
/// this is the one way the numbers stay tied to their triangles (even when the exporter drops a degenerate triangle).
/// A per-vertex <c>_PG_FACE_ID</c> attribute with the same value on all three corners of each triangle is read as well.
/// </summary>
public static class GlbImport
{
    /// <summary>The face kind given to every face of a mesh from Blender; marking faces comes in version 2.</summary>
    public const string UnmarkedKind = "unmarked";

    /// <summary>What reading found.</summary>
    /// <param name="Mesh">The mesh in the Z-up tile frame.</param>
    /// <param name="Piece">A piece with one unmarked face for each face number.</param>
    /// <param name="Warning">Something that did not match (the face numbers fell back to one per triangle), or null.</param>
    public sealed record Result(Mesh Mesh, Piece Piece, string? Warning);

    /// <summary>Reads the GLB.</summary>
    /// <param name="path">The .glb file.</param>
    /// <returns>The mesh, its piece and any warning.</returns>
    /// <exception cref="InvalidDataException">The file cannot be read as a GLB holding a mesh.</exception>
    public static Result Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        List<float> positions = [];
        List<float> uvs = [];
        List<int> indices = [];
        List<int> faceIds = [];
        bool faceComplete = true;
        try
        {
            var model = ModelRoot.Load(path);
            foreach (Node node in model.LogicalNodes.Where(n => n.Mesh is not null))
            {
                Matrix4x4 world = node.WorldMatrix;
                foreach (MeshPrimitive prim in node.Mesh.Primitives)
                {
                    AddPrimitive(prim, world, positions, uvs, indices, faceIds, ref faceComplete);
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a GLB file that can be read: {e.Message}", e);
        }

        if (indices.Count == 0)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} holds no triangles.");
        }

        int triangles = indices.Count / 3;
        string? warning = null;
        int[] faces;
        if (faceComplete && faceIds.Count == triangles)
        {
            faces = [.. faceIds];
        }
        else
        {
            faces = [.. Enumerable.Range(0, triangles)];
            warning = "This mesh carries no face numbers, so each triangle counts as a face.";
        }

        var mesh = new Mesh([.. positions], [.. uvs], [.. indices], faces);
        var none = new Dictionary<string, JsonElement>();
        List<FaceMark> marks = [.. faces.Distinct().Order().Select(id => new FaceMark(id, UnmarkedKind, none))];
        return new Result(mesh, new Piece("blender_mesh", marks), warning);
    }

    private static void AddPrimitive(
        MeshPrimitive prim, Matrix4x4 world, List<float> positions, List<float> uvs, List<int> indices,
        List<int> faceIds, ref bool faceComplete)
    {
        if (prim.DrawPrimitiveType != PrimitiveType.TRIANGLES)
        {
            return;
        }

        var pos = prim.GetVertexAccessor("POSITION")?.AsVector3Array();
        if (pos is null)
        {
            return;
        }

        var uv = prim.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        int baseVertex = positions.Count / 3;
        for (int i = 0; i < pos.Count; i++)
        {
            // glTF is Y up, the app's frame is Z up: the inverse of GlbExport's (x, y, z) to (x, z, -y).
            var g = Vector3.Transform(pos[i], world);
            positions.Add(g.X);
            positions.Add(-g.Z);
            positions.Add(g.Y);
            uvs.Add(uv is null ? 0f : uv[i].X);
            uvs.Add(uv is null ? 0f : 1f - uv[i].Y);
        }

        var index = prim.GetIndices();
        int count = index?.Count ?? pos.Count;
        for (int i = 0; i < count; i++)
        {
            indices.Add(baseVertex + (int)(index is null ? (uint)i : index[i]));
        }

        int[]? ids = ReadFaceIds(prim, index, count);
        if (ids is null)
        {
            faceComplete = false;
            return;
        }

        faceIds.AddRange(ids);
    }

    // The face number of each triangle, or null when the primitive has none that are usable: every corner of a
    // triangle must carry the same whole number, or the values were blended and cannot be trusted.
    private static int[]? ReadFaceIds(MeshPrimitive prim, IList<uint>? index, int count)
    {
        var uv1 = prim.GetVertexAccessor("TEXCOORD_1")?.AsVector2Array();
        var attribute = prim.GetVertexAccessor("_PG_FACE_ID")?.AsScalarArray();
        if (uv1 is null && attribute is null)
        {
            return null;
        }

        uint At(int corner) => index is null ? (uint)corner : index[corner];
        int[] ids = new int[count / 3];
        for (int t = 0; t < ids.Length; t++)
        {
            float a, b, c;
            if (uv1 is not null)
            {
                if (uv1[(int)At(3 * t)].Y != 1f || uv1[(int)At((3 * t) + 1)].Y != 1f || uv1[(int)At((3 * t) + 2)].Y != 1f)
                {
                    return null;
                }

                (a, b, c) = (uv1[(int)At(3 * t)].X, uv1[(int)At((3 * t) + 1)].X, uv1[(int)At((3 * t) + 2)].X);
            }
            else
            {
                (a, b, c) = (attribute![(int)At(3 * t)], attribute[(int)At((3 * t) + 1)], attribute[(int)At((3 * t) + 2)]);
            }

            if (a != b || b != c || a != MathF.Round(a) || a < 0)
            {
                return null;
            }

            ids[t] = (int)a;
        }

        return ids;
    }
}
