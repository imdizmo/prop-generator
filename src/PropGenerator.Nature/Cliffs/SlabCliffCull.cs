using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// A coarse grid over a <see cref="SlabCliffField"/> that ranks, for each cell, the blocks by the least their cheap
/// bounds can be in it, and the joints by the least theirs can be, keeping the <see cref="Ranks"/> lowest of each, so
/// that the field's union looks at a few of them instead of all. Each cell also keeps its blocks' and joints' edge: the
/// least bound of any left off its ranks (+∞ when none is). A block or joint the union reads at a point has a bound
/// under a threshold it knows (for the nearest block, under the smallest bound so far; for a joint, the nearest
/// block's field plus the chips' most; for a block, what the smallest field and the winner's let in); while that is
/// at most the edge, the ones it reads are a run from the start of the cell's ranks, and the field is the same as
/// had all been looked at. A bound's least over a cell is each plane's least over the cell's box, grown by a hair
/// against rounding.
/// <para>
/// The grid has at most <see cref="MostCells"/> cells, each with at most <see cref="Ranks"/> blocks and joints, so it
/// holds at most about 16 MB. It is made once per field, on its first read, the cells on several threads, each on
/// its own.
/// </para>
/// </summary>
internal sealed class SlabCliffCull
{
    /// <summary>How many of the lowest blocks, and of the lowest joints, each cell keeps.</summary>
    public const int Ranks = 40;

    /// <summary>The most cells the grid has.</summary>
    private const int MostCells = 16384;

    /// <summary>The least a cell's side is, in metres.</summary>
    private const double LeastSide = 1;

    private readonly V3 _origin;
    private readonly double _side;
    private readonly int _nx;
    private readonly int _ny;
    private readonly int _nz;
    private readonly int _blockRanks;
    private readonly int _jointRanks;
    private readonly int[] _blocks;
    private readonly double[] _blockLows;
    private readonly double[] _blockEdge;
    private readonly int[] _joints;
    private readonly double[] _jointLows;
    private readonly double[] _jointEdge;

    /// <summary>Makes the grid over a field's bounds.</summary>
    /// <param name="box">The field's bounds.</param>
    /// <param name="bounds">Each block's cheap bound: its <see cref="SlabCliffField.BoundPlanes"/> planes, block by block.</param>
    /// <param name="sideBounds">Each block's cheap side bound: its <see cref="SlabCliffField.SideBoundPlanes"/> planes, block by block.</param>
    /// <param name="joints">Each joint's upper and lower block, and the lower block's top plane.</param>
    /// <param name="reach">How far from a joint's plane its filler reaches, in metres.</param>
    public SlabCliffCull((V3 Min, V3 Max) box, Plane[] bounds, Plane[] sideBounds, (int Upper, int Lower, Plane Top)[] joints, double reach)
    {
        var (min, max) = box;
        var size = max - min;
        _side = Math.Max(JsMath.Pow(size.X * size.Y * size.Z / MostCells, 1.0 / 3), LeastSide);
        _nx = Math.Max((int)Math.Ceiling(size.X / _side), 1);
        _ny = Math.Max((int)Math.Ceiling(size.Y / _side), 1);
        _nz = Math.Max((int)Math.Ceiling(size.Z / _side), 1);
        while ((long)_nx * _ny * _nz > MostCells)
        {
            _side *= 1.05;
            _nx = Math.Max((int)Math.Ceiling(size.X / _side), 1);
            _ny = Math.Max((int)Math.Ceiling(size.Y / _side), 1);
            _nz = Math.Max((int)Math.Ceiling(size.Z / _side), 1);
        }

        _origin = min;
        int cells = _nx * _ny * _nz;
        int n = bounds.Length / SlabCliffField.BoundPlanes;
        int count = joints.Length;
        _blockRanks = Math.Min(n, Ranks);
        _jointRanks = Math.Min(count, Ranks);
        _blocks = new int[cells * _blockRanks];
        _blockLows = new double[cells * _blockRanks];
        _blockEdge = new double[cells];
        _joints = new int[cells * _jointRanks];
        _jointLows = new double[cells * _jointRanks];
        _jointEdge = new double[cells];
        Parallel.For(
            0,
            cells,
            () => (Low: new double[Math.Max(n, count)], Order: new int[Math.Max(n, count)]),
            (c, _, scratch) =>
            {
                var low = _origin + (new V3(c % _nx, c / _nx % _ny, c / (_nx * _ny)) * _side) - new V3(SlabCliffField.Hair, SlabCliffField.Hair, SlabCliffField.Hair);
                var high = low + new V3(_side + (2 * SlabCliffField.Hair), _side + (2 * SlabCliffField.Hair), _side + (2 * SlabCliffField.Hair));
                var centre = (low + high) / 2;
                var half = (high - low) / 2;
                for (int i = 0; i < n; i++)
                {
                    double b = double.NegativeInfinity;
                    for (int k = i * SlabCliffField.BoundPlanes; k < (i + 1) * SlabCliffField.BoundPlanes; k++)
                    {
                        b = Math.Max(b, Span(bounds[k], centre, half).Least);
                    }

                    scratch.Low[i] = b - SlabCliffField.Hair;
                }

                _blockEdge[c] = Rank(scratch.Low, scratch.Order, n, _blockRanks, _blocks, _blockLows, c);
                for (int j = 0; j < count; j++)
                {
                    var (upper, lower, top) = joints[j];
                    var (tLow, tHigh) = Span(top, centre, half);
                    double off = Math.Max(Math.Max(tLow, -tHigh), 0) - reach;
                    double side = Math.Max(SideLeast(sideBounds, upper, centre, half), SideLeast(sideBounds, lower, centre, half));
                    scratch.Low[j] = Math.Max(off, side) - SlabCliffField.Hair;
                }

                _jointEdge[c] = Rank(scratch.Low, scratch.Order, count, _jointRanks, _joints, _jointLows, c);
                return scratch;
            },
            _ => { });
    }

