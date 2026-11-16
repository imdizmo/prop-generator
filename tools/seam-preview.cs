#:project ../src/PropGenerator.Core/PropGenerator.Core.csproj
#:property PublishAot=false

// Renders the composed floor atlas (1024 px, the default settings) in a few wood colours, with the seam colours as the
// palette derives them now, to PNGs for looking at the seams. Usage, from the repository root:
//   dotnet run tools/seam-preview.cs -- OUTDIR #a07850 #d8c6a0 ...
// A colour given as #rrggbb=#deep=#light also writes a "-before" picture with those seam colours, for a comparison.
// The painter runs as the app runs it: painter/worker.py in a systemd scope capped at 2 GB.
using PropGenerator;

string outDir = args[0];
Directory.CreateDirectory(outDir);
string root = Directory.GetCurrentDirectory();
var specs = ParamSchema.Load(Path.Combine(root, "painter", "propgen_painter", "floor_params.json"));
var settings = Settings.Defaults(specs);
await using var painter = new WorkerClient(Path.Combine(root, "painter", "worker.py"), new SystemdWorkerLauncher());
painter.Failed += Console.Error.WriteLine;
Layers layers = await painter.PaintAsync(1024, settings, CancellationToken.None)
    ?? throw new InvalidOperationException("The painter gave no layers.");
foreach (string arg in args.Skip(1))
{
    string[] parts = arg.Split('=');
    var p = Palette.Derive(Rgb.FromHex(parts[0]));
    string name = parts[0].TrimStart('#').ToLowerInvariant();
    Write($"seams-{name}.png", p);
    Console.WriteLine($"{parts[0]}: deep seam {Bytes(p.SeamDeep)}, light seam {Bytes(p.SeamLight)}");
    if (parts.Length == 3)
    {
        Write($"seams-{name}-before.png", p with { SeamDeep = Rgb.FromHex(parts[1]), SeamLight = Rgb.FromHex(parts[2]) });
    }
}

void Write(string file, Palette p)
{
    byte[] rgba = ToneComposer.Compose(layers, p, settings.GetFloat("contrast"), settings.GetFloat("drift"));
    File.WriteAllBytes(Path.Combine(outDir, file), PngEncoder.Encode(rgba, layers.Size, layers.Size));
    Console.WriteLine(Path.Combine(outDir, file));
}

static string Bytes(Rgb c) => $"({Math.Round(c.R * 255)}, {Math.Round(c.G * 255)}, {Math.Round(c.B * 255)})";
