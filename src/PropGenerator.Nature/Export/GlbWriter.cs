// Ported from easy-props frontend/src/core/export/glb-exporter.ts (MIT, © 2026 AHS12) and three.js r186
// examples/jsm/exporters/GLTFExporter.js (MIT, © 2010-2026 three.js authors).
using System.Runtime.InteropServices;
using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Textures;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using GltfMesh = SharpGLTF.Schema2.Mesh;

namespace PropGenerator.Nature.Export;

/// <summary>
/// Writes meshes as a binary glTF (GLB) laid out as easy-props' <c>writeGLB</c> lays it out through three's
/// GLTFExporter: the same scene, nodes, meshes, accessors, materials, textures and images, in the same order, holding
/// the same values. Only the bytes of the file and of its PNGs differ, as any two writers' do.
/// </summary>
public static class GlbWriter
{
    private static readonly Dictionary<string, string> AttributeNames = new(StringComparer.Ordinal)
    {
        ["position"] = "POSITION",
        ["normal"] = "NORMAL",
        ["color"] = "COLOR_0",
        ["uv"] = "TEXCOORD_0",
        ["uv1"] = "TEXCOORD_1",
    };

    /// <summary>
    /// The meshes as one GLB file: one scene named <paramref name="rootName"/>, one node per mesh, named as the mesh. Per
    /// mesh, in its order: an accessor per attribute (float32, in the mesh's attribute order; normals renormalised when
    /// any is off unit length), then per primitive an index accessor (16 or 32 bits as the mesh holds it) and its
    /// material. A material is written once however many nodes share the instance; its colour texture comes before its
    /// relief; a texture is written once per file, each with its image and sampler. Textures come from
    /// <paramref name="textures"/>, built when it no longer holds them.
    /// </summary>
    public static byte[] Write(IReadOnlyList<GlbNode> nodes, string rootName, TextureCache textures)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(rootName);
        ArgumentNullException.ThrowIfNull(textures);
        var model = ModelRoot.CreateModel();
        Scene scene = model.UseScene(rootName);
        model.DefaultScene = scene;
        var writer = new FileState(model, textures);
        foreach (GlbNode node in nodes)
        {
            Node gltfNode = scene.CreateNode(node.Name);
            GltfMesh? mesh = writer.Mesh(node);
            if (mesh is not null)
            {
                gltfNode.Mesh = mesh;
            }
        }

