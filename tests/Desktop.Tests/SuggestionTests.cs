using System.IO.Compression;
using System.Net;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PropGenerator.Desktop.Crash;
using PropGenerator.Desktop.Pictures;
using PropGenerator.Desktop.Team;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class SuggestionTests
{
    private static readonly byte[] Png = PngEncoder.Encode(new byte[4 * 4], 2, 2);

    [Fact]
    public void OnlyPngAndJpegArePictures()
    {
        Assert.True(Suggestion.IsPicture(Png));
        Assert.True(Suggestion.IsPicture([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.False(Suggestion.IsPicture("GIF89a"u8));
        Assert.False(Suggestion.IsPicture([]));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task SendIsOffUntilSomethingIsWrittenAndSendsTheTextAndPictures()
    {
        Suggestion? got = null;
        var said = new List<(string, bool)>();
        using var s = new Scratch();
        File.WriteAllBytes(s.P("shot.png"), Png);
        File.WriteAllBytes(s.P("notes.txt"), "hello"u8.ToArray());
        File.WriteAllBytes(s.P("big.png"), new byte[Suggestion.MaxPictureBytes + 1]);
        IReadOnlyList<string> picked = [s.P("shot.png"), s.P("notes.txt"), s.P("big.png"), s.P("shot.png")];
        var card = new SuggestionCard(x => { got = x; return Task.FromResult((true, "sent")); }, () => Task.FromResult(picked), () => { }, (t, e) => said.Add((t, e)));
        Assert.False(card.CanSend);
        Assert.False(card.Busy);

        card.UseWindow(Png);
        card.AddWindow();
        await card.AddFilesAsync();
        Assert.Equal(["window.png", "shot.png", "shot-2.png"], card.Pictures.Select(p => p.Name));
        Assert.Contains("too big", card.Note, StringComparison.Ordinal);
        Assert.True(card.Busy);  // Esc or a click beside it does not throw away the pictures
        Assert.False(card.CanSend);

        Ui.Named<Button>(card, "Remove shot-2.png").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        card.Text = "  The seed box is too small.  ";
        Assert.True(card.CanSend);
        await card.SendAsync();
        Assert.Equal("The seed box is too small.", got!.Text);
        Assert.Equal(["window.png", "shot.png"], got.Pictures.Select(p => p.Name));
        Assert.Equal([("sent", false)], said);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task AFailedSendKeepsTheCardAndSaysWhy()
    {
        var said = new List<(string, bool)>();
        var card = new SuggestionCard(_ => Task.FromResult((false, "The suggestion could not be sent: the server is down.")), () => Task.FromResult<IReadOnlyList<string>>([]), () => { }, (t, e) => said.Add((t, e)))
        {
            Text = "More presets",
        };
        await card.SendAsync();
        Assert.Empty(said);
        Assert.Equal("The suggestion could not be sent: the server is down.", card.Note);
        Assert.True(card.CanSend);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void WithoutATeamServerTheCardSaysSoAndCannotSend()
    {
        var card = new SuggestionCard(null, () => Task.FromResult<IReadOnlyList<string>>([]), () => { }, (_, _) => { })
        {
            Text = "More presets",
        };
        Assert.False(card.CanSend);
        Assert.True(Ui.Named<TextBlock>(card, "No team server").IsVisible);
    }

    [Fact]
    public async Task ASuggestionIsPostedAsAZipOfItsTextAndPictures()
    {
        HttpRequestMessage? got = null;
        byte[]? body = null;
        var ok = new StubHttp(req =>
        {
            got = req;
            body = req.Content!.ReadAsByteArrayAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"id\":\"x\"}", Encoding.UTF8, "application/json") };
        });
        using var s = new Scratch();
        (bool sent, string said) = await Team(s, ok).SendSuggestionAsync(new Suggestion("Bigger sliders", [new SuggestionPicture("window.png", Png), new SuggestionPicture("My shot (2).jpeg", [0xFF, 0xD8, 0xFF, 0xE0])]));
        Assert.True(sent, said);
        Assert.Equal(HttpMethod.Post, got!.Method);
        Assert.Equal("/team/suggestions", got.RequestUri!.AbsolutePath);
        Assert.Equal($"?tool=prop-generator&version={RunLog.Version}&os={RunLog.Os}", got.RequestUri.Query);
        Assert.Equal("application/zip", got.Content!.Headers.ContentType!.MediaType);
        using var zip = new ZipArchive(new MemoryStream(body!));
        Assert.Equal(["suggestion.txt", "screenshot-1.png", "screenshot-2.jpg"], zip.Entries.Select(e => e.Name));
        Assert.Equal("Bigger sliders", new StreamReader(zip.GetEntry("suggestion.txt")!.Open()).ReadToEnd());

        var refused = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"error\":\"You are not on the team.\"}") });
        (sent, said) = await Team(s, refused).SendSuggestionAsync(new Suggestion("x", []));
        Assert.False(sent);
        Assert.Equal("The suggestion could not be sent: You are not on the team.", said);

        var old = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":\"Not found.\"}") });
        (sent, said) = await Team(s, old).SendSuggestionAsync(new Suggestion("x", []));
        Assert.False(sent);
        Assert.Equal("The suggestion could not be sent: Your team's server is too old for suggestions: ask its owner to update it.", said);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task OffTheTeamThereIsNoSuggestionButtonAndItsCardKeepsWhatWasWritten()
    {
        var (w, _, _, _, _) = ClaudeKit.Make();
        await w.CheckTeamAsync();
        Assert.False(w.OnTeam);
        Assert.False(w.TopBar.Suggest.IsVisible);

        await w.ShowSuggestionAsync();
        var card = Assert.IsType<SuggestionCard>(w.Cards.Shown);
        card.Text = "Keep me";
        w.Cards.Close();
        await w.ShowSuggestionAsync();
        Assert.Same(card, w.Cards.Shown);
        Assert.Equal("Keep me", card.Text);
        w.Close();
    }

    private static TeamActions Team(Scratch s, HttpMessageHandler handler) =>
        new(new Prefs(s.P("prefs.json")), new Library(s.P("library.json")), new ThumbCache(s.P("thumbs")), new FakeDialogs(), s.Dir, (_, _) => { })
        {
            Handler = handler,
            Launcher = () => "nas",
        };
}
