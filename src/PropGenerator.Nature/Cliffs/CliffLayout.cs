using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// Lays a cliff's blocks out from its settings. Blocks columns stand side by side along the width (x), each
/// overlapping its neighbours; each column is stacked into Tiers levels, each lower than the one below it, set back
/// (toward +y) and sunk into it; and with Layers above 0 each level is cut into that many horizontal plates, each a
/// block of its own, slightly shifted, turned and inset. The end blocks of every run turn their outer end faces back (as far as End turn lets them)
/// and get a bite or a bevel there. Counts are held to what fits, so nothing is thinner than Min thickness, and every
/// block stands on the ground or on the blocks it rests on. Every draw is salted by the column (and tier or plate)
/// it is for, so changing Width or Blocks never reshuffles the other blocks' draws.
/// </summary>
internal static class CliffLayout
{
    /// <summary>No block is thinner than this, in metres, or than a third of Depth where that is less.</summary>
    private const double MostMinThickness = 0.6;

    /// <summary>How far neighbouring columns overlap, as a share of the narrower one, before the allowances.</summary>
    private const double OverlapShare = 0.3;

    /// <summary>How far the columns' width shares spread, at Block variety 1: each is 1 ± this.</summary>
    private const double WidthSpread = 0.5;

    /// <summary>How far the columns' heights and depths spread, at Block variety 1: each is × (1 ± this).</summary>
    private const double SizeSpread = 0.35;

    /// <summary>How far a column is set forward or back, at Block variety 1, as a share of Depth.</summary>
    private const double OffsetSpread = 0.2;

    /// <summary>The most a side plane is set in, as a share of the mean column width and of Depth.</summary>
    private const double RoughShare = 0.1;

    /// <summary>How much thicker than Min thickness a column, tier or plate is laid out, before roughness.</summary>
    private const double ThickMargin = 1.2;

    /// <summary>The least a tier is as tall as the one below it.</summary>
    private const double TierLeast = 0.55;

    /// <summary>The most a tier is as tall as the one below it.</summary>
    private const double TierMost = 0.75;

    /// <summary>The least a tier's front is set back from the top front edge of the one below it, as a share of that one's depth.</summary>
    private const double SetBackLeast = 0.15;

    /// <summary>The most a tier's front is set back from the top front edge of the one below it, as a share of that one's depth.</summary>
    private const double SetBackMost = 0.3;

    /// <summary>How far a tier is sunk into the one below it at least, in metres.</summary>
    private const double TierSink = 0.2;

    /// <summary>How much deeper than its seal a block resting on others starts sunk, in metres.</summary>
    private const double SinkMargin = 0.05;

    /// <summary>
    /// How deep, at least, two stacked blocks overlap, in metres, before the surface detail: where they overlap less,
    /// the union is so shallow that the detail would open a hairline slot along the joint. Twice the chisel's depth is
    /// added (see <see cref="Seal"/>).
    /// </summary>
    private const double SealDepth = 0.1;

    /// <summary>How far the plates' thickness shares spread, at Layer variety 1: each is 1 ± this.</summary>
    private const double PlateSpread = 0.5;

    /// <summary>How far a plate is shifted, as a share of the mean column width (along x) and of its depth (along y).</summary>
    private const double PlateShift = 0.02;

    /// <summary>How far a plate's sides are set in, as a share of the mean column width (each end) and of its depth (the front).</summary>
    private const double PlateInset = 0.03;

    /// <summary>The most a plate is turned about the upright, in degrees.</summary>
    private const double PlateTurn = 2;

    /// <summary>
    /// The most the top of a block that carries another tilts, in degrees (Top tilt's first top): a steeper bed top
    /// folds the joint with the block resting on it, so only the blocks on top tilt further.
    /// </summary>
    private const double CarriedTilt = 25;

    /// <summary>The most a plate's top tilts, in degrees, so plates stack flat.</summary>
    private const double PlateTilt = 4;

    /// <summary>The least an end face turns back at the default End turn, in degrees.</summary>
    private const double TurnLeast = 20;

    /// <summary>The most an end face turns back, in degrees: End turn's default and top, at which the seeded turns are not scaled.</summary>
    private const double TurnMost = 50;

    /// <summary>
    /// How far in from the cliff's low corner, in metres, its tilted bottom starts to rise: the flat bottom keeps that
    /// strip, so the cliff rests on the ground line on a patch the mesh keeps, not on a point it rounds off.
    /// </summary>
    private const double ContactWidth = 0.3;

    /// <summary>The cliff's tilted bottom rises across the cliff by at most this share of the height of the blocks on the ground.</summary>
    private const double RiseShare = 0.5;

    /// <summary>How many times a foot, a taper or a tilt is halved, at most, while it leaves a block thinner than Min thickness.</summary>
    private const int NarrowTries = 4;

    /// <summary>How many halvings the search for the most of a foot or taper a block can keep makes.</summary>
    private const int HoldSteps = 30;

    /// <summary>Half the thickness of the slab a block is cut by to find its section at a height, in metres.</summary>
    private const double SectionHalf = 1e-7;

    /// <summary>A cut goes at most this share of its block's local thickness.</summary>
    private const double MaxCutShare = 0.4;

    /// <summary>How many times a cut is moved out to hold it to its finished shell's limit, at most.</summary>
    private const int LimitPasses = 3;

    /// <summary>A cut is held this far inside its limit, in metres, against rounding.</summary>
    private const double LimitHair = 1e-7;

    /// <summary>A cut shallower than this, in metres, is left out.</summary>
    private const double MinCut = 0.05;

    /// <summary>The most the rim chamfers cut in, as a share of the block's smallest size, so a small block keeps its top.</summary>
    private const double BevelShare = 0.1;

    /// <summary>How far an end bevel cuts in, as a share of the smaller of the block's width and height.</summary>
    private const double EndBevelShare = 0.2;

