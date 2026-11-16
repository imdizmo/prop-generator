using System.Text;
using System.Text.Json;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Tests.Support;
using PropGenerator.Nature.Textures;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class EngineKitsTests
{
    [Fact]
    public void EveryKitFileIsEasyPropsByteForByte()
    {
        using var doc = Fixture.Json("kits.json");
        foreach (var kit in doc.RootElement.GetProperty("kits").EnumerateObject())
        {
            AssertFiles(kit.Value, EngineKits.KitFiles(kit.Name), kit.Name);
            Assert.Equal(doc.RootElement.GetProperty("folders").GetProperty(kit.Name).GetString(), EngineKits.Folder(kit.Name));
            Assert.Equal(doc.RootElement.GetProperty("labels").GetProperty(kit.Name).GetString(), EngineKits.Labels[kit.Name]);
        }
    }

    [Fact]
    public void EveryLooksGodotMaterialsAreEasyPropsByteForByte()
    {
        using var doc = Fixture.Json("kits.json");
        foreach (var look in doc.RootElement.GetProperty("materials").EnumerateObject())
        {
            AssertFiles(look.Value, EngineKits.GodotMaterialFiles(look.Name), look.Name);
        }
    }

    [Fact]
    public void GodotMaterialsGoBesideTheFilesOnlyForGodot()
    {
        var source = PropGenerator.Nature.Stylized.StylizedRockGenerator.Source(PropGenerator.Nature.Stylized.StylizedRockSettings.Defaults);
        var godot = Package.Build(source, new ExportSettings("rock", "godot", 1, Look: ExportLook.Cel, Materials: true), new TextureCache());
        Assert.Equal(
            ["leaves_comic.gdshader", "outline.gdshader", "wood_comic.gdshader"],
            godot.Where(f => f.Name.EndsWith(".gdshader", StringComparison.Ordinal)).Select(f => f.Name).Order(StringComparer.Ordinal));
        var unreal = Package.Build(source, new ExportSettings("rock", "unreal", 1, Look: ExportLook.Cel, Materials: true), new TextureCache());
        Assert.DoesNotContain(unreal, f => f.Name.EndsWith(".gdshader", StringComparison.Ordinal));
        var off = Package.Build(source, new ExportSettings("rock", "godot", 1), new TextureCache());
        Assert.DoesNotContain(off, f => f.Name.EndsWith(".gdshader", StringComparison.Ordinal));
    }

    [Fact]
    public void AToonShaderKeepsTheLitMaterialsWind()
    {
        string lit = EngineKits.Text("godot/wood.gdshader");
        string comic = GodotToon.Shader(ExportLook.Cel, "wood");
        Assert.Contains("void vertex() {\n\tface_colour = COLOR.rgb;", comic, StringComparison.Ordinal);
        Assert.Contains(lit[lit.IndexOf("shader_type", StringComparison.Ordinal)..lit.IndexOf("void vertex()", StringComparison.Ordinal)].Trim(), comic, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => GodotToon.Shader(ExportLook.Lit, "wood"));
    }

    private static void AssertFiles(JsonElement want, IReadOnlyList<ExportFile> ours, string where)
    {
        Assert.Equal(want.EnumerateArray().Select(f => f.GetProperty("name").GetString()), ours.Select(f => f.Name));
        foreach (var (file, mine) in want.EnumerateArray().Zip(ours))
        {
            byte[] bytes = Convert.FromBase64String(file.GetProperty("bytes").GetString()!);
            Assert.True(bytes.AsSpan().SequenceEqual(mine.Bytes), $"{where} {mine.Name}:\n{Diff(Encoding.UTF8.GetString(bytes), Encoding.UTF8.GetString(mine.Bytes))}");
        }
    }

    private static string Diff(string want, string ours)
    {
        int at = 0;
        while (at < Math.Min(want.Length, ours.Length) && want[at] == ours[at])
        {
            at++;
        }

        int from = Math.Max(0, at - 80);
        return $"differs at {at}\neasy-props: …{want[from..Math.Min(want.Length, at + 80)]}\nours:       …{ours[from..Math.Min(ours.Length, at + 80)]}";
    }
}
