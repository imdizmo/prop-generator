using PropGenerator.Nature.Js;
using PropGenerator.Nature.Parameters;

namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// The slab cliff's settings as typed values: one property per slider of <see cref="CliffParameterSet"/> (lengths in
/// metres, angles in degrees, counts whole) and the seed. Only <see cref="From"/> and <see cref="Defaults"/> give valid
/// settings: an object built by hand (or a <c>with</c> copy that sets a size to zero) may hold values the generator
/// refuses (see <see cref="CliffGenerator.Build(CliffSettings, double)"/>).
/// </summary>
public sealed record CliffSettings
{
    /// <summary>Gets the settings every slider's default gives, with seed 1: the Long cliff.</summary>
    public static CliffSettings Defaults { get; } = From(CliffParameterSet.Instance.Defaults, 1);

    /// <summary>Gets how far the cliff spreads from left to right (x), in metres.</summary>
    public double Width { get; init; }

    /// <summary>Gets how tall the cliff stands above the ground line (z = 0), in metres.</summary>
    public double Height { get; init; }

    /// <summary>Gets how far the cliff runs from front to back (y), in metres.</summary>
    public double Depth { get; init; }

    /// <summary>Gets how many blocks stand side by side along the width (1 to 12).</summary>
    public int Blocks { get; init; }

    /// <summary>Gets how much the blocks differ in width, height, depth and set-back, from 0 (alike) to 1.</summary>
    public double BlockVariety { get; init; }

    /// <summary>Gets how many levels are stacked up, each lower and set back (1 to 4).</summary>
    public int Tiers { get; init; }

    /// <summary>Gets how many horizontal plates each block is cut into (0 to 12; 0 leaves it whole).</summary>
    public int Layers { get; init; }

    /// <summary>Gets how much the plates differ in thickness, from 0 (alike) to 1.</summary>
    public double LayerVariety { get; init; }

    /// <summary>
    /// Gets how wide the crack is, in metres, where two blocks of the same tier and layer meet at the surface (0 leaves a
    /// bare seam).
    /// </summary>
    public double JointCrack { get; init; }

    /// <summary>Gets how far the front faces batter back at the top, in degrees; negative overhangs.</summary>
    public double Lean { get; init; }

    /// <summary>Gets how far each block's top tilts from level, in degrees, in a seeded direction.</summary>
    public double TopTilt { get; init; }

    /// <summary>
    /// Gets how much the top blocks narrow toward their top, as a share of their depth (front and back) and, at the
    /// row's ends, of their width (0 to 0.6; 0 leaves them as they are).
    /// </summary>
    public double Taper { get; init; }

    /// <summary>
    /// Gets the most the end blocks' outer end faces turn back toward the front, in degrees (0 to 50; 0 leaves square
    /// end corners): each end's seeded turn is scaled by this over 50.
    /// </summary>
    public double EndTurn { get; init; }

    /// <summary>
    /// Gets how much the blocks on the ground narrow toward the ground line, as a share of their depth (front and back)
    /// and, at the row's ends, of their width (0 to 0.4; 0 stands them on their whole base).
    /// </summary>
    public double Foot { get; init; }

    /// <summary>
    /// Gets how far the bottom under the blocks on the ground tilts, in degrees (0 to 30), rising from the cliff's
    /// lowest corner toward a seeded side.
    /// </summary>
    public double UndersideTilt { get; init; }

    /// <summary>Gets how many side planes surround each block's footprint (4 to 16).</summary>
    public int Faces { get; init; }

    /// <summary>Gets the most a side plane is set in from the footprint, in metres.</summary>
    public double Roughness { get; init; }

    /// <summary>Gets how far the chamfers across the top and bottom rims cut in, in metres.</summary>
    public double Bevel { get; init; }

    /// <summary>Gets how far the flat bottom lies below the ground line, in metres.</summary>
    public double Sink { get; init; }

    /// <summary>Gets how many step cuts each block takes out of its top, on average.</summary>
    public int Steps { get; init; }

    /// <summary>Gets how many bites each block takes out of its upright corners, on average.</summary>
    public int Bites { get; init; }

    /// <summary>Gets how many grooves each block has along a face, on average.</summary>
    public int Grooves { get; init; }

    /// <summary>Gets how deep the cuts go, from 0 to 1 of the most allowed (40% of the block's local thickness).</summary>
    public double CutDepth { get; init; }

    /// <summary>Gets how deep the chisel cuts the surface into flat facets, in metres (0 leaves it as it is).</summary>
    public double Chisel { get; init; }

    /// <summary>Gets how broad each chiselled facet is, in metres.</summary>
    public double ChiselSize { get; init; }

    /// <summary>Gets how deep the flat chips knocked off the edges reach, in metres (0 leaves the edges as they are).</summary>
    public double EdgeChips { get; init; }

    /// <summary>Gets how long each chip along an edge is, in metres.</summary>
    public double ChipSize { get; init; }

    /// <summary>Gets how deep the uneven wear of the edges reaches, in metres.</summary>
    public double EdgeWear { get; init; }

    /// <summary>Gets how far in from each edge the wear spreads, in metres.</summary>
    public double WearWidth { get; init; }

    /// <summary>Gets how big each worn lump is, in metres.</summary>
    public double WearSize { get; init; }

