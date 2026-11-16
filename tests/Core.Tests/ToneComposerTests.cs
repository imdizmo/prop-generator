using System.Diagnostics;
using System.Text;
using PropGenerator.Tests.Support;
using Xunit;

namespace PropGenerator.Tests;

public class ToneComposerTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "painter", "worker.py")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static bool HasNumpy()
    {
        try
        {
            using Process p = Process.Start(new ProcessStartInfo("python3", "-c \"import numpy\"")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            })!;
            p.WaitForExit(30000);
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [Fact]
    public void ComposeMakesSeamsTheSeamColour()
    {
        const int s = 256;
        static float[] Z() => new float[s * s];
        float[] region = Z();
        Array.Fill(region, 2f);
        float[] seam = Z();
        Array.Fill(seam, 1f);
        var l = new Layers(s, Z(), Z(), Z(), region, Z(), seam, Z());
        var p = Palette.Derive(new Rgb(0.272f, 0.188f, 0.170f));
        byte[] rgba = ToneComposer.Compose(l, p, 0.65f, 0.6f);
        Assert.Equal(s * s * 4, rgba.Length);
        Assert.Equal((byte)(p.SeamDeep.R * 255 + 0.5f), rgba[0]);
        Assert.Equal(255, rgba[3]);
    }

    [Fact]
    public void ComposeMatchesTheV5GoldenWithinOne()
    {
        if (!HasNumpy())
        {
            Assert.Skip("python3 with numpy is not available, so the real painter cannot run.");
        }

        string root = RepoRoot();
        const int size = 1024;
        var psi = new ProcessStartInfo("python3")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root,
        };
        psi.ArgumentList.Add("-I");
        psi.ArgumentList.Add(Path.Combine(root, "painter", "worker.py"));
        using Process proc = Process.Start(psi)!;
        int pid = proc.Id;
        Layers layers;
        try
        {
            proc.StandardError.BaseStream.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken);
            proc.StandardInput.Write("{\"id\":1,\"size\":1024,\"settings\":{}}\n");
            proc.StandardInput.Flush();
            Stream stdout = proc.StandardOutput.BaseStream;
            var head = new StringBuilder();
            int b;
            while ((b = stdout.ReadByte()) >= 0 && b != '\n')
            {
                head.Append((char)b);
            }

            using var doc = System.Text.Json.JsonDocument.Parse(head.ToString());
            Assert.False(doc.RootElement.TryGetProperty("error", out System.Text.Json.JsonElement err), err.ToString());
            int count = doc.RootElement.GetProperty("bytes").GetInt32();
            byte[] data = new byte[count];
            stdout.ReadExactly(data);
            layers = Layers.FromWorkerBytes(size, data);
        }
        finally
        {
            try
            {
                proc.StandardInput.Close();
                if (!proc.WaitForExit(15000))
                {
                    Process.GetProcessById(pid).Kill(true);
                }
            }
            catch (Exception)
            {
                try
                {
                    Process.GetProcessById(pid).Kill(true);
                }
                catch (ArgumentException)
                {
                }
            }
        }

        var p = Palette.Derive(new Rgb(0.272f, 0.188f, 0.170f));
        byte[] rgba = ToneComposer.Compose(layers, p, 0.65f, 0.6f);
        (int w, int h, byte[] gold) = PngReader.ReadRgb(Path.Combine(root, "painter", "tests", "golden", "floor_atlas_v5.png"));
        Assert.Equal(size, w);
        Assert.Equal(size, h);
        int max = 0;
        for (int i = 0; i < size * size; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                max = Math.Max(max, Math.Abs(rgba[i * 4 + c] - gold[i * 3 + c]));
            }
        }

        Console.WriteLine($"max channel difference vs golden: {max}");
        Assert.True(max <= 1, $"max channel difference {max}");
    }
}