    /// <summary>The cell a point is in, or −1 outside the grid.</summary>
    /// <param name="p">The point.</param>
    public int CellOf(V3 p)
    {
        double x = Math.Floor((p.X - _origin.X) / _side);
        double y = Math.Floor((p.Y - _origin.Y) / _side);
        double z = Math.Floor((p.Z - _origin.Z) / _side);
        return x >= 0 && y >= 0 && z >= 0 && x < _nx && y < _ny && z < _nz ? (int)x + (_nx * ((int)y + (_ny * (int)z))) : -1;
    }

    /// <summary>A cell's ranked blocks, lowest bound first.</summary>
    /// <param name="cell">The cell.</param>
    public ReadOnlySpan<int> Blocks(int cell) => _blocks.AsSpan(cell * _blockRanks, _blockRanks);

    /// <summary>The least each of a cell's ranked blocks' cheap bounds can be in it.</summary>
    /// <param name="cell">The cell.</param>
    public ReadOnlySpan<double> BlockLows(int cell) => _blockLows.AsSpan(cell * _blockRanks, _blockRanks);

    /// <summary>The least the cheap bound of any block left off a cell's ranks can be in it (+∞ when none is).</summary>
    /// <param name="cell">The cell.</param>
    public double BlockEdge(int cell) => _blockEdge[cell];

    /// <summary>A cell's ranked joints, lowest bound first.</summary>
    /// <param name="cell">The cell.</param>
    public ReadOnlySpan<int> Joints(int cell) => _joints.AsSpan(cell * _jointRanks, _jointRanks);

    /// <summary>The least each of a cell's ranked joints' cheap bounds can be in it.</summary>
    /// <param name="cell">The cell.</param>
    public ReadOnlySpan<double> JointLows(int cell) => _jointLows.AsSpan(cell * _jointRanks, _jointRanks);

    /// <summary>The least the cheap bound of any joint left off a cell's ranks can be in it (+∞ when none is).</summary>
    /// <param name="cell">The cell.</param>
    public double JointEdge(int cell) => _jointEdge[cell];

    /// <summary>
    /// Keeps a cell's lowest <paramref name="ranks"/> of <paramref name="count"/> lows (by low, then index) in
    /// <paramref name="into"/> and <paramref name="lows"/>; returns the lowest low left off (+∞ when none is).
    /// </summary>
    private static double Rank(double[] low, int[] order, int count, int ranks, int[] into, double[] lows, int cell)
    {
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, 0, count, Comparer<int>.Create((a, b) => low[a] != low[b] ? low[a].CompareTo(low[b]) : a.CompareTo(b)));
        for (int r = 0; r < ranks; r++)
        {
            into[(cell * ranks) + r] = order[r];
            lows[(cell * ranks) + r] = low[order[r]];
        }

        return count > ranks ? low[order[ranks]] : double.PositiveInfinity;
    }

    /// <summary>The least and most a plane's value is over a box (its centre and half size).</summary>
    private static (double Least, double Most) Span(Plane plane, V3 centre, V3 half)
    {
        double at = plane.At(centre);
        double spread = (Math.Abs(plane.Normal.X) * half.X) + (Math.Abs(plane.Normal.Y) * half.Y) + (Math.Abs(plane.Normal.Z) * half.Z);
        return (at - spread, at + spread);
    }

    /// <summary>The least a block's cheap side bound can be over a box.</summary>
    private static double SideLeast(Plane[] sideBounds, int i, V3 centre, V3 half)
    {
        double b = double.NegativeInfinity;
        for (int k = i * SlabCliffField.SideBoundPlanes; k < (i + 1) * SlabCliffField.SideBoundPlanes; k++)
        {
            b = Math.Max(b, Span(sideBounds[k], centre, half).Least);
        }

        return b;
    }
}
