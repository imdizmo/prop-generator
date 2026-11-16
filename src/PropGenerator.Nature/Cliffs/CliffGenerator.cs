using System.Runtime.CompilerServices;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// Builds a slab cliff from its settings: the blocks <see cref="CliffLayout"/> lays out from the sub-seed
/// <see cref="CliffRules"/> picks (each the slab-rock formula: a convex shell of planes minus convex wedge cuts), joined
/// by <see cref="SlabCliffField"/> (hard-joined side by side with a narrow V-crack, with a bedding groove between plates
/// and tiers) with the surface detail pushed into them, as a signed distance field meshed by dual contouring, so the
/// surface is always closed (the few folds a slider mix can make are held to a budget, see <see cref="MeshFold"/>).
/// The pivot is the ground line z = 0, and the flat bottom lies Sink below it.
/// </summary>
public static class CliffGenerator
{
    /// <summary>The most triangles a cliff may have; denser cliffs get a coarser grid.</summary>
    public const int MaxTriangles = 200_000;

    /// <summary>How much the surface's triangles may outnumber a flat box's for the same area (slopes, bumps).</summary>
    private const double SurfaceAllowance = 1.2;

    /// <summary>
    /// The most grid points one part's grid may have (the mesher keeps about 26 bytes a point while it runs, so about
    /// 200 MB); a part whose bounds would need more gets a coarser grid.
    /// </summary>
    private const long MaxGridPoints = 8_000_000;

    /// <summary>The triangle budgets never make a level's cells coarser than the cliff's smallest size over this.</summary>
    private const double ExtentCells = 3;

    /// <summary>How much finer each retry of a level with no rock at its own grid is.</summary>
    private const double RetryStep = 1.5;

    /// <summary>
    /// A lower level with at most this many triangles is checked against the level above it: a sliver that small can
    /// land in more cells of a coarser grid, and then the level takes the level above's lighter parts.
    /// </summary>
    private const int TinyTriangles = 1000;

    /// <summary>A shell of a part with less than this share of the part's largest shell's volume is dropped as a crumb.</summary>
    private const double LooseShare = 0.01;

    /// <summary>The narrowest groove a level's mesh can show, in its grid cells: the bedding grooves are made wider.</summary>
    private const double GrooveCells = 1.5;

    /// <summary>
    /// The error stop of the thinning (see <see cref="MeshSimplify"/>), in the level's grid cells: no collapse moves a
    /// point this far from the planes of the triangles it gathered. Every level thins by the same rule.
    /// </summary>
    internal const double SimplifyCells = 0.15;

    /// <summary>
    /// The finished cliff: its parts (see <see cref="Pieces(CliffSettings, double)"/>) shaded crisp along sharp edges
    /// and coloured, where they lie, with its blocks' collision boxes (see <see cref="CliffChunk.Of"/>). The sub-seed is
    /// picked once (see <see cref="Layout"/>).
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="detail">The detail level's share of the points per metre: 1 for full detail.</param>
    /// <returns>The cliff.</returns>
    /// <exception cref="ArgumentException">Width, Depth, Height, Detail or Chisel size is not above 0.</exception>
    public static CliffBlock Build(CliffSettings s, double detail) => Build(s, Layout(s), detail);

    /// <summary>
    /// The finished cliff (see <see cref="Build(CliffSettings, double)"/>) from laid blocks already picked, so the levels
    /// of one cliff can share one pick; its collision chunks are its blocks' (see <see cref="CliffChunk.Of"/>).
    /// </summary>
    /// <param name="s">The settings, already checked.</param>
    /// <param name="blocks">The cliff's laid blocks, as <see cref="Layout"/> picks them.</param>
    /// <param name="detail">The detail level's share of the points per metre: 1 for full detail.</param>
    internal static CliffBlock Build(CliffSettings s, IReadOnlyList<LaidBlock> blocks, double detail)
    {
        List<SolidMesh> parts = [.. Pieces(s, blocks, detail).Select(part => part.Mesh)];
        return CliffBlock.From(parts, s, Chunks(s, blocks));
    }

