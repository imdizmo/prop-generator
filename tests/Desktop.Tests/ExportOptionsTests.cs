using System.Buffers.Binary;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PropGenerator.Desktop.Batch;
using PropGenerator.Desktop.Cards;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Props;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// The export's options from easy-props 0.4.5 (decision E6): the picture size of the closest level (512, 1024 or
/// 2048, remembered, read by every export path), a piece's cut ends as their own mesh, and a wet part's shine, all
/// from upstream's export package.
/// </summary>
public class ExportOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // How many pixels across a PNG is (its IHDR's width).
    private static int Width(byte[] png) => (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16));

    private static List<JsonElement> Items(GlbFile glb, string name) =>
        glb.Json.RootElement.TryGetProperty(name, out JsonElement list) ? [.. list.EnumerateArray()] : [];

    // The widths of every image in the file.
    private static int[] ImageWidths(byte[] bytes)
    {
        using var glb = GlbFile.Parse(bytes);
        return [.. Items(glb, "images").Select(i => Width(glb.ImageBytes(i)))];
    }

    // The width of the picture a mesh's material prints, by the mesh's name.
    private static int MapWidthOf(GlbFile glb, string meshName)
    {
        JsonElement mesh = Items(glb, "meshes").Single(m => m.GetProperty("name").GetString() == meshName);
        int material = mesh.GetProperty("primitives")[0].GetProperty("material").GetInt32();
        int texture = Items(glb, "materials")[material].GetProperty("pbrMetallicRoughness").GetProperty("baseColorTexture").GetProperty("index").GetInt32();
        int image = Items(glb, "textures")[texture].GetProperty("source").GetInt32();
        return Width(glb.ImageBytes(Items(glb, "images")[image]));
    }

    // Every material's roughness (glTF's default, 1, when it has none).
    private static double[] Roughness(byte[] bytes)
    {
        using var glb = GlbFile.Parse(bytes);
        return [.. Items(glb, "materials").Select(m =>
            m.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) && pbr.TryGetProperty("roughnessFactor", out JsonElement r) ? r.GetDouble() : 1.0)];
    }

    private static Task<byte[]> GlbAsync(IMeshRecipe recipe, Settings s, string model, ExportOptions options) =>
        Task.Run(() => recipe.Glb(s, model, options, Ct), Ct);

    private static string Map(Scratch s, MainWindow w)
    {
        string map = s.P("map");
        Directory.CreateDirectory(map);
        File.WriteAllText(Path.Combine(map, "map.json"), "{}");
        w.Files.Prefs.MapFolder = map;
        return map;
    }

    // A log made from the oak, as the Pieces card makes it, as a prop of its kind.
    private static Settings OakLog()
    {
        string scene = EngineKit.Recipe("painted-tree").SceneJson(YourPropsTests.Point("oak").Settings);
        EnginePiece log = EngineKit.OnThePool(() => EngineHost.Default.Builds.MakePieceAsync(scene, "oak", "log", "low-poly", Ct).GetAwaiter().GetResult());
        return EngineKit.Recipe("tree-log").SettingsOf(log.Scene);
    }

    [Fact]
    public async Task PictureSizeReachesTheExport()
    {
        EngineRecipe rock = EngineKit.Recipe("cut-rock");
        Settings boulder = YourPropsTests.Point("boulder").Settings;
        int[] usual = ImageWidths(await GlbAsync(rock, boulder, "rock", new ExportOptions(ExportLook.Lit)));
        int[] large = ImageWidths(await GlbAsync(rock, boulder, "rock", new ExportOptions(ExportLook.Lit, 2048)));
        Assert.NotEmpty(usual);
        Assert.Equal(512, usual.Max());

        // The stone's picture and its relief are drawn again at 2048, not stretched.
        Assert.Equal(2048, large.Max());
        Assert.Equal(usual.Length, large.Length);
    }

    [Fact]
    public void AnUnknownSizeReads512()
    {
        using var s = new Scratch();
        foreach ((string json, int want) in ((string, int)[])[
            ("{\"exportPictureSize\": 1024}", 1024),
            ("{\"exportPictureSize\": 2048}", 2048),
            ("{\"exportPictureSize\": 777}", 512),
            ("{\"exportPictureSize\": 4096}", 512),
            ("{\"exportPictureSize\": \"2048\"}", 512),
            ("{\"exportPictureSize\": 1024.5}", 512),
            ("{}", 512)])
        {
            string path = s.P("prefs.json");
            File.WriteAllText(path, json);
            Assert.True(want == new Prefs(path).ExportPictureSize, json);
        }

        // Chosen, it is remembered.
        var prefs = new Prefs(s.P("chosen.json")) { ExportPictureSize = 2048 };
        prefs.Save();
        Assert.Equal(2048, new Prefs(s.P("chosen.json")).ExportPictureSize);
        Assert.Equal(512, ExportOptions.KeptPictureSize(300));
        Assert.Equal(1024, ExportOptions.KeptPictureSize(1024));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ExportFromYourPropsUsesThePictureSize()
    {
        Scratch s = Ui.NewScratch();
        var dialogs = new FakeDialogs();
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        w.Library.Add(YourPropsTests.SaveProp(s, "A", YourPropsTests.Point("boulder")));
        dialogs.Folder = Map(s, w);
        w.Files.Prefs.ExportLook = ExportLook.Cel;
        w.Files.Prefs.ExportPictureSize = 2048;
        var seen = new List<ExportOptions>();
        w.Files.WriteMesh = (_, name, _, _, options) =>
        {
            lock (seen)
            {
                seen.Add(options);
            }

            return "pg_" + name;
        };

        CommandListTests.Run(w, "my-props");
        PropsPage page = Assert.IsType<PropsPage>(w.Cards.Shown);
        page.Tick("A");
        ClaudeKit.Run(page.RunAsync("Export…"));
        Ui.Pump();
        Assert.Equal([new ExportOptions(ExportLook.Cel, 2048)], seen);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheOpenPropAndExportManyUseThePictureSize()
    {
        Scratch s = Ui.NewScratch();
        MainWindow w = Ui.Open(new FakePainter(), new FakeDialogs(), s);
        string path = YourPropsTests.SaveProp(s, "Boulder", YourPropsTests.Point("boulder"));
        Assert.True(CommandListTests.RunAsync(w.Files.OpenPathAsync(path)));
        string map = Map(s, w);
        w.Files.Prefs.ExportPictureSize = 1024;
        var seen = new List<ExportOptions>();
        w.Files.WriteMesh = (_, name, _, _, options) =>
        {
            lock (seen)
            {
                seen.Add(options);
            }

            return "pg_" + name;
        };

        (string[]? models, string? error) = ClaudeKit.Run(w.Files.ExportWithoutAskingAsync(map), 120000);
        Assert.Null(error);
        Assert.NotNull(models);
        IReadOnlyList<BatchVersion> items = ClaudeKit.Run(w.Versions.ItemsAsync(null, 2, Ct), 60000);
        Assert.Equal(2, ClaudeKit.Run(w.Files.ExportManyAsync(items), 120000));
        Assert.Equal(3, seen.Count);
        Assert.All(seen, o => Assert.Equal(new ExportOptions(ExportLook.Lit, 1024), o));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ThePictureSizeIsChosenInSettingsAndInExportMany()
    {
        var (w, _, _, _, s) = ClaudeKit.Make();
        var prefs = new Prefs(s.P("size-prefs.json"));
        var window = new SettingsWindow(w.Claude, prefs);
        window.Show();
        Ui.Pump();

        ComboBox size = window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Picture size");
        Assert.Equal(["512", "1024", "2048"], size.Items.Cast<ComboBoxItem>().Select(i => i.Content as string));
        Assert.Equal(ExportOptions.PictureSizeHelp, ToolTip.GetTip(size) as string);
        Assert.Equal(0, size.SelectedIndex);
        size.SelectedIndex = 2;
        Ui.Pump();
        Assert.Equal(2048, prefs.ExportPictureSize);
        Assert.Equal(2048, new Prefs(s.P("size-prefs.json")).ExportPictureSize);
        window.Close();

        // The Export many card offers the same choice for an engine kind, and remembers it.
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(YourPropsTests.Point("boulder"))));
        CommandListTests.Run(w, "export-many");
        ExportManyCard card = Assert.IsType<ExportManyCard>(w.Cards.Shown);
        ComboBox many = Assert.IsType<ComboBox>(card.PictureSize);
        Assert.True(many.IsVisible);
        Assert.Equal(ExportOptions.PictureSizeHelp, ToolTip.GetTip(many) as string);
        Assert.Equal(ExportOptions.PictureSizes.ToList().IndexOf(w.Files.Prefs.ExportPictureSize), many.SelectedIndex);
        many.SelectedIndex = 1;
        Ui.Pump();
        Assert.Equal(1024, w.Files.Prefs.ExportPictureSize);
        Assert.Equal(1024, new Prefs(w.Files.Prefs.FilePath).ExportPictureSize);
        Assert.Equal(1024, w.Files.ExportOptionsNow.PictureSize);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void APieceExportsItsEndsMesh()
    {
        EngineRecipe recipe = EngineKit.Recipe("tree-log");
        Settings log = OakLog();
        byte[] usual = EngineKit.OnThePool(() => recipe.Glb(log, "oak_log", new ExportOptions(ExportLook.Lit), Ct));
        byte[] large = EngineKit.OnThePool(() => recipe.Glb(log, "oak_log", new ExportOptions(ExportLook.Lit, 2048), Ct));
        using var a = GlbFile.Parse(usual);
        using var b = GlbFile.Parse(large);
        Assert.Contains("oak_log_ends", Items(a, "meshes").Select(m => m.GetProperty("name").GetString()));

        // The cut ends print the end grain, drawn again at the size asked.
        Assert.Equal(512, MapWidthOf(a, "oak_log_ends"));
        Assert.Equal(2048, MapWidthOf(b, "oak_log_ends"));
    }

    [Fact]
    public async Task AWetPartExportsShinier()
    {
        EngineRecipe rock = EngineKit.Recipe("cut-rock");
        Settings boulder = YourPropsTests.Point("boulder").Settings;
        double[] dry = Roughness(await GlbAsync(rock, boulder.With("wet", 0.0), "rock", new ExportOptions(ExportLook.Lit)));
        double[] wet = Roughness(await GlbAsync(rock, boulder.With("wet", 1.0), "rock", new ExportOptions(ExportLook.Lit)));
        Assert.All(dry, r => Assert.Equal(1, r, 6));
        Assert.Contains(wet, r => r < 1);
    }

    [Fact]
    public async Task OurKindsIgnoreThePictureSize()
    {
        // (guard) Our own kinds have no pictures to draw larger: the cliff's and the stylized rock's files are the same.
        foreach (string id in (string[])["slab-cliff", "stylized-rock"])
        {
            IMeshRecipe recipe = MeshRecipes.Find(id)!;
            var s = Settings.Defaults(recipe.Specs);
            byte[] usual = await GlbAsync(recipe, s, "pg_test", new ExportOptions(ExportLook.Lit));
            byte[] large = await GlbAsync(recipe, s, "pg_test", new ExportOptions(ExportLook.Lit, 2048));
            Assert.True(usual.AsSpan().SequenceEqual(large), id);
        }
    }
}