    /// <summary>An end bevel reaches at most this share of the way from the top's far end corner to its middle.</summary>
    private const double EndBevelReach = 0.75;

    /// <summary>How far the floor of an end bite slopes down toward the end, in degrees.</summary>
    private const double EndBiteSlope = 15;

    /// <summary>How steeply the foot of a block resting on others recedes below its ground line, in degrees from upright.</summary>
    private const double FootSlope = 45;

    /// <summary>How many points a side a shell is sampled on to see how much of a cut's removed rock is hidden.</summary>
    private const int HiddenSamples = 16;

    /// <summary>More than this share of a cut's removed rock must lie inside the block above for the cut to give way.</summary>
    private const double HiddenShare = 0.5;

    /// <summary>The furthest behind its own front a block's foot starts, in metres.</summary>
    private const double FootReach = 0.1;

    /// <summary>How far in front of a block's own front the front of the blocks below is looked for, in metres.</summary>
    private const double FootSearch = 3;

    /// <summary>The step of that search, in metres.</summary>
    private const double FootStep = 0.02;

    /// <summary>A block resting on others is sunk deeper, a step at a time, until this share of its bottom is held.</summary>
    private const double HeldShare = 0.8;

    /// <summary>How much deeper the first try sinks a block that is not held enough, in metres; each try doubles it.</summary>
    private const double SettleStep = 0.1;

    /// <summary>The most tries a block is sunk deeper (the last sinks it 12.7 m more, unless that is past the floor under it).</summary>
    private const int SettleTries = 7;

    /// <summary>How many points a side the bottom of a block is sampled on to see how much of it is held.</summary>
    private const int HeldSamples = 12;

    /// <summary>How many heights of a block's sunk part are looked at for the blocks below it.</summary>
    private const int HeldLevels = 6;

    /// <summary>How far above a block's flat bottom the blocks below must still fill, in metres.</summary>
    private const double HeldOverlap = 0.02;

    private const double Deg = Math.PI / 180;

    /// <summary>How much of the lean the tiers try, in turn, before a tier is dropped.</summary>
    private static readonly double[] LeanRungs = [1, 0.5, 0];

    // Salts of the cliff seed's draws: a stride per column, per tier and per plate, so no draw moves when a count changes.
    private const int ColumnSalt = 100;
    private const int ColumnStride = 8;
    private const int TierSalt = 400;
    private const int PlateSalt = 500;
    private const int PlateStride = 16;
    private const int BlockSalt = 1000;
    private const int UndersideSalt = 2400;

    // Salts of a block's own seed, past BlockMaker's.
    private const int JitterSalt = 5000;
    private const int CountSalt = 5100;
    private const int EndSalt = 5200;

    /// <summary>The cliff's blocks, tier by tier, plate by plate, column by column.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="seed">The seed of every draw (the cliff's sub-seed).</param>
    /// <returns>The blocks; the same settings and seed always give the same blocks.</returns>
    public static IReadOnlyList<LaidBlock> Make(CliffSettings s, uint seed)
    {
        ArgumentNullException.ThrowIfNull(s);
        double thin = MinThickness(s);
        int count = Columns(s, thin);
        var columns = Lay(s, seed, count);
        var tiers = Tiers(s, seed, columns, thin);
        var laid = new List<LaidBlock>();
        for (int k = 0; k < tiers.Length; k++)
        {
            var plates = new List<Box>[count];
            for (int c = 0; c < count; c++)
            {
                plates[c] = Plates(s, seed, tiers[k][c], k, thin, Need(s, count, thin));
            }

            int most = plates.Max(p => p.Count);
            for (int j = 0; j < most; j++)
            {
                List<LaidBlock> below = j > 0 ? [.. laid.Where(b => b.Tier == k && b.Layer == j - 1)]
                    : k > 0 ? [.. laid.Where(b => b.Tier == k - 1)]
                    : [];
                var level = new List<LaidBlock>();
                for (int c = 0; c < count; c++)
                {
                    if (j < plates[c].Count)
                    {
                        bool carries = j < plates[c].Count - 1 || k < tiers.Length - 1;
                        var block = Block(s, seed, columns, c, k, j, plates[c][j], s.Layers > 0, thin, below, carries);
                        if (below.Count > 0 && Held(block.Block, block.Shape, below, Seal(s)) < HeldShare && GiveWay(block, below, laid, Seal(s), count))
                        {
                            block = Block(s, seed, columns, c, k, j, plates[c][j], s.Layers > 0, thin, below, carries);
                        }

                        level.Add(block);
                    }
                }

                // The cliff's bottom tilts under the blocks on the ground, before anything rests on them.
                if (k == 0 && j == 0 && s.UndersideTilt > 0)
                {
                    level = Tilted(level, s, seed, thin);
                }

                laid.AddRange(level);
            }
        }

        return laid;
    }

    /// <summary>The least thickness any block may have: 0.6 m, or a third of Depth where that is less.</summary>
    /// <param name="s">The settings.</param>
    internal static double MinThickness(CliffSettings s) => Math.Min(MostMinThickness, s.Depth / 3);

    /// <summary>
    /// How many columns the width holds: the Blocks slider, held so that even the narrowest share the seed could draw
    /// leaves every column thick enough. The count does not depend on the seed.
    /// </summary>
    private static int Columns(CliffSettings s, double thin)
    {
        double least = 1 - (WidthSpread * s.BlockVariety);
        double most = 1 + (WidthSpread * s.BlockVariety);
        for (int n = Math.Max(s.Blocks, 1); n > 1; n--)
        {
            double allowance = Allowance(s, n);
            double narrowest = least * (s.Width + ((n - 1) * allowance)) / ((n * most) - (OverlapShare * (n - 1) * least));
            if (narrowest >= Need(s, n, thin))
            {
                return n;
            }
        }

        return 1;
    }