    /// <summary>
    /// The collision chunks of laid blocks: each block's upright boxes (see <see cref="CliffChunk.Of"/>), grown by how
    /// far the surface detail pushes the rock out (see <see cref="CliffCollision.Reach"/>), merged where there would be
    /// more than <see cref="CliffExport.MaxCollisionShapes"/> (see <see cref="CliffCollision"/>), so the export keeps
    /// every one and no block is left without collision.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="blocks">The cliff's laid blocks.</param>
    internal static List<CliffChunk> Chunks(CliffSettings s, IReadOnlyList<LaidBlock> blocks) =>
        CliffCollision.Chunks(blocks, CliffExport.MaxCollisionShapes, CliffCollision.Reach(s));

    /// <summary>
    /// The cliff's laid blocks, from the sub-seed <see cref="CliffRules"/> picks (once per call; the pick does not depend
    /// on the detail level, so every level of a cliff is laid out alike).
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <exception cref="ArgumentException">Width, Depth, Height, Detail or Chisel size is not above 0.</exception>
    internal static IReadOnlyList<LaidBlock> Layout(CliffSettings s)
    {
        Check(s);
        return CliffRules.Choose(s).Layout;
    }

    /// <summary>
    /// The cliff's parts, each its field and its closed mesh (whose tags say how far the surface was cut in there),
    /// thinned where it is flat (see <see cref="Finish"/>). The level starts at
    /// <see cref="StartCell(CliffSettings, double)"/> and is made coarser, if need be, until it has, as meshed, at
    /// most <see cref="MaxTriangles"/> × the share² triangles, but no coarser than the cliff's smallest size over
    /// <see cref="ExtentCells"/> for that budget alone; past it, only <see cref="MaxTriangles"/> binds, so such levels
    /// come out alike. A level with no rock at its own grid is retried on grids <see cref="RetryStep"/> finer each time,
    /// never finer than the full level starts, so a cliff has triangles at every level; and a lower level of at most
    /// <see cref="TinyTriangles"/> triangles that comes out heavier than the level above it (of
    /// <see cref="Package.LevelShares"/>) takes that level's parts instead.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="detail">The detail level's share of the points per metre: 1 for full detail.</param>
    /// <exception cref="ArgumentException">Width, Depth, Height, Detail or Chisel size is not above 0.</exception>
    internal static List<(ICliffPart Field, SolidMesh Mesh)> Pieces(CliffSettings s, double detail) =>
        Pieces(s, Layout(s), detail);

    /// <summary>
    /// The grid spacing a detail level starts at, in metres: its <see cref="CellSize"/>, no coarser than the cliff's
    /// smallest size over <see cref="ExtentCells"/> (so even a small cliff has rock at every level), and coarse enough
    /// that no part's grid has more than <see cref="MaxGridPoints"/> points (which wins over the size).
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="detail">The detail level's share of the points per metre: 1 for full detail.</param>
    internal static double StartCell(CliffSettings s, double detail) =>
        StartCell(s, detail, Layout(s));

    /// <summary>The most grid points any part of the cliff can need at a spacing.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="cell">The grid's spacing, in metres.</param>
    internal static long MostGridPoints(CliffSettings s, double cell) =>
        GridPoints(Bounds(s, Layout(s)), cell);

    /// <summary>
    /// The grid spacing for a detail share, in metres: 1 / (Detail × share), but at full detail never so fine that a
    /// flat box of the cliff's size (Width × Depth × (Height + Sink)) would have more than <see cref="MaxTriangles"/>
    /// triangles less an allowance for slopes; the share applies after that, so lower detail levels of a large cliff
    /// stay coarser than the full one.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="share">The detail level's share of the points per metre: 1 for full detail.</param>
    internal static double CellSize(CliffSettings s, double share)
    {
        ArgumentNullException.ThrowIfNull(s);
        double tall = s.Height + s.Sink;
        double area = 2 * ((s.Width * s.Depth) + (s.Depth * tall) + (s.Width * tall));

        // About two triangles per cell of surface.
        double cell = 1 / s.Detail;
        double triangles = 2 * area * SurfaceAllowance / (cell * cell);
        if (triangles > MaxTriangles)
        {
            cell *= JsMath.Sqrt(triangles / MaxTriangles);
        }

        return cell / share;
    }

    /// <summary>The parts at a detail level, with the cliff's laid blocks (see <see cref="Pieces(CliffSettings, double)"/>).</summary>
    /// <param name="s">The settings, already checked.</param>
    /// <param name="blocks">The cliff's laid blocks.</param>
    /// <param name="detail">The detail level's share of the points per metre: 1 for full detail.</param>
    internal static List<(ICliffPart Field, SolidMesh Mesh)> Pieces(CliffSettings s, IReadOnlyList<LaidBlock> blocks, double detail) =>
        Finish(s, Unsimplified(s, blocks, detail));

