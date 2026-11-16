using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Details;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class SettingsPanelTests
{
    private static string? ContentText(Control c) => c switch
    {
        ContentControl cc => cc.Content?.ToString(),
        TextBox t => t.Text,
        _ => null,
    };

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheColourSquareShowsOnlyTheSpectrumAndEveryPartHasAWrittenTooltip()
    {
        MainWindow w = Ui.Open(new FakePainter());
        ColourRow row = w.Panel.ColourRow!;
        row.OpenPicker();
        Ui.Pump();
        var shown = row.Picker.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible).ToList();

        // The palette and the components (number boxes) are not needed: the #RRGGBB box is under the square. No alpha.
        Assert.Single(shown.OfType<TabItem>());
        Assert.DoesNotContain(shown, c => c.Name == "ColorSpectrumAlphaSlider");
        Assert.DoesNotContain(shown, c => c is NumericUpDown or TextBox or ListBox);
        Assert.True(row.HexBox.IsEffectivelyVisible);

        string[] accents = ["PART_AccentDecrement2Border", "PART_AccentDecrement1Border", "PART_AccentIncrement1Border", "PART_AccentIncrement2Border"];
        var parts = shown.Where(c => c is TabItem or ColorSlider or ColorSpectrum or ColorPreviewer || accents.Contains(c.Name)).ToList();
        Assert.Equal(1 + 1 + 1 + 1 + 4, parts.Count);
        Assert.All(parts, c =>
        {
            string? tip = ToolTip.GetTip(c) as string;
            Assert.False(string.IsNullOrWhiteSpace(tip), $"{c.GetType().Name} '{c.Name}' has no tooltip");
            Assert.True(tip!.Length >= 40, $"tooltip too short: '{tip}'");
            Assert.NotEqual(AutomationProperties.GetName(c), tip);
            Assert.NotEqual(ContentText(c), tip);
        });
        Assert.Equal(parts.Count, parts.Select(ToolTip.GetTip).Distinct().Count());
        row.Custom.Flyout!.Hide();
        Ui.Pump();
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EachSettingGetsItsKindOfRowWithTheSchemasTooltip()
    {
        IReadOnlyList<ParamSpec> specs = Ui.Specs();
        var panel = new SettingsPanel(specs);

        // Numbers (whole ones too) are sliders, on/off a switch, the colour swatches; the seed has no row.
        foreach (ParamSpec s in specs)
        {
            Grid? row = panel.RowFor(s.Key);
            if (s.Key == "seed")
            {
                Assert.Null(row);
                continue;
            }

            Assert.IsType(
                s.Type switch { "float" or "int" => typeof(NumberRow), "bool" => typeof(SwitchRow), _ => typeof(ColourRow) },
                row);
            Assert.Equal(s.Tooltip, ToolTip.GetTip(row!.Children.OfType<TextBlock>().First()));
        }

        Assert.Equal(specs.Count(s => s.Type is "float" or "int") - 1, panel.GetLogicalDescendantsOfPanel().OfType<Slider>().Count());
        Assert.Single(panel.GetLogicalDescendantsOfPanel().OfType<ToggleSwitch>());
        Assert.StartsWith(specs.Single(s => s.Key == "contrast").Tooltip, ToolTip.GetTip(panel.NumberRowFor("contrast")!.Slider) as string, StringComparison.Ordinal);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EachSettingShowsItsCurrentValue()
    {
        MainWindow w = Ui.Open(new FakePainter());
        var contrast = Ui.Named<Slider>(w, "Contrast");
        TextBox box = w.Panel.ValueBoxFor("contrast");
        Assert.Equal("0.65", box.Text);

        contrast.Value = 0.9;
        Ui.Pump();
        Assert.Equal("0.90", box.Text);
        Assert.Equal(0.9, w.Panel.Current.GetFloat("contrast"), 3);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TypingAValueMovesTheSlider()
    {
        MainWindow w = Ui.Open(new FakePainter());
        w.Panel.Groups.Single(g => g.Title == "Planks").IsOpen = true;
        var lit = Ui.Named<Slider>(w, "Light along plank");
        TextBox box = w.Panel.ValueBoxFor("lit");
        box.Text = "0.2";
        w.Panel.CommitValueBox("lit");
        Ui.Pump();
        Assert.Equal(0.2, lit.Value, 6);
        Assert.Equal(0.2f, w.Panel.Current.GetFloat("lit"), 5);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheDefaultColourKeepsItsExactNumbersUntilTouched()
    {
        MainWindow w = Ui.Open(new FakePainter());
        TextBox hex = w.Panel.ValueBoxFor("colour");
        Assert.Equal("#45302B", hex.Text);
        Assert.Equal(new Rgb(0.272f, 0.188f, 0.170f), w.Panel.Current.GetColour("colour"));

        hex.Text = "#C8A27A";
        w.Panel.CommitValueBox("colour");
        Ui.Pump();
        Assert.Equal(Rgb.FromHex("#C8A27A"), w.Panel.Current.GetColour("colour"));
        Assert.Equal(Avalonia.Media.Color.Parse("#C8A27A"), w.Panel.ColourRow!.Picker.Color);
    }

    private static IReadOnlyList<ParamSpec> RockSpecs() =>
    [
        new("kind", "Kind", "Which kind of stone.", "paint", "choice", null, null, null,
            System.Text.Json.JsonSerializer.SerializeToElement("boulder"), "Shape",
            [new ChoiceItem("boulder", "Boulder"), new ChoiceItem("outcrop", "Outcrop")]),
        new("size", "Size", "How big the stone is.", "paint", "float", 0.5, 2.0, 0.1,
            System.Text.Json.JsonSerializer.SerializeToElement(1.0), "Shape", null),
        new("grit", "Grit", "How rough the stone is.", "paint", "float", 0, 1, 0.05,
            System.Text.Json.JsonSerializer.SerializeToElement(0.5), null, null),
        new("rock_seed", "Rock seed", "Picks one layout of the stone.", "paint", "seed", null, null, null,
            System.Text.Json.JsonSerializer.SerializeToElement(7), "Variation", null),
    ];

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RebuildSwapsRowsAndKeepsEvents()
    {
        var panel = new SettingsPanel(Ui.Specs());
        var labels = new List<string>();
        panel.Committed += labels.Add;
        Assert.Equal(5, panel.Groups.Count);

        IReadOnlyList<ParamSpec> rock = RockSpecs();
        panel.Rebuild(rock, Settings.Defaults(rock));

        // A setting with no group sits under Shape, as in easy-props; the seed has no row, so no Variation group.
        Assert.Empty(labels);
        Assert.Equal(["Shape"], panel.Groups.Select(g => g.Title));
        Assert.Equal(3, panel.Rows.Count);
        Assert.Same(panel.Groups[0], panel.GroupFor("grit"));
        Assert.Equal(rock, panel.Specs);
        Assert.Equal("boulder", panel.Current.GetString("kind"));

        var combo = panel.GetLogicalDescendantsOfPanel().OfType<ComboBox>().Single();
        combo.SelectedIndex = 1;
        Assert.Single(labels);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ARebuildThatFailsLeavesThePanelAsItWas()
    {
        var panel = new SettingsPanel(Ui.Specs());
        IReadOnlyList<ParamSpec> before = panel.Specs;
        Settings values = panel.Current;
        int children = panel.Children.Count;
        IReadOnlyList<ParamSpec> odd =
        [
            .. RockSpecs(),
            new("blob", "Blob", "A setting of a kind no panel knows.", "paint", "blob", null, null, null,
                System.Text.Json.JsonSerializer.SerializeToElement(1), "Variation", null),
        ];

        Assert.Throws<InvalidDataException>(() => panel.Rebuild(odd, Settings.Defaults(odd)));

        Assert.Equal(before, panel.Specs);
        Assert.Equal(values, panel.Current);
        Assert.Equal(children, panel.Children.Count);
        panel.ValueBoxFor("meander").Text = "3.5";
        panel.CommitValueBox("meander");
        Assert.Equal(3.5f, panel.Current.GetFloat("meander"));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AValueBoxLeftAfterARebuildCommitsNothing()
    {
        var panel = new SettingsPanel(Ui.Specs());
        var labels = new List<string>();
        panel.Committed += labels.Add;
        TextBox old = panel.ValueBoxFor("meander");
        IReadOnlyList<ParamSpec> rock = RockSpecs();
        panel.Rebuild(rock, Settings.Defaults(rock));

        // The old box is gone with its setting: committing it (as its late focus loss would) changes nothing.
        old.Text = "3.5";
        panel.CommitValueBox("meander");
        Assert.Empty(labels);
        Assert.Equal(Settings.Defaults(rock), panel.Current);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ChoiceChangeCommitsOneStep()
    {
        var panel = new SettingsPanel(RockSpecs());
        var labels = new List<string>();
        int paints = 0;
        panel.Committed += labels.Add;
        panel.PaintChanged += _ => paints++;
        var combo = panel.GetLogicalDescendantsOfPanel().OfType<ComboBox>().Single();
        Assert.Equal("Which kind of stone.", ToolTip.GetTip(combo));

        combo.SelectedIndex = 1;

        Assert.Equal("outcrop", panel.Current.GetString("kind"));
        Assert.Equal(["Kind"], labels);
        Assert.Equal(1, paints);
        Assert.Equal("Kind Outcrop", panel.DescribeChange("Kind"));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ASeedIsSetFromOutsideAsOneStep()
    {
        var panel = new SettingsPanel(RockSpecs());
        var labels = new List<string>();
        int paints = 0;
        panel.Committed += labels.Add;
        panel.PaintChanged += _ => paints++;

        Assert.Null(panel.SetFromOutside("rock_seed", System.Text.Json.JsonSerializer.SerializeToElement(4294967295L)));
        Assert.Null(panel.SetFromOutside("rock_seed", System.Text.Json.JsonSerializer.SerializeToElement(4294967295L)));

        Assert.Equal(4294967295u, panel.Current.GetSeed("rock_seed"));
        Assert.Equal(["Seed 4294967295"], labels);
        Assert.Equal(1, paints);
        foreach (object bad in new object[] { -1, 4294967296L, "abc", 1.5 })
        {
            Assert.NotNull(panel.SetFromOutside("rock_seed", System.Text.Json.JsonSerializer.SerializeToElement(bad)));
        }

        Assert.Equal(4294967295u, panel.Current.GetSeed("rock_seed"));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ChoiceAndSeedCanBeSetFromOutside()
    {
        var panel = new SettingsPanel(RockSpecs());
        Assert.Null(panel.SetFromOutside("kind", System.Text.Json.JsonSerializer.SerializeToElement("outcrop")));
        Assert.Equal("outcrop", panel.Current.GetString("kind"));
        string? bad = panel.SetFromOutside("kind", System.Text.Json.JsonSerializer.SerializeToElement("blob"));
        Assert.Contains("boulder, outcrop", bad, StringComparison.Ordinal);
        Assert.Null(panel.SetFromOutside("rock_seed", System.Text.Json.JsonSerializer.SerializeToElement(99)));
        Assert.Equal(99u, panel.Current.GetSeed("rock_seed"));
        Assert.NotNull(panel.SetFromOutside("rock_seed", System.Text.Json.JsonSerializer.SerializeToElement(-3)));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void EveryRowHasATooltip()
    {
        var panel = new SettingsPanel(RockSpecs());
        var mine = panel.GetLogicalDescendantsOfPanel();
        Assert.All(mine.Where(c => c is ComboBox or Slider or Button or TextBox), c =>
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(c) as string), $"{c.GetType().Name} has no tooltip"));
    }
}
