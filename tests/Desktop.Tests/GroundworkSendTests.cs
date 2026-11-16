using System.Text.Json;
using Avalonia.Input;
using ModelContextProtocol.Protocol;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Nature.Export;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.BridgeKit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

public class GroundworkSendTests
{
    private static StartingPoint Boulder => RockRecipe.StartingPoints.Single(p => p.Id == "boulder");

    private static string InboxOf(Scratch s) => s.P("groundwork", "inbox");

    private static bool Waited(Func<bool> done, int ms = 20000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    // The deliveries Groundwork would read: folders whose names do not start with '.'.
    private static string[] Deliveries(Scratch s) =>
        Directory.Exists(InboxOf(s))
            ? [.. Directory.GetDirectories(InboxOf(s)).Where(d => !Path.GetFileName(d).StartsWith('.'))]
            : [];

    private static JsonElement SendJson(string delivery) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(delivery, "send.json"))).RootElement.Clone();

    // Stands in for Groundwork: answers the first delivery that shows up with the answer made from its id.
    private static Task Groundwork(Scratch s, Func<string, string> answer) => Task.Run(async () =>
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (Deliveries(s).FirstOrDefault() is { } d)
            {
                string results = Path.Combine(InboxOf(s), ".results");
                Directory.CreateDirectory(results);
                string id = Path.GetFileName(d);
                File.WriteAllText(Path.Combine(results, id + ".json.tmp"), answer(id));
                File.Move(Path.Combine(results, id + ".json.tmp"), Path.Combine(results, id + ".json"));
                return;
            }

            await Task.Delay(20);
        }
    });

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheFloorGoesAsItsTwoTilesFlatWithSendJsonAndWaitsWithNoMapOpen()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("Oak 1.propgen") };
        MainWindow w = Open(s, dialogs);
        w.Files.GroundworkData = s.P("groundwork");
        w.Files.GroundworkAnswerTimeout = TimeSpan.FromMilliseconds(300);
        FileActionsTests.Press(w, PhysicalKey.S);

        RunCommand(w, "Send to Groundwork");
        Assert.True(Waited(() => (w.StatusLine.Text ?? string.Empty).Contains("Waiting in Groundwork's inbox", StringComparison.Ordinal)), w.StatusLine.Text);

        string delivery = Assert.Single(Deliveries(s));
        Assert.Matches("^propgen-[0-9]{8}-[0-9]{6}-[0-9a-f]{4}$", Path.GetFileName(delivery));
        Assert.Equal([Path.GetFileName(delivery)], Directory.GetFileSystemEntries(InboxOf(s)).Select(Path.GetFileName));
        Assert.Equal(
            ["pg_oak_1_floor_tile.glb", "pg_oak_1_floor_tri.glb", "send.json"],
            Directory.GetFileSystemEntries(delivery).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        JsonElement send = SendJson(delivery);
        Assert.Equal(1, send.GetProperty("version").GetInt32());
        Assert.Equal("Prop Generator", send.GetProperty("source").GetString());
        JsonElement[] kinds = [.. send.GetProperty("kinds").EnumerateArray()];
        Assert.Equal(["pg_oak_1_floor_tile", "pg_oak_1_floor_tri"], kinds.Select(k => k.GetProperty("model").GetString()));
        Assert.Equal(["Oak 1 floor tile", "Oak 1 floor triangle"], kinds.Select(k => k.GetProperty("label").GetString()));
        Assert.All(kinds, k => Assert.Equal("Buildings", k.GetProperty("category").GetString()));
        Assert.All(kinds, k => Assert.DoesNotContain('/', k.GetProperty("file").GetString()!));
        Assert.All(kinds, k => Assert.Equal(k.GetProperty("model").GetString() + ".glb", k.GetProperty("file").GetString()));

        // Each tile holds its painted picture.
        foreach (JsonElement k in kinds)
        {
            byte[] glb = File.ReadAllBytes(Path.Combine(delivery, k.GetProperty("file").GetString()!));
            Assert.Equal(1, GlbContents.Images(glb));
            Assert.Null(GlbContents.NotSelfContained(glb));
        }

        Assert.Equal("Sent Oak 1 to Groundwork: Waiting in Groundwork's inbox (open a map in Groundwork).", w.StatusLine.Text);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnUntitledPieceMustBeSavedFirstAndNothingIsSentWhenTheSaveIsCancelled()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = null };
        MainWindow w = Open(s, dialogs);
        w.Files.GroundworkData = s.P("groundwork");

        RunCommand(w, "Send to Groundwork");
        Ui.Wait(500);

        Assert.Equal(["untitled.propgen"], dialogs.SaveSuggestions);
        Assert.Empty(Deliveries(s));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ARockGoesAsItsOneModelInRocksAndTheOpenMapsAnswerIsSaid()
    {
        using var s = new Scratch();
        MainWindow w = Open(s);
        w.Files.GroundworkData = s.P("groundwork");
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Assert.Null(w.Files.SaveWithoutAsking(s.P("Mossy.propgen"), false));
        Task groundwork = Groundwork(s, _ => """{"ok":true,"map":"Isle","models":["pg_mossy_rock"]}""");

        RunCommand(w, "Send to Groundwork");
        Assert.True(Waited(() => (w.StatusLine.Text ?? string.Empty).Contains("In Groundwork map", StringComparison.Ordinal)), w.StatusLine.Text);
        Run(groundwork);

        Assert.Equal("Sent Mossy to Groundwork: In Groundwork map Isle.", w.StatusLine.Text);
        string delivery = Assert.Single(Deliveries(s));
        JsonElement kind = Assert.Single(SendJson(delivery).GetProperty("kinds").EnumerateArray());
        Assert.Equal("pg_mossy_rock", kind.GetProperty("model").GetString());
        Assert.Equal("Mossy rock", kind.GetProperty("label").GetString());
        Assert.Equal("Rocks", kind.GetProperty("category").GetString());

        // easy-props' engine writes the rock's GLB: whatever pictures it has are inside it.
        byte[] glb = File.ReadAllBytes(Path.Combine(delivery, "pg_mossy_rock.glb"));
        Assert.Null(GlbContents.NotSelfContained(glb));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ARefusalIsSaidAsAnError()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { SavePath = s.P("Pine.propgen") };
        MainWindow w = Open(s, dialogs);
        w.Files.GroundworkData = s.P("groundwork");
        FileActionsTests.Press(w, PhysicalKey.S);
        Task groundwork = Groundwork(s, _ => """{"ok":false,"map":"Isle","models":[],"error":"The map has another model named pg_pine_floor_tile: rename it before sending."}""");

        RunCommand(w, "Send to Groundwork");
        Assert.True(Waited(() => (w.StatusLine.Text ?? string.Empty).Contains("refused", StringComparison.Ordinal)), w.StatusLine.Text);
        Run(groundwork);

        Assert.Equal("Sent Pine to Groundwork: Groundwork refused it: The map has another model named pg_pine_floor_tile: rename it before sending..", w.StatusLine.Text);
        Assert.True(w.StatusBar.IsError);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ClaudesToolSendsWithoutADialogAndReturnsTheModelsAndTheAnswer()
    {
        var (w, tools, _, dialogs, s) = Make();
        w.Files.GroundworkAnswerTimeout = TimeSpan.FromMilliseconds(300);

        CallToolResult untitled = Run(tools.SendToGroundwork());
        Assert.True(untitled.IsError);
        Assert.Contains("save", Text(untitled), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Deliveries(s));

        Run(tools.Save(s.P("Dark Oak.propgen"), false));
        CallToolResult waiting = Run(tools.SendToGroundwork());
        Assert.NotEqual(true, waiting.IsError);
        Assert.Equal("Sent: pg_dark_oak_floor_tile, pg_dark_oak_floor_tri. Waiting in Groundwork's inbox (open a map in Groundwork).", Text(waiting));

        foreach (string d in Deliveries(s))
        {
            Directory.Delete(d, true);
        }

        w.Files.GroundworkAnswerTimeout = TimeSpan.FromSeconds(10);
        Task groundwork = Groundwork(s, _ => """{"ok":true,"map":"Isle","models":["pg_dark_oak_floor_tile","pg_dark_oak_floor_tri"]}""");
        CallToolResult ok = Run(tools.SendToGroundwork());
        Run(groundwork);
        Assert.Equal("Sent: pg_dark_oak_floor_tile, pg_dark_oak_floor_tri. In Groundwork map Isle.", Text(ok));
        Assert.Empty(dialogs.Told);
        Assert.Equal(0, dialogs.FolderAsked);
        Assert.Empty(dialogs.SaveSuggestions);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void YourPropsSendsSeveralInOneDeliveryAndLeavesTheFloorOut()
    {
        using var s = new Scratch();
        MainWindow w = Open(s);
        w.Files.GroundworkData = s.P("groundwork");
        w.Files.GroundworkAnswerTimeout = TimeSpan.FromMilliseconds(300);
        Assert.Null(w.Files.SaveWithoutAsking(s.P("Planks.propgen"), false));
        Assert.True(Run(w.Files.StartFromAsync(Boulder)));
        Assert.Null(w.Files.SaveWithoutAsking(s.P("Big Stone.propgen"), false));
        Assert.Null(w.Files.SaveWithoutAsking(s.P("Small Stone.propgen"), false));

        int sent = Run(w.Files.SendFilesToGroundworkAsync([s.P("Big Stone.propgen"), s.P("Planks.propgen"), s.P("Small Stone.propgen")]));

        Assert.Equal(2, sent);
        string delivery = Assert.Single(Deliveries(s));
        Assert.Equal(
            ["pg_big_stone_rock", "pg_small_stone_rock"],
            SendJson(delivery).GetProperty("kinds").EnumerateArray().Select(k => k.GetProperty("model").GetString()));
        Assert.Contains("Sent 2 models to Groundwork: Waiting in Groundwork's inbox", w.StatusLine.Text, StringComparison.Ordinal);
        Assert.Contains("Planks (a floor tile: open it to send it)", w.StatusLine.Text, StringComparison.Ordinal);
        w.Close();
    }

    // Groundwork refuses a GLB whose images or buffers have a uri other than data:. The floor's tiles and the engine's
    // kinds (an oak, a boulder) are made as Send makes them, checked, and taken by a delivery.
    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData("oak", 1)]
    [InlineData("boulder", 0)]
    public void EveryGlbSentHoldsItsPicturesAndBuffersWithNoUriOutside(string point, int leastImages)
    {
        using var s = new Scratch();
        StartingPoint start = YourPropsTests.Point(point);
        IMeshRecipe recipe = MeshRecipes.Find(start.Recipe)!;
        InboxKind kind = GroundworkSend.MeshKind(point, recipe);
        byte[] glb = EngineKit.OnThePool(() => recipe.Glb(start.Settings, kind.Model, new ExportOptions(ExportLook.Lit)));

        Assert.True(GlbContents.Images(glb) >= leastImages, $"{GlbContents.Images(glb)} pictures");
        AssertNoOutsideUri(glb);
        using InboxDelivery d = new GroundworkInbox(s.P("groundwork")).Begin();
        d.Add(kind, glb);
        Assert.True(File.Exists(Path.Combine(d.PartialFolder, kind.Model + ".glb")));
    }

    [Fact]
    public void TheFloorsTilesHoldTheirPictureWithNoUriOutside()
    {
        foreach ((InboxKind kind, byte[] glb) in GroundworkSend.Floor("Oak", new byte[64 * 64 * 4], 64))
        {
            Assert.Equal(1, GlbContents.Images(glb));
            AssertNoOutsideUri(glb);
            Assert.Equal("Buildings", kind.Category);
        }
    }

    // Read straight from the JSON chunk, apart from GlbContents: no images[].uri or buffers[].uri but data: ones.
    private static void AssertNoOutsideUri(byte[] glb)
    {
        int length = BitConverter.ToInt32(glb, 12);
        using var json = JsonDocument.Parse(glb.AsMemory(20, length));
        foreach (string list in new[] { "images", "buffers" })
        {
            if (!json.RootElement.TryGetProperty(list, out JsonElement items))
            {
                continue;
            }

            foreach (JsonElement item in items.EnumerateArray())
            {
                if (item.TryGetProperty("uri", out JsonElement uri))
                {
                    Assert.StartsWith("data:", uri.GetString(), StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void EveryKindGoesInOneOfTheInboxsGroups()
    {
        var expected = new Dictionary<string, string>
        {
            ["cut-rock"] = "Rocks",
            ["grass"] = "Plants",
            ["plant"] = "Plants",
            ["bush"] = "Bushes",
            ["cactus"] = "Plants",
            ["painted-tree"] = "Trees",
            ["branching-tree"] = "Trees",
            ["palm-tree"] = "Trees",
            ["tree-stump"] = "Props",
            ["tree-log"] = "Props",
            ["tree-branch"] = "Props",
            ["tree-litter"] = "Props",
        };
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), EngineKinds.All.Select(k => k.Id).Order(StringComparer.Ordinal));
        foreach (EngineKind kind in EngineKinds.All)
        {
            Assert.Equal(expected[kind.Id], GroundworkSend.Category(kind.ExportName));
        }

        // Our own cliff and stylized rock.
        Assert.Equal("Rocks", GroundworkSend.Category(CliffRecipe.Instance));
        Assert.Equal("Rocks", GroundworkSend.Category(StylizedRockRecipe.Instance));
        Assert.All(expected.Values, g => Assert.Contains(g, GroundworkInbox.Categories));
        Assert.Contains(GroundworkSend.FloorCategory, GroundworkInbox.Categories);
    }

    [Fact]
    public void TheTestsNeverReachTheRealGroundworkInbox()
    {
        string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Groundwork");
        Assert.Equal(Path.Combine(GroundworkGuard.DataHome, "Groundwork"), GroundworkInbox.DefaultDataDir());
        Assert.NotEqual(real, GroundworkInbox.DefaultDataDir());
    }
}
