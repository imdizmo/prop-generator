using PropGenerator.Desktop.Engine;
using PropGenerator.Engine;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// The engine's output as the app reads it: a build as the view's coloured mesh (Z-up, as the port's builds are), the
/// collision as shapes, and the export package's GLB.
/// </summary>
internal static class EngineMeshes
{
    // The atlases printed parts read, by their texture keys: the bush's default leaf ("leaf") and its other leaves
    // ("leaf:<style>:<bloom>"), the painted tree's printed leaves ("tree-leaf:<style>", easy-props 0.4.5) and its
    // brush dabs ("dab").
    private const string LeafMap = "leaf";
    private const string DabMap = "dab";
    private const string LeafPrefix = "leaf:";
    private const string TreeLeafPrefix = "tree-leaf:";
    private const string BarkPrefix = "bark:";
    private const string ReliefPrefix = "relief:";

    /// <summary>
    /// The built parts as one coloured mesh, Z-up, in metres, as the port's <c>Build</c> makes it: each part placed by its
    /// matrix (normals by the matrix's 3x3, renormalised), Y-up turned to Z-up as (x, -z, y), each vertex's colour its
    /// vertex colour times the part's tint (or the tint alone), proxy parts kept apart for picking, one
    /// <see cref="MeshPart"/> per part by component id, and the texture coordinates of printed parts (1, reading one atlas
    /// a prop) and of bark (2, reading one bark and its relief a prop), and each vertex's roughness when a part shines
    /// (its surface's, as a wet part's; null when every part is fully rough). Waits for the textures it names, so it never
    /// runs on the engine's own thread.
    /// </summary>
    /// <param name="mesh">The engine's build.</param>
    /// <param name="textures">Where the atlas and bark pictures are read from.</param>
    /// <param name="kind">The turn a texture missing from the cache is fetched on: the build's own.</param>
    /// <param name="cancel">Stops the wait for a texture.</param>
    /// <param name="frame">How a new piece of the prop is framed (<see cref="PieceFrames.Of"/>).</param>
    /// <param name="flutterByPart">Whether the wind goes part by part, so each vertex says how much it flutters.</param>
    /// <returns>The mesh.</returns>
    /// <exception cref="InvalidOperationException">Called on the engine's thread (by the texture cache).</exception>
    /// <exception cref="EngineException">A texture could not be read.</exception>
    /// <exception cref="OperationCanceledException">The wait was stopped.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "The plan's signature: the turn and its token together, then how the mesh is framed.")]
    public static ColouredMesh ToColoured(EngineMesh mesh, EngineTextures textures, EngineJobKind kind, CancellationToken cancel, PieceFrame frame, bool flutterByPart)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(textures);
        var drawn = new Buffers();
        var proxies = new Buffers();
        var texCoords = new List<float>();
        var flutter = new List<float>();
        var roughness = new List<float>();
        bool shines = false;
        MeshTexture? texture = null;
        string? atlas = null;
        MeshTexture? bark = null;
        MeshTexture? relief = null;
        string? barkKey = null;
        foreach (EnginePart part in mesh.Parts)
        {
            if (part.Proxy)
            {
                // Never drawn, but what the pointer lands on.
                proxies.Append(part);
                continue;
            }

            int before = drawn.Positions.Count / 3;
            drawn.Append(part);
            float[]? uv = part.Uvs;
            string? map = part.Surface.Map;

            // One atlas a prop: a part printed with another one shows in its colours alone.
            bool printed = IsAtlas(map) && uv is not null && (atlas is null || atlas == map);
            if (printed && texture is null)
            {
                atlas = map!;
                texture = textures.Get(atlas, kind, cancel) with { AlphaCut = (float)(part.Surface.AlphaTest ?? 0) };
            }

            // One bark a prop, as one atlas.
            bool barked = !printed && uv is not null && map is not null && map.StartsWith(BarkPrefix, StringComparison.Ordinal)
                && (barkKey is null || map == barkKey);
            if (barked && bark is null)
            {
                barkKey = map!;
                bark = textures.Get(barkKey, kind, cancel);
                relief = textures.Get(ReliefOf(barkKey, part.Surface.NormalMap), kind, cancel);
            }

            bool foliage = part.RoleOfPart == "foliage";
            float[]? rigid = part.Rigid;
            bool read = printed || barked;

            // A wet part shines: its surface's roughness (upstream's wetSurface), 1 when it has none.
            float rough = part.Surface.Roughness is double r && double.IsFinite(r) ? (float)Math.Clamp(r, 0, 1) : 1;
            shines |= rough < 1;
            for (int i = 0; i < (drawn.Positions.Count / 3) - before; i++)
            {
                texCoords.Add(read ? uv![i * 2] : 0);
                texCoords.Add(read ? uv![(i * 2) + 1] : 0);
                texCoords.Add(printed ? 1 : barked ? 2 : 0);
                flutter.Add(foliage ? 1 - (rigid?[i] ?? 0) : 0);
                roughness.Add(rough);
            }
        }

