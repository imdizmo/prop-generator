using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

// Save a picture (the file side, with a fake capture: the tests have no OpenGL) and Report a problem (nothing is sent).
public class PictureAndReportTests
{
    [Theory]
    [InlineData("Big rock", "Big rock")]
    [InlineData("a/b:c*d?\"e<f>g|h", "a b c d e f g h")]
    [InlineData("..hidden", "hidden")]
    [InlineData("  ", "Prop picture")]
    public void APicturesNameIsSafeAsAFileName(string name, string safe) => Assert.Equal(safe, PictureFile.SafeName(name));

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void SaveAPictureWritesTheViewAsAPng()
    {
        Scratch s = Ui.NewScratch();
        var dialogs = new FakeDialogs();
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        byte[] rgba = new byte[4 * 3 * 4];
        rgba[0] = 255;
        w.Files.Capture = () => Task.FromResult((rgba, 4, 3));
        dialogs.SaveFilePath = s.P("shot.png");

        CommandListTests.Run(w, "save-picture");

        Assert.Equal("untitled.png", dialogs.SaveFileSuggestions[0]);
        byte[] png = File.ReadAllBytes(s.P("shot.png"));
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        Assert.Equal(4, (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19]);
        Assert.Equal("Saved shot.png", w.StatusLine.Text);

        w.Files.Capture = () => throw new InvalidOperationException("no view");
        CommandListTests.Run(w, "save-picture");
        Assert.StartsWith("The picture couldn't be drawn", w.StatusLine.Text, StringComparison.Ordinal);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AReportHasTheVersionTheSystemAndThePropAndIsCopiedOrSaved()
    {
        Scratch s = Ui.NewScratch();
        var dialogs = new FakeDialogs();
        MainWindow w = Ui.Open(new FakePainter(), dialogs, s);
        Assert.True(ClaudeKit.Run(w.Files.StartFromAsync(YourPropsTests.Point("boulder"))));
        string? copied = null;
        w.CopyText = t =>
        {
            copied = t;
            return Task.FromResult(true);
        };

        CommandListTests.Run(w, "report-problem");
        ReportCard card = Assert.IsType<ReportCard>(w.Cards.Shown);
        string report = card.Text;
        Assert.Contains($"Prop Generator {BuildInfo.Version}", report, StringComparison.Ordinal);
        Assert.Contains(System.Runtime.InteropServices.RuntimeInformation.OSDescription, report, StringComparison.Ordinal);
        Assert.Contains("Rock, from Boulder", report, StringComparison.Ordinal);
        Assert.Contains($"seed {w.Panel.SeedValue}", report, StringComparison.Ordinal);
        Assert.Contains("build: ", report, StringComparison.Ordinal);

        ClaudeKit.Run(card.CopyAsync());
        Assert.Equal(report, copied);
        Assert.Contains("Copied the report. Nothing was sent.", w.RecentMessages);

        dialogs.SaveFilePath = s.P("report.txt");
        ClaudeKit.Run(card.SaveAsync());
        Assert.Equal(report, File.ReadAllText(s.P("report.txt")));
        w.Close();
    }
}
