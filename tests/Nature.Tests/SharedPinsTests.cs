using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PropGenerator.Nature.Cliffs;
using PropGenerator.Nature.Export;
using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Random;
using PropGenerator.Nature.Stylized;
using PropGenerator.Nature.Tests.Support;
using PropGenerator.Nature.Textures;
using Xunit;

namespace PropGenerator.Nature.Tests;

/// <summary>
/// The export path cliffs and stylized rocks go through, pinned to today's output (phase 4, D3): each unit is fed two
/// cliff presets, two stylized-rock presets and one hand-made source that sways, has foliage and offers a far card, and
/// a SHA-256 of what it gives is compared with Fixtures/shared-hashes.json. Positions and other floats are rounded to
/// 1e-6 before hashing; GLB and other file bytes are hashed as they are. The hashes were written on the commit whose
/// golden tests for the same units were green (so they pin output the 0.4.2 goldens proved). After a deliberate change,
/// run once with <c>PROPGEN_REPIN=1</c> to write them again, and say why in the commit.
/// </summary>
public sealed class SharedPinsTests
{
    private static readonly Lock RepinLock = new();

    private static readonly Lazy<IReadOnlyList<(string Name, ExportSource Source)>> Inputs = new(MakeInputs);

    private static readonly Lazy<IReadOnlyList<(string Name, ExportSource Source, IReadOnlyList<GeneratedPart> Parts)>> Full = new(
        () => [.. Inputs.Value.Select(i => (i.Name, i.Source, i.Source.BuildAtDetail(1)))]);

    [Fact]
    public void MergePin()
    {
        var pin = new Pin();
        foreach (var (name, source, parts) in Full.Value)
        {
            WindExtent? wind = Wind.ForExport(parts, source.Pivot, true, source.Sways, source.SwaysEvenly);
            foreach (bool comic in new[] { false, true })
            {
                IReadOnlyList<MergedMesh> merged = Export.Merge.Parts(parts, name, source.Pivot, wind, true, source.SolidName, comic);
                foreach (MergedMesh m in merged)
                {
                    pin.Add($"{name} {comic} {m.Name} {m.Faces} {m.Material}").Add(m.Mesh);
                }

                foreach (JoinedMesh j in Export.Merge.Join(merged, name))
                {
                    pin.Add($"{name} {comic} joined {j.Name} {j.Faces} {string.Join(',', j.Materials)}").Add(j.Mesh);
                }
            }
        }

        Pins.Check("Merge", pin);
    }

    [Fact]
    public void CollisionPin()
    {
        var pin = new Pin();
        foreach (var (name, source, parts) in Full.Value)
        {
            IReadOnlyList<CollisionShape> shapes = Export.Collision.Choose(source.Collision, parts, source.MaxCollisionShapes);
            pin.Add($"{name} {shapes.Count}");
            for (int i = 0; i < shapes.Count; i++)
            {
                CollisionShape s = shapes[i];
                pin.Add(Export.Collision.NodeName("unreal", name, i)).Add(Export.Collision.NodeName("godot", name, i, "owner"))
                    .Add([s.Start.X, s.Start.Y, s.Start.Z, s.End.X, s.End.Y, s.End.Z, s.R0, s.R1])
                    .Add(Export.Collision.Prism(s, source.Pivot));
            }
        }

        // The pipeline's own shapes, when a source offers none.
        foreach (var (name, _, parts) in Full.Value)
        {
            foreach (CollisionShape s in Export.Collision.Choose([], parts))
            {
                pin.Add($"{name} own").Add([s.Start.X, s.Start.Y, s.Start.Z, s.End.X, s.End.Y, s.End.Z, s.R0, s.R1]);
            }
        }

        Pins.Check("Collision", pin);
    }