    /// <summary>Gets how far the soft noise pushes and pulls the surface, in metres.</summary>
    public double Smudge { get; init; }

    /// <summary>Gets how big each smear is, in metres.</summary>
    public double SmudgeSize { get; init; }

    /// <summary>Gets how many upright cracks run down the cliff (0 to 4).</summary>
    public int Creases { get; init; }

    /// <summary>Gets how deep each crack cuts in, in metres.</summary>
    public double CreaseDepth { get; init; }

    /// <summary>Gets how wide each crack (and each ledge) is at the surface, in metres.</summary>
    public double CreaseWidth { get; init; }

    /// <summary>Gets how far the rock on one side of each crack is set back, in metres.</summary>
    public double CreaseStep { get; init; }

    /// <summary>Gets how far the cracks turn from square to the faces, in degrees.</summary>
    public double CreaseAngle { get; init; }

    /// <summary>Gets how far each crack and ledge wanders off a straight line, in metres.</summary>
    public double CreaseWobble { get; init; }

    /// <summary>Gets the number that picks a different layout for the cracks only.</summary>
    public int CreaseSeed { get; init; }

    /// <summary>Gets how many horizontal breaks cross the cliff (0 to 3).</summary>
    public int Ledges { get; init; }

    /// <summary>Gets how deep each ledge cuts in, in metres.</summary>
    public double LedgeDepth { get; init; }

    /// <summary>Gets how far the rock on one side of each ledge is set back, in metres.</summary>
    public double LedgeStep { get; init; }

    /// <summary>Gets how many thin horizontal lines run round the sides (0 to 40).</summary>
    public int Lines { get; init; }

    /// <summary>Gets how deep the thin lines are cut, in metres.</summary>
    public double LineDepth { get; init; }

    /// <summary>Gets how wavy the thin lines are; 0 keeps them level.</summary>
    public double LineWobble { get; init; }

    /// <summary>Gets the mesh points per metre at full detail.</summary>
    public double Detail { get; init; }

    /// <summary>Gets the angle, in degrees, past which a bend between faces is shaded crisp.</summary>
    public double SharpAngle { get; init; }

    /// <summary>Gets the rock palette the cliff is coloured from, by name (granite, slate, ...).</summary>
    public string Palette { get; init; } = "granite";

    /// <summary>Gets how much the colour varies across the cliff, from 0 (even) to 1 (patchy).</summary>
    public double Variation { get; init; }

    /// <summary>Gets the number that picks a different cliff from the same settings: every block's share, plane and cut, and the noise.</summary>
    public uint Seed { get; init; }

    /// <summary>Reads the settings from validated parameter values, keyed as <see cref="CliffParameterSet"/> keys them.</summary>
    /// <param name="p">The values; every key of the parameter set must be there.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The settings.</returns>
    public static CliffSettings From(ParameterDictionary p, uint seed)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new CliffSettings
        {
            Width = p.Number("width"),
            Height = p.Number("height"),
            Depth = p.Number("depth"),
            Blocks = Whole(p, "blocks"),
            BlockVariety = p.Number("block-variety"),
            Tiers = Whole(p, "tiers"),
            Layers = Whole(p, "layers"),
            LayerVariety = p.Number("layer-variety"),
            JointCrack = p.Number("joint-crack"),
            Lean = p.Number("lean"),
            TopTilt = p.Number("top-tilt"),
            Taper = p.Number("taper"),
            EndTurn = p.Number("end-turn"),
            Foot = p.Number("foot"),
            UndersideTilt = p.Number("underside-tilt"),
            Faces = Whole(p, "faces"),
            Roughness = p.Number("roughness"),
            Bevel = p.Number("bevel"),
            Sink = p.Number("sink"),
            Steps = Whole(p, "steps"),
            Bites = Whole(p, "bites"),
            Grooves = Whole(p, "grooves"),
            CutDepth = p.Number("cut-depth"),
            Chisel = p.Number("chisel"),
            ChiselSize = p.Number("chisel-size"),
            EdgeChips = p.Number("edge-chips"),
            ChipSize = p.Number("chip-size"),
            EdgeWear = p.Number("edge-wear"),
            WearWidth = p.Number("wear-width"),
            WearSize = p.Number("wear-size"),
            Smudge = p.Number("smudge"),
            SmudgeSize = p.Number("smudge-size"),
            Creases = Whole(p, "creases"),
            CreaseDepth = p.Number("crease-depth"),
            CreaseWidth = p.Number("crease-width"),
            CreaseStep = p.Number("crease-step"),
            CreaseAngle = p.Number("crease-angle"),
            CreaseWobble = p.Number("crease-wobble"),
            CreaseSeed = Whole(p, "crease-seed"),
            Ledges = Whole(p, "ledges"),
            LedgeDepth = p.Number("ledge-depth"),
            LedgeStep = p.Number("ledge-step"),
            Lines = Whole(p, "lines"),
            LineDepth = p.Number("line-depth"),
            LineWobble = p.Number("line-wobble"),
            Detail = p.Number("detail"),
            SharpAngle = p.Number("sharp-angle"),
            Palette = p.Text("palette"),
            Variation = p.Number("variation"),
            Seed = seed,
        };
    }

    /// <summary>A count slider's value as a whole number (validated values are whole already).</summary>
    private static int Whole(ParameterDictionary p, string key) => (int)JsMath.Round(p.Number(key));
}
