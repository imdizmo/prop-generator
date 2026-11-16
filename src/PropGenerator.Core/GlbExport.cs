using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;

namespace PropGenerator;

/// <summary>Writes a painted tile as a binary glTF (GLB) with its picture inside.</summary>
public static class GlbExport
{
    /// <summary>Writes the tile.</summary>
    /// <param name="path">Where the .glb goes.</param>
    /// <param name="m">The tile's mesh in the Z-up tile frame.</param>
    /// <param name="rgbaTopDown">The painted picture, RGBA8, rows top-down, size by size pixels.</param>
    /// <param name="size">The picture's width and height.</param>
    /// <param name="name">The model's name (used for the node and the mesh).</param>
    public static void Write(string path, Mesh m, byte[] rgbaTopDown, int size, string name = "tile")
    {
        Build(m, rgbaTopDown, size, name).SaveGLB(path);
    }

    /// <summary>Builds the same file in memory and returns its bytes.</summary>
    public static byte[] ToBytes(Mesh m, byte[] rgbaTopDown, int size, string name = "tile")
    {
        return [.. Build(m, rgbaTopDown, size, name).WriteGLB()];
    }

    private static ModelRoot Build(Mesh m, byte[] rgba, int size, string name)
    {
        if (rgba.Length != size * size * 4)
        {
            throw new ArgumentException("The picture is not size x size RGBA.");
        }

        var image = ImageBuilder.From(new MemoryImage(PngEncoder.Encode(rgba, size, size)), "texture");
        var material = new MaterialBuilder("painted_planks")
            .WithMetallicRoughnessShader()
            .WithChannelImage(KnownChannel.BaseColor, image)
            .WithChannelParam(KnownChannel.MetallicRoughness, KnownProperty.MetallicFactor, 0f)
            .WithChannelParam(KnownChannel.MetallicRoughness, KnownProperty.RoughnessFactor, 0.9f);

        var mesh = new MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty>(name);
        var prim = mesh.UsePrimitive(material);

        // Tile frame is Z up; glTF is Y up. Blender's exporter maps (x, y, z) to (x, z, -y), and so does this.
        static Vector3 ToGltf(float x, float y, float z) => new(x, z, -y);

        int count = m.Positions.Length / 3;
        var verts = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>[count];
        Vector3 P(int i) => ToGltf(m.Positions[3 * i], m.Positions[3 * i + 1], m.Positions[3 * i + 2]);
        // Each face has its own vertices, so a flat normal per triangle is right for all of its vertices.
        var normals = new Vector3[count];
        for (int t = 0; t < m.Indices.Length; t += 3)
        {
            int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
            var n = Vector3.Normalize(Vector3.Cross(P(b) - P(a), P(c) - P(a)));
            normals[a] = normals[b] = normals[c] = n;
        }
        for (int i = 0; i < count; i++)
        {
            // glTF's v counts down from the top of the image; the mesh's counts up from the bottom.
            var uv = new Vector2(m.Uvs[2 * i], 1f - m.Uvs[2 * i + 1]);
            verts[i] = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(
                new VertexPositionNormal(P(i), normals[i]), new VertexTexture1(uv));
        }
        for (int t = 0; t < m.Indices.Length; t += 3)
        {
            prim.AddTriangle(verts[m.Indices[t]], verts[m.Indices[t + 1]], verts[m.Indices[t + 2]]);
        }

        var scene = new SharpGLTF.Scenes.SceneBuilder();
        scene.AddRigidMesh(mesh, Matrix4x4.Identity).WithName(name);
        var model = scene.ToGltf2();
        model.LogicalScenes[0].VisualChildren.First().Name = name;
        return model;
    }
}