    /// <summary>The least width or depth a column, tier or plate is laid out with: a margin over Min thickness plus what roughness and plate jitter can take off both sides.</summary>
    private static double Need(CliffSettings s, int n, double thin) => (ThickMargin * thin) + Allowance(s, n);

    /// <summary>What roughness and plate jitter can take off the two blocks of a joint, in metres, and so what each overlap adds.</summary>
    private static double Allowance(CliffSettings s, int n)
    {
        double mean = s.Width / n;
        double plate = s.Layers > 0 ? ((PlateShift + PlateInset) * mean) + (0.5 * s.Depth * (1 + SizeSpread) * JsMath.Tan(PlateTurn * Deg)) : 0;
        return 2 * (Roughness(s, n) + plate);
    }

    /// <summary>The roughness each block gets: the slider's, held to a share of the mean column width and of Depth.</summary>
    private static double Roughness(CliffSettings s, int n) => Math.Min(s.Roughness, RoughShare * Math.Min(s.Width / n, s.Depth));

    /// <summary>The columns from -x to +x: their width shares normalised to the Width plus the overlaps, and their seeded sizes and set-backs.</summary>
    private static Column[] Lay(CliffSettings s, uint seed, int n)
    {
        double[] shares = new double[n];
        for (int c = 0; c < n; c++)
        {
            shares[c] = 1 + (WidthSpread * s.BlockVariety * Signed(seed, ColumnSalt + (c * ColumnStride)));
        }

        double allowance = Allowance(s, n);
        double overlapping = 0;
        for (int c = 0; c + 1 < n; c++)
        {
            overlapping += Math.Min(shares[c], shares[c + 1]);
        }

        double scale = (s.Width + ((n - 1) * allowance)) / (shares.Sum() - (OverlapShare * overlapping));
        var columns = new Column[n];
        double left = -s.Width / 2;
        for (int c = 0; c < n; c++)
        {
            int salt = ColumnSalt + (c * ColumnStride);
            double width = scale * shares[c];
            columns[c] = new Column(
                left + (width / 2),
                width,
                s.Height * (1 + (SizeSpread * s.BlockVariety * Signed(seed, salt + 1))),
                s.Depth * (1 + (SizeSpread * s.BlockVariety * Signed(seed, salt + 2))),
                OffsetSpread * s.BlockVariety * s.Depth * Signed(seed, salt + 3));
            if (c + 1 < n)
            {
                left += width - ((OverlapShare * scale * Math.Min(shares[c], shares[c + 1])) + allowance);
            }
        }

        return columns;
    }

    /// <summary>
    /// Each tier's slab of each column (by tier, then column): the column's height shared out so each tier is 55 to 75%
    /// as tall as the one below it, each set back from the top front edge of the one below it by 15 to 30% of its depth.
    /// What does not fit eases the lean first (all of it, then half, then none) and then drops the top tier.
    /// </summary>
    private static Box[][] Tiers(CliffSettings s, uint seed, Column[] columns, double thin)
    {
        for (int count = Math.Clamp(s.Tiers, 1, 4); ; count--)
        {
            foreach (double ease in LeanRungs)
            {
                if (Tiers(s, seed, columns, thin, count, s.Lean * ease * Deg) is { } tiers)
                {
                    return tiers;
                }
            }
        }
    }

    /// <summary>The tiers at a count and lean (in radians), or null when one would be thinner than allowed (never for one tier).</summary>
    private static Box[][]? Tiers(CliffSettings s, uint seed, Column[] columns, double thin, int count, double lean)
    {
        double need = Need(s, columns.Length, thin);
        var tiers = new Box[count][];
        bool fits = true;
        for (int k = 0; k < count; k++)
        {
            tiers[k] = new Box[columns.Length];
        }

        for (int c = 0; c < columns.Length; c++)
        {
            var column = columns[c];
            double[] heights = new double[count];
            heights[0] = 1;
            for (int k = 1; k < count; k++)
            {
                heights[k] = heights[k - 1] * (TierLeast + ((TierMost - TierLeast) * CliffNoise.Hash01(seed, TierSalt + k)));
            }

            double sum = heights.Sum();
            var box = Leaning(new Box(0, column.Height * heights[0] / sum, column.Depth, column.Y, s.Sink, 0), lean, thin);
            for (int k = 0; k < count; k++)
            {
                if (k > 0)
                {
                    var lower = tiers[k - 1][c];
                    double setBack = (SetBackLeast + ((SetBackMost - SetBackLeast) * CliffNoise.Hash01(seed, ColumnSalt + (c * ColumnStride) + 4 + k)))
                        * lower.Depth;
                    double back = setBack + Math.Max(0, lower.Height * JsMath.Tan(lower.Lean));
                    double sink = Math.Max(TierSink, Seal(s) + SinkMargin);
                    box = Leaning(new Box(lower.Z + lower.Height, column.Height * heights[k] / sum, lower.Depth - back, lower.Y + (back / 2), sink, 0), lean, thin);
                }

                tiers[k][c] = box;
                fits &= box.Height >= thin && box.Depth >= need;
            }
        }

        return fits || count == 1 ? tiers : null;
    }

    /// <summary>
    /// A tier's plates from the bottom up: with Layers 0, the tier itself; otherwise its height shared out by seeded
    /// shares (the same for every column), each plate's front set back as far as the lean takes the face at its height.
    /// The plate count is held to what fits.
    /// </summary>
    private static List<Box> Plates(CliffSettings s, uint seed, Box tier, int k, double thin, double need)
    {
        if (s.Layers <= 0)
        {
            return [tier];
        }

        // The lean is held so the top plate keeps the depth it needs.
        double deep = need / (1 - PlateInset - PlateShift);
        double angle = Math.Min(tier.Lean, JsMath.Atan(Math.Max(tier.Depth - deep, 0) / tier.Height));
        double lean = JsMath.Tan(angle);
        for (int count = s.Layers; ; count--)
        {
            double[] shares = new double[count];
            for (int j = 0; j < count; j++)
            {
                shares[j] = 1 + (PlateSpread * s.LayerVariety * Signed(seed, PlateSalt + (k * PlateStride) + j));
            }

            double sum = shares.Sum();
            var plates = new List<Box>();
            double z = tier.Z;
            bool fits = true;
            for (int j = 0; j < count; j++)
            {
                double back = (z - tier.Z) * lean;
                var plate = new Box(z, tier.Height * shares[j] / sum, tier.Depth - back, tier.Y + (back / 2), j == 0 ? tier.Sink : Seal(s) + SinkMargin, angle);
                plates.Add(plate);
                fits &= plate.Height >= thin && plate.Depth >= deep;
                z += plate.Height;
            }

            if (fits || count == 1)
            {
                return plates;
            }
        }
    }