        return drawn.ToMesh() with
        {
            TexCoords = texture is null && bark is null ? null : [.. texCoords],
            Texture = texture,
            Bark = bark,
            Relief = relief,
            Frame = frame,
            Flutter = flutterByPart ? [.. flutter] : null,
            Roughness = shines ? [.. roughness] : null,
            Proxies = proxies.Parts.Count == 0 ? null : proxies.ToMesh(),
        };
    }

    /// <summary>The shapes a player bumps into, as the export writes them, in the prop's own Y-up space.</summary>
    /// <param name="collision">The engine's collision.</param>
    /// <returns>The shapes (round rings: <see cref="CollisionShape.Ring"/> is null); empty when there are none.</returns>
    public static IReadOnlyList<CollisionShape> ToShapes(EngineCollision collision)
    {
        ArgumentNullException.ThrowIfNull(collision);
        return [.. collision.Shapes.Select(c => new CollisionShape(Point(c.Start), Point(c.End), c.StartRadius, c.EndRadius))];
    }

    /// <summary>
    /// The export settings of an app export: one level, no collision, wind data, in the chosen look, its closest level's
    /// pictures (bark, stone, cut ends) at the chosen size, and, in a set, upstream's set name for shared materials.
    /// </summary>
    /// <param name="modelName">The model's name, already safe as a file name.</param>
    /// <param name="options">The look (<see cref="ExportLook"/>), picture size and set.</param>
    /// <returns>The settings JSON.</returns>
    public static string ExportSettings(string modelName, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(modelName);
        ArgumentNullException.ThrowIfNull(options);
        string look = options.Look;
        var settings = new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = modelName,
            ["engine"] = "unreal",
            ["levels"] = 1,
            ["collision"] = false,
            ["wind"] = true,
            ["look"] = look,
            ["pictureSize"] = ExportOptions.KeptPictureSize(options.PictureSize),
        };
        if (options.SetName is string set)
        {
            settings["setName"] = set;
        }

        return settings.ToJsonString();
    }

    /// <summary>
    /// Builds the prop's GLB file with upstream's export (<see cref="ExportSettings"/>), waiting for the engine's
    /// Export job. Never called on the engine's own thread, nor on the UI thread.
    /// </summary>
    /// <param name="builds">The build engine.</param>
    /// <param name="sceneJson">The scene (<see cref="EngineScenes.Of(string, string, uint, PropGenerator.Desktop.Scenes.Scene?)"/>).</param>
    /// <param name="modelName">The model's name, already safe as a file name.</param>
    /// <param name="options">The look, picture size and set (<see cref="ExportSettings"/>).</param>
    /// <param name="cancel">Stops the export.</param>
    /// <param name="progress">Gets the export's progress, 0 to 1 (reported on the engine's thread), or null.</param>
    /// <returns>The bytes of the single .glb the package holds.</returns>
    /// <exception cref="InvalidOperationException">Called on the engine's thread, or the package has no single .glb.</exception>
    /// <exception cref="EngineException">The export failed in the engine.</exception>
    /// <exception cref="OperationCanceledException">The export was stopped.</exception>
    public static byte[] Glb(EngineThread builds, string sceneJson, string modelName, ExportOptions options, CancellationToken cancel, IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(builds);
        if (builds.IsEngineThread)
        {
            throw new InvalidOperationException("An export is waited for on the engine's own thread, which is the one that would run it.");
        }

        IReadOnlyList<EngineFile> files = builds.ExportAsync(sceneJson, "low-poly", ExportSettings(modelName, options), progress, cancel).GetAwaiter().GetResult();
        return GlbOf(files);
    }

    /// <summary>The single .glb of an export package.</summary>
    /// <param name="files">The package's files.</param>
    /// <returns>Its bytes.</returns>
    /// <exception cref="InvalidOperationException">The package has no .glb, or more than one.</exception>
    public static byte[] GlbOf(IReadOnlyList<EngineFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        EngineFile[] glbs = [.. files.Where(f => f.Name.EndsWith(".glb", StringComparison.Ordinal))];
        return glbs.Length == 1
            ? glbs[0].Bytes
            : throw new InvalidOperationException($"The export package holds {glbs.Length} .glb files, not one.");
    }

    /// <summary>Whether a part's map is a printed atlas the view reads (a leaf or a dab picture), not bark or a stone.</summary>
    /// <param name="map">The texture key the part's surface names, or null.</param>
    /// <returns>True for "leaf", "dab", "leaf:&lt;style&gt;:&lt;bloom&gt;" and "tree-leaf:&lt;style&gt;".</returns>
    internal static bool IsAtlas(string? map) =>
        map is LeafMap or DabMap
        || (map is not null && (map.StartsWith(LeafPrefix, StringComparison.Ordinal) || map.StartsWith(TreeLeafPrefix, StringComparison.Ordinal)));

    // A bark's relief: the one its surface names, or the relief of the bark's pattern (bark:<pattern>:<strength>).
    private static string ReliefOf(string barkKey, string? normalMap) =>
        normalMap is not null && normalMap.StartsWith(ReliefPrefix, StringComparison.Ordinal)
            ? normalMap
            : ReliefPrefix + barkKey.Split(':')[1];

    private static Vec3d Point(IReadOnlyList<double> p) => new(p[0], p[1], p[2]);

    // The vertex lists of drawn or proxy parts, and their parts by component.
    private sealed class Buffers
    {
        public List<float> Positions { get; } = [];

        public List<float> Normals { get; } = [];

        public List<float> Colours { get; } = [];

        public List<int> Indices { get; } = [];

        public List<MeshPart> Parts { get; } = [];

        public ColouredMesh ToMesh() =>
            new([.. Positions], [.. Normals], [.. Colours], [.. Indices]) { Parts = Parts };

        // Appends one part, placed, turned Z-up, with its colours and its triangles.
        public void Append(EnginePart part)
        {
            int firstTriangle = Indices.Count / 3;
            float[] position = part.Positions;
            bool hasNormals = part.Attributes.ContainsKey("normal");
            float[] normal = part.Normals;
            EngineVertexData? color = part.Attributes.GetValueOrDefault("color");
            int count = position.Length / 3;
            int offset = Positions.Count / 3;
            IReadOnlyList<double> m = part.Matrix;
            double tr = 1, tg = 1, tb = 1;
            if (part.Tint is { Count: >= 3 } tint)
            {
                (tr, tg, tb) = (tint[0], tint[1], tint[2]);
            }

            for (int i = 0; i < count; i++)
            {
                double x = position[i * 3], y = position[(i * 3) + 1], z = position[(i * 3) + 2];
                double px = (m[0] * x) + (m[4] * y) + (m[8] * z) + m[12];
                double py = (m[1] * x) + (m[5] * y) + (m[9] * z) + m[13];
                double pz = (m[2] * x) + (m[6] * y) + (m[10] * z) + m[14];

                // Y-up to Z-up: (x, y, z) becomes (x, -z, y), a pure turn, so triangles keep their winding.
                Positions.Add((float)px);
                Positions.Add((float)-pz);
                Positions.Add((float)py);

                double nx = 0, ny = 1, nz = 0;
                if (hasNormals)
                {
                    double ax = normal[i * 3], ay = normal[(i * 3) + 1], az = normal[(i * 3) + 2];
                    nx = (m[0] * ax) + (m[4] * ay) + (m[8] * az);
                    ny = (m[1] * ax) + (m[5] * ay) + (m[9] * az);
                    nz = (m[2] * ax) + (m[6] * ay) + (m[10] * az);
                    double len = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
                    if (len > 0)
                    {
                        nx /= len;
                        ny /= len;
                        nz /= len;
                    }
                }

                Normals.Add((float)nx);
                Normals.Add((float)-nz);
                Normals.Add((float)ny);

                // The material's colour times the vertex colour, as three draws it (a colour of four reads its first three).
                if (color is { } c)
                {
                    int at = i * c.ItemSize;
                    Colours.Add((float)(c.Data[at] * tr));
                    Colours.Add((float)(c.Data[at + 1] * tg));
                    Colours.Add((float)(c.Data[at + 2] * tb));
                }
                else
                {
                    Colours.Add((float)tr);
                    Colours.Add((float)tg);
                    Colours.Add((float)tb);
                }
            }

            if (part.Index is { } index)
            {
                foreach (uint v in index)
                {
                    Indices.Add(offset + (int)v);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    Indices.Add(offset + i);
                }
            }

            Parts.Add(new MeshPart(part.ComponentId ?? string.Empty, firstTriangle, (Indices.Count / 3) - firstTriangle));
        }
    }
}
