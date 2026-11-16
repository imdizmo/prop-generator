using System.Runtime.CompilerServices;

namespace PropGenerator.Nature.Cliffs;

/// <summary>The small seeded noise the slab cliff uses: an integer hash and a smooth fractal noise.</summary>
public static class CliffNoise
{
    /// <summary>A repeatable number from 0 to 1 for a seed and a salt (a 32-bit integer mix).</summary>
    /// <param name="seed">The seed.</param>
    /// <param name="salt">Tells apart the numbers drawn from one seed.</param>
    /// <returns>A value in 0 to 1, always the same for the same two inputs.</returns>
    public static double Hash01(uint seed, int salt) => Mix(seed, unchecked((uint)salt), 0, 0, 0) / 4294967296.0;

    /// <summary>
    /// Smooth fractal noise on a point. <paramref name="w"/> is folded in as a seeded offset of the lattice, so each
    /// value of it gives its own noise field and moving it changes the pattern smoothly.
    /// </summary>
    /// <param name="p">The point.</param>
    /// <param name="w">A seed-like offset of the lattice: each value picks a different pattern.</param>
    /// <param name="octaves">How many layers are added, each twice as fine and half as strong.</param>
    /// <returns>A value in 0 to 1 around 0.5, changing smoothly with the point.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Fbm(V3 p, double w, int octaves = 3)
    {
        double total = 0;
        double amplitude = 1;
        double norm = 0;
        double scale = 1;
        double ox = (w * 31.7) + 11.3;
        double oy = (w * 17.9) - 5.1;
        double oz = (w * 23.3) + 7.7;
        for (int i = 0; i < octaves || i == 0; i++)
        {
            total += amplitude * Lattice((p.X * scale) + ox, (p.Y * scale) + oy, (p.Z * scale) + oz);
            norm += amplitude;
            amplitude *= 0.5;
            scale *= 2;
            ox += 19.19;
            oy += 13.37;
            oz += 7.53;
        }

        return total / norm;
    }

    /// <summary>
    /// Piecewise-flat noise: seeded values on the corners of the simplex grid, blended linearly inside each of its
    /// tetrahedra. The field is flat (linear) inside each tetrahedron and joins its neighbours without a step, so any
    /// plane through it is covered by flat facets that meet at straight creases.
    /// </summary>
    /// <param name="p">The point, in grid cells.</param>
    /// <param name="seed">Picks the values.</param>
    /// <returns>A value in 0 to 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static double Facets(V3 p, uint seed)
    {
        Tetrahedron(p, out int x, out int y, out int z, out int a, out int b, out double f0, out double f1, out double f2);
        return ((1 - f0) * Value(seed, x, y, z)) + ((f0 - f1) * Value(seed, x + (a & 1), y + ((a >> 1) & 1), z + (a >> 2)))
            + ((f1 - f2) * Value(seed, x + (b & 1), y + ((b >> 1) & 1), z + (b >> 2))) + (f2 * Value(seed, x + 1, y + 1, z + 1));
    }

    /// <summary>Three piecewise-flat noises at once (see <see cref="Facets"/>), each corner's three values drawn from one hash.</summary>
    /// <param name="p">The point, in grid cells.</param>
    /// <param name="seed">Picks the values.</param>
    /// <returns>Three values, each in 0 to 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static V3 Facets3(V3 p, uint seed)
    {
        Tetrahedron(p, out int x, out int y, out int z, out int a, out int b, out double f0, out double f1, out double f2);
        return (Value3(seed, x, y, z) * (1 - f0)) + (Value3(seed, x + (a & 1), y + ((a >> 1) & 1), z + (a >> 2)) * (f0 - f1))
            + (Value3(seed, x + (b & 1), y + ((b >> 1) & 1), z + (b >> 2)) * (f1 - f2)) + (Value3(seed, x + 1, y + 1, z + 1) * f2);
    }

