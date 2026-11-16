using System.Collections.ObjectModel;
using PropGenerator.Nature.Cliffs;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Stylized;

/// <summary>
/// A finished stylized rock, ready to draw or export: Z up, in metres, sitting on the ground line z = 0, every facet
/// flat (each triangle has its own three vertices, all with the facet's normal and colour).
/// </summary>
/// <param name="Positions">x, y, z of each vertex.</param>
/// <param name="Normals">The unit normal of each vertex.</param>
/// <param name="Colours">The linear red, green and blue of each vertex.</param>
/// <param name="Indices">Three vertex indices per triangle, counter-clockwise seen from outside.</param>
/// <param name="Radius">How far the rock reaches from its axis, in metres: the collision prism's radius.</param>
/// <param name="Top">How high the rock reaches above the ground, in metres: the collision prism's height.</param>
public sealed record StylizedRock(float[] Positions, float[] Normals, float[] Colours, int[] Indices, double Radius, double Top);

/// <summary>
/// Builds a stylized rock the way the one-minute Blender tutorial does: a cube subdivided into a sphere, each vertex
/// pushed along its radius by Voronoi noise (bumps where the cells meet, hollows at their centres), squashed to the
/// rock's size, then thinned by quadric edge collapse (the tutorial's two decimates and the dissolve) until only facets
/// the Facets slider allows are left. Then every facet is shaded flat and coloured from the palette by how far it faces up.
/// </summary>
public static class StylizedRockGenerator
{
    /// <summary>The version of the generator the export records and the drawer's pictures are keyed by.</summary>
    public const int Version = 1;

    /// <summary>What the rock's mesh and material are named after: <c>{name}_rock</c>.</summary>
    public const string SolidName = "rock";

    /// <summary>How many cells each face of the cube is cut into, per side, before it is rounded into a sphere.</summary>
    private const int Grid = 24;

    /// <summary>The farthest a facet's colour is moved along the ramp from its neighbours', at Variation 1.</summary>
    private const double FacetShift = 0.16;

    private static readonly ReadOnlyCollection<string> AttributeOrder = new(["position", "normal", "color"]);

    /// <summary>Builds the rock for these settings at a share of its full detail.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="detail">The share of full detail: 1, 0.4 or 0.12. A lower share allows larger facets.</param>
    /// <returns>The rock.</returns>
    public static StylizedRock Build(StylizedRockSettings s, double detail)
    {
        ArgumentNullException.ThrowIfNull(s);
        SolidMesh mesh = Solid(s, detail);
        var ramp = CliffColours.RampFor(s.Palette);
        int triangles = mesh.TriangleCount;
        float[] positions = new float[9 * triangles];
        float[] normals = new float[9 * triangles];
        float[] colours = new float[9 * triangles];
        int[] indices = new int[3 * triangles];
        double radius = 0;
        double top = 0;
        for (int t = 0; t < triangles; t++)
        {
            V3 a = mesh.Points[mesh.Indices[3 * t]];
            V3 b = mesh.Points[mesh.Indices[(3 * t) + 1]];
            V3 c = mesh.Points[mesh.Indices[(3 * t) + 2]];
            V3 n = V3.Cross(b - a, c - a).Normalized();
            V3 centre = (a + b + c) / 3;
            double value = CliffColours.Value(n.Z, CliffColours.Shift(s.Seed, centre, s.Variation), 0)
                + ((CliffNoise.Hash01(s.Seed, t) - 0.5) * FacetShift * s.Variation);
            Color3d colour = ramp.Colour(value);
            for (int k = 0; k < 3; k++)
            {
                V3 p = k == 0 ? a : k == 1 ? b : c;
                int at = (9 * t) + (3 * k);
                positions[at] = (float)p.X;
                positions[at + 1] = (float)p.Y;
                positions[at + 2] = (float)p.Z;
                normals[at] = (float)n.X;
                normals[at + 1] = (float)n.Y;
                normals[at + 2] = (float)n.Z;
                colours[at] = (float)colour.R;
                colours[at + 1] = (float)colour.G;
                colours[at + 2] = (float)colour.B;
                radius = Math.Max(radius, Math.Sqrt((p.X * p.X) + (p.Y * p.Y)));
                top = Math.Max(top, p.Z);
            }

            indices[3 * t] = 3 * t;
            indices[(3 * t) + 1] = (3 * t) + 1;
            indices[(3 * t) + 2] = (3 * t) + 2;
        }

        return new StylizedRock(positions, normals, colours, indices, radius, top);
    }

    /// <summary>
    /// The export source of a rock: its mesh at any share of detail as one painted, vertex-coloured part in the export's
    /// Y-up space, one upright eight-sided prism around it to bump into, the pivot at the origin (the ground line), and
    /// no sway.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="appVersion">The app version the description records.</param>
    public static ExportSource Source(StylizedRockSettings s, string appVersion = "console")
    {
        ArgumentNullException.ThrowIfNull(s);
        StylizedRock full = Build(s, 1);
        return new ExportSource(
            detail => [Part(detail == 1 ? full : Build(s, detail))],
            [new CollisionShape(new Vec3d(0, 0, 0), new Vec3d(0, full.Top, 0), full.Radius, full.Radius)],
            new Vec3d(0, 0, 0),
            Sways: false,
            SwaysEvenly: false,
            new AssetDescription(StylizedRockParameterSet.Id, Version, s.Seed, "low-poly", s.Parameters(), appVersion),
            SolidName: SolidName);
    }