    /// <summary>The slab with the lean (in radians) it gets: the one given, held (as <see cref="BlockMaker"/> holds it) so its top keeps Min thickness.</summary>
    private static Box Leaning(Box box, double lean, double thin) =>
        box with { Lean = Math.Min(lean, JsMath.Atan(Math.Max(box.Depth - thin, 0) / box.Height)) };

    /// <summary>One laid block: its shape from its slab and its own seed's draws, made, turned, finished at its ends and settled onto the blocks below.</summary>
    private static LaidBlock Block(CliffSettings s, uint seed, Column[] columns, int c, int k, int j, Box box, bool plate, double thin, List<LaidBlock> below, bool carries)
    {
        uint own = (uint)(CliffNoise.Hash01(seed, BlockSalt + (((k * PlateStride) + j) * PlateStride) + c) * 4294967296.0);
        var column = columns[c];
        int n = columns.Length;
        double mean = s.Width / n;
        double dx = 0;
        double dy = 0;
        double width = column.Width;
        double depth = box.Depth;
        double turn = 0;
        if (plate)
        {
            dx = PlateShift * mean * Signed(own, JitterSalt);
            double inset = PlateInset * mean * CliffNoise.Hash01(own, JitterSalt + 1);
            double front = PlateInset * box.Depth * CliffNoise.Hash01(own, JitterSalt + 2);
            width -= 2 * inset;
            depth -= front;
            dy = (front / 2) + (PlateShift * box.Depth * Signed(own, JitterSalt + 3));
            turn = PlateTurn * Deg * Signed(own, JitterSalt + 4);
        }

        double height = box.Height;
        var shape = new BlockShape(
            new V3(column.X + dx, box.Y + dy, box.Z),
            width,
            depth,
            height,
            box.Sink,
            s.Faces,
            Roughness(s, n),
            box.Lean / Deg,
            plate ? Math.Min(s.TopTilt, PlateTilt) : carries ? Math.Min(s.TopTilt, CarriedTilt) : s.TopTilt,
            Math.Min(s.Bevel, BevelShare * Math.Min(Math.Min(width, depth), height + box.Sink)),
            Vary(s.Steps, own, CountSalt),
            Vary(s.Bites, own, CountSalt + 1),
            Vary(s.Grooves, own, CountSalt + 2),
            s.CutDepth,
            c == 0 ? EndTurn(s, own, EndSalt) : 0,
            c == n - 1 ? EndTurn(s, own, EndSalt + 1) : 0,
            thin);

        // Sunk deeper a try at a time until held, or until its flat bottom reaches the floor of everything that could
        // hold it (never below the cliff's own flat bottom, Sink under the ground line), keeping the best try. A sink
        // that would take it past the floor stops on it instead, the first one's too (review-final I2: the doubling
        // took blocks metres through the blocks under them and out of the cliff's underside, and a plate's own sink
        // took it under a thin ground plate).
        double floor = below.Count == 0 ? -s.Sink : Math.Max(below.Min(b => b.Block.Bounds().Min.Z), -s.Sink);
        double deepest = Math.Max(shape.Base.Z - floor, 0);
        LaidBlock? best = null;
        double bestHeld = -1;
        for (int attempt = 0; attempt <= SettleTries; attempt++)
        {
            var settled = shape with { Sink = Math.Min(shape.Sink + (SettleStep * ((1 << attempt) - 1)), deepest) };
            var block = BlockMaker.Make(settled, own);
            if (turn != 0)
            {
                block = Turned(block, settled.Base, turn);
            }

            if (c == 0)
            {
                block = Finished(block, settled, -1, own, EndSalt + 2, n == 1 && carries);
            }

            if (c == n - 1)
            {
                block = Finished(block, settled, 1, own, EndSalt + 6, n == 1 && carries);
            }

            // Taper narrows a block that carries nothing toward its top, measured before its foot (if it rests on others).
            if (!carries && s.Taper > 0)
            {
                block = Unfinned(Narrowed(block, settled, c, n, s.Taper, false), thin);
            }

            if (below.Count > 0)
            {
                block = Footed(block, settled, below);
            }

            // Foot narrows a block on the ground toward the ground line; a cut's face may then stand a thin fin from it.
            if (below.Count == 0 && s.Foot > 0)
            {
                block = Unfinned(Narrowed(block, settled, c, n, s.Foot, true), thin);
            }

            // The foot and the end bevels narrow the shell after its cuts were drawn: each cut is held to the finished
            // shell's limit.
            block = Limited(block);

            double held = below.Count == 0 ? 1 : Held(block, settled, below, Seal(s));
            if (held > bestHeld)
            {
                best = new LaidBlock(block, settled, c, k, j);
                bestHeld = held;
            }

            if (held >= HeldShare || settled.Sink >= deepest)
            {
                break;
            }
        }

        return best!;
    }

