// Ported from easy-props frontend/src/core/export/pipeline/package.ts (MIT, © 2026 AHS12).
using System.Text;
using PropGenerator.Nature.Textures;

namespace PropGenerator.Nature.Export;

/// <summary>
/// The whole export (easy-props' <c>buildExport</c>): every level, the collision shapes and the description. Each level
/// is built, merged and written before the next is built, so only one level's meshes are held at a time.
/// </summary>
public static class Package
{
    /// <summary>The share of full detail each level is built at (easy-props' <c>LEVEL_SHARES</c>).</summary>
    public static IReadOnlyList<double> LevelShares { get; } = [1, 0.4, 0.12];

    /// <summary>The engines an export can be for.</summary>
    public static IReadOnlyList<string> Engines { get; } = ["unreal", "godot", "plain"];

    /// <summary>
    /// Every file of an export, without writing any of them: <c>{name}_LOD{n}.glb</c> per level (<c>{name}.glb</c> for
    /// one level), <c>{name}_collision.glb</c> when there are shapes (for Unreal joined they ride inside the first level's
    /// file instead, named for it and borrowing its first material), and <c>{name}.asset.json</c>; listed in easy-props'
    /// order (<see cref="FileNameOrder"/>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Levels is not 1, 2 or 3.</exception>
    /// <exception cref="ArgumentException">The engine is not unreal, godot or plain, or the name is empty.</exception>
    public static IReadOnlyList<ExportFile> Build(ExportSource source, ExportSettings settings, TextureCache textures, IFarPainter? farPainter = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(textures);
        Check(settings);
        string name = settings.Name;
        var files = new List<ExportFile>();
        var faces = new List<int>();
        WindExtent? wind = null;
        FarCardData? card = null;
        bool far = settings.Far && source.FarAway && farPainter is not null;
        foreach (ExportLevel level in Levels(source, settings))
        {
            // Drawn from what ships at full detail. Without a painter (no GPU to draw with) there is no card.
            if (level.Level == 0 && far)
            {
                card = FarCard.Build(level.Merged, level.Wind, farPainter!);
                if (card is not null)
                {
                    GlbNode cardNode = FarCard.Mesh(card, name, settings.Look == ExportLook.Cel);
                    files.Add(new ExportFile($"{name}_far.glb", GlbWriter.Write([cardNode], $"{name}_far", textures), FarCard.Faces));
                }
            }

            IReadOnlyList<GlbNode> meshes = level.Joined is { } joined
                ? [.. joined.Select(GlbNode.Of)]
                : [.. level.Merged.Select(GlbNode.Of)];
            bool inline = settings.Join && settings.Engine == "unreal" && meshes.Count > 0;
            IReadOnlyList<GlbNode> shapeNodes = [];
            if (level.Shapes is { } shapes)
            {
                // Beside the prop they borrow its material, as one of their own would be one more for an engine to list.
                GlbMaterial material = inline ? meshes[0].Materials[0] : new ShapeMaterial($"{name}_collision");
                var nodes = shapes
                    .Select((shape, index) => GlbNode.Shape(
                        Collision.NodeName(settings.Engine, name, index, inline ? name : null),
                        Collision.Prism(shape, source.Pivot),
                        material))
                    .ToList();
                if (inline)
                {
                    shapeNodes = nodes;
                }
                else if (nodes.Count > 0)
                {
                    files.Add(new ExportFile($"{name}_collision.glb", GlbWriter.Write(nodes, $"{name}_collision", textures), null));
                }
            }

            string fileName = settings.Levels > 1 ? $"{name}_LOD{level.Level}.glb" : $"{name}.glb";
            faces.Add(level.Faces);
            wind = level.Wind;
            files.Add(new ExportFile(fileName, GlbWriter.Write([.. meshes, .. shapeNodes], name, textures), level.Faces));
        }

        string asset = AssetJson.Write(name, settings.Engine, settings.Look, faces, wind, source.Description, card);
        if (settings.Materials && settings.Engine == "godot")
        {
            files.AddRange(EngineKits.GodotMaterialFiles(settings.Look));
        }

        files.Add(new ExportFile($"{name}.asset.json", Encoding.UTF8.GetBytes(asset), null));
        return [.. files.OrderBy(file => file.Name, FileNameOrder.Instance)];
    }

    /// <summary>
    /// The export's levels, built one at a time as they are asked for: the parts at the level's share of detail,
    /// merged (and joined, when the settings join). The wind extent is measured once, at level 0, so every level moves
    /// alike; for a prop that sways evenly, its reach is <see cref="Wind.EvenReach"/>. The collision shapes are chosen
    /// once, at level 0, when the settings ask for them.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Levels is not 1, 2 or 3.</exception>
    /// <exception cref="ArgumentException">The engine is not unreal, godot or plain, or the name is empty.</exception>
    public static IEnumerable<ExportLevel> Levels(ExportSource source, ExportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        Check(settings);
        return Iterate(source, settings);
    }

    private static IEnumerable<ExportLevel> Iterate(ExportSource source, ExportSettings settings)
    {
        WindExtent? wind = null;
        for (int level = 0; level < settings.Levels; level++)
        {
            IReadOnlyList<GeneratedPart> parts = source.BuildAtDetail(LevelShares[level]);

            // Measured once at full detail, so every level moves alike.
            if (level == 0)
            {
                wind = Wind.ForExport(parts, source.Pivot, settings.Wind, source.Sways, source.SwaysEvenly);
            }

            // Only the full level is seen up close, where a small relief shows its pixels.
            IReadOnlyList<MergedMesh> merged = Merge.Parts(parts, settings.Name, source.Pivot, wind, level == 0, source.SolidName, settings.Look == ExportLook.Cel);

            // One set of shapes, made once: they stand for every level.
            IReadOnlyList<CollisionShape>? shapes = level == 0 && settings.Collision ? Collision.Choose(source.Collision, parts, source.MaxCollisionShapes) : null;
            yield return new ExportLevel(level, merged, settings.Join ? Merge.Join(merged, settings.Name) : null, wind, shapes);
        }
    }

    private static void Check(ExportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(settings.Name);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Levels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.Levels, LevelShares.Count);
        if (!Engines.Contains(settings.Engine))
        {
            throw new ArgumentException($"No engine '{settings.Engine}': unreal, godot or plain", nameof(settings));
        }

        if (!ExportLook.All.Contains(settings.Look))
        {
            throw new ArgumentException($"No look '{settings.Look}': lit, cartoon or cel", nameof(settings));
        }
    }
}