    /// <summary>A seeded number for one cell of a line (a chip cell along an edge), the same for the whole cell.</summary>
    /// <param name="cell">The cell's number along the line.</param>
    /// <param name="seed">Picks the values.</param>
    /// <param name="line">Tells the lines apart.</param>
    /// <param name="salt">Tells the numbers drawn for one cell apart.</param>
    /// <returns>A value in 0 to 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static double Cell01(int cell, uint seed, int line, uint salt) =>
        Mix(seed, unchecked((uint)cell), unchecked((uint)line), salt, 0xC4117u) / 4294967296.0;

    /// <summary>
    /// Piecewise-flat noise along a line: seeded values at whole numbers, joined by straight segments (see
    /// <see cref="Facets"/>), one pattern per line number.
    /// </summary>
    /// <param name="u">The place along the line, in grid cells.</param>
    /// <param name="seed">Picks the values.</param>
    /// <param name="line">Tells the lines apart.</param>
    /// <returns>A value in 0 to 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static double Facets1(double u, uint seed, int line)
    {
        double f = Math.Floor(u);
        int i = (int)f;
        double t = u - f;
        double a = Mix(seed, unchecked((uint)i), unchecked((uint)line), 0x11E5u, 0x5EEDu) / 4294967296.0;
        double b = Mix(seed, unchecked((uint)(i + 1)), unchecked((uint)line), 0x11E5u, 0x5EEDu) / 4294967296.0;
        return a + ((b - a) * t);
    }

