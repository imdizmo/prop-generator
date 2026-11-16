using System.Security.Cryptography;
using System.Text.Json;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

/// <summary>The rock at easy-props 0.4.2 in the app: the Normal / Low-poly switch and My own colours.</summary>
public class RockBuildSwitchTests
{
    // A boulder as a rock work file saved before the switch held it: no build and no stone colours.
    private const string OldBoulder = """
        {"kind":"boulder","width":3,"height":2,"depth":2.6,"chunks":4,"faces":0.6,"cuts":0.65,"edges":0.7,"bend":0.5,
         "moss":0.5,"variation":0.5,"palette":"lavender","seed":401}
        """;

    private static Settings Old() =>
        new(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(OldBoulder)!);

    internal static string Hash(ColouredMesh m)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (float[] floats in new[] { m.Positions, m.Normals, m.Colours })
        {
            sha.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(floats.AsSpan()));
        }

        sha.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(m.Indices.AsSpan()));
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static StartingPoint Boulder => RockRecipe.StartingPoints[0];

    // The hash of the rock's full build, worked out on the pool: the engine's rock never builds on the UI thread.
    private static string Built(Settings s) => EngineKit.OnThePool(() => Hash(RockRecipe.Build(s)));

    private static JsonElement Value(string value) => JsonSerializer.SerializeToElement(value);

    private static bool Shown(MainWindow w, string key) => w.Panel.RowFor(key) is { IsVisible: true };

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnOldRockWorkFileOpensAsANormalRockWithTheSameMesh()
    {
        var (w, tools, _, _, s) = Make();
        string path = s.P("old-boulder.propgen");
        var history = new History();
        history.Push("Boulder", Old());
        WorkFile.Save(path, new Piece(RockRecipe.Id, []), Old(), history);

        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));

        Assert.Equal("painted", w.Panel.Current.GetString("build"));
        Assert.Equal("lavender", w.Panel.Current.GetString("palette"));
        Assert.False(Shown(w, "stoneShadow"));
        Assert.False(Shown(w, "build"));
        Assert.Equal("painted", JsonDocument.Parse(Text(Run(tools.GetState()))).RootElement.GetProperty("build").GetString());
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void OwnColourRowsShowOnlyOnMyOwnColoursAndStartFromTheNamedPalette()
    {
        var (w, _, _, _, _) = Make();
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(Boulder)));
        Assert.False(Shown(w, "stoneShadow"));

        Assert.Null(w.Panel.SetFromOutside("palette", Value("slate")));
        Assert.Null(w.Panel.SetFromOutside("palette", Value("custom")));
        Assert.True(Shown(w, "stoneShadow") && Shown(w, "stoneHighlight"));
        Assert.Equal(EngineKit.Named(RockRecipe.Id, "stoneShadow", "slate"), w.Panel.Current.GetColour("stoneShadow"));
        Assert.Equal(EngineKit.Named(RockRecipe.Id, "stoneHighlight", "slate"), w.Panel.Current.GetColour("stoneHighlight"));

        Assert.Null(w.Panel.SetFromOutside("stoneShadow", Value("#ff0000")));
        Assert.Null(w.Panel.SetFromOutside("palette", Value("granite")));
        Assert.False(Shown(w, "stoneShadow"));
        Assert.Equal(Rgb.FromHex("#ff0000"), w.Panel.Current.GetColour("stoneShadow"));

        // Back on My own colours, they start again from the palette that was showing, as in easy-props.
        Assert.Null(w.Panel.SetFromOutside("palette", Value("custom")));
        Assert.Equal(EngineKit.Named(RockRecipe.Id, "stoneShadow", "granite"), w.Panel.Current.GetColour("stoneShadow"));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void OwnStoneColoursPaintTheRock()
    {
        var (w, _, _, _, _) = Make();
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(Boulder)));
        string before = Built(w.Panel.Current);
        Assert.Null(w.Panel.SetFromOutside("palette", Value("custom")));
        Assert.Equal(before, Built(w.Panel.Current));
        Assert.Null(w.Panel.SetFromOutside("stoneLight", Value("#ff0000")));
        Assert.NotEqual(before, Built(w.Panel.Current));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheBuildSwitchIsOneUndoStepWithKeysOneAndTwo()
    {
        var (w, _, _, _, _) = Make();
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(Boulder)));
        int steps = w.Files.History.Steps.Count;
        Assert.Equal("1", CommandListTests.Find(w, "build-normal").Keys);
        Assert.Equal("2", CommandListTests.Find(w, "build-low-poly").Keys);
        Assert.True(CommandListTests.Find(w, "build-normal").Checked());
        Assert.True(w.Toolbar.Style.IsVisible);
        Assert.True(w.Toolbar.Normal.Pressed);
        Assert.False(w.Toolbar.LowPoly.Pressed);

        CommandListTests.Run(w, "build-low-poly");
        Assert.True(w.Toolbar.LowPoly.Pressed);
        Assert.False(w.Toolbar.Normal.Pressed);

        Assert.Equal("low-poly", w.Panel.Current.GetString("build"));
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.True(CommandListTests.Find(w, "build-low-poly").Checked());
        Assert.False(Shown(w, "edges"));
        Assert.False(Shown(w, "bend"));
        Assert.True(Shown(w, "moss"));

        CommandListTests.Run(w, "undo");
        Assert.Equal("painted", w.Panel.Current.GetString("build"));
        Assert.True(Shown(w, "edges"));
        Assert.True(w.Toolbar.Normal.Pressed);

        // The toolbar's own button switches too.
        w.Toolbar.LowPoly.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Ui.Pump();
        Assert.Equal("low-poly", w.Panel.Current.GetString("build"));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheBuildSwitchIsGreyedForAKindWithoutALowPolyStyle()
    {
        var (w, _, _, _, _) = Make();
        Assert.Equal("This kind has no low-poly style", CommandListTests.Find(w, "build-low-poly").Unavailable());
        Assert.Equal("This kind has no low-poly style", CommandListTests.Find(w, "build-normal").Unavailable());
        Assert.False(w.Toolbar.Style.IsVisible);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnOldRockOpenedAfterALowPolyOneIsNormalAndItsUndoWalksBack()
    {
        var (w, _, _, dialogs, s) = Make();
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(Boulder)));
        CommandListTests.Run(w, "build-low-poly");
        dialogs.Choice = SaveChoice.Discard;

        string path = s.P("old-two-steps.propgen");
        var history = new History();
        history.Push("Boulder", Old());
        Settings wider = Old().With("width", 4.0);
        history.Push("Width 4", wider);
        WorkFile.Save(path, new Piece(RockRecipe.Id, []), wider, history);
        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));

        Assert.Equal("painted", w.Panel.Current.GetString("build"));
        Assert.Equal(4.0, w.Panel.Current.GetFloat("width"));
        CommandListTests.Run(w, "undo");
        Assert.Equal(3.0, w.Panel.Current.GetFloat("width"));
        Assert.Equal(0, w.Files.History.Index);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TwoColourRowsChangedQuicklyAreTwoUndoSteps()
    {
        var (w, _, _, _, _) = Make();
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(Boulder)));
        Assert.Null(w.Panel.SetFromOutside("palette", Value("custom")));
        int steps = w.Files.History.Steps.Count;

        // A pick in one row's square, still settling, then a hex typed into another row.
        w.Panel.ColourRowFor("stoneShadow")!.Picker.Color = Avalonia.Media.Color.Parse("#112233");
        var light = w.Panel.ColourRowFor("stoneLight")!;
        light.HexBox.Text = "#445566";
        w.Panel.CommitValueBox("stoneLight");
        Ui.Wait(600);

        Assert.Equal(["Stone shadow", "Stone light"], w.Files.History.Steps.Skip(steps).Select(step => step.Label.Split(' ')[0] + " " + step.Label.Split(' ')[1]));
        w.Close();
    }
}
