using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using PropGenerator.Desktop.Tests.Support;
using SharpGLTF.Schema2;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.BridgeKit;

namespace PropGenerator.Desktop.Tests;

public class FileActionsTests
{
    internal static void Press(MainWindow w, PhysicalKey key, bool shift = false)
    {
        Avalonia.Input.RawInputModifiers m = Avalonia.Input.RawInputModifiers.Control;
        if (shift)
        {
            m |= Avalonia.Input.RawInputModifiers.Shift;
        }

        Avalonia.Headless.HeadlessWindowExtensions.KeyPressQwerty(w, key, m);
        Ui.Pump();
    }

    internal static void SetSeed(MainWindow w, int seed)
    {
        Assert.Null(w.Panel.SetFromOutside("seed", System.Text.Json.JsonSerializer.SerializeToElement(seed)));
        Ui.Pump();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ANewPieceIsUntitledAndSaveAsksForAName()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("oak.propgen") };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        Assert.Equal("untitled.propgen — Prop Generator", w.Title);

        Press(w, PhysicalKey.S);

        Assert.Equal(["untitled.propgen"], dialogs.SaveSuggestions);
        Assert.True(File.Exists(s.P("oak.propgen")));
        Assert.Equal("oak.propgen — Prop Generator", w.Title);

        // Saving again with a name goes straight to the file, with no prompt.
        SetSeed(w, 5);
        Assert.StartsWith("• ", w.Title);
        Press(w, PhysicalKey.S);
        Assert.Single(dialogs.SaveSuggestions);
        Assert.Equal("oak.propgen — Prop Generator", w.Title);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void CancellingTheNamePromptSavesNothing()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = null };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        SetSeed(w, 3);

        Press(w, PhysicalKey.S);

        Assert.Empty(Directory.GetFiles(s.Dir, "*.propgen"));
        Assert.StartsWith("• ", w.Title);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SavingOntoAnExistingFileAsksAndNoLeavesItUnchanged()
    {
        using var s = new Scratch();
        File.WriteAllText(s.P("old.propgen"), "keep me");
        var dialogs = new FakeDialogs { SavePath = s.P("old.propgen"), Yes = false };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        SetSeed(w, 3);

        Press(w, PhysicalKey.S);

        Assert.Equal(["Replace old.propgen?"], dialogs.Questions);
        Assert.Equal("keep me", File.ReadAllText(s.P("old.propgen")));
        Assert.StartsWith("• ", w.Title);

        dialogs.Yes = true;
        Press(w, PhysicalKey.S);
        Assert.NotEqual("keep me", File.ReadAllText(s.P("old.propgen")));
        Assert.Equal("old.propgen — Prop Generator", w.Title);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ClosingWithUnsavedChangesAsks()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { Choice = SaveChoice.Cancel };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        bool closed = false;
        w.Closed += (_, _) => closed = true;

        // Nothing changed: it just closes (a second window checks that below).
        SetSeed(w, 9);
        w.Close();
        Ui.Pump();
        Assert.Equal(1, dialogs.SaveChangesAsked);
        Assert.False(closed);

        dialogs.Choice = SaveChoice.Discard;
        w.Close();
        Ui.Pump();
        Assert.Equal(2, dialogs.SaveChangesAsked);
        Assert.True(closed);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ClosingAnUntouchedPieceDoesNotAsk()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs();
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        bool closed = false;
        w.Closed += (_, _) => closed = true;

        w.Close();
        Ui.Pump();

        Assert.Equal(0, dialogs.SaveChangesAsked);
        Assert.True(closed);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ClosingWithSaveChoiceSavesThenCloses()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { Choice = SaveChoice.Save, SavePath = s.P("a.propgen") };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        bool closed = false;
        w.Closed += (_, _) => closed = true;
        SetSeed(w, 2);

        w.Close();
        Ui.Pump();

        Assert.True(File.Exists(s.P("a.propgen")));
        Assert.True(closed);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void UndoAndRedoWalkTheHistoryAndRepaintWithoutPushingSteps()
    {
        using var s = new Scratch();
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter, new FakeDialogs(), s);
        Settings start = w.Panel.Current;
        SetSeed(w, 4);
        SetSeed(w, 8);
        Assert.Equal(["Opened", "Seed 4", "Seed 8"], w.Files.History.Steps.Select(x => x.Label));
        int before = painter.Requests.Count;

        Press(w, PhysicalKey.Z);
        Assert.Equal(4, w.Panel.Current.GetInt("seed"));
        Assert.Equal(3, w.Files.History.Steps.Count);
        Assert.Equal(1, w.Files.History.Index);
        Assert.Equal(before + 1, painter.Requests.Count);
        Assert.Equal((1024, 4), (painter.Requests[^1].Size, painter.Requests[^1].Settings.GetInt("seed")));
        Assert.Equal(4, w.Panel.Current.GetInt("seed"));

        Press(w, PhysicalKey.Z);
        Assert.Equal(start, w.Panel.Current);
        Assert.False(w.Files.Dirty);

        Press(w, PhysicalKey.Z, shift: true);
        Assert.Equal(4, w.Panel.Current.GetInt("seed"));
        Ui.Wait(100);
        Assert.Equal(3, w.Files.History.Steps.Count);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportWithABrokenCatalogShowsTheErrorAndWritesNothing()
    {
        using var s = new Scratch();
        string map = s.P("map");
        Directory.CreateDirectory(map);
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        File.WriteAllText(Path.Combine(map, "catalog.json"), "{ this is not json");
        var dialogs = new FakeDialogs { SavePath = s.P("oak.propgen"), Folder = map };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        Press(w, PhysicalKey.S);
        string[] before = Directory.GetFileSystemEntries(map, "*", SearchOption.AllDirectories);
        string catalog = File.ReadAllText(Path.Combine(map, "catalog.json"));

        _ = w.Files.ExportAsync();
        Ui.Wait(300);

        Assert.Single(dialogs.Told);
        Assert.Contains("catalog", dialogs.Told[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, Directory.GetFileSystemEntries(map, "*", SearchOption.AllDirectories));
        Assert.Equal(catalog, File.ReadAllText(Path.Combine(map, "catalog.json")));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportWritesBothGlbsAndTheCatalogAndRemembersTheMapFolder()
    {
        using var s = new Scratch();
        string map = s.P("map");
        Directory.CreateDirectory(Path.Combine(map, "models"));
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        var dialogs = new FakeDialogs { SavePath = s.P("Dark Oak-2.propgen"), Folder = map };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        Press(w, PhysicalKey.S);

        _ = w.Files.ExportAsync();
        Ui.Wait(1500);

        Assert.Empty(dialogs.Told);
        Assert.True(File.Exists(Path.Combine(map, "models", "pg_dark_oak_2_floor_tile.glb")));
        Assert.True(File.Exists(Path.Combine(map, "models", "pg_dark_oak_2_floor_tri.glb")));
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(map, "catalog.json")));
        var kinds = doc.RootElement.GetProperty("kinds").EnumerateArray().ToList();
        Assert.Equal(2, kinds.Count);
        Assert.Equal("Dark Oak-2 floor tile", kinds[0].GetProperty("label").GetString());
        Assert.Equal("Dark Oak-2 floor triangle", kinds[1].GetProperty("label").GetString());
        Assert.Equal("Construction", kinds[0].GetProperty("category").GetString());
        Assert.Equal(map, new Prefs(s.P("prefs.json")).MapFolder);

        // The second export does not ask for the folder again.
        int asked = dialogs.FolderAsked;
        _ = w.Files.ExportAsync();
        Ui.Wait(1500);
        Assert.Equal(asked, dialogs.FolderAsked);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportOfAnUntitledPieceAsksForANameFirstAndStopsIfCancelled()
    {
        using var s = new Scratch();
        string map = s.P("map");
        Directory.CreateDirectory(map);
        var dialogs = new FakeDialogs { SavePath = null, Folder = map };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);

        _ = w.Files.ExportAsync();
        Ui.Wait(200);

        Assert.Equal(["untitled.propgen"], dialogs.SaveSuggestions);
        Assert.Equal(0, dialogs.FolderAsked);
        Assert.Empty(Directory.GetFileSystemEntries(map));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportWaitsForTheFullDetailPaintOfTheCurrentSettings()
    {
        using var s = new Scratch();
        string map = s.P("map");
        Directory.CreateDirectory(Path.Combine(map, "models"));
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        var gate = new TaskCompletionSource<Layers?>();
        var painter = new FakePainter();
        var dialogs = new FakeDialogs { SavePath = s.P("oak.propgen"), Folder = map };
        MainWindow w = Ui.Open(painter, dialogs, s);
        Press(w, PhysicalKey.S);
        painter.Reply = (_, _) => gate.Task;
        SetSeed(w, 6);

        _ = w.Files.ExportAsync();
        Ui.Wait(300);
        Assert.Equal("Waiting for full detail…", w.StatusLine.Text);
        Assert.False(File.Exists(Path.Combine(map, "catalog.json")));

        gate.SetResult(FakePainter.Blank(1024));
        Ui.Wait(1500);
        Assert.True(File.Exists(Path.Combine(map, "catalog.json")));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void CtrlZInAValueBoxIsTheBoxsOwn()
    {
        using var s = new Scratch();
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        w.Panel.ValueBoxFor("meander").Text = "3.5";
        w.Panel.CommitValueBox("meander");
        Ui.Pump();
        Assert.Equal(2, w.Files.History.Steps.Count);
        TextBox box = w.Panel.ValueBoxFor("meander");
        w.Panel.GroupFor("meander")!.IsOpen = true;
        Ui.Pump();
        box.Focus();
        Ui.Pump();
        Assert.True(box.IsFocused);

        // As in easy-props, no shortcut takes a key from a box being typed in: the piece is not undone.
        Press(w, PhysicalKey.Z);

        Assert.Equal(1, w.Files.History.Index);
        Assert.Equal(3.5f, w.Panel.Current.GetFloat("meander"));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void WhileAQuestionIsOpenOtherCommandsAndASecondCloseAreIgnored()
    {
        using var s = new Scratch();
        var hold = new TaskCompletionSource();
        var dialogs = new FakeDialogs { Choice = SaveChoice.Cancel, Hold = hold.Task, SavePath = s.P("x.propgen") };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        bool closed = false;
        w.Closed += (_, _) => closed = true;
        SetSeed(w, 3);

        Press(w, PhysicalKey.N);
        Press(w, PhysicalKey.N);
        Press(w, PhysicalKey.O);
        Press(w, PhysicalKey.S);
        w.Close();
        w.Close();
        Ui.Pump();

        Assert.Equal(1, dialogs.SaveChangesAsked);
        Assert.Empty(dialogs.SaveSuggestions);
        Assert.False(closed);
        hold.SetResult();
        Ui.Pump();

        // Once answered, commands work again.
        Press(w, PhysicalKey.S);
        Assert.Single(dialogs.SaveSuggestions);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnExportThatBreaksUnexpectedlyEndsOnTheStatusLineAndLeavesNoFaultedTask()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("oak.propgen"), FolderThrows = true };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        Press(w, PhysicalKey.S);

        Task export = w.Files.ExportAsync();
        Ui.Wait(300);

        Assert.True(export.IsCompletedSuccessfully);
        Assert.Contains("folder dialog broke", w.StatusLine.Text);
        Assert.StartsWith("Export failed", w.StatusLine.Text);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ALibraryThatCannotBeWrittenDoesNotFailASaveOrAnOpen()
    {
        using var s = new Scratch();
        File.WriteAllText(s.P("notafolder"), "x");
        var dialogs = new FakeDialogs { SavePath = s.P("oak.propgen"), Choice = SaveChoice.Discard };
        var painter = new FakePainter();
        var w = new MainWindow(
            painter, Ui.Specs(), dialogs, new Prefs(s.P("prefs.json")), new Library(s.P("notafolder", "library.json")), s.P("bridge"),
            Ui.Thumbs());
        Ui.Track(w);
        w.Show();
        Ui.Pump();
        SetSeed(w, 3);

        Press(w, PhysicalKey.S);
        Assert.True(File.Exists(s.P("oak.propgen")));
        Assert.Equal("oak.propgen — Prop Generator", w.Title);

        Press(w, PhysicalKey.N);
        Task<bool> open = w.Files.OpenPathAsync(s.P("oak.propgen"));
        Ui.Pump();
        Assert.True(open.IsCompleted);
        Assert.True(open.Result);
        Assert.Equal(3, w.Panel.Current.GetInt("seed"));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportRefusesAModelNameAnotherLabelAlreadyUses()
    {
        using var s = new Scratch();
        string map = s.P("map");
        Directory.CreateDirectory(Path.Combine(map, "models"));
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        string catalog = "{\"kinds\":[{\"model\":\"pg_oak_1_floor_tile\",\"label\":\"Oak-1 floor tile\",\"category\":\"Construction\",\"scale\":[1,1]}]}";
        File.WriteAllText(Path.Combine(map, "catalog.json"), catalog);
        var dialogs = new FakeDialogs { SavePath = s.P("Oak 1.propgen"), Folder = map };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        Press(w, PhysicalKey.S);

        _ = w.Files.ExportAsync();
        Ui.Wait(500);

        Assert.Contains("rename", w.StatusLine.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Oak-1 floor tile", w.StatusLine.Text);
        Assert.Equal(catalog, File.ReadAllText(Path.Combine(map, "catalog.json")));
        Assert.Empty(Directory.GetFiles(Path.Combine(map, "models")));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportRefusesAFileNameThatLeavesNothingToNameTheModelBy()
    {
        using var s = new Scratch();
        string map = s.P("map");
        Directory.CreateDirectory(Path.Combine(map, "models"));
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        var dialogs = new FakeDialogs { SavePath = s.P("日本.propgen"), Folder = map };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        Press(w, PhysicalKey.S);

        _ = w.Files.ExportAsync();
        Ui.Wait(500);

        Assert.Contains("rename", w.StatusLine.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(Path.Combine(map, "models")));
        Assert.False(File.Exists(Path.Combine(map, "catalog.json")));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SendToBlenderWritesBothTilesToTheOutboxNamedAfterTheWorkFile()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("Oak 1.propgen") };
        MainWindow w = Open(s, dialogs);
        FileActionsTests.Press(w, PhysicalKey.S);

        RunCommand(w, "Send to Blender");
        Ui.Wait(1500);

        string tile = Path.Combine(Outbox(s), "pg_oak_1_floor_tile.glb");
        string tri = Path.Combine(Outbox(s), "pg_oak_1_floor_tri.glb");
        Assert.True(File.Exists(tile), w.StatusLine.Text);
        Assert.True(File.Exists(tri));
        Assert.Equal(2, Directory.GetFiles(Outbox(s)).Length);
        Assert.Equal(TileMesh.Square().Indices.Length, ModelRoot.Load(tile).LogicalMeshes.Single().Primitives.Single().GetIndexAccessor().Count);
        Assert.Equal(TileMesh.Triangle().Indices.Length, ModelRoot.Load(tri).LogicalMeshes.Single().Primitives.Single().GetIndexAccessor().Count);
        Assert.Contains("Blender", w.StatusLine.Text);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnUntitledPieceIsSentAsUntitled()
    {
        using var s = new Scratch();
        MainWindow w = Open(s);

        RunCommand(w, "Send to Blender");
        Ui.Wait(1500);

        Assert.True(File.Exists(Path.Combine(Outbox(s), "pg_untitled_floor_tile.glb")), w.StatusLine.Text);
        Assert.True(File.Exists(Path.Combine(Outbox(s), "pg_untitled_floor_tri.glb")));
        w.Close();
    }

    // A full-detail paint that failed is not on its way any more: sending (or exporting) asks for a new one at once
    // instead of waiting out the timeout for a paint nobody asked for.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SendingAfterAFailedFullPaintAsksForANewOneInsteadOfWaitingOutTheTimeout()
    {
        using var s = new Scratch();
        var painter = new FakePainter();
        MainWindow w = Ui.Open(painter, new FakeDialogs(), s, s.P("bridge"));
        int fail = 1;
        painter.Reply = (size, _) => size == 1024 && fail-- > 0 ? Task.FromResult<Layers?>(null) : null!;
        Assert.Null(w.Panel.SetFromOutside("seed", System.Text.Json.JsonSerializer.SerializeToElement(7)));
        Ui.Pump();
        Assert.False(w.Repaint.FullIsCurrent);
        int asked = painter.Requests.Count;
        w.Files.FullPaintTimeout = TimeSpan.FromSeconds(3);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var sending = w.Files.SendToBlenderAsync(Outbox(s));
        while (!sending.IsCompleted && clock.ElapsedMilliseconds < 10000)
        {
            Ui.Wait(20);
        }

        Assert.True(sending.IsCompleted);
        Assert.Null(sending.Result.Error);
        Assert.NotNull(sending.Result.Files);
        Assert.True(clock.ElapsedMilliseconds < 2000, $"it took {clock.ElapsedMilliseconds} ms");
        var again = Assert.Single(painter.Requests.Skip(asked));
        Assert.Equal((1024, 7), (again.Size, again.Settings.GetInt("seed")));
        w.Close();
    }

    // A work file edited by hand: a value of the wrong type is refused when the file is opened, with a message, and
    // nothing of it is applied (before, the panel took the settings half way and then threw).
    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData("settings", "contrast", "\"high\"")]
    [InlineData("settings", "seed", "2.5")]
    [InlineData("settings", "broken_edge", "1")]
    [InlineData("settings", "colour", "[0.1, 0.2]")]
    [InlineData("step", "colour", "\"#ff0000\"")]
    public void AWorkFileWithAValueOfTheWrongTypeIsRefusedAndNothingOfItApplied(string where, string key, string json)
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("oak.propgen") };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s, s.P("bridge"));
        Press(w, PhysicalKey.S);
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(s.P("oak.propgen")))!;
        var target = where == "settings" ? root["settings"]! : root["history"]!["steps"]![0]!["settings"]!;
        target[key] = System.Text.Json.Nodes.JsonNode.Parse(json);
        target["meander"] = 3.5;
        File.WriteAllText(s.P("bad.propgen"), root.ToJsonString());
        Settings before = w.Panel.Current;
        int steps = w.Files.History.Steps.Count;
        string title = w.Title!;

        Task<bool> open = w.Files.OpenPathAsync(s.P("bad.propgen"));
        Ui.Wait(100);

        Assert.True(open.IsCompletedSuccessfully);
        Assert.False(open.Result);
        Assert.Contains(key, Assert.Single(dialogs.Told), StringComparison.Ordinal);
        Assert.Equal(before, w.Panel.Current);
        Assert.Equal(steps, w.Files.History.Steps.Count);
        Assert.Equal(title, w.Title);
        w.Close();
    }

    // As Claude's save does: a work file is always a .propgen, whatever name was typed, so Save as never writes over
    // another kind of file.
    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData("Oak v1.2", "Oak v1.2.propgen")]
    [InlineData("notes.txt", "notes.txt.propgen")]
    [InlineData("oak", "oak.propgen")]
    [InlineData("oak.PROPGEN", "oak.PROPGEN")]
    public void SaveAsAlwaysWritesADotPropgenFile(string typed, string written)
    {
        using var s = new Scratch();
        string work = s.P("work");
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "notes.txt"), "my notes");
        var dialogs = new FakeDialogs { SavePath = Path.Combine(work, typed), Yes = true };
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s, s.P("bridge"));

        Task<bool> saved = w.Files.SaveAsAsync();
        Ui.Wait(100);

        Assert.True(saved.IsCompletedSuccessfully && saved.Result);
        Assert.Equal(
            new[] { "notes.txt", written }.Order(StringComparer.Ordinal),
            Directory.GetFiles(work).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal));
        Assert.Equal("my notes", File.ReadAllText(Path.Combine(work, "notes.txt")));
        w.Close();
    }
}