    /// <summary>
    /// The rock before shading: a closed, consistently wound solid, Z up, in metres, its lowest point on z = 0: the
    /// displaced sphere thinned to its facets.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="detail">The share of full detail; the facets' error stop grows as its square root shrinks.</param>
    internal static SolidMesh Solid(StylizedRockSettings s, double detail)
    {
        SolidMesh mesh = Sphere(Grid);
        double half = s.Size / 2;
        double cells = s.Bumps / 2;
        for (int i = 0; i < mesh.Points.Count; i++)
        {
            V3 p = mesh.Points[i];
            double r = 1 + (s.Bumpiness * (Voronoi(p * cells, s.Seed) - 0.5));
            mesh.Points[i] = new V3(p.X * r * half, p.Y * r * half, p.Z * r * half * s.Height);
        }

        // ponytail: one error stop stands in for the tutorial's two decimates and the hand dissolve; the look is the same knob.
        MeshSimplify.Simplify(mesh, s.Facets * s.Size / Math.Sqrt(detail), []);

        double floor = mesh.Points.Min(p => p.Z);
        for (int i = 0; i < mesh.Points.Count; i++)
        {
            mesh.Points[i] -= new V3(0, 0, floor);
        }

        return mesh;
    }

    /// <summary>
    /// A unit sphere from a cube of six faces each cut into <paramref name="n"/> × <paramref name="n"/> cells, its
    /// points pushed out to the sphere, the faces' shared edges and corners welded, every triangle wound outward.
    /// </summary>
    internal static SolidMesh Sphere(int n)
    {
        var mesh = new SolidMesh();
        var index = new Dictionary<(int, int, int), int>();
        int Point(int x, int y, int z)
        {
            if (!index.TryGetValue((x, y, z), out int at))
            {
                at = mesh.Points.Count;
                index[(x, y, z)] = at;
                mesh.Points.Add(new V3(x, y, z).Normalized());
            }

            return at;
        }

        void Triangle(int a, int b, int c)
        {
            V3 pa = mesh.Points[a], pb = mesh.Points[b], pc = mesh.Points[c];
            if (V3.Dot(V3.Cross(pb - pa, pc - pa), pa + pb + pc) < 0)
            {
                (b, c) = (c, b);
            }

            mesh.Indices.Add(a);
            mesh.Indices.Add(b);
            mesh.Indices.Add(c);
        }

        for (int axis = 0; axis < 3; axis++)
        {
            foreach (int sign in new[] { -1, 1 })
            {
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        int p00 = Corner(axis, sign, n, i, j, Point);
                        int p10 = Corner(axis, sign, n, i + 1, j, Point);
                        int p01 = Corner(axis, sign, n, i, j + 1, Point);
                        int p11 = Corner(axis, sign, n, i + 1, j + 1, Point);
                        Triangle(p00, p10, p11);
                        Triangle(p00, p11, p01);
                    }
                }
            }
        }

        return mesh;
    }

    /// <summary>
    /// Voronoi F1: the distance from a point to the nearest of the seeded feature points, one per unit cell, held to 1.
    /// Smallest at a feature point (a hollow), largest where cells meet (a ridge).
    /// </summary>
    internal static double Voronoi(V3 q, uint seed)
    {
        int cx = (int)Math.Floor(q.X), cy = (int)Math.Floor(q.Y), cz = (int)Math.Floor(q.Z);
        double best = 1;
        for (int x = cx - 1; x <= cx + 1; x++)
        {
            for (int y = cy - 1; y <= cy + 1; y++)
            {
                for (int z = cz - 1; z <= cz + 1; z++)
                {
                    int salt = unchecked((x * 73856093) ^ (y * 19349663) ^ (z * 83492791));
                    var feature = new V3(
                        x + CliffNoise.Hash01(seed, salt),
                        y + CliffNoise.Hash01(unchecked(seed + 0x9E3779B9u), salt),
                        z + CliffNoise.Hash01(unchecked(seed + 0x7F4A7C15u), salt));
                    best = Math.Min(best, (feature - q).Length);
                }
            }
        }

        return best;
    }

    // The lattice point of a face's cell corner: the face's axis held at ±n, the other two running -n..n in steps of 2.
    private static int Corner(int axis, int sign, int n, int i, int j, Func<int, int, int, int> point)
    {
        int[] c = new int[3];
        c[axis] = sign * n;
        c[(axis + 1) % 3] = (2 * i) - n;
        c[(axis + 2) % 3] = (2 * j) - n;
        return point(c[0], c[1], c[2]);
    }

    // The rock as a Y-up painted part (Z-up (x, y, z) becomes (x, z, -y)).
    private static GeneratedPart Part(StylizedRock rock)
    {
        int count = rock.Positions.Length / 3;
        float[] position = new float[rock.Positions.Length];
        float[] normal = new float[rock.Normals.Length];
        for (int i = 0; i < count; i++)
        {
            int k = 3 * i;
            position[k] = rock.Positions[k];
            position[k + 1] = rock.Positions[k + 2];
            position[k + 2] = -rock.Positions[k + 1];
            normal[k] = rock.Normals[k];
            normal[k + 1] = rock.Normals[k + 2];
            normal[k + 2] = -rock.Normals[k + 1];
        }

        var mesh = new PropMesh { Position = position, Normal = normal, Color = rock.Colours };
        mesh.SetIndex([.. rock.Indices.Select(i => (uint)i)]);
        return new GeneratedPart(mesh, "painted", "solid", new PaintedSurface(VertexColors: true), AttributeOrder, new Mat4d(), 1);
    }
}