    /// <summary>
    /// The simplex grid's tetrahedron around a point: the point is skewed onto a cube grid, whose cell is cut into six
    /// tetrahedra by walking its corners from the low one to the high one an axis at a time, the axis the point is
    /// furthest along first. Gives the low corner, the first two steps' corners as bit masks (x 1, y 2, z 4) and the
    /// point's fractions along the three steps, largest first: the corners' weights are 1 − f0, f0 − f1, f1 − f2, f2.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Tetrahedron(V3 p, out int x, out int y, out int z, out int a, out int b, out double f0, out double f1, out double f2)
    {
        double skew = (p.X + p.Y + p.Z) / 3;
        double sx = p.X + skew;
        double sy = p.Y + skew;
        double sz = p.Z + skew;
        double fx = Math.Floor(sx);
        double fy = Math.Floor(sy);
        double fz = Math.Floor(sz);
        x = (int)fx;
        y = (int)fy;
        z = (int)fz;
        double dx = sx - fx;
        double dy = sy - fy;
        double dz = sz - fz;
        if (dx >= dy)
        {
            if (dy >= dz)
            {
                (a, b, f0, f1, f2) = (1, 3, dx, dy, dz);
            }
            else if (dx >= dz)
            {
                (a, b, f0, f1, f2) = (1, 5, dx, dz, dy);
            }
            else
            {
                (a, b, f0, f1, f2) = (4, 5, dz, dx, dy);
            }
        }
        else if (dx >= dz)
        {
            (a, b, f0, f1, f2) = (2, 3, dy, dx, dz);
        }
        else if (dy >= dz)
        {
            (a, b, f0, f1, f2) = (2, 6, dy, dz, dx);
        }
        else
        {
            (a, b, f0, f1, f2) = (4, 6, dz, dy, dx);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double Value(uint seed, int x, int y, int z) => Mix(seed, unchecked((uint)x), unchecked((uint)y), unchecked((uint)z), 0x5EEDu) / 4294967296.0;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static V3 Value3(uint seed, int x, int y, int z)
    {
        uint h = Mix(seed, unchecked((uint)x), unchecked((uint)y), unchecked((uint)z), 0xFACEu);
        return new V3((h & 0x7FF) / 2047.0, ((h >> 11) & 0x7FF) / 2047.0, (h >> 22) / 1023.0);
    }

    /// <summary>Lattice cells whose corner values each thread keeps, the newest in each place (a power of two).</summary>
    private const int CachedCells = 512;

    // Each thread's cache of lattice cells: three coordinates and eight corner values per place, a cell's place set by a
    // hash of its coordinates. A nearby point reads the same cells, so most corners are not hashed again; the numbers
    // are the same either way. About 40 KB per thread that reads noise.
    [ThreadStatic]
    private static int[]? _threadCells;

    [ThreadStatic]
    private static double[]? _threadCorners;

    /// <summary>Value noise on a three-dimensional lattice, with a smooth fade between the lattice points.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double Lattice(double x, double y, double z)
    {
        double fx = Math.Floor(x);
        double fy = Math.Floor(y);
        double fz = Math.Floor(z);
        int ix = (int)fx;
        int iy = (int)fy;
        int iz = (int)fz;
        int[] cells = _threadCells ??= NewCells();
        double[] corners = _threadCorners ??= new double[8 * CachedCells];
        int place = (int)(unchecked((uint)((ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791))) % CachedCells);
        int at = 8 * place;
        if (cells[3 * place] != ix || cells[(3 * place) + 1] != iy || cells[(3 * place) + 2] != iz)
        {
            corners[at] = Corner(ix, iy, iz);
            corners[at + 1] = Corner(ix + 1, iy, iz);
            corners[at + 2] = Corner(ix, iy + 1, iz);
            corners[at + 3] = Corner(ix + 1, iy + 1, iz);
            corners[at + 4] = Corner(ix, iy, iz + 1);
            corners[at + 5] = Corner(ix + 1, iy, iz + 1);
            corners[at + 6] = Corner(ix, iy + 1, iz + 1);
            corners[at + 7] = Corner(ix + 1, iy + 1, iz + 1);
            cells[3 * place] = ix;
            cells[(3 * place) + 1] = iy;
            cells[(3 * place) + 2] = iz;
        }

        double tx = Fade(x - fx);
        double ty = Fade(y - fy);
        double tz = Fade(z - fz);
        double x00 = Lerp(corners[at], corners[at + 1], tx);
        double x10 = Lerp(corners[at + 2], corners[at + 3], tx);
        double x01 = Lerp(corners[at + 4], corners[at + 5], tx);
        double x11 = Lerp(corners[at + 6], corners[at + 7], tx);
        return Lerp(Lerp(x00, x10, ty), Lerp(x01, x11, ty), tz);
    }

    /// <summary>An empty cache: no place holds a cell (no point's cell has these coordinates).</summary>
    private static int[] NewCells()
    {
        int[] cells = new int[3 * CachedCells];
        Array.Fill(cells, int.MinValue);
        return cells;
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double Corner(int x, int y, int z) => Mix(0x9E3779B9u, unchecked((uint)x), unchecked((uint)y), unchecked((uint)z), 0) / 4294967296.0;

    private static double Fade(double t) => t * t * t * (t * ((t * 6) - 15) + 10);

    /// <summary>Mixes five integers into one well-spread 32-bit number (xxhash-style rounds).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static uint Mix(uint a, uint b, uint c, uint d, uint e)
    {
        uint h = 0x165667B1u + a;
        h += b * 0xC2B2AE3Du;
        h = ((h << 17) | (h >> 15)) * 0x27D4EB2Fu;
        h += c * 0xC2B2AE3Du;
        h = ((h << 17) | (h >> 15)) * 0x27D4EB2Fu;
        h += d * 0xC2B2AE3Du;
        h = ((h << 17) | (h >> 15)) * 0x27D4EB2Fu;
        h += e * 0xC2B2AE3Du;
        h = ((h << 17) | (h >> 15)) * 0x27D4EB2Fu;

        h ^= h >> 15;
        h *= 0x85EBCA77u;
        h ^= h >> 13;
        h *= 0xC2B2AE3Du;
        h ^= h >> 16;
        return h;
    }
}
