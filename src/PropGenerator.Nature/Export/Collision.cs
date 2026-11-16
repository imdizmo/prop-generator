// Ported from easy-props frontend/src/core/export/pipeline/collision.ts (MIT, © 2026 AHS12).
using System.Globalization;
using PropGenerator.Nature.Js;
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Nature.Export;

/// <summary>
/// Collision shapes. Each is a tapered prism, two parallel rings joined up: convex by construction, so there is nothing to
/// decompose and an engine can use it as it is.
/// </summary>
public static class Collision
{
    /// <summary>The most shapes a prop gets, unless its export source allows more (a cliff allows 32).</summary>
    public const int MaxShapes = 8;

    /// <summary>The sides of each prism's rings.</summary>
    public const int Sides = 8;

    /// <summary>How much of a part's size counts as its ends, when making a shape for it.</summary>
    public const double EndBand = 0.1;

    /// <summary>
    /// easy-props' <c>collisionShapes</c>: the shapes offered, or, when none are, one per solid part (not a proxy, not
    /// foliage) from the ends of its placed box; heaviest first (length times the summed radii squared), in the order
    /// V8's <c>Array.prototype.sort</c> gives (see <see cref="SortHeaviestFirst"/>), never more than
    /// <paramref name="maxShapes"/>.
    /// </summary>
    /// <param name="offered">The shapes the prop offers; empty lets its parts make their own.</param>
    /// <param name="parts">The prop's parts at full detail.</param>
    /// <param name="maxShapes">The most shapes kept: <see cref="MaxShapes"/> unless the prop allows more.</param>
    public static IReadOnlyList<CollisionShape> Choose(IReadOnlyList<CollisionShape> offered, IReadOnlyList<GeneratedPart> parts, int maxShapes = MaxShapes)
    {
        ArgumentNullException.ThrowIfNull(offered);
        ArgumentNullException.ThrowIfNull(parts);
        var shapes = offered.Count > 0
            ? [.. offered]
            : parts
                .Where(part => !part.IsProxy && part.RoleOfPart != "foliage")
                .Select(ShapeOfPart)
                .OfType<CollisionShape>()
                .ToList();

        var weights = shapes.Select(WeightOf).ToList();
        var order = Enumerable.Range(0, shapes.Count).ToList();
        SortHeaviestFirst(order, weights);
        return [.. order.Take(maxShapes).Select(i => shapes[i])];
    }

    /// <summary>
    /// easy-props' <c>prismGeometry</c>: a closed prism of <see cref="Sides"/> sides, faces pointing out, relative to the
    /// pivot; positions and normals (computed as three computes them), a 16- or 32-bit index as three picks. A shape with
    /// a polygon ring (see <see cref="CollisionShape.Ring"/>) is the prism of that polygon instead, its corners in turn.
    /// </summary>
    public static PropMesh Prism(CollisionShape shape, Vec3d pivot)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(pivot);
        Vec3d start = shape.Start.Clone().Sub(pivot);
        Vec3d end = shape.End.Clone().Sub(pivot);
        if (shape.Ring is { } ring)
        {
            return Polygon(start, end, ring);
        }

        Vec3d axis = end.Clone().Sub(start).Normalize();
        Vec3d helper = Math.Abs(axis.Y) > 0.9 ? new Vec3d(1, 0, 0) : new Vec3d(0, 1, 0);
        Vec3d u = new Vec3d().CrossVectors(axis, helper).Normalize();
        Vec3d v = new Vec3d().CrossVectors(axis, u);
        var positions = new List<float>(Sides * 6);
        var indices = new List<uint>();

        foreach (var (centre, radius) in new[] { (start, shape.R0), (end, shape.R1) })
        {
            for (int side = 0; side < Sides; side++)
            {
                double angle = ((double)side / Sides) * Math.PI * 2;
                Vec3d point = centre.Clone()
                    .AddScaledVector(u, JsMath.Cos(angle) * radius)
                    .AddScaledVector(v, JsMath.Sin(angle) * radius);
                positions.Add((float)point.X);
                positions.Add((float)point.Y);
                positions.Add((float)point.Z);
            }
        }