    [Fact]
    public void WindPin()
    {
        var pin = new Pin();
        foreach (var (name, source, parts) in Full.Value)
        {
            WindExtent measured = Export.Wind.MeasureExtent(parts, source.Pivot);
            pin.Add(name).Add([measured.Height, measured.Reach]);
            foreach (bool evenly in new[] { false, true })
            {
                WindExtent? wind = Export.Wind.ForExport(parts, source.Pivot, true, sways: true, evenly);
                pin.Add($"{evenly}").Add(wind is null ? [] : [wind.Height, wind.Reach]);
                if (wind is not null)
                {
                    foreach (GeneratedPart part in parts)
                    {
                        PropMesh placed = MeshOps.ApplyMatrix4(part.Mesh.Clone(), part.Matrix);
                        pin.Add(Export.Wind.Uv1(placed, part, source.Pivot, wind));
                    }

                    foreach (string role in new[] { "foliage", "solid", "rigid" })
                    {
                        (double sway, double flutter) = Export.Wind.Weight(0.3, 1.2, -0.4, role, wind);
                        pin.Add([sway, flutter]);
                    }
                }
            }

            pin.Add($"{Export.Wind.ForExport(parts, source.Pivot, false, true, false) is null}");
        }

        Pins.Check("Wind", pin);
    }

    [Fact]
    public void FarCardPin()
    {
        var pin = new Pin();
        foreach (var (name, source, parts) in Full.Value)
        {
            WindExtent? wind = Export.Wind.ForExport(parts, source.Pivot, true, source.Sways, source.SwaysEvenly);
            IReadOnlyList<MergedMesh> merged = Export.Merge.Parts(parts, name, source.Pivot, wind, true, source.SolidName);
            FarFrame frame = Export.FarCard.Frame(merged);
            pin.Add(name).Add([frame.Reach, frame.Bottom, frame.Top]).Add(Export.FarCard.Geometry(frame, wind));
            FarCardData? card = Export.FarCard.Build(merged, wind, new PatternFarPainter());
            pin.Add($"{card is null}");
            if (card is not null)
            {
                pin.Add(card.Pictures.Colour).Add(card.Pictures.Relief).Add(card.Geometry);
                foreach (bool comic in new[] { false, true })
                {
                    pin.Add(GlbWriter.Write([Export.FarCard.Mesh(card, name, comic)], $"{name}_far", new TextureCache()));
                }
            }
        }

        Pins.Check("FarCard", pin);
    }

    [Fact]
    public void PackagePin()
    {
        ExportSettings[] settings =
        [
            new("pin", "unreal", 3),
            new("pin", "unreal", 1, Join: true),
            new("pin", "godot", 2, Wind: false, Look: ExportLook.Cel, Materials: true),
            new("pin", "plain", 3, Collision: false, Far: true, Look: ExportLook.Cartoon),
            new("pin", "godot", 1, Join: true, Far: true),
        ];
        var pin = new Pin();
        foreach (var (name, source) in Inputs.Value)
        {
            foreach (ExportSettings s in settings)
            {
                foreach (ExportFile file in Export.Package.Build(source, s, new TextureCache(), new PatternFarPainter()))
                {
                    pin.Add($"{name} {s} {file.Name} {file.Faces}").Add(file.Bytes);
                }
            }
        }

        Pins.Check("Package", pin);
    }

    [Fact]
    public void GlbWriterPin()
    {
        var pin = new Pin();
        foreach (var (name, source, parts) in Full.Value)
        {
            IReadOnlyList<MergedMesh> merged = Export.Merge.Parts(parts, name, source.Pivot, null, true, source.SolidName);
            pin.Add(Export.GlbWriter.Write([.. merged.Select(GlbNode.Of)], name, new TextureCache()));
            pin.Add(Export.GlbWriter.Write([.. Export.Merge.Join(merged, name).Select(GlbNode.Of)], name, new TextureCache()));
        }

        Pins.Check("GlbWriter", pin);
    }