        // easy-props writes what it is given, a shape of NaN included (its bounds then null); a strict check would refuse
        // such a file, so none is made here. The tests read every file back, strictly unless easy-props' holds a NaN.
        return [.. model.WriteGLB(new WriteSettings { Validation = SharpGLTF.Validation.ValidationMode.Skip })];
    }

    /// <summary>
    /// GLTFExporter's <c>isNormalizedNormalAttribute</c> and <c>createNormalizedNormalAttribute</c>: when any normal's
    /// length is more than 0.0005 from 1 (the glTF validator's tolerance), every normal is divided by its length and a
    /// zero one becomes (1, 0, 0); otherwise they are written as they are.
    /// </summary>
    internal static float[] Normalised(float[] normal)
    {
        var v = new Vec3d();
        bool unit = true;
        for (int i = 0; i < normal.Length / 3 && unit; i++)
        {
            unit = !(Math.Abs(v.FromBufferAttribute(normal, i).Length() - 1.0) > 0.0005);
        }

        if (unit)
        {
            return normal;
        }

        float[] result = new float[normal.Length];
        for (int i = 0; i < normal.Length / 3; i++)
        {
            v.FromBufferAttribute(normal, i);
            if (v.X == 0 && v.Y == 0 && v.Z == 0)
            {
                // three's v.setX(1): the other two keep their (signed) zeros.
                v.X = 1;
            }
            else
            {
                v.Normalize();
            }

            result[i * 3] = (float)v.X;
            result[(i * 3) + 1] = (float)v.Y;
            result[(i * 3) + 2] = (float)v.Z;
        }

        return result;
    }

    /// <summary>
    /// GLTFExporter's <c>getMinMax</c> over items <paramref name="start"/> to <paramref name="start"/> +
    /// <paramref name="count"/>, as JSON.stringify writes it: JavaScript's Math.min and Math.max (NaN wins, -0 below
    /// +0), then -0 written as 0. Null when a bound is NaN, which JSON writes as null and a glTF bound cannot hold: the
    /// accessor then has no bounds.
    /// </summary>
    internal static (double[] Min, double[] Max)? Bounds(Func<int, double> value, int itemSize, int start, int count)
    {
        double[] min = new double[itemSize];
        double[] max = new double[itemSize];
        Array.Fill(min, double.PositiveInfinity);
        Array.Fill(max, double.NegativeInfinity);
        for (int i = start; i < start + count; i++)
        {
            for (int a = 0; a < itemSize; a++)
            {
                double v = value((i * itemSize) + a);
                min[a] = Math.Min(min[a], v);
                max[a] = Math.Max(max[a], v);
            }
        }

        if (min.Any(double.IsNaN) || max.Any(double.IsNaN))
        {
            return null;
        }

        // JSON.stringify(-0) is "0".
        return ([.. min.Select(v => v == 0 ? 0 : v)], [.. max.Select(v => v == 0 ? 0 : v)]);
    }

    /// <summary>Sets an accessor's bounds to <paramref name="bounds"/>, or to none.</summary>
    private static void SetBounds(Accessor accessor, (double[] Min, double[] Max)? bounds)
    {
        // SharpGLTF computes float bounds itself (and none for an index); its Bounds are the lists it writes. Pinned
        // to SharpGLTF 1.0.6, where they are List<double>; ABoundThatWouldHoldNanIsLeftOutAndMinusZeroIsWrittenAsZero and
        // EveryGlbIsEasyPropsStructurallyAndLoadsWithSharpGltf fail if an upgrade changes that.
        if (accessor.Bounds.Min is not List<double> min || accessor.Bounds.Max is not List<double> max)
        {
            throw new InvalidOperationException("SharpGLTF no longer keeps an accessor's bounds as lists; GlbWriter needs another way to set them");
        }

        min.Clear();
        max.Clear();
        if (bounds is { } values)
        {
            min.AddRange(values.Min);
            max.AddRange(values.Max);
        }
    }

    /// <summary>What one file has written so far: its materials by instance and its textures by key.</summary>
    private sealed class FileState(ModelRoot model, TextureCache textures)
    {
        private readonly Dictionary<GlbMaterial, Material> _materials = new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<string, Texture> _textures = new(StringComparer.Ordinal);

        /// <summary>GLTFExporter's <c>processMeshAsync</c>; null for a mesh with no vertices, which it skips.</summary>
        public GltfMesh? Mesh(GlbNode node)
        {
            PropMesh geometry = node.Mesh;
            if (geometry.VertexCount == 0)
            {
                return null;
            }

            GltfMesh mesh = model.CreateMesh(node.Name);
            var attributes = new List<(string Name, Accessor Accessor)>();
            foreach (string attribute in node.AttributeOrder)
            {
                float[] values = attribute switch
                {
                    "position" => geometry.Position,
                    "normal" => Normalised(geometry.Normal ?? throw Missing(node, attribute)),
                    "color" => geometry.Color ?? throw Missing(node, attribute),
                    "uv" => geometry.Uv ?? throw Missing(node, attribute),
                    "uv1" => geometry.Uv1 ?? throw Missing(node, attribute),
                    _ => throw new ArgumentException($"{node.Name}: no glTF attribute for '{attribute}'", nameof(node)),
                };
                int itemSize = attribute is "uv" or "uv1" ? 2 : 3;
                attributes.Add((AttributeNames[attribute], VertexAccessor(values, itemSize)));
            }

            uint[] index = geometry.Index ?? throw new ArgumentException($"{node.Name} has no index; every exported mesh has one", nameof(node));
            List<(int Start, int Count, int Material)> groups = node.ByGroup
                ? geometry.Groups
                : [(0, index.Length, 0)];
            foreach (var (start, count, material) in groups)
            {
                MeshPrimitive primitive = mesh.CreatePrimitive();
                foreach (var (name, accessor) in attributes)
                {
                    primitive.SetVertexAccessor(name, accessor);
                }

                if (count > 0)
                {
                    primitive.SetIndexAccessor(IndexAccessor(index, geometry.IndexIsUint32, start, count));
                }

                primitive.Material = Material(node.Materials[material]);
            }

            return mesh;
        }

        private static ArgumentException Missing(GlbNode node, string attribute) =>
            new($"{node.Name} lists '{attribute}' but has none", nameof(node));

        private Accessor VertexAccessor(float[] values, int itemSize)
        {
            int count = values.Length / itemSize;
            BufferView view = model.UseBufferView(MemoryMarshal.AsBytes(values.AsSpan()).ToArray(), 0, null, itemSize * 4, BufferMode.ARRAY_BUFFER);
            Accessor accessor = model.CreateAccessor();
            accessor.SetVertexData(view, 0, count, new AttributeFormat(itemSize == 2 ? DimensionType.VEC2 : DimensionType.VEC3, EncodingType.FLOAT, false));
            SetBounds(accessor, Bounds(i => values[i], itemSize, 0, count));
            return accessor;
        }

        private Accessor IndexAccessor(uint[] index, bool wide, int start, int count)
        {
            byte[] bytes = wide
                ? MemoryMarshal.AsBytes(index.AsSpan(start, count)).ToArray()
                : MemoryMarshal.AsBytes(index.AsSpan(start, count).ToArray().Select(i => (ushort)i).ToArray().AsSpan()).ToArray();
            BufferView view = model.UseBufferView(bytes, 0, null, 0, BufferMode.ELEMENT_ARRAY_BUFFER);
            Accessor accessor = model.CreateAccessor();
            accessor.SetIndexData(view, 0, count, wide ? IndexEncodingType.UNSIGNED_INT : IndexEncodingType.UNSIGNED_SHORT);
            SetBounds(accessor, Bounds(i => index[i], 1, start, count));
            return accessor;
        }

        /// <summary>GLTFExporter's <c>processMaterialAsync</c> for the materials easy-props writes.</summary>
        private Material Material(GlbMaterial source)
        {
            if (_materials.TryGetValue(source, out Material? written))
            {
                return written;
            }

            Material material = model.CreateMaterial(source.Name);
            material.InitializePBRMetallicRoughness();
            MaterialChannel metallicRoughness = material.FindChannel("MetallicRoughness")!.Value;
            metallicRoughness.SetFactor("MetallicFactor", (float)MergedMaterial.Metalness);
            if (source is MergedMaterial merged)
            {
                metallicRoughness.SetFactor("RoughnessFactor", (float)merged.Roughness);

                // The colour texture first, then the relief: the order the file lists them in.
                material.FindChannel("BaseColor")!.Value.SetTexture(0, Texture(merged.MapKey, merged.ReliefFull));
                if (merged.ReliefKey is not null)
                {
                    // normalScale (1, -1) without tangents: GLTFExporter writes the relief as it is, scale 1.
                    material.FindChannel("Normal")!.Value.SetTexture(0, Texture(merged.ReliefKey, merged.ReliefFull));
                }

                if (merged.AlphaTest > 0)
                {
                    material.Alpha = AlphaMode.MASK;
                    material.AlphaCutoff = (float)merged.AlphaTest;
                }
            }
            else if (source is FarMaterial card)
            {
                // The far-away card: its picture, then its relief, cut out at 0.5.
                metallicRoughness.SetFactor("RoughnessFactor", 1);
                material.FindChannel("BaseColor")!.Value.SetTexture(0, Picture(card.Map));
                material.FindChannel("Normal")!.Value.SetTexture(0, Picture(card.Relief));
                material.Alpha = AlphaMode.MASK;
                material.AlphaCutoff = (float)FarMaterial.AlphaTest;
            }
            else
            {
                // A MeshStandardMaterial with only a name: roughness 1.
                metallicRoughness.SetFactor("RoughnessFactor", 1);
            }

            _materials.Add(source, material);
            return material;
        }

        /// <summary>A picture made for this file (the far-away card's): clamped, linear with mipmaps, named as the picture is.</summary>
        private Texture Picture(RgbaImage image)
        {
            Image gltfImage = model.CreateImage();
            gltfImage.Content = new MemoryImage(PngEncoder.Encode(image.Rgba, image.W, image.H));
            Texture texture = model.UseTexture(
                gltfImage,
                model.UseTextureSampler(TextureWrapMode.CLAMP_TO_EDGE, TextureWrapMode.CLAMP_TO_EDGE, TextureMipMapFilter.LINEAR_MIPMAP_LINEAR, TextureInterpolationFilter.LINEAR));
            if (image.Name.Length > 0)
            {
                texture.Name = image.Name;
            }

            return texture;
        }

        /// <summary>GLTFExporter's <c>processTextureAsync</c>: one texture, image and sampler per texture of the file.</summary>
        private Texture Texture(string key, bool reliefFull)
        {
            string id = key.StartsWith("relief:", StringComparison.Ordinal) ? $"{key}:{(reliefFull ? "full" : "half")}" : key;
            if (_textures.TryGetValue(id, out Texture? written))
            {
                return written;
            }

            var (image, wrap, min, mag) = GlbTextures.Resolve(key, textures);
            Image gltfImage = model.CreateImage();
            gltfImage.Content = new MemoryImage(PngEncoder.Encode(image.Rgba, image.W, image.H));
            Texture texture = model.UseTexture(gltfImage, model.UseTextureSampler(wrap, wrap, min, mag));
            if (image.Name.Length > 0)
            {
                texture.Name = image.Name;
            }

            _textures.Add(id, texture);
            return texture;
        }
    }
}
