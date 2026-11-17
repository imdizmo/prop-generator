using System.IO.Compression;
using System.Net;
using System.Text;
using PropGenerator.Desktop.Crash;
using PropGenerator.Desktop.Pictures;
using PropGenerator.Desktop.Team;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class CrashReportTests
{
    [Fact]
    public void ARunWithoutACleanExitIsTheNextStartsCrashAndACleanOneIsNot()
    {
        using var s = new Scratch();
        string dir = s.P("logs");
        string first;
        using (var run = new RunLog(dir))
        {
            Assert.Null(run.Crashed);
            first = run.Path;
            run.Write("status: painting");
            run.Error("unhandled", new InvalidOperationException("the view broke"));
        }

        // No Clean: the process died.
        string second;
        using (var run = new RunLog(dir))
        {
            Assert.Equal(first, run.Crashed);
            second = run.Path;
            run.Clean();
        }

        using (var run = new RunLog(dir))
        {
            Assert.Null(run.Crashed);
            Assert.NotEqual(second, run.Path);
        }

        string text = File.ReadAllText(first);
        Assert.Contains("status: painting", text, StringComparison.Ordinal);
        Assert.Contains("error (unhandled): System.InvalidOperationException: the view broke", text, StringComparison.Ordinal);
        Assert.DoesNotContain("exit: clean", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNewestFiveLogsAreKeptAndAlwaysTheCrashedOne()
    {
        using var s = new Scratch();
        string dir = s.P("logs");
        string crashed;
        using (var run = new RunLog(dir))
        {
            crashed = run.Path;
        }

        File.SetLastWriteTimeUtc(crashed, DateTime.UtcNow.AddDays(-30));  // the oldest of all
        for (int i = 0; i < 7; i++)
        {
            string old = Path.Combine(dir, $"run-2026010{i}-000000-1.log");
            File.WriteAllText(old, "old");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10 + i));
        }

        using var now = new RunLog(dir);
        Assert.Equal(crashed, now.Crashed);
        string[] left = Directory.GetFiles(dir, "run-*.log");
        Assert.Contains(crashed, left);
        Assert.Contains(now.Path, left);
        Assert.Equal(RunLog.Keep + 1, left.Length);
    }

    [Fact]
    public void ALogStopsAtItsLimit()
    {
        using var s = new Scratch();
        using var run = new RunLog(s.P("logs"));
        string line = new('x', 1000);
        for (int i = 0; i < 2200; i++)
        {
            run.Write(line);
        }

        long size = new FileInfo(run.Path).Length;
        Assert.InRange(size, RunLog.MaxBytes, RunLog.MaxBytes + 2000);
        Assert.EndsWith("for this run)\n", File.ReadAllText(run.Path), StringComparison.Ordinal);
    }

    [Fact]
    public void TheReportHoldsASummaryAndTheWholeLogAndShowsExactlyThat()
    {
        using var s = new Scratch();
        string log;
        using (var run = new RunLog(s.P("logs")))
        {
            log = run.Path;
            run.Memory();
            run.Error("unhandled", new InvalidOperationException("boom"));
        }

        var r = CrashReport.Of(log);
        using var zip = new ZipArchive(new MemoryStream(r.Zip));
        Assert.Equal(["report.txt", Path.GetFileName(log)], zip.Entries.Select(e => e.Name));
        string summary = new StreamReader(zip.GetEntry("report.txt")!.Open()).ReadToEnd();
        Assert.Contains("Tool: prop-generator", summary, StringComparison.Ordinal);
        Assert.Contains("Errors in the log: 1", summary, StringComparison.Ordinal);
        Assert.Contains("memory: working set", summary, StringComparison.Ordinal);
        string whole = new StreamReader(zip.GetEntry(Path.GetFileName(log))!.Open()).ReadToEnd();
        Assert.Equal(File.ReadAllText(log), whole);
        Assert.Contains(summary, r.Shown, StringComparison.Ordinal);
        Assert.Contains(whole, r.Shown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReportIsPostedAsTheServerAsksAndAFailureIsSaidAsTheServerSaysIt()
    {
        HttpRequestMessage? got = null;
        byte[]? body = null;
        var ok = new StubHttp(req =>
        {
            got = req;
            body = req.Content!.ReadAsByteArrayAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"id\":\"prop-generator/20261117-010203-ana.zip\"}", Encoding.UTF8, "application/json") };
        });
        using var s = new Scratch();
        TeamActions team = Team(s, ok);
        (bool sent, string said) = await team.SendCrashAsync([1, 2, 3]);
        Assert.True(sent);
        Assert.Contains("prop-generator/20261117-010203-ana.zip", said, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Post, got!.Method);
        Assert.Equal("/team/crashes", got.RequestUri!.AbsolutePath);
        Assert.Equal($"?tool=prop-generator&version={RunLog.Version}&os={RunLog.Os}", got.RequestUri.Query);
        Assert.Equal("application/zip", got.Content!.Headers.ContentType!.MediaType);
        Assert.Equal([1, 2, 3], body);

        var refused = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge) { Content = new StringContent("{\"error\":\"The report is over 20 MB.\"}") });
        (sent, said) = await Team(s, refused).SendCrashAsync([1]);
        Assert.False(sent);
        Assert.Equal("The report could not be sent: The report is over 20 MB.", said);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task TheCardSendsOnlyOnSendAndShowsWhatIsSent()
    {
        var report = new CrashReport([7], "report.txt\nTool: prop-generator");
        int sends = 0;
        var answers = new List<(string, bool)>();
        var card = new CrashCard(report, zip => { sends++; return Task.FromResult((true, "sent")); }, (said, error) => answers.Add((said, error)));
        Assert.False(card.ShowingWhatIsSent);
        Ui.Named<Avalonia.Controls.Button>(card, "Show what is sent").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.True(card.ShowingWhatIsSent);
        Assert.Equal(report.Shown, Ui.Named<Avalonia.Controls.TextBox>(card, "What is sent").Text);

        card.DontSend();
        Assert.Equal(0, sends);
        Assert.Equal(("The crash report was not sent.", false), answers[^1]);

        await card.SendAsync();
        Assert.Equal(1, sends);
        Assert.Equal(("sent", false), answers[^1]);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void NothingIsOfferedWithoutATeamServerAndTheCrashIsOfferedOnce()
    {
        var (w, _, _, _, s) = ClaudeKit.Make();
        string dir = s.P("crash-logs");
        using (new RunLog(dir))
        {
        }

        using var log = new RunLog(dir);
        Assert.NotNull(log.Crashed);
        App.OfferCrashReport(w, log);
        Assert.IsNotType<CrashCard>(w.Cards.Shown);
        Assert.NotNull(log.Crashed);

        w.Files.Prefs.TeamServer = "nas";
        App.OfferCrashReport(w, log);
        Assert.IsType<CrashCard>(w.Cards.Shown);
        Assert.Null(log.Crashed);
        w.Cards.Close();
        App.OfferCrashReport(w, log);
        Assert.Null(w.Cards.Shown);
        w.Close();
    }

    private static TeamActions Team(Scratch s, HttpMessageHandler handler) =>
        new(new Prefs(s.P("prefs.json")), new Library(s.P("library.json")), new ThumbCache(s.P("thumbs")), new FakeDialogs(), s.Dir, (_, _) => { })
        {
            Handler = handler,
            Launcher = () => "nas",
        };
}