    [Fact]
    public void AssetJsonPin()
    {
        var pin = new Pin();
        foreach (var (name, source) in Inputs.Value)
        {
            foreach (string engine in Export.Package.Engines)
            {
                foreach (string look in ExportLook.All)
                {
                    pin.Add(Export.AssetJson.Write(name, engine, look, [1200, 480, 144], new WindExtent(2.5, 1.25), source.Description));
                }
            }

            FarCardData card = new(new FarFrame(1.5, 0, 3), PatternFarPainter.Draw(1, 8), new PropMesh { Position = [0, 0, 0] });
            pin.Add(Export.AssetJson.Write(name, "plain", ExportLook.Lit, [10], null, source.Description, card));
        }

        Pins.Check("AssetJson", pin);
    }

    [Fact]
    public void EngineKitsPin()
    {
        var pin = new Pin();
        foreach (string engine in new[] { "godot", "unreal" })
        {
            pin.Add(Export.EngineKits.Folder(engine)).Add(Export.EngineKits.Labels.GetValueOrDefault(engine) ?? "-");
            foreach (ExportFile file in Export.EngineKits.KitFiles(engine))
            {
                pin.Add(file.Name).Add(file.Bytes);
            }
        }

        foreach (string look in ExportLook.All)
        {
            foreach (ExportFile file in Export.EngineKits.GodotMaterialFiles(look))
            {
                pin.Add(file.Name).Add(file.Bytes);
            }
        }

        Pins.Check("EngineKits", pin);
    }

    [Fact]
    public void FileNameOrderPin()
    {
        var pin = new Pin();
        foreach (var (name, source) in Inputs.Value)
        {
            string[] names = [.. Export.Package.Build(source, new ExportSettings(name, "godot", 3, Far: true, Materials: true), new TextureCache(), new PatternFarPainter()).Select(f => f.Name)];
            pin.Add(string.Join('|', names.Reverse().Order(Export.FileNameOrder.Instance)));
        }

        string[] synthetic = ["b.asset.json", "b_LOD2.glb", "a_collision.glb", "b_far.glb", "a.glb", "b_LOD0.glb", "outline.gdshader", "b_LOD10.glb", "b_LOD1.glb", "A.glb"];
        pin.Add(string.Join('|', synthetic.Order(Export.FileNameOrder.Instance)));
        Pins.Check("FileNameOrder", pin);
    }

    [Fact]
    public void MaterialsPin()
    {
        var pin = new Pin();
        string[] materials = ["bark", "bark-light", "leaves", "leaves-dark", "coconut", "painted", "frond", "stone"];
        foreach (var scene in new[] { Nature.Style.SceneMaterials.Default, new Nature.Style.SceneMaterials("#123456", "#abcdef"), new Nature.Style.SceneMaterials("#fff", "#000") })
        {
            foreach (string m in materials)
            {
                try
                {
                    Color3d tint = Style.Materials.Tint(m, scene);
                    pin.Add($"{m} {Style.Materials.ColourOf(m, scene)}").Add([tint.R, tint.G, tint.B]);
                }
                catch (ArgumentException)
                {
                    pin.Add($"{m} unknown");
                }
            }
        }

        foreach (double amount in new[] { 0, 0.15, 0.5, 1 })
        {
            pin.Add(Style.Materials.Lighten("#6b4e35", amount)).Add(Style.Materials.Darken("#4e7d3e", amount));
        }

        Pins.Check("Materials", pin);
    }

    [Fact]
    public void MeshOpsPin()
    {
        var pin = new Pin();
        foreach (var (name, _, parts) in Full.Value)
        {
            PropMesh mesh = parts[0].Mesh.Clone();
            pin.Add(name);
            PropMesh normals = mesh.Clone();
            Kernel.MeshOps.ComputeVertexNormals(normals);
            pin.Add(normals);
            pin.Add(Kernel.MeshOps.ToNonIndexed(mesh.Clone()));
            pin.Add(Kernel.MeshOps.MergeVertices(Kernel.MeshOps.ToNonIndexed(mesh.Clone()), 1e-4));
            pin.Add(Kernel.MeshOps.Translate(mesh.Clone(), 0.5, -1, 2)).Add(Kernel.MeshOps.Scale(mesh.Clone(), 0.8, 1.7, 0.45));
            pin.Add(Kernel.MeshOps.ApplyMatrix4(mesh.Clone(), new Mat4d().MakeRotationAxis(new Vec3d(0, 1, 0), 0.7)));
            pin.Add(Kernel.MeshOps.Merge([.. parts.Select(p => p.Mesh)], useGroups: true));
        }

        Pins.Check("MeshOps", pin);
    }