    /// <summary>
    /// The share of a block's footprint (sampled over its section just below its ground line) where the blocks below
    /// it overlap it in z by at least the seal: at some height of its sunk part, from that far above its flat bottom
    /// (but at most its sink less <see cref="HeldOverlap"/>) up to its ground line, both it and a block below fill the
    /// point.
    /// </summary>
    private static double Held(ConvexBlock block, BlockShape shape, List<LaidBlock> below, double seal)
    {
        var (min, max) = block.Bounds();
        var under = below.Where(b => b.Block.Bounds().Max.X > min.X && b.Block.Bounds().Min.X < max.X).ToList();
        int footprint = 0;
        int held = 0;
        foreach (var column in Sunk(block, shape, seal))
        {
            footprint++;
            held += column.Any(p => under.Any(b => b.Block.Distance(p) < 0)) ? 1 : 0;
        }

        return footprint == 0 ? 1 : (double)held / footprint;
    }

    /// <summary>
    /// The points <see cref="Held"/> looks at: for each sample of the block's footprint (its section just below its
    /// ground line), the heights of its sunk part, from the seal (but at most its sink less <see cref="HeldOverlap"/>)
    /// above its flat bottom up to its ground line, where the block itself fills the point.
    /// </summary>
    private static IEnumerable<List<V3>> Sunk(ConvexBlock block, BlockShape shape, double seal)
    {
        var (min, max) = block.Bounds();
        double overlap = Math.Max(Math.Min(seal, shape.Sink - HeldOverlap), HeldOverlap);
        double low = shape.Base.Z - shape.Sink + overlap;
        double span = Math.Max(shape.Base.Z - HeldOverlap - low, 0);
        for (int i = 0; i < HeldSamples; i++)
        {
            for (int j = 0; j < HeldSamples; j++)
            {
                double x = min.X + ((max.X - min.X) * (i + 0.5) / HeldSamples);
                double y = min.Y + ((max.Y - min.Y) * (j + 0.5) / HeldSamples);
                if (block.Distance(new V3(x, y, shape.Base.Z - HeldOverlap)) < 0)
                {
                    yield return [.. Enumerable.Range(0, HeldLevels)
                        .Select(l => new V3(x, y, low + (span * l / (HeldLevels - 1))))
                        .Where(p => block.Distance(p) < 0)];
                }
            }
        }
    }

    /// <summary>
    /// Makes the blocks below a block that they do not hold give way, for each cut that takes rock out of the block's
    /// sunk part (an end column's end bite, which finishes it, excepted): a cut whose removed rock lies mostly inside
    /// the block is dropped, since that shows little; any other is kept but stopped at the block's flat bottom, so it
    /// still shows below it and takes nothing out from under it. The changed blocks replace the old ones in
    /// <paramref name="below"/> and <paramref name="laid"/>.
    /// </summary>
    /// <returns>Whether any cut was dropped or stopped.</returns>
    private static bool GiveWay(LaidBlock upper, List<LaidBlock> below, List<LaidBlock> laid, double seal, int columns)
    {
        var points = Sunk(upper.Block, upper.Shape, seal).SelectMany(column => column).ToList();

        // A plane whose front is everything below the block's flat bottom: a cut that also needs it removes nothing
        // higher up.
        var floor = new Plane(new V3(0, 0, -1), -(upper.Shape.Base.Z - upper.Shape.Sink));
        bool dropped = false;
        for (int i = 0; i < below.Count; i++)
        {
            var b = below[i];
            var shell = new ConvexBlock(b.Block.Shell, []);
            var inShell = points.Where(p => shell.Distance(p) < 0).ToList();
            var kept = new List<IReadOnlyList<Plane>>();
            bool changed = false;
            foreach (var cut in b.Block.Cuts)
            {
                if (Finishes(cut, b.Column, columns) || !inShell.Any(p => Removes(cut, p)))
                {
                    kept.Add(cut);
                }
                else if (!Hidden(cut, shell, upper.Block))
                {
                    kept.Add([.. cut, floor]);
                    changed = true;
                }
                else
                {
                    changed = true;
                }
            }

            if (changed)
            {
                var given = b with { Block = new ConvexBlock(b.Block.Shell, kept) };
                laid[laid.IndexOf(b)] = given;
                below[i] = given;
                dropped = true;
            }
        }

        return dropped;
    }

    /// <summary>Whether a cut removes a point: the point is in front of all its planes.</summary>
    private static bool Removes(IReadOnlyList<Plane> cut, V3 p) => cut.All(plane => plane.At(p) > 0);

    /// <summary>
    /// Whether most of the rock a cut takes out of a shell (sampled on a grid over the shell's bounds) lies inside
    /// another block, where it cannot be seen.
    /// </summary>
    private static bool Hidden(IReadOnlyList<Plane> cut, ConvexBlock shell, ConvexBlock over)
    {
        var (min, max) = shell.Bounds();
        int removed = 0;
        int hidden = 0;
        for (int i = 0; i < HiddenSamples; i++)
        {
            for (int j = 0; j < HiddenSamples; j++)
            {
                for (int k = 0; k < HiddenSamples; k++)
                {
                    var p = new V3(
                        min.X + ((max.X - min.X) * (i + 0.5) / HiddenSamples),
                        min.Y + ((max.Y - min.Y) * (j + 0.5) / HiddenSamples),
                        min.Z + ((max.Z - min.Z) * (k + 0.5) / HiddenSamples));
                    if (shell.Distance(p) < 0 && Removes(cut, p))
                    {
                        removed++;
                        hidden += over.Distance(p) < 0 ? 1 : 0;
                    }
                }
            }
        }

        return hidden > removed * HiddenShare;
    }

    /// <summary>Whether a cut is an end column's end bite (see <see cref="Finished"/>: its floor plane slopes down toward an end), and so stays.</summary>
    private static bool Finishes(IReadOnlyList<Plane> cut, int column, int columns) =>
        (column == 0 && cut.Any(p => p.Normal == EndBiteFloor(-1))) || (column == columns - 1 && cut.Any(p => p.Normal == EndBiteFloor(1)));