        for (uint side = 0; side < Sides; side++)
        {
            uint next = (side + 1) % Sides;
            indices.AddRange([side, next, Sides + next, side, Sides + next, Sides + side]);
        }

        for (uint side = 1; side < Sides - 1; side++)
        {
            indices.AddRange([0, side + 1, side]);
            indices.AddRange([Sides, Sides + side, Sides + side + 1]);
        }

        var geometry = new PropMesh { Position = [.. positions] };
        geometry.SetIndex([.. indices]);
        MeshOps.ComputeVertexNormals(geometry);
        return geometry;
    }

    /// <summary>The prism of a polygon ring: its corners around the start, then around the end, closed as the round prism is.</summary>
    private static PropMesh Polygon(Vec3d start, Vec3d end, IReadOnlyList<Vec3d> ring)
    {
        uint sides = (uint)ring.Count;
        var positions = new List<float>((int)sides * 6);
        foreach (Vec3d centre in new[] { start, end })
        {
            foreach (Vec3d corner in ring)
            {
                Vec3d point = centre.Clone().Add(corner);
                positions.Add((float)point.X);
                positions.Add((float)point.Y);
                positions.Add((float)point.Z);
            }
        }

        var indices = new List<uint>();
        for (uint side = 0; side < sides; side++)
        {
            uint next = (side + 1) % sides;
            indices.AddRange([side, next, sides + next, side, sides + next, sides + side]);
        }

        for (uint side = 1; side < sides - 1; side++)
        {
            indices.AddRange([0, side + 1, side]);
            indices.AddRange([sides, sides + side, sides + side + 1]);
        }

        var geometry = new PropMesh { Position = [.. positions] };
        geometry.SetIndex([.. indices]);
        MeshOps.ComputeVertexNormals(geometry);
        return geometry;
    }

    /// <summary>
    /// Sorts <paramref name="order"/> (indices into <paramref name="weights"/>) as V8's <c>Array.prototype.sort</c> sorts
    /// with easy-props' comparator <c>(a, b) =&gt; weightOf(b) - weightOf(a)</c>, a comparison that gives NaN counting as
    /// equal. V8 sorts with TimSort; a list shorter than 64 is one run: the longest run at its start (turned round when
    /// it goes strictly the wrong way), then binary insertion of the rest (V8's <c>CountAndMakeRun</c> and
    /// <c>BinaryInsertionSort</c>, third_party/v8/builtins/array-sort.tq). That is exact for any weights, NaN and
    /// infinities too. A list of 64 or more (TimSort then merges runs; no prop ported so far comes near it) is sorted
    /// stably by insertion, which agrees with TimSort only while every weight is finite.
    /// </summary>
    private static void SortHeaviestFirst(List<int> order, List<double> weights)
    {
        // comparefn(x, y) as V8 reads it: NaN is 0.
        int Compare(int x, int y)
        {
            double result = weights[y] - weights[x];
            return double.IsNaN(result) || result == 0 ? 0 : result < 0 ? -1 : 1;
        }

        int length = order.Count;
        if (length < 2)
        {
            return;
        }

        if (length >= 64)
        {
            for (int i = 1; i < length; i++)
            {
                int moving = order[i];
                int at = i;
                while (at > 0 && Compare(moving, order[at - 1]) < 0)
                {
                    order[at] = order[at - 1];
                    at--;
                }

                order[at] = moving;
            }

            return;
        }

        // CountAndMakeRun(0, length).
        int run = 2;
        bool descending = Compare(order[1], order[0]) < 0;
        for (int i = 2; i < length; i++)
        {
            int compared = Compare(order[i], order[i - 1]);
            if (descending ? compared >= 0 : compared < 0)
            {
                break;
            }

            run++;
        }

        if (descending)
        {
            order.Reverse(0, run);
        }

        // BinaryInsertionSort(0, run, length).
        for (int start = run; start < length; start++)
        {
            int pivot = order[start];
            int left = 0;
            int right = start;
            while (left < right)
            {
                int middle = left + ((right - left) >> 1);
                if (Compare(pivot, order[middle]) < 0)
                {
                    right = middle;
                }
                else
                {
                    left = middle + 1;
                }
            }

            for (int p = start; p > left; p--)
            {
                order[p] = order[p - 1];
            }

            order[left] = pivot;
        }
    }

    /// <summary>A shape's weight: its length times the square of its two radii summed.</summary>
    private static double WeightOf(CollisionShape shape) =>
        JsMath.Hypot(shape.End.X - shape.Start.X, shape.End.Y - shape.Start.Y, shape.End.Z - shape.Start.Z)
        * JsMath.Pow(shape.R0 + shape.R1, 2);

    /// <summary>
    /// easy-props' <c>shapeOfPart</c>, for a solid part of a prop that offers no shapes: along the longest side of its
    /// placed box, the points within a tenth of that side of each end, averaged into a centre and a mean radius. Null for
    /// a part with no points or whose ends meet.
    /// </summary>
    private static CollisionShape? ShapeOfPart(GeneratedPart placed)
    {
        float[] source = placed.Mesh.Position;
        int count = source.Length / 3;
        if (count == 0)
        {
            return null;
        }

        var matrix = new Mat4d().FromArray(placed.Matrix.Elements);
        var point = new Vec3d();
        var points = new List<Vec3d>(count);
        var box = new Box3d();
        for (int i = 0; i < count; i++)
        {
            point.FromBufferAttribute(source, i).ApplyMatrix4(matrix);
            points.Add(point.Clone());
            box.ExpandByPoint(point);
        }

        Vec3d size = box.GetSize(new Vec3d());
        int axis = size.Y >= size.X && size.Y >= size.Z ? 1 : size.X >= size.Z ? 0 : 2;
        double band = Get(size, axis) * EndBand;

        (Vec3d Centre, double Radius) EndOf(double near)
        {
            var inBand = points.Where(p => Math.Abs(Get(p, axis) - near) <= band).ToList();
            var centre = new Vec3d();
            foreach (Vec3d p in inBand)
            {
                centre.Add(p);
            }

            centre.DivideScalar(inBand.Count);
            double radius = 0;
            foreach (Vec3d p in inBand)
            {
                radius += p.DistanceTo(centre);
            }

            return (centre, radius / inBand.Count);
        }

        var (lowCentre, lowRadius) = EndOf(Get(box.Min, axis));
        var (highCentre, highRadius) = EndOf(Get(box.Max, axis));
        return lowCentre.DistanceTo(highCentre) < 1e-3
            ? null
            : new CollisionShape(lowCentre, highCentre, lowRadius, highRadius);
    }

    private static double Get(Vec3d v, int axis) => axis switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z,
    };

    /// <summary>
    /// The node name that tells an engine a mesh is for colliding: Unreal's <c>UCX_{owner}_NN</c> (owner
    /// <c>{name}_LOD0</c>, or <paramref name="owner"/> when the shapes ride in the same file), Godot's
    /// <c>{name}_N-convcolonly</c>, and <c>{name}_collision_N</c> for a plain GLB (any other engine).
    /// </summary>
    public static string NodeName(string engine, string name, int index, string? owner = null)
    {
        string number = index.ToString(CultureInfo.InvariantCulture);
        return engine switch
        {
            "unreal" => $"UCX_{owner ?? $"{name}_LOD0"}_{number.PadLeft(2, '0')}",
            "godot" => $"{name}_{number}-convcolonly",
            _ => $"{name}_collision_{number}",
        };
    }
}
