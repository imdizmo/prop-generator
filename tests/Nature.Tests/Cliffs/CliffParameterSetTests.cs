using PropGenerator.Nature.Cliffs;
using PropGenerator.Nature.Parameters;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

public class CliffParameterSetTests
{
    /// <summary>The spec's sliders in panel order: label, group, range, step, default and unit (the palette is a choice).</summary>
    private static readonly (string Label, string Group, double Min, double Max, double Step, double Default, string? Unit)[] Expected =
    [
        ("Width", "Proportions", 2, 60, 0.1, 28, "m"),
        ("Height", "Proportions", 1, 40, 0.1, 9, "m"),
        ("Depth", "Proportions", 1, 25, 0.1, 6, "m"),
        ("Blocks", "Blocks", 1, 12, 1, 5, null),
        ("Block variety", "Blocks", 0, 1, 0.01, 0.5, null),
        ("Tiers", "Blocks", 1, 4, 1, 1, null),
        ("Layers", "Blocks", 0, 12, 1, 0, null),
        ("Layer variety", "Blocks", 0, 1, 0.01, 0.5, null),
        ("Joint crack", "Blocks", 0, 0.2, 0.005, 0.08, "m"),
        ("Lean", "Shape", -10, 30, 0.5, 8, "°"),
        ("Top tilt", "Shape", 0, 60, 0.5, 4, "°"),
        ("Taper", "Shape", 0, 0.6, 0.01, 0, null),
        ("End turn", "Shape", 0, 50, 0.5, 50, "°"),
        ("Foot", "Shape", 0, 0.4, 0.01, 0, null),
        ("Underside tilt", "Shape", 0, 30, 0.5, 0, "°"),
        ("Faces", "Shape", 4, 16, 1, 8, null),
        ("Roughness", "Shape", 0, 1.5, 0.01, 0.3, "m"),
        ("Bevel", "Shape", 0, 1.5, 0.01, 0.3, "m"),
        ("Sink", "Shape", 0, 2, 0.05, 0.5, "m"),
        ("Steps", "Cuts", 0, 8, 1, 2, null),
        ("Bites", "Cuts", 0, 8, 1, 2, null),
        ("Grooves", "Cuts", 0, 4, 1, 1, null),
        ("Cut depth", "Cuts", 0, 1, 0.01, 0.6, null),
        ("Chisel", "Surface", 0, 0.5, 0.005, 0.03, "m"),
        ("Chisel size", "Surface", 0.2, 5, 0.05, 2.5, "m"),
        ("Edge chips", "Surface", 0, 1, 0.01, 0.3, "m"),
        ("Chip size", "Surface", 0.1, 3, 0.05, 1.5, "m"),
        ("Edge wear", "Surface", 0, 1, 0.01, 0.15, "m"),
        ("Wear width", "Surface", 0.1, 3, 0.05, 0.8, "m"),
        ("Wear size", "Surface", 0.1, 5, 0.05, 2.0, "m"),
        ("Smudge", "Surface", 0, 1.5, 0.01, 0.02, "m"),
        ("Smudge size", "Surface", 0.3, 10, 0.1, 5, "m"),
        ("Creases", "Cracks", 0, 4, 1, 1, null),
        ("Crease depth", "Cracks", 0, 2, 0.01, 0.2, "m"),
        ("Crease width", "Cracks", 0.05, 2, 0.01, 0.1, "m"),
        ("Crease step", "Cracks", 0, 1.5, 0.01, 0, "m"),
        ("Crease angle", "Cracks", 0, 90, 1, 10, "°"),
        ("Crease wobble", "Cracks", 0, 1.5, 0.01, 0.15, "m"),
        ("Crease seed", "Cracks", 0, 9999, 1, 31, null),
        ("Ledges", "Cracks", 0, 3, 1, 0, null),
        ("Ledge depth", "Cracks", 0, 2, 0.01, 0.3, "m"),
        ("Ledge step", "Cracks", 0, 1.5, 0.01, 0.2, "m"),
        ("Lines", "Cracks", 0, 40, 1, 0, null),
        ("Line depth", "Cracks", 0, 0.5, 0.005, 0.05, "m"),
        ("Line wobble", "Cracks", 0, 10, 0.1, 2, null),
        ("Detail", "Mesh", 2, 24, 0.5, 8, null),
        ("Sharp angle", "Mesh", 5, 90, 1, 30, "°"),
        ("Palette", "Colour", 0, 0, 0, 0, null),
        ("Variation", "Colour", 0, 1, 0.01, 0.5, null),
    ];

    /// <summary>The sliders that take whole numbers.</summary>
    private static readonly string[] Counts =
        ["blocks", "tiers", "layers", "faces", "steps", "bites", "grooves", "creases", "crease-seed", "ledges", "lines"];