    /// <summary>The normal of an end bite's floor at an end (-1 for -x, 1 for +x).</summary>
    private static V3 EndBiteFloor(int end) => new(end * JsMath.Sin(EndBiteSlope * Deg), 0, JsMath.Cos(EndBiteSlope * Deg));

    /// <summary>The block turned about the upright through a point.</summary>
    private static ConvexBlock Turned(ConvexBlock block, V3 pivot, double angle)
    {
        double cos = JsMath.Cos(angle);
        double sin = JsMath.Sin(angle);
        return new ConvexBlock([.. block.Shell.Select(Turn)], [.. block.Cuts.Select(cut => (IReadOnlyList<Plane>)[.. cut.Select(Turn)])]);

        Plane Turn(Plane plane)
        {
            var n = plane.Normal;
            var turned = new V3((n.X * cos) - (n.Y * sin), (n.X * sin) + (n.Y * cos), n.Z);
            return new Plane(turned, plane.Offset - V3.Dot(n, pivot) + V3.Dot(turned, pivot));
        }
    }

    /// <summary>
    /// A block narrowed by a foot (toward its ground line) or by a taper (toward its top): a plane on its front, on its
    /// back and on each outer end of the row (the -x end of the first column, the +x end of the last), set in at the
    /// narrow end by <paramref name="share"/> of the block's depth (at the front and back) or width (at the ends) over
    /// two, and by nothing at the other end, running straight between. Each is measured from the block's own sides: a
    /// foot from where the side meets the ground line, a taper from where its side planes reach the top, so it adds to
    /// any lean. The narrowing is held, along x and along y, so the narrow end keeps <see cref="ThickMargin"/> × Min
    /// thickness across (a foot down to the bottom of the block's sunk part), and halved while the block would be
    /// thinner than Min thickness anywhere (left out after <see cref="NarrowTries"/> halvings).
    /// </summary>
    private static ConvexBlock Narrowed(ConvexBlock block, BlockShape shape, int c, int n, double share, bool foot)
    {
        double low = shape.Base.Z;
        double height = shape.Height;
        double high = low + height;
        double bottom = low - shape.Sink;
        double floor = ThickMargin * shape.MinThickness;
        V3[] outs = [new(0, -1, 0), new(0, 1, 0), new(-1, 0, 0), new(1, 0, 0)];
        bool[] narrowed = [true, true, c == 0, c == n - 1];
        double[] insets = [share * shape.Depth / 2, share * shape.Depth / 2, share * shape.Width / 2, share * shape.Width / 2];

        // Each side's reach where the narrowing plane runs out (a foot's at the ground line, a taper's where the side
        // planes reach the top) and at the narrow end before narrowing (a foot's at the bottom, a taper's where the side
        // planes reach the top).
        double[] grounds = new double[4];
        double[] wide = new double[4];
        double[] narrow = new double[4];
        var sides = new ConvexBlock([.. block.Shell.Take(Math.Max(shape.Faces, 4))], []);
        for (int i = 0; i < 4; i++)
        {
            if (foot)
            {
                grounds[i] = Reach(block, outs[i], low);
                wide[i] = grounds[i];
                narrow[i] = Reach(block, outs[i], bottom + SectionHalf);
            }
            else
            {
                // A taper is measured, and held, on the block's side planes alone, which a steep top tilt does not cut into.
                grounds[i] = Reach(sides, outs[i], low);
                wide[i] = Reach(sides, outs[i], high);
                narrow[i] = wide[i];
            }

            narrowed[i] &= !double.IsNaN(grounds[i]) && !double.IsNaN(wide[i]) && !double.IsNaN(narrow[i]);
        }

        // The most of the narrowing each axis keeps: its width at the narrow end, the narrowed sides' reach there being
        // the least of the side's own and the narrowing plane's, holds the floor.
        double[] keep = new double[4];
        for (int axis = 0; axis < 4; axis += 2)
        {
            double Across(double k)
            {
                double across = 0;
                for (int i = axis; i < axis + 2; i++)
                {
                    double run = foot ? insets[i] * (high - bottom) / height : insets[i];
                    across += narrowed[i] ? Math.Min(narrow[i], wide[i] - (k * run)) : narrow[i];
                }

                return across;
            }

            double most = Across(1) >= floor ? 1 : 0;
            if (most == 0 && Across(0) > floor)
            {
                double lo = 0;
                double hi = 1;
                for (int step = 0; step < HoldSteps; step++)
                {
                    double mid = (lo + hi) / 2;
                    (lo, hi) = Across(mid) >= floor ? (mid, hi) : (lo, mid);
                }

                most = lo;
            }

            keep[axis] = most;
            keep[axis + 1] = most;
        }

        for (int attempt = 0; attempt < NarrowTries; attempt++)
        {
            var planes = new List<Plane>();
            for (int i = 0; i < 4; i++)
            {
                double d = insets[i] * keep[i] / (1 << attempt);
                if (!narrowed[i] || d <= 0)
                {
                    continue;
                }

                var h = outs[i];
                if (foot)
                {
                    // Through the side's ground-line edge set in by d, and its top edge where the side is.
                    double length = Math.Sqrt((height * height) + (d * d));
                    var normal = new V3(h.X * height / length, h.Y * height / length, -d / length);
                    planes.Add(new Plane(normal, ((height * wide[i]) - (d * high)) / length));
                }
                else
                {
                    // Through the side's ground-line edge, and where it would reach the top set in by d.
                    double run = grounds[i] - wide[i] + d;
                    double length = Math.Sqrt((height * height) + (run * run));
                    var normal = new V3(h.X * height / length, h.Y * height / length, run / length);
                    planes.Add(new Plane(normal, ((height * grounds[i]) + (run * low)) / length));
                }
            }

            if (planes.Count == 0)
            {
                return block;
            }

            var narrower = new ConvexBlock([.. block.Shell, .. planes], block.Cuts);
            if (narrower.MinWidth() >= shape.MinThickness)
            {
                return narrower;
            }
        }

        return block;
    }

