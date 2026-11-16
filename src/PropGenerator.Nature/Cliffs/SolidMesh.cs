namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// A solid made of triangles: a list of points, a number per point (its tag), and three point indices per triangle,
/// counter-clockwise seen from outside. Each piece of a slab cliff is meshed as one of these before it is shaded.
/// </summary>
public sealed class SolidMesh
{
    /// <summary>The points.</summary>
    public List<V3> Points { get; } = [];

    /// <summary>
    /// A number per point, as many as <see cref="Points"/>: how far the surface was cut in at the point, to darken it.
    /// </summary>
    public List<double> Tags { get; } = [];

    /// <summary>Three point indices per triangle, counter-clockwise seen from outside.</summary>
    public List<int> Indices { get; } = [];

    /// <summary>The number of triangles.</summary>
    public int TriangleCount => Indices.Count / 3;

    /// <summary>
    /// Drops the shells no one should see: an inside-out shell (a pocket sealed inside the rock) and any shell with less
    /// than <paramref name="share"/> of the largest one's volume (a crumb a cut left loose). The points and triangles left
    /// keep their order, and so do their tags.
    /// </summary>
    /// <param name="share">The smallest share of the largest shell's volume a shell may have and stay.</param>
    /// <returns>How many shells were dropped.</returns>
    internal int DropLooseShells(double share)
    {
        int count = Points.Count;
        int[] root = new int[count];
        for (int i = 0; i < count; i++)
        {
            root[i] = i;
        }

        for (int t = 0; t < Indices.Count; t += 3)
        {
            Join(root, Indices[t], Indices[t + 1]);
            Join(root, Indices[t], Indices[t + 2]);
        }

        // Each shell's volume: the signed volumes of the tetrahedra from a point near the mesh to its triangles.
        var origin = count > 0 ? Points[0] : default;
        var volume = new Dictionary<int, double>();
        for (int t = 0; t < Indices.Count; t += 3)
        {
            int shell = Find(root, Indices[t]);
            var a = Points[Indices[t]] - origin;
            var b = Points[Indices[t + 1]] - origin;
            var c = Points[Indices[t + 2]] - origin;
            volume[shell] = volume.GetValueOrDefault(shell) + (V3.Dot(a, V3.Cross(b, c)) / 6);
        }

        if (volume.Count <= 1)
        {
            return 0;
        }

        double largest = volume.Values.Max();
        var keep = volume.Where(pair => pair.Value > 0 && pair.Value >= share * largest).Select(pair => pair.Key).ToHashSet();
        if (keep.Count == volume.Count)
        {
            return 0;
        }

        int[] moved = new int[count];
        var points = new List<V3>();
        var tags = new List<double>();
        for (int i = 0; i < count; i++)
        {
            moved[i] = -1;
            if (keep.Contains(Find(root, i)))
            {
                moved[i] = points.Count;
                points.Add(Points[i]);
                if (Tags.Count == count)
                {
                    tags.Add(Tags[i]);
                }
            }
        }

        var indices = new List<int>();
        for (int t = 0; t < Indices.Count; t += 3)
        {
            if (moved[Indices[t]] >= 0)
            {
                indices.Add(moved[Indices[t]]);
                indices.Add(moved[Indices[t + 1]]);
                indices.Add(moved[Indices[t + 2]]);
            }
        }

        Points.Clear();
        Points.AddRange(points);
        Tags.Clear();
        Tags.AddRange(tags);
        Indices.Clear();
        Indices.AddRange(indices);
        return volume.Count - keep.Count;
    }

    private static int Find(int[] root, int i)
    {
        while (root[i] != i)
        {
            i = root[i] = root[root[i]];
        }

        return i;
    }

    private static void Join(int[] root, int a, int b)
    {
        int ra = Find(root, a);
        int rb = Find(root, b);
        if (ra != rb)
        {
            root[rb] = ra;
        }
    }
}
