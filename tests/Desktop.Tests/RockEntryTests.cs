using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using PropGenerator.Desktop.Pictures;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.BridgeKit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

public class RockEntryTests
{
    private static StartingPoint Boulder => RockRecipe.StartingPoints[0];

    // A tiny triangle whose size tells which settings it was built from.
    private static ColouredMesh Tagged(Settings s)
    {
        float k = s.GetFloat("width");
        return new ColouredMesh([0, 0, 0, k, 0, 0, 0, k, 1], [0, 0, 1, 0, 0, 1, 0, 0, 1], [1, 1, 1, 1, 1, 1, 1, 1, 1], [0, 1, 2]);
    }

    private static bool Waited(Func<bool> done, int ms = 10000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    // A width the boulder does not have, on the slider's step.
    private static double OtherWidth(MainWindow w)
    {
        ParamSpec spec = w.Panel.Specs.Single(x => x.Key == "width");
        double now = w.Panel.Current.GetFloat("width");
        double v = Math.Round(((spec.Min!.Value + spec.Max!.Value) / 2) / spec.Step!.Value) * spec.Step.Value;
        return Math.Abs(v - now) < 1e-6 ? v + spec.Step.Value : v;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void StartingARockShowsItAndRebuildsThePanel()
    {
        var (w, _, painter, dialogs, _) = Make();
        Ui.Wait(200);
        int painted = painter.Requests.Count;

        Assert.True(Run(w.Files.StartFromAsync(Boulder)));

        Assert.Equal(RockRecipe.Id, w.Files.Recipe);
        // Thirty rows, six of them hidden until they do something: the Style, the own stone colours and, for a
        // boulder, the overhang (easy-props 0.4.5).
        Assert.Equal(30, w.Panel.Rows.Count);
        Assert.Equal(24, w.Panel.Rows.Count(r => r.IsVisible));
        Assert.Equal(["Shape", "Surface", "Weather", "Damage"], w.Panel.Groups.Select(g => g.Title));
        Assert.True(w.View.ShowingRock);
        Assert.Equal("untitled.propgen — Prop Generator", w.Title);
        Assert.False(w.Files.Dirty);
        Assert.Equal([Boulder.Name], w.Files.History.Steps.Select(x => x.Label));
        Assert.Equal(Boulder.Settings, w.Panel.Current);
        Ui.Pump();
        var touchable = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w.Panel).OfType<Control>()
            .Where(c => c.TemplatedParent is null && c is Slider or NumericUpDown or ComboBox or TextBox or Button)
            .ToList();
        Assert.True(touchable.Count >= 12);
        Assert.All(touchable, c => Assert.True(ToolTip.GetTip(c) is string { Length: >= 40 }, c.GetType().Name));

        // The floor's painter is not asked for anything while a rock is the entry, whatever changes in the panel.
        Assert.Null(w.Panel.SetFromOutside("width", J(OtherWidth(w).ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Ui.Wait(900);
        Assert.Equal(painted, painter.Requests.Count);
        Assert.True(w.Files.Dirty);

        // New brings the floor back, with its rows and a fresh paint.
        dialogs.Choice = SaveChoice.Discard;
        Assert.True(Run(w.Files.NewAsync()));
        Assert.Equal(FloorRecipe.Id, w.Files.Recipe);
        Assert.Equal(w.FloorSpecs, w.Panel.Specs);
        Assert.False(w.View.ShowingRock);
        Assert.Equal(["Floor tile (v5)"], w.Files.History.Steps.Select(x => x.Label));
        Ui.Wait(300);
        Assert.True(painter.Requests.Count > painted);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DraggingARockSliderNeverShowsAnOlderBuild()
    {
        var (w, _, painter, _, _) = Make();
        var built = new List<Settings>();
        var shown = new List<Settings>();
        int calls = 0;
        Settings? slow = null;
        w.Rocks.Builder = s =>
        {
            int n = Interlocked.Increment(ref calls);
            if (n == 2)
            {
                slow = s;
            }

            // The first build of the drag is the slowest: it finishes after the newer ones were asked for.
            Thread.Sleep(n == 2 ? 400 : 15);
            lock (built)
            {
                built.Add(s);
            }

            return Tagged(s);
        };
        w.Rocks.Showed += (_, s) => shown.Add(s);
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Assert.Equal([Boulder.Settings], shown);
        Ui.Wait(100);
        int painted = painter.Requests.Count;
        Settings defaults = w.Panel.Current;

        Ui.Drag(w, Ui.Named<Slider>(w, "Width"), 8);
        Assert.True(Waited(() => shown.Count > 1 && shown[^1].Equals(w.Panel.Current)));
        Ui.Wait(600);

        Assert.NotEqual(defaults, w.Panel.Current);
        Assert.Equal(w.Panel.Current, shown[^1]);
        Assert.Equal(w.Panel.Current.GetFloat("width"), w.Rocks.Mesh!.Positions[3]);
        Assert.DoesNotContain(defaults, shown.Skip(1));

        // The slow first build of the drag finished after newer ones were asked for: shown (it is newer than the
        // preset's), and then the newest after it, never the other way round.
        Assert.NotNull(slow);
        Assert.NotEqual(w.Panel.Current, slow);
        Assert.True(shown.FindIndex(x => x.Equals(slow)) is var at && (at < 0 || at < shown.Count - 1));

        // Every rock shown after the first is newer than the one before it.
        List<int> order = [.. shown.Skip(1).Select(s => built.FindLastIndex(b => b.Equals(s)))];
        Assert.Equal(order.Order(), order);
        Assert.Equal(painted, painter.Requests.Count);
        Assert.Equal(2, w.Files.History.Steps.Count);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DraggingARockSliderShowsItWhileTheHandMoves()
    {
        var (w, _, painter, _, _) = Make();
        var built = new List<Settings>();
        var shown = new List<Settings>();
        w.Rocks.Builder = s =>
        {
            // Slower than the pause between two moves of the mouse.
            Thread.Sleep(40);
            lock (built)
            {
                built.Add(s);
            }

            return Tagged(s);
        };
        w.Rocks.Showed += (_, s) => shown.Add(s);
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Settings preset = w.Panel.Current;
        int painted = painter.Requests.Count;
        int shownBeforeLettingGo = 0;

        Ui.Drag(w, Ui.Named<Slider>(w, "Width"), 14, () =>
        {
            Thread.Sleep(16);
            Ui.Pump();
            shownBeforeLettingGo = shown.Count(s => !s.Equals(preset));
        });
        Assert.True(Waited(() => shown[^1].Equals(w.Panel.Current)));
        Ui.Wait(300);

        // The rock moved with the hand, before the button was let go.
        Assert.True(shownBeforeLettingGo >= 2, $"{shownBeforeLettingGo} rocks shown during the drag");
        Assert.DoesNotContain(preset, shown.Skip(1));
        List<int> order = [.. shown.Skip(1).Select(s => built.FindLastIndex(b => b.Equals(s)))];
        Assert.Equal(order.Order(), order);
        Assert.Equal(order.Distinct(), order);
        Assert.Equal(w.Panel.Current, shown[^1]);
        Assert.Equal(w.Panel.Current.GetFloat("width"), w.Rocks.Mesh!.Positions[3]);
        Assert.Equal(painted, painter.Requests.Count);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnUndoIsNotOverwrittenByAnOlderSliderBuild()
    {
        var (w, _, _, _, _) = Make();
        var shown = new List<Settings>();
        var gate = new ManualResetEventSlim(true);
        w.Rocks.Builder = s =>
        {
            gate.Wait(5000);
            return Tagged(s);
        };
        w.Rocks.Showed += (_, s) => shown.Add(s);
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Settings preset = w.Panel.Current;

        // A slider build is held back; meanwhile the change is undone.
        gate.Reset();
        Assert.Null(w.Panel.SetFromOutside("width", J(OtherWidth(w).ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Ui.Wait(50);
        FileActionsTests.Press(w, PhysicalKey.Z);
        Assert.Equal(preset, w.Panel.Current);
        gate.Set();
        Assert.True(Waited(() => shown.Count >= 2));
        Ui.Wait(300);

        Assert.Equal([preset, preset], shown);
        Assert.Equal(preset, w.Rocks.ShownSettings);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RockUndoRedoAndSaveReopen()
    {
        var (w, _, _, dialogs, s) = Make();
        dialogs.SavePath = s.P("Big stone.propgen");
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        float start = w.Panel.Current.GetFloat("width");
        double other = OtherWidth(w);
        Assert.Null(w.Panel.SetFromOutside("width", J(other.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Equal(2, w.Files.History.Steps.Count);
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));

        FileActionsTests.Press(w, PhysicalKey.Z);
        Assert.Equal(start, w.Panel.Current.GetFloat("width"));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        FileActionsTests.Press(w, PhysicalKey.Z, shift: true);
        Assert.Equal((float)other, w.Panel.Current.GetFloat("width"), 4);
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));

        FileActionsTests.Press(w, PhysicalKey.S);
        Assert.True(File.Exists(s.P("Big stone.propgen")));
        Assert.False(w.Files.Dirty);
        string[] labels = [.. w.Files.History.Steps.Select(x => x.Label)];

        Assert.True(Run(w.Files.NewAsync()));
        Assert.Equal(FloorRecipe.Id, w.Files.Recipe);

        Assert.True(Run(w.Files.OpenPathAsync(s.P("Big stone.propgen"))));
        Assert.Equal(RockRecipe.Id, w.Files.Recipe);
        Assert.Equal((float)other, w.Panel.Current.GetFloat("width"), 4);
        Assert.Equal(labels, w.Files.History.Steps.Select(x => x.Label));
        Assert.Equal(1, w.Files.History.Index);
        Assert.True(w.View.ShowingRock);
        Assert.Equal("Big stone.propgen — Prop Generator", w.Title);
        Assert.False(w.Files.Dirty);

        // Undo after reopening walks the saved history.
        FileActionsTests.Press(w, PhysicalKey.Z);
        Assert.Equal(start, w.Panel.Current.GetFloat("width"));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void OpeningAnUnknownRecipeSaysSoAndKeepsTheEntry()
    {
        var (w, _, _, dialogs, s) = Make();
        var h = new History();
        h.Push("Opened", new Settings(new Dictionary<string, JsonElement>()));
        WorkFile.Save(s.P("hedge.propgen"), new Piece("sundial", []), new Settings(new Dictionary<string, JsonElement>()), h);
        Settings before = w.Panel.Current;

        Assert.False(Run(w.Files.OpenPathAsync(s.P("hedge.propgen"))));

        Assert.Equal(["hedge.propgen is an Unknown kind (sundial): this version of the app cannot open it (a newer version made it, or the kind is gone)."], dialogs.Told);
        Assert.Equal(FloorRecipe.Id, w.Files.Recipe);
        Assert.Equal(before, w.Panel.Current);
        Assert.Equal(w.FloorSpecs, w.Panel.Specs);
        Assert.Equal("untitled.propgen — Prop Generator", w.Title);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void StartingFromAPresetAsksAboutUnsavedChanges()
    {
        var (w, _, _, dialogs, _) = Make();
        FileActionsTests.SetSeed(w, 7);
        Assert.True(w.Files.Dirty);
        Settings before = w.Panel.Current;
        dialogs.Choice = SaveChoice.Cancel;

        Assert.False(Run(w.Files.StartFromAsync(Boulder)));

        Assert.Equal(1, dialogs.SaveChangesAsked);
        Assert.Equal(FloorRecipe.Id, w.Files.Recipe);
        Assert.Equal(before, w.Panel.Current);
        Assert.Equal(w.FloorSpecs, w.Panel.Specs);
        Assert.False(w.View.ShowingRock);
        Assert.True(w.Files.Dirty);

        // Discarding goes on.
        dialogs.Choice = SaveChoice.Discard;
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Assert.Equal(RockRecipe.Id, w.Files.Recipe);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RockExportAddsOneRockToTheMapCatalog()
    {
        var (w, _, _, dialogs, s) = Make();
        string map = s.P("map");
        Directory.CreateDirectory(Path.Combine(map, "models"));
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        dialogs.SavePath = s.P("Big Stone.propgen");
        dialogs.Folder = map;
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        FileActionsTests.Press(w, PhysicalKey.S);

        _ = w.Files.ExportAsync();
        string glb = Path.Combine(map, "models", "pg_big_stone_rock.glb");
        Assert.True(Waited(() => File.Exists(Path.Combine(map, "catalog.json"))), w.StatusLine.Text);

        Assert.Empty(dialogs.Told);
        Assert.True(File.Exists(glb));
        Assert.Equal("glTF"u8.ToArray(), File.ReadAllBytes(glb)[..4]);
        Assert.Single(Directory.GetFiles(Path.Combine(map, "models")));
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(map, "catalog.json")));
        JsonElement kind = doc.RootElement.GetProperty("kinds").EnumerateArray().Single();
        Assert.Equal("pg_big_stone_rock", kind.GetProperty("model").GetString());
        Assert.Equal("Big Stone rock", kind.GetProperty("label").GetString());
        Assert.Equal("Rocks", kind.GetProperty("category").GetString());
        Assert.Contains("rock", w.StatusLine.Text, StringComparison.Ordinal);
        Assert.Contains("Big Stone", w.StatusLine.Text, StringComparison.Ordinal);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void RockSendToBlenderWritesItsGlb()
    {
        using var s = new Scratch();
        MainWindow w = Open(s);
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));

        RunCommand(w, "Send to Blender");
        string glb = Path.Combine(Outbox(s), "pg_untitled_rock.glb");
        Assert.True(Waited(() => File.Exists(glb)), w.StatusLine.Text);
        Ui.Wait(100);

        Assert.Single(Directory.GetFiles(Outbox(s)));
        Assert.Equal("glTF"u8.ToArray(), File.ReadAllBytes(glb)[..4]);
        Assert.Contains("rock", w.StatusLine.Text, StringComparison.Ordinal);
        Assert.Contains("Blender", w.StatusLine.Text, StringComparison.Ordinal);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SavingStoresAPictureOfTheView()
    {
        var (w, _, _, dialogs, s) = Make();
        dialogs.SavePath = s.P("pebble.propgen");

        // A wide picture: the middle square is red, the sides blue; the stored picture is the middle, 128 px square.
        const int W = 300, H = 200;
        byte[] rgba = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int i = ((y * W) + x) * 4;
                bool middle = x is >= 50 and < 250;
                rgba[i] = (byte)(middle ? 255 : 0);
                rgba[i + 2] = (byte)(middle ? 0 : 255);
                rgba[i + 3] = 255;
            }
        }

        w.Files.Capture = () => Task.FromResult((rgba, W, H));
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        FileActionsTests.Press(w, PhysicalKey.S);

        string key = ThumbCache.FileKey(Path.GetFullPath(s.P("pebble.propgen")));
        Assert.True(Waited(() => w.Thumbs.Get(key) is not null));
        byte[] png = w.Thumbs.Get(key)!;
        Assert.Equal(128, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(128, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        (byte[] square, int size) = ViewThumb.Square(rgba, W, H, 128);
        Assert.Equal(128, size);
        Assert.All(Enumerable.Range(0, size * size), p => Assert.Equal((255, 0), (square[p * 4], square[(p * 4) + 2])));

        // A view that cannot be pictured does not stop a save.
        w.Files.Capture = () => throw new InvalidOperationException("no view");
        Assert.Null(w.Panel.SetFromOutside("seed", J("5")));

        // The rock is built first, so its "Rock built" line does not land after the save's.
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        FileActionsTests.Press(w, PhysicalKey.S);
        Assert.False(w.Files.Dirty);
        Assert.Equal("Saved pebble.propgen.", w.StatusLine.Text);
    }

    [Fact]
    public void ThumbKeysAreSharedWithTheDrawer()
    {
        Assert.Equal("file:/a/b.propgen", ThumbCache.FileKey("/a/b.propgen"));
        // The rock is easy-props' engine's: its painter-drawn pictures carry the bundle's tag and commit (D6).
        IMeshRecipe rock = MeshRecipes.Find(RockRecipe.Id)!;
        string tag = ShippedThumbs.EngineTag(RockRecipe.Id)!;
        Assert.Equal($"preset:boulder:cut-rock:v{rock.Version}:{tag}", ThumbCache.PresetKey("boulder", RockRecipe.Id, rock.Version, tag));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void BackToThePieceShowsTheRockAgainAfterAMeshFromBlender()
    {
        using var s = new Scratch();
        MainWindow w = Open(s);
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));

        DropMesh(s, "Crate", 0);
        Assert.True(Waited(() => w.View.ShowingPiece));
        Assert.False(w.View.ShowingRock);
        RunCommand(w, "Back to the piece");
        Ui.Wait(200);
        Assert.False(w.View.ShowingPiece);
        Assert.True(w.View.ShowingRock);
        w.Close();
    }

    private static void Focus(TextBox box)
    {
        box.Focus();
        Ui.Pump();
        Assert.True(box.IsFocused);
    }

    private static int Rows(MainWindow w) => w.Panel.Rows.Count;

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AFocusedBoxDoesNotBreakASwitchOfRecipe()
    {
        var (w, _, _, dialogs, _) = Make();
        dialogs.Choice = SaveChoice.Discard;
        StartingPoint floor = FloorRecipe.StartingPoint(w.FloorSpecs);

        // A floor value box, then the colour's hex box (under the colour square), each focused with something typed and
        // not yet confirmed: the switch to a rock removes the box, which loses the focus on the way out. What was typed
        // is dropped.
        foreach ((string key, string typed) in new[] { ("carve_strength", "1.2"), ("colour", "#112233") })
        {
            if (key == "colour")
            {
                w.Panel.ColourRow!.OpenPicker();
                Ui.Pump();
            }

            TextBox box = w.Panel.ValueBoxFor(key);
            Focus(box);
            box.Text = typed;

            Assert.True(Run(w.Files.StartFromAsync(Boulder)));

            Assert.Equal(RockRecipe.Id, w.Files.Recipe);
            Assert.Equal(RockRecipe.Specs.Count - 1, Rows(w));
            Assert.Equal(RockRecipe.Specs, w.Panel.Specs);
            Assert.Equal(Boulder.Settings, w.Panel.Current);
            Assert.False(w.Files.Dirty);

            // The rock's width box focused, a width typed, and New floor tile (from the menu: Ctrl+N is the box's while it
            // is typed in): the floor comes back whole, with its own settings.
            TextBox width = w.Panel.ValueBoxFor("width");
            Focus(width);
            width.Text = "12";
            CommandListTests.Run(w, "new-floor");
            Ui.Wait(50);

            Assert.Equal(FloorRecipe.Id, w.Files.Recipe);
            Assert.Equal(w.FloorSpecs.Count - 1, Rows(w));
            Assert.Equal(w.FloorSpecs, w.Panel.Specs);
            Assert.Equal(floor.Settings, w.Panel.Current);
            Assert.False(w.Files.Dirty);
        }

        // The rebuilt rows still work.
        w.Panel.ValueBoxFor("meander").Text = "3.5";
        w.Panel.CommitValueBox("meander");
        Assert.Equal(3.5f, w.Panel.Current.GetFloat("meander"));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AFloorPaintThatArrivesWhileARockIsTheEntryIsNeitherShownNorAnnounced()
    {
        var gate = new TaskCompletionSource<Layers?>();
        var painter = new FakePainter { Reply = (_, _) => gate.Task };
        MainWindow w = Ui.Open(painter, new FakeDialogs(), Ui.NewScratch());
        int shown = 0;
        w.Repaint.Shown += (_, _) => shown++;

        // The floor's first paint is still on its way when a rock is started.
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Assert.StartsWith("Rock built in ", w.StatusLine.Text, StringComparison.Ordinal);

        gate.SetResult(FakePainter.Blank(LiveRepaint.FullSize));
        Ui.Wait(300);

        Assert.Equal(0, shown);
        Assert.Null(w.Repaint.FullLayers);
        Assert.StartsWith("Rock built in ", w.StatusLine.Text, StringComparison.Ordinal);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ARockBuiltAfterAFailedOneClearsTheFailureFromTheStatusLine()
    {
        var (w, _, _, _, _) = Make();
        int calls = 0;
        w.Rocks.Builder = s => Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("no stone today") : Tagged(s);

        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Assert.Contains("no stone today", w.StatusLine.Text, StringComparison.Ordinal);

        Assert.Null(w.Panel.SetFromOutside("width", J(OtherWidth(w).ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        Assert.StartsWith("Rock built in ", w.StatusLine.Text, StringComparison.Ordinal);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void StartingOrOpeningIsRefusedWhileAnExportOrASendWaitsForTheFloor()
    {
        var (w, _, painter, dialogs, s) = Make();
        string map = s.P("map");
        Directory.CreateDirectory(Path.Combine(map, "models"));
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        dialogs.SavePath = s.P("oak.propgen");
        dialogs.Folder = map;
        dialogs.Choice = SaveChoice.Discard;
        FileActionsTests.Press(w, PhysicalKey.S);
        Assert.True(File.Exists(s.P("oak.propgen")));

        // A floor export waits for its full-detail paint; meanwhile a start (as from the drawer) and an open are refused,
        // without asking about the unsaved seed.
        var gate = new TaskCompletionSource<Layers?>();
        painter.Reply = (_, _) => gate.Task;
        FileActionsTests.SetSeed(w, 6);
        Settings exported = w.Panel.Current;
        Task export = w.Files.ExportAsync();
        Ui.Wait(200);
        Assert.Equal("Waiting for full detail…", w.StatusLine.Text);

        Assert.False(Run(w.Files.StartFromAsync(Boulder)));
        Assert.Contains("export", w.StatusLine.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(Run(w.Files.OpenPathAsync(s.P("oak.propgen"))));
        Assert.Contains("export", w.StatusLine.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FloorRecipe.Id, w.Files.Recipe);
        Assert.Equal(exported, w.Panel.Current);
        Assert.Equal(0, dialogs.SaveChangesAsked);

        gate.SetResult(FakePainter.Blank(LiveRepaint.FullSize));
        Run(export);
        Assert.True(File.Exists(Path.Combine(map, "models", "pg_oak_floor_tile.glb")), w.StatusLine.Text);
        Assert.StartsWith("Exported oak", w.StatusLine.Text, StringComparison.Ordinal);

        // The same for a send to Blender.
        var gate2 = new TaskCompletionSource<Layers?>();
        painter.Reply = (_, _) => gate2.Task;
        FileActionsTests.SetSeed(w, 7);
        Task<(string[]? Files, string? Error)> send = w.Files.SendToBlenderAsync(Outbox(s));
        Ui.Wait(200);
        Assert.False(Run(w.Files.StartFromAsync(Boulder)));
        Assert.Contains("Blender", w.StatusLine.Text, StringComparison.Ordinal);
        Assert.Equal(FloorRecipe.Id, w.Files.Recipe);

        gate2.SetResult(FakePainter.Blank(LiveRepaint.FullSize));
        (string[]? sent, string? error) = Run(send);
        Assert.Null(error);
        Assert.Equal(["pg_oak_floor_tile.glb", "pg_oak_floor_tri.glb"], sent!.Select(Path.GetFileName));

        // Once done, starting works again.
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Assert.Equal(RockRecipe.Id, w.Files.Recipe);
        w.Close();
    }
}
