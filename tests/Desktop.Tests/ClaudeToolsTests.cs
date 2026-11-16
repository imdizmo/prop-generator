using System.Text.Json;
using Avalonia.Controls;
using ModelContextProtocol.Protocol;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Tests.Support;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

public class ClaudeToolsTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SetControlMovesTheSliderAndAddsExactlyOneHistoryStep()
    {
        var (w, tools, painter, _, _) = Make();
        int steps = w.Files.History.Steps.Count;

        CallToolResult r = Run(tools.SetControl("gap_scale", J("0.3")));

        Assert.NotEqual(true, r.IsError);
        Assert.Equal(0.3, Ui.Named<Slider>(w, "Plank gap width").Value, 3);
        Assert.Equal(0.3f, w.Panel.Current.GetFloat("gap_scale"), 3);
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Contains("gap", w.Files.History.Steps[^1].Label, StringComparison.OrdinalIgnoreCase);
        Assert.True(w.Files.Dirty);

        // A paint key paints at full detail, as the user's own change does.
        Ui.Wait(900);
        Assert.Contains(painter.Requests, q => q.Size == LiveRepaint.FullSize && Math.Abs(q.Settings.GetFloat("gap_scale") - 0.3f) < 1e-4);

        // The same value again is no change and no step.
        Run(tools.SetControl("gap_scale", J("0.3")));
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);

        // Undo takes it back.
        Run(tools.Undo());
        Assert.Equal(0.45f, w.Panel.Current.GetFloat("gap_scale"), 3);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ToneKeysChangeOnlyTheColouringAndPaintNothing()
    {
        var (w, tools, painter, _, _) = Make();
        Ui.Wait(900);
        int painted = painter.Requests.Count;
        int steps = w.Files.History.Steps.Count;

        CallToolResult r = Run(tools.SetControl("contrast", J("0.9")));
        Ui.Wait(900);

        Assert.NotEqual(true, r.IsError);
        Assert.Equal(painted, painter.Requests.Count);
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal(0.9f, w.Panel.Current.GetFloat("contrast"), 3);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SetControlTakesWholeNumbersTogglesAndColoursInBothWrittenForms()
    {
        var (w, tools, _, _, _) = Make();

        Assert.NotEqual(true, Run(tools.SetControl("creases", J("7"))).IsError);
        Assert.Equal(7, w.Panel.Current.GetInt("creases"));
        Assert.NotEqual(true, Run(tools.SetControl("broken_edge", J("false"))).IsError);
        Assert.False(w.Panel.Current.GetBool("broken_edge"));

        int steps = w.Files.History.Steps.Count;
        Assert.NotEqual(true, Run(tools.SetControl("colour", J("\"#336699\""))).IsError);
        Assert.Equal(Rgb.FromHex("#336699"), w.Panel.Current.GetColour("colour"));
        Assert.Equal("#336699", w.Panel.ValueBoxFor("colour").Text);
        Assert.NotEqual(true, Run(tools.SetControl("colour", J("[0.5, 0.25, 0.125]"))).IsError);
        Assert.Equal(new Rgb(0.5f, 0.25f, 0.125f), w.Panel.Current.GetColour("colour"));
        Assert.Equal(steps + 2, w.Files.History.Steps.Count);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnUnknownKeyIsAnErrorNamingTheValidKeys()
    {
        var (w, tools, _, _, _) = Make();
        int steps = w.Files.History.Steps.Count;

        CallToolResult r = Run(tools.SetControl("gap_sacle", J("0.3")));

        Assert.True(r.IsError);
        string text = Text(r);
        Assert.Contains("gap_sacle", text, StringComparison.Ordinal);
        foreach (ParamSpec s in Ui.Specs())
        {
            Assert.Contains(s.Key, text, StringComparison.Ordinal);
        }

        Assert.Equal(steps, w.Files.History.Steps.Count);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ValuesOutsideTheRangeOrOfTheWrongKindAreErrorsThatSayWhatIsValid()
    {
        var (w, tools, _, _, _) = Make();
        int steps = w.Files.History.Steps.Count;

        CallToolResult high = Run(tools.SetControl("gap_scale", J("9")));
        Assert.True(high.IsError);
        Assert.Contains("0.1", Text(high), StringComparison.Ordinal);
        Assert.Contains("1.5", Text(high), StringComparison.Ordinal);

        Assert.True(Run(tools.SetControl("gap_scale", J("\"wide\""))).IsError);
        Assert.True(Run(tools.SetControl("creases", J("2.5"))).IsError);
        Assert.True(Run(tools.SetControl("creases", J("11"))).IsError);
        Assert.True(Run(tools.SetControl("broken_edge", J("3"))).IsError);
        Assert.True(Run(tools.SetControl("colour", J("\"blue\""))).IsError);
        Assert.True(Run(tools.SetControl("colour", J("[0.1, 0.2]"))).IsError);
        Assert.True(Run(tools.SetControl("colour", J("[0.1, 0.2, 7]"))).IsError);
        Assert.Equal(steps, w.Files.History.Steps.Count);
        Assert.Equal(0.45f, w.Panel.Current.GetFloat("gap_scale"), 3);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ListControlsGivesKeyLabelTooltipValueAndRange()
    {
        var (_, tools, _, _, _) = Make();
        Run(tools.SetControl("gap_scale", J("0.3")));

        string listed = Text(Run(tools.ListControls()));
        Assert.StartsWith("[", listed, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(listed);
        List<JsonElement> all = [.. doc.RootElement.EnumerateArray()];
        Assert.Equal(Ui.Specs().Count, all.Count);
        JsonElement gap = all.Single(c => c.GetProperty("key").GetString() == "gap_scale");
        Assert.Equal("Plank gap width", gap.GetProperty("label").GetString());
        Assert.False(string.IsNullOrWhiteSpace(gap.GetProperty("tooltip").GetString()));
        Assert.Equal(0.3, gap.GetProperty("value").GetDouble(), 3);
        Assert.Equal(0.1, gap.GetProperty("min").GetDouble(), 3);
        Assert.Equal(1.5, gap.GetProperty("max").GetDouble(), 3);
        JsonElement colour = all.Single(c => c.GetProperty("key").GetString() == "colour");
        Assert.Equal(3, colour.GetProperty("value").GetArrayLength());
        Assert.StartsWith("#", colour.GetProperty("hex").GetString(), StringComparison.Ordinal);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SaveNeedsAPathForAnUntitledPieceNeverReplacesAFileUnlessToldAndAsksNothing()
    {
        var (w, tools, _, dialogs, s) = Make();
        Run(tools.SetControl("gap_scale", J("0.3")));

        CallToolResult none = Run(tools.Save(null, false));
        Assert.True(none.IsError);
        Assert.Contains("path", Text(none), StringComparison.OrdinalIgnoreCase);

        string file = s.P("oak.propgen");
        Assert.NotEqual(true, Run(tools.Save(file, false)).IsError);
        Assert.True(File.Exists(file));
        Assert.False(w.Files.Dirty);
        Assert.Equal("oak.propgen", w.Files.Name);

        // Saving to the current file with no path.
        Run(tools.SetControl("gap_scale", J("0.5")));
        Assert.NotEqual(true, Run(tools.Save(null, false)).IsError);
        Assert.False(w.Files.Dirty);

        // Another existing file is not replaced without overwrite.
        string other = s.P("other.propgen");
        Run(tools.Save(other, false));
        Run(tools.SetControl("gap_scale", J("0.6")));
        Run(tools.Save(file, true));
        string kept = File.ReadAllText(other);
        CallToolResult refused = Run(tools.Save(other, false));
        Assert.True(refused.IsError);
        Assert.Contains("overwrite", Text(refused), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(kept, File.ReadAllText(other));
        Assert.NotEqual(true, Run(tools.Save(other, true)).IsError);
        Assert.NotEqual(kept, File.ReadAllText(other));

        Assert.True(Run(tools.Save("relative.propgen", false)).IsError);
        Assert.True(Run(tools.Save(s.P("nodir", "x.propgen"), false)).IsError);

        Assert.Empty(dialogs.Told);
        Assert.Empty(dialogs.Questions);
        Assert.Equal(0, dialogs.SaveChangesAsked);
        Assert.Equal(0, dialogs.FolderAsked);
        Assert.Empty(dialogs.SaveSuggestions);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportUsesTheGivenOrRememberedFolderAndNeverOpensACard()
    {
        var (w, tools, _, dialogs, s) = Make();
        string map = s.P("map");
        Directory.CreateDirectory(Path.Combine(map, "models"));
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");

        // Untitled: a name is needed first.
        CallToolResult untitled = Run(tools.Export(map));
        Assert.True(untitled.IsError);
        Assert.Contains("save", Text(untitled), StringComparison.OrdinalIgnoreCase);

        Run(tools.Save(s.P("Dark Oak.propgen"), false));

        // No folder given and none remembered.
        CallToolResult nofolder = Run(tools.Export(null));
        Assert.True(nofolder.IsError);
        Assert.Contains("mapFolder", Text(nofolder), StringComparison.Ordinal);

        CallToolResult ok = Run(tools.Export(map));
        Assert.NotEqual(true, ok.IsError);
        Assert.Contains("pg_dark_oak_floor_tile", Text(ok), StringComparison.Ordinal);
        Assert.Contains("pg_dark_oak_floor_tri", Text(ok), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(map, "models", "pg_dark_oak_floor_tile.glb")));
        Assert.Equal(map, new Prefs(s.P("prefs.json")).MapFolder);

        // Remembered now.
        Assert.NotEqual(true, Run(tools.Export(null)).IsError);

        Assert.True(Run(tools.Export(s.P("missing"))).IsError);
        Assert.Empty(dialogs.Told);
        Assert.Equal(0, dialogs.FolderAsked);
        Assert.Empty(dialogs.Questions);
        _ = w;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ScreenshotWithoutAnOpenGlViewIsAClearError()
    {
        var (_, tools, _, _, _) = Make();

        CallToolResult r = Run(tools.Screenshot());

        Assert.True(r.IsError);
        Assert.Contains("3D view", Text(r), StringComparison.Ordinal);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ScreenshotReturnsAPngImageBlock()
    {
        var (_, tools, _, _, _) = Make();
        byte[] rgba = new byte[4 * 3 * 4];
        for (int i = 0; i < rgba.Length; i++)
        {
            rgba[i] = (byte)(i * 9);
        }

        tools.Capture = () => Task.FromResult((rgba, 4, 3));

        CallToolResult r = Run(tools.Screenshot());

        Assert.NotEqual(true, r.IsError);
        ImageContentBlock img = Assert.IsType<ImageContentBlock>(Assert.Single(r.Content));
        Assert.Equal("image/png", img.MimeType);
        byte[] png = img.DecodedData.ToArray();
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        Assert.Equal(4, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(3, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportThatTimesOutWaitingForFullDetailSaysSoOnTheStatusLine()
    {
        var (w, tools, painter, _, s) = Make();
        string map = s.P("map");
        Directory.CreateDirectory(Path.Combine(map, "models"));
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        Run(tools.Save(s.P("oak.propgen"), false));
        w.Files.FullPaintTimeout = TimeSpan.FromMilliseconds(300);
        painter.Reply = (_, _) => new TaskCompletionSource<Layers?>().Task;
        Run(tools.SetControl("gap_scale", J("0.3")));

        CallToolResult r = Run(tools.Export(map));

        Assert.True(r.IsError);
        Assert.Contains("nothing was exported", Text(r), StringComparison.Ordinal);
        Assert.NotEqual("Waiting for full detail…", w.StatusLine.Text);
        Assert.Contains("nothing was exported", w.StatusLine.Text, StringComparison.Ordinal);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SaveOnlyWritesDotPropgenFilesAndOnlyReplacesRealWorkFiles()
    {
        var (_, tools, _, _, s) = Make();
        string rc = s.P(".bashrc");
        File.WriteAllText(rc, "export A=1");

        CallToolResult a = Run(tools.Save(rc, true));
        Assert.True(a.IsError);
        Assert.Contains(".propgen", Text(a), StringComparison.Ordinal);
        Assert.Equal("export A=1", File.ReadAllText(rc));
        Assert.True(Run(tools.Save(s.P("notes.txt"), false)).IsError);
        Assert.False(File.Exists(s.P("notes.txt")));

        // A .propgen name on something that is not a work file is not replaced either.
        string fake = s.P("fake.propgen");
        File.WriteAllText(fake, "not a work file");
        CallToolResult b = Run(tools.Save(fake, true));
        Assert.True(b.IsError);
        Assert.Equal("not a work file", File.ReadAllText(fake));

        // The extension is matched without regard to case; a real work file can be replaced.
        string upper = s.P("Big.PROPGEN");
        Assert.NotEqual(true, Run(tools.Save(upper, false)).IsError);
        Assert.NotEqual(true, Run(tools.Save(upper, true)).IsError);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportNeedsAnAbsoluteMapFolderAndRemembersItNormalised()
    {
        var (_, tools, _, _, s) = Make();
        string map = s.P("map");
        Directory.CreateDirectory(Path.Combine(map, "models"));
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        Run(tools.Save(s.P("oak.propgen"), false));

        Assert.True(Run(tools.Export("map")).IsError);
        Assert.NotEqual(true, Run(tools.Export(Path.Combine(map, "models", ".."))).IsError);
        Assert.Equal(map, new Prefs(s.P("prefs.json")).MapFolder);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ClaudeSettingAnotherControlDuringADragLeavesTheDragAlone()
    {
        var (w, tools, _, _, _) = Make();
        w.Panel.GroupFor("gap_scale")!.IsOpen = true;
        Ui.Pump();
        Slider gap = Ui.Named<Slider>(w, "Plank gap width");
        int start = w.Files.History.Steps.Count;
        int during = -1;

        Ui.Drag(w, gap, 4, () =>
        {
            if (during < 0)
            {
                Run(tools.SetControl("contrast", J("0.9")));
                during = w.Files.History.Steps.Count;
            }
        });

        // The drag is still one step (the contrast step came first), not a step per move.
        Assert.Equal(start + 1, during);
        Assert.Equal(start + 2, w.Files.History.Steps.Count);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AColourPickedJustBeforeClaudeSetsAnotherIsNotLost()
    {
        var (w, tools, _, _, _) = Make();
        int steps = w.Files.History.Steps.Count;

        w.Panel.ColourRow!.Picker.Color = Avalonia.Media.Color.FromRgb(0x20, 0x40, 0x60);
        Ui.Pump();
        Run(tools.SetControl("colour", J("\"#AA5500\"")));

        Assert.Equal(steps + 2, w.Files.History.Steps.Count);
        Assert.Equal("Wood colour #204060", w.Files.History.Steps[steps].Label);
        Ui.Wait(700);
        Assert.Equal(steps + 2, w.Files.History.Steps.Count);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void StartFromPresetStartsARock()
    {
        var (w, tools, _, dialogs, _) = Make();
        string id = Recipes.RockRecipe.StartingPoints[0].Id;

        CallToolResult r = Run(tools.StartFromPreset(id));

        Assert.NotEqual(true, r.IsError);
        Assert.Contains(Recipes.RockRecipe.StartingPoints[0].Name, Text(r), StringComparison.Ordinal);
        Assert.Equal("cut-rock", w.Files.Recipe);
        Assert.True(w.View.ShowingRock);
        Assert.Equal(0, dialogs.SaveChangesAsked);

        // A rock's choice takes an option's value, as listed.
        string listed = Text(Run(tools.ListControls()));
        Assert.Contains("\"key\":\"palette\"", listed, StringComparison.Ordinal);
        string other = Recipes.RockRecipe.Specs.Single(s => s.Key == "kind").Options!.Select(o => o.Value).First(v => v != w.Panel.Current.GetString("kind"));
        CallToolResult set = Run(tools.SetControl("kind", J($"\"{other}\"")));
        Assert.NotEqual(true, set.IsError);
        Assert.Equal(other, w.Panel.Current.GetString("kind"));

        // An unknown id names the valid ones.
        CallToolResult bad = Run(tools.StartFromPreset("no-such-rock"));
        Assert.True(bad.IsError);
        Assert.Contains("floor-v5", Text(bad), StringComparison.Ordinal);
        Assert.Contains(id, Text(bad), StringComparison.Ordinal);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void StartFromPresetRefusesUnsavedWork()
    {
        var (w, tools, _, dialogs, _) = Make();
        Run(tools.SetControl("gap_scale", J("0.3")));
        Settings before = w.Panel.Current;

        CallToolResult r = Run(tools.StartFromPreset(Recipes.RockRecipe.StartingPoints[0].Id));

        Assert.True(r.IsError);
        Assert.Equal("The piece has unsaved changes; save it first.", Text(r));
        Assert.Equal(0, dialogs.SaveChangesAsked);
        Assert.Equal("floor_tile", w.Files.Recipe);
        Assert.Equal(before, w.Panel.Current);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void GetStateNamesTheRecipe()
    {
        var (_, tools, _, _, _) = Make();
        Assert.Equal("floor_tile", JsonElement.Parse(Text(Run(tools.GetState()))).GetProperty("recipe").GetString());

        Run(tools.StartFromPreset(Recipes.RockRecipe.StartingPoints[0].Id));

        Assert.Equal("cut-rock", JsonElement.Parse(Text(Run(tools.GetState()))).GetProperty("recipe").GetString());
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RunCommandRunsAndRefusesGreyed()
    {
        var (w, tools, _, _, _) = Make();

        CallToolResult ok = Run(tools.RunCommand("stats"));
        Assert.NotEqual(true, ok.IsError);
        Assert.Contains("It is on now", Text(ok), StringComparison.Ordinal);
        Assert.True(w.ViewState.ShowStats);

        // Flat is for rocks: on the floor it is greyed, and refused with the reason.
        CallToolResult flat = Run(tools.RunCommand("shading-flat"));
        Assert.True(flat.IsError);
        Assert.Contains("shading is for rocks", Text(flat), StringComparison.Ordinal);
        Assert.Equal("lit", w.ViewState.Shading);

        int steps = w.Files.History.Steps.Count;
        Assert.NotEqual(true, Run(tools.RunCommand("surprise")).IsError);
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);

        // Greyed: refused with the reason the menu shows.
        CallToolResult grey = Run(tools.RunCommand("copy-part"));
        Assert.True(grey.IsError);
        Assert.Contains("This kind of piece has no parts to pick", Text(grey), StringComparison.Ordinal);

        // Unknown: the valid ids are listed.
        CallToolResult bad = Run(tools.RunCommand("nope"));
        Assert.True(bad.IsError);
        Assert.Contains("shading-flat", Text(bad), StringComparison.Ordinal);
        Assert.Contains("surprise", Text(bad), StringComparison.Ordinal);

        // A command that needs the user is not run.
        Assert.True(Run(tools.RunCommand("settings")).IsError);

        // A list of choices is no command: its choices' ids are listed.
        foreach ((string parent, string child) in new[] { ("shading", "shading-lit"), ("lod", "lod-0"), ("camera-angle", "camera-top"), ("scene", "scene-grey") })
        {
            CallToolResult list = Run(tools.RunCommand(parent));
            Assert.True(list.IsError);
            Assert.Contains("list of choices", Text(list), StringComparison.Ordinal);
            Assert.Contains(child, Text(list), StringComparison.Ordinal);
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RunCommandReportsAFailureTheCommandSaid()
    {
        var (w, tools, _, _, s) = Make();

        // The outbox is a file, so Send to Blender cannot write into it: it says so on the status bar, and so does the tool.
        Directory.CreateDirectory(Path.GetDirectoryName(BridgeKit.Outbox(s))!);
        File.WriteAllText(BridgeKit.Outbox(s), "in the way");
        CallToolResult failed = Run(tools.RunCommand("send-blender"));

        Assert.True(failed.IsError);
        Assert.True(w.StatusBar.IsError);
        Assert.StartsWith("Send to Blender did not work: ", Text(failed), StringComparison.Ordinal);
        Assert.Contains(w.StatusLine.Text ?? "?", Text(failed), StringComparison.Ordinal);
        Assert.DoesNotContain("Ran ", Text(failed), StringComparison.Ordinal);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void GetStateNamesTheViewState()
    {
        var (w, tools, _, _, _) = Make();

        // The view state keeps the rock's shading while the floor is shown.
        w.ViewState.Shading = ViewState.Wireframe;
        Run(tools.RunCommand("scene-grey"));
        Run(tools.RunCommand("maximize"));

        var s = JsonElement.Parse(Text(Run(tools.GetState())));

        Assert.Equal("wireframe", s.GetProperty("shading").GetString());
        Assert.Equal(0, s.GetProperty("lod").GetInt32());
        Assert.Equal("grey", s.GetProperty("scene").GetString());
        Assert.Equal(w.ViewState.Budget, s.GetProperty("budget").GetString());
        Assert.True(s.GetProperty("maximized").GetBoolean());
        JsonElement faces = s.GetProperty("faces");
        Assert.True(faces.GetProperty("full").GetInt32() > 0);
        Assert.True(faces.GetProperty("allowed").GetInt32() > 0);
        Assert.Equal(faces.GetProperty("full").GetInt32() > faces.GetProperty("allowed").GetInt32(), faces.GetProperty("over").GetBoolean());
    }
}

/// <summary>Claude's list of controls follows the rows as the panel shows them.</summary>
[Collection(MeshRecipesAlone.Name)]
public class ClaudeToolsShownRowsTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ListControlsReportsShownRows()
    {
        using IDisposable _ = PropGenerator.Desktop.Recipes.MeshRecipes.Override(PanelShownRowsTests.Relabelled());
        MainWindow w = PanelShownRowsTests.Rock();
        var tools = new ClaudeTools(w);
        PanelShownRowsTests.Set(w, "height", 4);

        using var doc = JsonDocument.Parse(Text(Run(tools.ListControls())));
        JsonElement width = doc.RootElement.EnumerateArray().Single(c => c.GetProperty("key").GetString() == "width");
        Assert.Equal("Breadth", width.GetProperty("label").GetString());
        Assert.Equal("Outline", width.GetProperty("group").GetString());
        JsonElement depth = doc.RootElement.EnumerateArray().Single(c => c.GetProperty("key").GetString() == "depth");
        Assert.Equal(("Depth", "Shape"), (depth.GetProperty("label").GetString(), depth.GetProperty("group").GetString()));

        // Setting it names the shown label.
        Assert.StartsWith("Breadth (width) is now", Text(Run(tools.SetControl("width", J("3.5")))), StringComparison.Ordinal);
        w.Close();
    }
}