    [Fact]
    public void FileReliefPin()
    {
        var pin = new Pin();
        foreach (int size in new[] { 2, 16, 64 })
        {
            var random = new SeededRandom((uint)size);
            byte[] rgba = new byte[size * size * 4];
            for (int i = 0; i < rgba.Length; i++)
            {
                rgba[i] = (byte)Math.Floor(random.Next() * 256);
            }

            var image = new RgbaImage(size, size, rgba);
            pin.Add(Textures.FileRelief.Build(image, full: true).Rgba).Add(Textures.FileRelief.Build(image, full: false).Rgba);
        }

        FarPictures far = PatternFarPainter.Draw(2, 32);
        pin.Add(Textures.FileRelief.Build(new RgbaImage(far.Size, far.Size, far.Relief), full: true).Rgba);
        Pins.Check("FileRelief", pin);
    }

    [Fact]
    public void WhiteTexturePin()
    {
        RgbaImage white = Textures.WhiteTexture.Build();
        var pin = new Pin().Add($"{white.W}x{white.H}").Add(white.Rgba);
        Pins.Check("WhiteTexture", pin);
    }

    [Fact]
    public void EveryUnitIsPinned()
    {
        using JsonDocument hashes = Fixture.Json("shared-hashes.json");
        string[] facts = [.. typeof(SharedPinsTests).GetMethods().Where(m => m.GetCustomAttributes(typeof(FactAttribute), false).Length > 0 && m.Name != nameof(EveryUnitIsPinned)).Select(m => m.Name[..^"Pin".Length])];
        Assert.Equal(facts.Order(StringComparer.Ordinal), hashes.RootElement.GetProperty("hashes").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    // Two cliff and two stylized-rock presets, and the hand-made source.
    private static IReadOnlyList<(string Name, ExportSource Source)> MakeInputs() =>
    [
        ("cliff-a", CliffExport.Source(CliffPresets.All[0].Settings)),
        ("cliff-layered", CliffExport.Source(CliffPresets.All.Single(p => p.Id == "layered-cliff").Settings)),
        ("stylized-pebble", StylizedRockGenerator.Source(StylizedRockPresets.All[0].Settings)),
        ("stylized-boulder", StylizedRockGenerator.Source(StylizedRockPresets.All[^1].Settings)),
        ("synthetic", Synthetic()),
    ];

    // A small prop that sways and has foliage: a vertex-coloured trunk (solid) and two leaf blobs (foliage, one with a
    // part matrix), so the merge's buckets, the wind's weights and the far card all have work a rock does not give them.
    // PackageTests counts its builds.
    internal static ExportSource Synthetic()
    {
        static PropMesh Box(double sx, double sy, double sz, double y0, float r, float g, float b)
        {
            float[] p = [.. new[] { (-1, 0, -1), (1, 0, -1), (1, 0, 1), (-1, 0, 1), (-1, 1, -1), (1, 1, -1), (1, 1, 1), (-1, 1, 1) }
                .SelectMany(v => new[] { (float)(v.Item1 * sx), (float)(y0 + (v.Item2 * sy)), (float)(v.Item3 * sz) })];
            var mesh = new PropMesh { Position = p, Color = [.. Enumerable.Range(0, 8).SelectMany(i => new[] { r * (1 - (i * 0.05f)), g, b })] };
            mesh.SetIndex([0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7]);
            Kernel.MeshOps.ComputeVertexNormals(mesh);
            return mesh;
        }

        IReadOnlyList<string> order = ["position", "normal", "color"];
        var surface = new PaintedSurface(VertexColors: true);
        IReadOnlyList<GeneratedPart> Build(double detail) =>
        [
            new GeneratedPart(Box(0.2, 2.4 * detail, 0.2, 0, 0.4f, 0.3f, 0.2f), "bark", null, surface, order, new Mat4d(), 1),
            new GeneratedPart(Box(0.9, 0.8, 0.7, 2.2, 0.2f, 0.6f, 0.2f), "leaves", null, surface, order, new Mat4d(), 1),
            new GeneratedPart(Box(0.6, 0.5, 0.6, 0, 0.1f, 0.5f, 0.15f), "leaves-dark", "foliage", surface, order, new Mat4d().MakeTranslation(0.7, 2.6, -0.3), 0.8),
        ];

        var description = new AssetDescription("synthetic", 1, 7, "low-poly", new Dictionary<string, object?>(StringComparer.Ordinal) { ["height"] = 2.4, ["seed"] = 7.0 }, "pins");
        return new ExportSource(Build, [], new Vec3d(0, 0, 0), Sways: true, SwaysEvenly: false, description) { FarAway = true };
    }

    // A running SHA-256 of what a unit gave.
    private sealed class Pin
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public Pin Add(string text)
        {
            _hash.AppendData(Encoding.UTF8.GetBytes(text + "\n"));
            return this;
        }

        public Pin Add(byte[] bytes)
        {
            _hash.AppendData(BitConverter.GetBytes(bytes.Length));
            _hash.AppendData(bytes);
            return this;
        }

        public Pin Add(IEnumerable<double>? values)
        {
            var text = new StringBuilder();
            foreach (double v in values ?? [])
            {
                text.Append(double.IsFinite(v) ? Math.Round(v * 1e6, MidpointRounding.ToEven).ToString("0", CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture)).Append(',');
            }

            return Add(values is null ? "null" : text.ToString());
        }

        public Pin Add(float[]? values) => Add(values?.Select(v => (double)v));

        public Pin Add(PropMesh? mesh)
        {
            if (mesh is null)
            {
                return Add("no mesh");
            }

            Add(mesh.Position).Add(mesh.Normal).Add(mesh.Color).Add(mesh.Uv).Add(mesh.Uv1).Add(mesh.Rigid);
            Add(mesh.Index is null ? "no index" : string.Join(',', mesh.Index));
            return Add(string.Join(';', mesh.Groups.Select(g => $"{g.Start}+{g.Count}@{g.Material}")));
        }

        public string Hex() => Convert.ToHexStringLower(_hash.GetHashAndReset());
    }

    private static class Pins
    {
        public static void Check(string unit, Pin pin)
        {
            string got = pin.Hex();
            string path = Fixture.Path("shared-hashes.json");
            if (Environment.GetEnvironmentVariable("PROPGEN_REPIN") == "1")
            {
                lock (RepinLock)
                {
                    JsonObject root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : new JsonObject
                    {
                        ["how"] = "SHA-256 per unit of SharedPinsTests: floats rounded to 1e-6, file bytes as they are. Written with PROPGEN_REPIN=1.",
                        ["hashes"] = new JsonObject(),
                    };
                    root["hashes"]![unit] = got;
                    var sorted = new JsonObject(root["hashes"]!.AsObject().OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value!.GetValue<string>())));
                    root["hashes"] = sorted;
                    File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
                }

                return;
            }

            using JsonDocument hashes = Fixture.Json("shared-hashes.json");
            Assert.True(
                hashes.RootElement.GetProperty("hashes").TryGetProperty(unit, out JsonElement want),
                $"{unit} is not pinned in Fixtures/shared-hashes.json");
            Assert.True(want.GetString() == got, $"{unit}: today's output hashes to {got}, the pin is {want.GetString()}");
        }
    }
}