    /// <summary>
    /// The parts at a detail level as meshed and mended, before <see cref="Finish"/> thins them and tags their points,
    /// each with the grid spacing it was meshed at; the level's spacing is chosen on these meshes' triangles (see
    /// <see cref="Pieces(CliffSettings, double)"/>).
    /// </summary>
    /// <param name="s">The settings, already checked.</param>
    /// <param name="blocks">The cliff's laid blocks.</param>
    /// <param name="detail">The detail level's share of the points per metre: 1 for full detail.</param>
    internal static List<(ICliffPart Field, SolidMesh Mesh, double Cell)> Unsimplified(CliffSettings s, IReadOnlyList<LaidBlock> blocks, double detail)
    {
        double share = Math.Min(detail, 1);
        double budget = MaxTriangles * share * share;
        double extent = Extent(s);
        double full = StartCell(s, 1, blocks);
        double cell = StartCell(s, detail, blocks);
        var parts = Fit(s, blocks, cell, extent, budget);
        while (parts.Count == 0 && cell > full)
        {
            cell = Math.Max(cell / RetryStep, full);
            parts = Fit(s, blocks, cell, extent, budget);
        }

        long triangles = parts.Sum(part => (long)part.Mesh.TriangleCount);
        if (detail < 1 && triangles <= TinyTriangles)
        {
            double above = Package.LevelShares.Where(level => level > detail).DefaultIfEmpty(1).Min();
            var finer = Unsimplified(s, blocks, above);
            if (finer.Sum(part => (long)part.Mesh.TriangleCount) < triangles)
            {
                return finer;
            }
        }

        return parts;
    }

    /// <summary>
    /// The parts made ready to shade: each mesh thinned where it is flat (see <see cref="MeshSimplify"/>), stopping at
    /// <see cref="SimplifyCells"/> of the grid spacing it was meshed at, with the ground line z = 0 and the flat bottom
    /// Sink below it held, and any fold left collapsed away (see <see cref="MeshFold"/>); then each point tagged with how far the surface was cut in there.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="parts">The parts as <see cref="Unsimplified"/> makes them; their meshes are changed in place.</param>
    internal static List<(ICliffPart Field, SolidMesh Mesh)> Finish(CliffSettings s, List<(ICliffPart Field, SolidMesh Mesh, double Cell)> parts)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(parts);
        double[] held = [0, -s.Sink];
        var done = new List<(ICliffPart Field, SolidMesh Mesh)>(parts.Count);
        foreach (var (field, mesh, cell) in parts)
        {
            MeshSimplify.Simplify(mesh, SimplifyCells * cell, held);

            // A fold the thinning left (it adds none, but keeps one it found) is collapsed away too.
            MeshFold.Collapse(mesh, field, cell, SimplifyCells * cell, held);
            double[] tags = new double[mesh.Points.Count];
            Parallel.For(0, tags.Length, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (i) => tags[i] = field.Tag(mesh.Points[i]));
            mesh.Tags.Clear();
            mesh.Tags.AddRange(tags);
            done.Add((field, mesh));
        }