    /// <summary>
    /// The blocks on the ground with the cliff's bottom tilted under them: one plane, tilted by Underside tilt from level
    /// and rising toward a seeded direction, from <see cref="ContactWidth"/> in from the corner of their flat bottoms
    /// furthest the other way, so the cliff still rests on its flat bottom there, on a strip the mesh keeps. The tilt is
    /// held so the plane rises across the blocks by at most <see cref="RiseShare"/> of the lowest of their heights (with their
    /// sinks), and halved while it would leave any of them thinner than Min thickness (left out after
    /// <see cref="NarrowTries"/> halvings). A cut's face that would then stand a thin fin from it is left out, and each
    /// cut is held to its finished shell's limit.
    /// </summary>
    private static List<LaidBlock> Tilted(List<LaidBlock> level, CliffSettings s, uint seed, double thin)
    {
        double aim = 2 * Math.PI * CliffNoise.Hash01(seed, UndersideSalt);
        var toward = new V3(JsMath.Cos(aim), JsMath.Sin(aim), 0);
        var corners = level.SelectMany(b => b.Block.Corners()).ToList();
        double bottom = corners.Min(p => p.Z);
        double from = corners.Where(p => p.Z <= bottom + LimitHair).Min(p => V3.Dot(toward, p)) + ContactWidth;
        double span = corners.Max(p => V3.Dot(toward, p)) - from;
        double rise = RiseShare * level.Min(b => b.Shape.Height + b.Shape.Sink);
        double tilt = Math.Min(s.UndersideTilt * Deg, JsMath.Atan(rise / Math.Max(span, LimitHair)));
        for (int attempt = 0; attempt < NarrowTries; attempt++, tilt /= 2)
        {
            double sin = JsMath.Sin(tilt);
            double cos = JsMath.Cos(tilt);
            var plane = new Plane(new V3(sin * toward.X, sin * toward.Y, -cos), (sin * from) - (cos * bottom));
            var tilted = level.ConvertAll(b => b with { Block = new ConvexBlock([.. b.Block.Shell, plane], b.Block.Cuts) });
            if (tilted.TrueForAll(b => b.Block.MinWidth() >= thin))
            {
                return tilted.ConvertAll(b => b with { Block = Limited(Unfinned(b.Block, thin)) });
            }
        }

        return level;
    }

    /// <summary>
    /// The block without the cuts that would leave a fin of rock thinner than a thickness against its shell or an
    /// earlier cut (see <see cref="BlockMaker.LeavesAnyFin"/>): after planes are added to its shell, which the cuts were
    /// not drawn against.
    /// </summary>
    private static ConvexBlock Unfinned(ConvexBlock block, double thickness)
    {
        var shell = new ConvexBlock(block.Shell, []);
        var kept = new List<IReadOnlyList<Plane>>(block.Cuts.Count);
        foreach (var cut in block.Cuts)
        {
            if (!BlockMaker.LeavesAnyFin(shell, cut, kept, thickness))
            {
                kept.Add(cut);
            }
        }

        return kept.Count == block.Cuts.Count ? block : new ConvexBlock(block.Shell, kept);
    }

    /// <summary>How far a block's shell reaches along a direction at a height (the most of its section's corners that way), or NaN where it does not reach the height.</summary>
    private static double Reach(ConvexBlock block, V3 along, double z)
    {
        var section = block.CornersWithin([new Plane(new V3(0, 0, 1), z + SectionHalf), new Plane(new V3(0, 0, -1), SectionHalf - z)]);
        return section.Count == 0 ? double.NaN : section.Max(p => V3.Dot(along, p));
    }

    /// <summary>
    /// The block with each cut held to <see cref="MaxCutShare"/> of the block's finished shell's width along the cut's
    /// first plane (how deep it goes is measured from the shell's farthest corner that way to the deepest point the cut
    /// takes): a cut drawn against the shell before its foot plane and end bevels were added, which narrow it, is moved
    /// out along that plane by as much as it goes too deep, and dropped if it then takes nothing.
    /// </summary>
    private static ConvexBlock Limited(ConvexBlock block)
    {
        var shell = new ConvexBlock(block.Shell, []);
        var cuts = new List<IReadOnlyList<Plane>>(block.Cuts.Count);
        bool changed = false;
        foreach (var cut in block.Cuts)
        {
            var held = cut;
            bool moved = false;
            for (int pass = 0; pass < LimitPasses; pass++)
            {
                double over = CutDepth(shell, held) - (MaxCutShare * shell.WidthAlong(held[0].Normal));
                if (over <= LimitHair)
                {
                    break;
                }

                held = [new Plane(held[0].Normal, held[0].Offset + over + LimitHair), .. held.Skip(1)];
                moved = true;
            }

            // A cut moved out until it takes nothing is dropped; any other is kept as it is.
            changed |= moved;
            if (!moved || CutDepth(shell, held) > 0)
            {
                cuts.Add(held);
            }
        }

        return changed ? new ConvexBlock(block.Shell, cuts) : block;
    }

    /// <summary>
    /// How deep a cut goes into a shell along its first plane's normal: from the shell's farthest corner that way to the
    /// deepest corner of what the cut takes (inside the shell and in front of every cut plane); 0 where it takes nothing.
    /// </summary>
    private static double CutDepth(ConvexBlock shell, IReadOnlyList<Plane> cut)
    {
        var n = cut[0].Normal;
        var taken = new ConvexBlock([.. shell.Shell, .. cut.Select(p => new Plane(-p.Normal, -p.Offset))], []).Corners();
        return taken.Count == 0 ? 0 : shell.Corners().Max(c => V3.Dot(n, c)) - taken.Min(c => V3.Dot(n, c));
    }

