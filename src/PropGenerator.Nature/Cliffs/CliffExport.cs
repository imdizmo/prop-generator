using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Cliffs;

/// <summary>The slab cliff as something the port's export pipeline can write: one painted part per detail level, with collision.</summary>
public static class CliffExport
{
    /// <summary>The version of the slab cliff generator the export records (the box-built cliff block was 5 and before).</summary>
    public const int Version = 6;

    /// <summary>The most collision shapes a cliff keeps: a cliff of many blocks needs more than a rock's <see cref="Collision.MaxShapes"/>.</summary>
    public const int MaxCollisionShapes = 32;

    /// <summary>Triangles with less area than this (in square metres) are left out: they draw nothing and only trouble a game engine.</summary>
    private const double MinArea = 1e-12;

    /// <summary>What the cliff's mesh and material are named after: <c>{name}_cliff</c>.</summary>
    private const string SolidName = "cliff";

    private static readonly ReadOnlyCollection<string> AttributeOrder = new(["position", "normal", "color"]);

    /// <summary>
    /// The export source of a cliff: its mesh at any share of detail as one painted, vertex-coloured part in the
    /// export's Y-up space, its blocks' upright collision boxes (see <see cref="CliffChunk.Of"/>; the export keeps up to
    /// <see cref="MaxCollisionShapes"/>, heaviest first), the pivot at the origin (the ground line), and no sway. The
    /// sub-seed is picked once, here, and every level is laid out from it.
    /// </summary>
    /// <param name="s">The cliff's settings.</param>
    /// <param name="appVersion">The app version the description records.</param>
    public static ExportSource Source(CliffSettings s, string appVersion = "console")
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(appVersion);
        var blocks = CliffGenerator.Layout(s);
        var shapes = CliffGenerator.Chunks(s, blocks).Select(Shape).ToList();
        return new ExportSource(
            detail => [Part(CliffGenerator.Build(s, blocks, detail))],
            shapes,
            new Vec3d(0, 0, 0),
            Sways: false,
            SwaysEvenly: false,
            new AssetDescription(CliffParameterSet.Id, Version, s.Seed, "low-poly", Parameters(s), appVersion),
            MaxCollisionShapes,
            SolidName);
    }

    // The chunk as a Y-up box (Z-up (x, y, z) becomes (x, z, -y)): its four corners around both ends, turning as the
    // round ring does, from -z toward -x; the radius is the distance to a corner.
    private static CollisionShape Shape(CliffChunk c)
    {
        double hx = c.HalfX;
        double hz = c.HalfY;
        double corner = Math.Sqrt((hx * hx) + (hz * hz));
        return new CollisionShape(
            new Vec3d(c.Base.X, c.Base.Z, -c.Base.Y),
            new Vec3d(c.Top.X, c.Top.Z, -c.Top.Y),
            corner,
            corner,
            [new Vec3d(-hx, 0, -hz), new Vec3d(-hx, 0, hz), new Vec3d(hx, 0, hz), new Vec3d(hx, 0, -hz)]);
    }

    private static GeneratedPart Part(CliffBlock block)
    {
        int count = block.Positions.Length / 3;
        float[] position = new float[block.Positions.Length];
        float[] normal = new float[block.Normals.Length];
        for (int i = 0; i < count; i++)
        {
            int k = 3 * i;
            position[k] = block.Positions[k];
            position[k + 1] = block.Positions[k + 2];
            position[k + 2] = -block.Positions[k + 1];
            normal[k] = block.Normals[k];
            normal[k + 1] = block.Normals[k + 2];
            normal[k + 2] = -block.Normals[k + 1];
        }

        var index = new List<uint>(block.Indices.Length);
        for (int t = 0; t + 2 < block.Indices.Length; t += 3)
        {
            int a = block.Indices[t];
            int b = block.Indices[t + 1];
            int c = block.Indices[t + 2];
            if (Area(block.Positions, a, b, c) >= MinArea)
            {
                index.AddRange([(uint)a, (uint)b, (uint)c]);
            }
        }

        var mesh = new PropMesh { Position = position, Normal = normal, Color = block.Colours };
        mesh.SetIndex([.. index]);
        return new GeneratedPart(mesh, "painted", "solid", new PaintedSurface(VertexColors: true), AttributeOrder, new Mat4d(), 1);
    }

    private static double Area(float[] p, int a, int b, int c)
    {
        double ux = p[3 * b] - p[3 * a], uy = p[(3 * b) + 1] - p[(3 * a) + 1], uz = p[(3 * b) + 2] - p[(3 * a) + 2];
        double vx = p[3 * c] - p[3 * a], vy = p[(3 * c) + 1] - p[(3 * a) + 1], vz = p[(3 * c) + 2] - p[(3 * a) + 2];
        double x = (uy * vz) - (uz * vy), y = (uz * vx) - (ux * vz), z = (ux * vy) - (uy * vx);
        return 0.5 * Math.Sqrt((x * x) + (y * y) + (z * z));
    }

    /// <summary>The description's parameters: the settings by their parameter keys (the property TopTilt is "top-tilt"), then the seed.</summary>
    /// <param name="s">The settings.</param>
    internal static Dictionary<string, object?> Parameters(CliffSettings s)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (PropertyInfo p in typeof(CliffSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.Name is "Seed" or "EqualityContract")
            {
                continue;
            }

            object? value = p.GetValue(s);
            values[Key(p.Name)] = value is int whole ? (double)whole : value;
        }

        values["seed"] = (double)s.Seed;
        return values;
    }

    private static string Key(string property)
    {
        var key = new StringBuilder();
        foreach (char c in property)
        {
            if (char.IsUpper(c) && key.Length > 0)
            {
                key.Append('-');
            }

            key.Append(char.ToLowerInvariant(c));
        }

        return key.ToString();
    }
}