        return done;
    }

    /// <summary>The grid spacing a level starts at (see <see cref="StartCell(CliffSettings, double)"/>), with the cliff's laid blocks.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="detail">The detail level's share of the points per metre: 1 for full detail.</param>
    /// <param name="blocks">The cliff's laid blocks.</param>
    internal static double StartCell(CliffSettings s, double detail, IReadOnlyList<LaidBlock> blocks)
    {
        double cell = Math.Min(CellSize(s, detail), Extent(s));

        // The memory cap: the part's bounds do not depend on the spacing, so the spacing grows until it fits.
        var bounds = Bounds(s, blocks);
        for (long most = GridPoints(bounds, cell); most > MaxGridPoints; most = GridPoints(bounds, cell))
        {
            cell *= Math.Max(JsMath.Pow((double)most / MaxGridPoints, 1.0 / 3), 1.01);
        }

        return cell;
    }

    /// <summary>The coarsest spacing the triangle budgets alone may push a level to: the cliff's smallest size over <see cref="ExtentCells"/>.</summary>
    private static double Extent(CliffSettings s) => Math.Min(Math.Min(s.Width, s.Depth), s.Height + s.Sink) / ExtentCells;

    /// <summary>The cliff's part's bounds (they do not depend on the groove floor).</summary>
    private static (V3 Min, V3 Max) Bounds(CliffSettings s, IReadOnlyList<LaidBlock> blocks) => SlabCliffField.BoundsOf(blocks, s);

    /// <summary>The grid points a part with these bounds needs at a spacing.</summary>
    private static long GridPoints((V3 Min, V3 Max) bounds, double cell)
    {
        var (min, max) = bounds;
        var (_, nx, ny, nz) = DualContour.Grid(min, max, cell);
        return (long)nx * ny * nz;
    }

    /// <summary>
    /// The parts at a spacing, rebuilt coarser while they have more triangles than the level's budget (below the
    /// extent's spacing) or <see cref="MaxTriangles"/> (at or past it).
    /// </summary>
    private static List<(ICliffPart Field, SolidMesh Mesh, double Cell)> Fit(CliffSettings s, IReadOnlyList<LaidBlock> blocks, double cell, double extent, double budget)
    {
        while (true)
        {
            var parts = Parts(s, blocks, cell);
            long triangles = parts.Sum(part => (long)part.Mesh.TriangleCount);
            double most = cell < extent ? budget : MaxTriangles;
            if (triangles <= most)
            {
                return parts;
            }

            double next = cell * JsMath.Sqrt(triangles / most) * 1.02;
            cell = cell < extent && triangles <= MaxTriangles ? Math.Min(next, extent) : next;
        }
    }

    /// <summary>
    /// The cliff's parts at one grid spacing: for now the whole layout as one part, its field's grooves no narrower than
    /// <see cref="GrooveCells"/> cells, meshed, its loose crumbs dropped, mended and its sliver folds collapsed; none when it meshes empty.
    /// </summary>
    private static List<(ICliffPart Field, SolidMesh Mesh, double Cell)> Parts(CliffSettings s, IReadOnlyList<LaidBlock> blocks, double cell)
    {
        var field = Field(s, blocks, cell);
        var mesh = Mesh(field, cell);

        // A cut steeper into the rock than the rock can seal a pocket inside it or cut a crumb loose: neither is anything
        // to see.
        mesh.DropLooseShells(LooseShare);
        if (mesh.TriangleCount == 0)
        {
            return [];
        }

        // Where two triangles fold over each other or one is empty, the mesh is mended there (see MeshMend), and a fold
        // left as a sliver finer than a cell is collapsed away (see MeshFold).
        MeshMend.Mend(mesh, field, cell);
        MeshFold.Collapse(mesh, field, cell, SimplifyCells * cell, [0, -s.Sink]);
        return [(field, mesh, cell)];
    }

    /// <summary>
    /// The whole layout meshed as one part at a grid spacing, as a level's part is meshed (see <see cref="Parts"/>) but
    /// with its crumbs kept and no tags: what <see cref="CliffRules"/> checks.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="blocks">The cliff's laid blocks.</param>
    /// <param name="cell">The grid's spacing, in metres.</param>
    internal static SolidMesh Mesh(CliffSettings s, IReadOnlyList<LaidBlock> blocks, double cell) => Mesh(Field(s, blocks, cell), cell);

    /// <summary>The layout's field at a grid spacing: its grooves no narrower than <see cref="GrooveCells"/> cells.</summary>
    private static SlabCliffField Field(CliffSettings s, IReadOnlyList<LaidBlock> blocks, double cell) => new(blocks, s, GrooveCells * cell);

    /// <summary>A field meshed over its bounds at a grid spacing.</summary>
    private static SolidMesh Mesh(SlabCliffField field, double cell)
    {
        var (min, max) = field.Bounds()!.Value;
        field.Prepare();
        return DualContour.Mesh(field, min, max, cell);
    }

    /// <summary>Refuses settings whose sizes would divide by zero or build an empty block.</summary>
    private static void Check(CliffSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        foreach (var (name, value) in new[]
        {
            ("Width", s.Width), ("Depth", s.Depth), ("Height", s.Height), ("Detail", s.Detail), ("ChiselSize", s.ChiselSize),
        })
        {
            if (!(value > 0))
            {
                throw new ArgumentException($"Cliff setting {name} must be above 0, not {value}; build settings with CliffSettings.From.", nameof(s));
            }
        }
    }
}