    /// <summary>
    /// The block with a foot plane across its front below its ground line, receding at <see cref="FootSlope"/>, so the
    /// part sunk into the blocks below never stands out in front of them as a thin lip. The foot starts at the front
    /// of the blocks below, found along the block's middle just under its ground line, so a block set back on them
    /// keeps all of its sunk part; it starts no further back than <see cref="FootReach"/> behind the block's own front.
    /// </summary>
    private static ConvexBlock Footed(ConvexBlock block, BlockShape shape, List<LaidBlock> below)
    {
        double front = shape.Base.Y - (shape.Depth / 2);
        double z = shape.Base.Z - HeldOverlap;
        double edgeY = front + FootReach;
        for (double y = front - FootSearch; y < front + FootReach; y += FootStep)
        {
            var p = new V3(shape.Base.X, y, z);
            if (below.Any(b => b.Block.Distance(p) < 0))
            {
                edgeY = y;
                break;
            }
        }

        var normal = new V3(0, -JsMath.Cos(FootSlope * Deg), -JsMath.Sin(FootSlope * Deg));
        var edge = new V3(shape.Base.X, edgeY, shape.Base.Z);
        return new ConvexBlock([.. block.Shell, new Plane(normal, V3.Dot(normal, edge))], block.Cuts);
    }

    /// <summary>
    /// The block with its outer end (-x for -1, +x for 1) finished as rock: a bite out of the end's top, its floor
    /// sloping down toward the end, as deep as Cut depth allows along the end face; or, by seed or where cuts are
    /// off, a bevel plane across the end's top (held short of the top's middle with <paramref name="keepMiddle"/>); a bevel
    /// too where the bite would leave a fin against the block's other cuts or its shell (see <see cref="BlockMaker.LeavesAnyFin"/>).
    /// </summary>
    private static ConvexBlock Finished(ConvexBlock block, BlockShape shape, int end, uint seed, int salt, bool keepMiddle)
    {
        var face = block.Shell.Where(p => Math.Abs(p.Normal.Z) < 0.5).MaxBy(p => end * p.Normal.X).Normal;
        double hi = block.Corners().Max(c => V3.Dot(face, c));
        double depth = shape.CutDepth * MaxCutShare * block.WidthAlong(face) * (0.5 + (0.5 * CliffNoise.Hash01(seed, salt + 1)));
        if (CliffNoise.Hash01(seed, salt) < 0.5 && depth >= MinCut)
        {
            double top = block.Corners().Where(c => V3.Dot(face, c) >= hi - depth).Max(c => c.Z);
            double floor = shape.Base.Z + ((top - shape.Base.Z) * (0.45 + (0.35 * CliffNoise.Hash01(seed, salt + 2))));
            var slope = EndBiteFloor(end);
            var at = new V3(shape.Base.X + (end * shape.Width / 2), shape.Base.Y, floor);
            Plane[] bite = [new Plane(face, hi - depth), new Plane(slope, V3.Dot(slope, at))];

            // A bite whose faces would cross another cut's at a knife edge is a bevel instead.
            var shell = new ConvexBlock(block.Shell, []);
            if (!BlockMaker.LeavesAnyFin(shell, bite, block.Cuts, shape.MinThickness))
            {
                return new ConvexBlock(block.Shell, [.. block.Cuts, bite]);
            }
        }

        var h = new V3(face.X, face.Y, 0).Normalized();
        var normal = new V3(h.X, h.Y, 0.8).Normalized();
        double cut = EndBevelShare * Math.Min(shape.Width, shape.Height) * (0.6 + (0.4 * CliffNoise.Hash01(seed, salt + 3)));

        // On a block bevelled at both ends that carries another, each bevel is held short of the middle of the top, so a
        // flat middle carries the block resting on it (else that block's base, cut back to match both bevels, meets it
        // nowhere and comes loose).
        double most = block.Corners().Max(c => V3.Dot(normal, c));
        if (keepMiddle)
        {
            var top = block.Shell[Math.Max(shape.Faces, 4)];
            double z = (top.Offset - (top.Normal.X * shape.Base.X) - (top.Normal.Y * shape.Base.Y)) / top.Normal.Z;
            cut = Math.Min(cut, EndBevelReach * (most - V3.Dot(normal, new V3(shape.Base.X, shape.Base.Y, z))));
        }

        var bevel = new Plane(normal, most - cut);
        return new ConvexBlock([.. block.Shell, bevel], block.Cuts);
    }

    /// <summary>
    /// How far an end face turns back, in degrees: a seeded turn from <see cref="TurnLeast"/> to <see cref="TurnMost"/>,
    /// scaled by End turn over <see cref="TurnMost"/> (at the default, 50, by exactly 1).
    /// </summary>
    private static double EndTurn(CliffSettings s, uint seed, int salt) =>
        (TurnLeast + ((TurnMost - TurnLeast) * CliffNoise.Hash01(seed, salt))) * (Math.Max(s.EndTurn, 0) / TurnMost);

    /// <summary>How deep two stacked blocks overlap at least, in metres: <see cref="SealDepth"/> plus twice the chisel's depth.</summary>
    private static double Seal(CliffSettings s) => SealDepth + (2 * Math.Max(s.Chisel, 0));

    /// <summary>A count slider's value for one block: the seed varies it by up to one either way, so the slider is the average; 0 stays 0.</summary>
    private static int Vary(int count, uint seed, int salt) =>
        count <= 0 ? 0 : count - 1 + Math.Min((int)(CliffNoise.Hash01(seed, salt) * 3), 2);

    /// <summary>A seeded number from -1 to 1.</summary>
    private static double Signed(uint seed, int salt) => (2 * CliffNoise.Hash01(seed, salt)) - 1;

    /// <summary>A column: its middle along x, its width, height and depth, and its middle along y.</summary>
    private readonly record struct Column(double X, double Width, double Height, double Depth, double Y);

    /// <summary>A slab a block is made from: its ground line, height, depth, middle along y, sink and lean (in radians).</summary>
    private readonly record struct Box(double Z, double Height, double Depth, double Y, double Sink, double Lean);
}
