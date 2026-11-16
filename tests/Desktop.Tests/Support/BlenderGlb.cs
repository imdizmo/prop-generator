using System.Numerics;
using SharpGLTF.Schema2;

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>Writes a GLB the way Blender's add-on does: the face number of each triangle in TEXCOORD_1 (u = id, v = 1).</summary>
internal static class BlenderGlb
{
    /// <summary>Writes the mesh with one face number per triangle.</summary>
    /// <param name="path">The .glb file.</param>
    /// <param name="mesh">The mesh in the Z-up frame.</param>
    /// <param name="triangleIds">The face number of each triangle.</param>
    public static void Write(string path, Mesh mesh, int[] triangleIds)
    {
        List<Vector3> pos = [];
        List<Vector2> uv0 = [];
        List<Vector2> uv1 = [];
        for (int t = 0; t < mesh.Indices.Length / 3; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int v = mesh.Indices[(3 * t) + k];
                pos.Add(new Vector3(mesh.Positions[3 * v], mesh.Positions[(3 * v) + 2], -mesh.Positions[(3 * v) + 1]));
                uv0.Add(new Vector2(mesh.Uvs[2 * v], 1f - mesh.Uvs[(2 * v) + 1]));
                uv1.Add(new Vector2(triangleIds[t], 1f));
            }
        }

        var model = ModelRoot.CreateModel();
        var prim = model.CreateMesh("m").CreatePrimitive();
        prim.WithVertexAccessor("POSITION", pos);
        prim.WithVertexAccessor("TEXCOORD_0", uv0);
        prim.WithVertexAccessor("TEXCOORD_1", uv1);
        prim.WithIndicesAccessor(PrimitiveType.TRIANGLES, [.. Enumerable.Range(0, pos.Count)]);
        model.UseScene("s").CreateNode("n").WithMesh(model.LogicalMeshes[0]);
        model.SaveGLB(path);
    }
}