    [Fact]
    public void TheSlidersAreTheSpecsInPanelOrder()
    {
        var controls = CliffParameterSet.Instance.Controls;
        Assert.Equal("slab-cliff", CliffParameterSet.Id);
        Assert.Equal(Expected.Select(e => e.Label), controls.Select(c => c.Label));
        Assert.Equal(Expected.Select(e => e.Label.ToLowerInvariant().Replace(' ', '-')), controls.Select(c => c.Key));
        Assert.Equal(Expected.Select(e => e.Group), controls.Select(c => c.Group));
        Assert.Equal(
            ["Proportions", "Blocks", "Shape", "Cuts", "Surface", "Cracks", "Mesh", "Colour"],
            controls.Select(c => c.Group).Distinct());
        Assert.DoesNotContain(controls, c => c.Key == "seed");
        foreach (var (label, _, min, max, step, value, unit) in Expected.Where(e => e.Label != "Palette"))
        {
            var spec = Assert.IsType<NumberSpec>(controls.Single(c => c.Label == label));
            Assert.Equal((label, min, max, step, value, unit), (label, spec.Min, spec.Max, spec.Step, spec.DefaultNumber, spec.Unit));
            Assert.Equal(Counts.Contains(spec.Key), spec.Whole);
        }
    }

    [Fact]
    public void ThePaletteIsAChoiceOfTheRockPalettes()
    {
        var palette = Assert.IsType<ChoiceSpec>(CliffParameterSet.Instance.Find("palette"));
        Assert.Equal("granite", palette.DefaultChoice);
        Assert.Equal(7, palette.Options.Count);
        Assert.Contains(palette.Options, o => o is { Value: "granite", Label: "Granite" });
        Assert.Contains(palette.Options, o => o is { Value: "lavender", Label: "Lavender" });
    }

    [Fact]
    public void TheDefaultsAreTheLongCliffsWithSeedOne()
    {
        var s = CliffSettings.Defaults;
        Assert.Equal((28.0, 9.0, 6.0), (s.Width, s.Height, s.Depth));
        Assert.Equal((5, 1, 0), (s.Blocks, s.Tiers, s.Layers));
        Assert.Equal((8, 2, 2, 1), (s.Faces, s.Steps, s.Bites, s.Grooves));
        Assert.Equal((0.03, 2.5, 31), (s.Chisel, s.ChiselSize, s.CreaseSeed));
        Assert.Equal(("granite", 1u), (s.Palette, s.Seed));
    }

    [Fact]
    public void EverySliderHasATooltipEndingWithAFullStop()
    {
        foreach (var spec in CliffParameterSet.Instance.Controls)
        {
            string text = CliffTooltips.For(spec.Key);
            Assert.True(text.Length >= 60, spec.Key);
            Assert.EndsWith(".", text);
            Assert.Equal(text, spec.Help);
            if (spec is NumberSpec n)
            {
                // The range is in it, in the panel's own numbers.
                string range = $"from {Number(n.Min)} to {Number(n.Max)}";
                Assert.True(text.Contains(range, StringComparison.Ordinal), $"{spec.Key}: no \"{range}\"");
            }

            if (spec is NumberSpec { Unit: "m" })
            {
                Assert.Contains("metres", text);
            }

            if (spec is NumberSpec { Unit: "°" })
            {
                Assert.Contains("degrees", text);
            }
        }

        Assert.Throws<ArgumentException>(() => CliffTooltips.For("nope"));
    }

    [Fact]
    public void TheRandomBatchCoversEachSlidersRange()
    {
        // Review-final Minor 7: seeded with consecutive integers, the batch's 200 settings were a lattice (each draw
        // stepping by a fixed amount from one setting to the next), and Layer variety's draws left 17% of its range
        // undrawn. Dealt from hashed seeds, no slider's 200 draws leave a gap over a tenth of its range.
        var settings = Enumerable.Range(0, 200).Select(CliffMeshes.WideRandom).ToList();
        (string Name, Func<CliffSettings, double> Of, double Min, double Max)[] sliders =
        [
            ("Width", s => s.Width, 2, 60),
            ("Block variety", s => s.BlockVariety, 0, 1),
            ("Layer variety", s => s.LayerVariety, 0, 1),
            ("Lean", s => s.Lean, -10, 30),
            ("Top tilt", s => s.TopTilt, 0, 60),
            ("Sink", s => s.Sink, 0, 2),
            ("Smudge", s => s.Smudge, 0, 1.5),
            ("Foot", s => s.Foot, 0, 0.4),
        ];
        foreach (var (name, of, min, max) in sliders)
        {
            double[] drawn = [min, .. settings.Select(of).Order(), max];
            double gap = Enumerable.Range(1, drawn.Length - 1).Max(i => drawn[i] - drawn[i - 1]) / (max - min);
            Assert.True(gap <= 0.1, $"{name}: {gap:P0} of its range undrawn");
        }
    }

    private static string Number(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
