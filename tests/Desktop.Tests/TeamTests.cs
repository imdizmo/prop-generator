using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PropGenerator.Desktop.Pictures;
using PropGenerator.Desktop.Team;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class TeamTests
{
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("  ", "", null)]
    [InlineData(null, "100.64.0.1", "http://100.64.0.1:8781/")]
    [InlineData("", "nas:9000", "http://nas:9000/")]
    [InlineData("mine.ts.net", "100.64.0.1", "http://mine.ts.net:8781/")]
    [InlineData("https://shelf.example:443", null, "https://shelf.example:8781/")]
    [InlineData("not an address", null, null)]
    public void TheSavedServerWinsOverTheLaunchersAndNeitherMeansOffline(string? saved, string? launcher, string? expected) =>
        Assert.Equal(expected, TeamClient.Address(saved, launcher)?.ToString());

    [Fact]
    public void NewIdsAreTwentyTwoUrlSafeCharacters()
    {
        string id = TeamClient.NewId();
        Assert.Matches("^[A-Za-z0-9_-]{22}$", id);
        Assert.NotEqual(id, TeamClient.NewId());
    }

    [Fact]
    public void APackHoldsTheWorkFileAndPictureAndRefusesAZipWithoutAWorkFile()
    {
        byte[] pack = TeamPack.Make("work"u8.ToArray(), [1, 2, 3]);
        (byte[] work, byte[]? picture) = TeamPack.Read(pack);
        Assert.Equal("work"u8.ToArray(), work);
        Assert.Equal([1, 2, 3], picture);
        Assert.Null(TeamPack.Read(TeamPack.Make("w"u8.ToArray(), null)).Picture);

        using var m = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(m, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("other.txt");
        }

        Assert.Contains("no work file", Assert.Throws<TeamException>(() => TeamPack.Read(m.ToArray())).Message, StringComparison.Ordinal);
        Assert.Throws<TeamException>(() => TeamPack.Read([1, 2, 3]));
    }

    [Fact]
    public void APictureOverItsLimitIsRefusedWhateverTheZipClaims()
    {
        byte[] pack = TeamPack.Make("w"u8.ToArray(), new byte[TeamPack.MaxPicture + 1]);
        Assert.Contains("larger than", Assert.Throws<TeamException>(() => TeamPack.Read(pack)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheClientSaysWhoYouAreAndShowsTheServersErrorAsItIs()
    {
        var shelf = new FakeShelf { Role = null };
        using var c = new TeamClient(new Uri("http://nas:8781/"), shelf);
        TeamMe me = await c.WhoAmIAsync();
        Assert.Equal("ana@example.com", me.Login);
        Assert.False(me.CanPublish);
        Assert.Contains("not on the team", me.Said, StringComparison.Ordinal);

        shelf.Error = (HttpStatusCode.Forbidden, "Observers cannot publish.");
        Assert.Equal("Observers cannot publish.", (await Assert.ThrowsAsync<TeamException>(() => c.PublishAsync("abc", "Rock", [1], null))).Message);
    }

    [Fact]
    public async Task ARowWhoseIdIsNotInTheAgreedFormIsLeftOut()
    {
        var shelf = new FakeShelf();
        using var c = new TeamClient(new Uri("http://nas:8781/"), shelf);
        await c.PublishAsync("good_id-1", "Rock", [1], null);
        shelf.Props["../../evil"] = [(1, "Evil", [1])];
        Assert.Equal(["good_id-1"], (await c.ListAsync()).Select(p => p.Id));
    }

    [Fact]
    public async Task AServerThatCannotBeReachedIsSaidSo()
    {
        using var c = new TeamClient(new Uri("http://nas:8781/"), new StubHttp(_ => throw new HttpRequestException("Connection refused")));
        TeamException e = await Assert.ThrowsAsync<TeamException>(() => c.ListAsync());
        Assert.True(e.Unreachable);
        Assert.Contains("nas:8781", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADamagedDownloadIsRefused()
    {
        var shelf = new FakeShelf { BadHash = true };
        using var c = new TeamClient(new Uri("http://nas:8781/"), shelf);
        await c.PublishAsync("abc", "Rock", [1, 2], null);
        Assert.Contains("damaged", (await Assert.ThrowsAsync<TeamException>(() => c.TakeAsync("abc"))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APublishedPropIsTakenBackWithItsPictureAndTheNextPublishIsANewVersion()
    {
        using var s = new Scratch();
        var shelf = new FakeShelf();
        var dialogs = new FakeDialogs();
        var said = new List<string>();
        (TeamActions team, Library library, ThumbCache thumbs) = Make(s, shelf, dialogs, said);
        string rock = SaveRock(s.P("Mossy rock.propgen"));
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9];
        thumbs.Put(ThumbCache.FileKey(rock), png);

        Assert.True(await team.PublishAsync(rock));
        Assert.Equal("Published Mossy rock to the team", said[^1]);
        string id = Assert.Single(shelf.Props.Keys);
        Assert.Null(shelf.LastIfMatch);

        Assert.True(await team.PublishAsync(rock));
        Assert.Equal("1", shelf.LastIfMatch);
        Assert.Equal("Published Mossy rock to the team (version 2)", said[^1]);

        TeamProp row = Assert.Single((await team.ListAsync())!);
        string? taken = await team.TakeAsync(row);
        Assert.Equal(s.P("saves", "Mossy rock.propgen"), taken);
        Assert.Equal(File.ReadAllBytes(rock), File.ReadAllBytes(taken!));
        Assert.Equal(png, thumbs.Get(ThumbCache.FileKey(taken!)));
        Assert.Contains(library.List(), e => e.Path == taken);
        Assert.Equal((id, 2), team.Links.Of(taken!));
        Assert.Empty(Directory.GetFiles(s.P("saves"), "*.tmp"));

        Assert.Equal(s.P("saves", "Mossy rock 2.propgen"), await team.TakeAsync(row));
        Assert.Empty(dialogs.Questions);
    }

    [Fact]
    public async Task SomeoneElsesNewerVersionIsOnlyPublishedOverWhenYouSaySo()
    {
        using var s = new Scratch();
        var shelf = new FakeShelf();
        var dialogs = new FakeDialogs { Yes = false };
        var said = new List<string>();
        (TeamActions team, _, _) = Make(s, shelf, dialogs, said);
        string rock = SaveRock(s.P("Rock.propgen"));
        await team.PublishAsync(rock);
        string id = shelf.Props.Keys.Single();
        shelf.Props[id].Add((2, "Rock", [7]));

        Assert.False(await team.PublishAsync(rock));
        Assert.Contains("newer version of Rock", Assert.Single(dialogs.Questions), StringComparison.Ordinal);
        Assert.Equal(2, shelf.Props[id].Count);

        dialogs.Yes = true;
        Assert.True(await team.PublishAsync(rock));
        Assert.Equal(3, shelf.Props[id].Count);
        Assert.Equal((id, 3), team.Links.Of(rock));
    }

    [Fact]
    public async Task AnUnreachableServerAsksRetryOrOfflineAndOfflineSaysSo()
    {
        using var s = new Scratch();
        var dialogs = new FakeDialogs { Yes = false };
        var said = new List<string>();
        var prefs = new Prefs(s.P("prefs.json")) { TeamServer = "nas" };
        var library = new Library(s.P("library.json"));
        var team = new TeamActions(prefs, library, new ThumbCache(s.P("thumbs")), dialogs, s.Dir, (m, _) => said.Add(m))
        {
            Handler = new StubHttp(_ => throw new HttpRequestException("No route to host")),
            Launcher = () => null,
        };

        Assert.Null(await team.ListAsync());
        Assert.Contains("Retry, or carry on offline?", Assert.Single(dialogs.Questions), StringComparison.Ordinal);
        Assert.Equal("Working offline.", said[^1]);

        prefs.TeamServer = null;
        Assert.Null(team.Server);
        Assert.Null(await team.ListAsync());
        Assert.Contains("working offline", said[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALinkFollowsItsWorkFileWhenItIsRenamed()
    {
        using var s = new Scratch();
        (TeamActions team, Library library, _) = Make(s, new FakeShelf(), new FakeDialogs(), []);
        string rock = SaveRock(s.P("Rock.propgen"));
        library.Add(rock);
        await team.PublishAsync(rock);
        var props = new Props.YourProps(library, new Props.RemovedBin(s.P("removed")));
        (string? renamed, _) = props.Rename(rock, "Boulder");
        Assert.Null(team.Links.Of(rock));
        Assert.NotNull(team.Links.Of(renamed!));
        Assert.NotNull(new TeamLinks(s.P("team-links.json")).Of(renamed!));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task TeamPropsListsTheTeamsPropsAndTakesOne()
    {
        using var s = new Scratch();
        var shelf = new FakeShelf();
        (TeamActions team, _, _) = Make(s, shelf, new FakeDialogs(), []);
        await team.PublishAsync(SaveRock(s.P("Rock.propgen")));
        string? got = null;
        var page = new TeamPage(team, () => { }, p => got = p);
        await page.Loading;
        Assert.Equal("Connected as Ana (ana@example.com), member", page.Who);
        Assert.Equal(["Rock"], page.Names);
        Assert.Equal(s.P("saves", "Rock.propgen"), await page.TakeAsync("Rock"));
        Assert.Equal(s.P("saves", "Rock.propgen"), got);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task SettingsKeepsTheTypedServerAndCheckSaysWhoYouAre()
    {
        var (w, _, _, _, s) = ClaudeKit.Make();
        var prefs = new Prefs(s.P("team-prefs.json"));
        var team = new TeamActions(prefs, new Library(s.P("team-library.json")), new ThumbCache(s.P("thumbs")), new FakeDialogs(), s.Dir, (_, _) => { })
        {
            Handler = new FakeShelf(),
            Launcher = () => "100.64.0.1",
        };
        var window = new SettingsWindow(w.Claude, prefs, null, team);
        window.Show();
        Ui.Pump();
        var box = Ui.Named<Avalonia.Controls.TextBox>(window, "Team server");
        Assert.Equal("100.64.0.1 (from the Launcher)", box.PlaceholderText);
        box.Text = "nas:9000";
        Ui.Named<Avalonia.Controls.Button>(window, "Check the team server").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await Task.Delay(50);
        Ui.Pump();
        Assert.Equal("nas:9000", new Prefs(s.P("team-prefs.json")).TeamServer);
        Assert.Contains(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.TextBlock>(), t => t.Text == "Connected as Ana (ana@example.com), member");
        window.Close();
    }

    // Against a real library service: PROPGEN_TEAM_LIVE=<host[:port]>. Publishes a prop called "Prop Generator test
    // (delete me)" and takes it back; delete it on the server afterwards (DELETE /props/{id}, owner or admin).
    [Fact]
    public async Task LivePublishAndTakeBack()
    {
        string? live = Environment.GetEnvironmentVariable("PROPGEN_TEAM_LIVE");
        Assert.SkipWhen(string.IsNullOrEmpty(live), "PROPGEN_TEAM_LIVE is not set.");
        using var s = new Scratch();
        var said = new List<string>();
        var prefs = new Prefs(s.P("prefs.json")) { SaveFolder = s.P("saves"), TeamServer = live };
        var team = new TeamActions(prefs, new Library(s.P("library.json")), new ThumbCache(s.P("thumbs")), new FakeDialogs(), s.Dir, (m, _) => said.Add(m));
        string rock = SaveRock(s.P("Prop Generator test (delete me).propgen"));
        Assert.True(await team.PublishAsync(rock), string.Join(" | ", said));
        (string id, int version) = team.Links.Of(rock)!.Value;
        TeamProp row = Assert.Single((await team.ListAsync())!, p => p.Id == id);
        Assert.Equal(version, row.Version);
        string? taken = await team.TakeAsync(row);
        Assert.NotNull(taken);
        Assert.Equal(File.ReadAllBytes(rock), File.ReadAllBytes(taken!));
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("PROPGEN_TEAM_LIVE_OUT") ?? s.P("id.txt")), id);
    }

    private static (TeamActions Team, Library Library, ThumbCache Thumbs) Make(Scratch s, FakeShelf shelf, FakeDialogs dialogs, List<string> said)
    {
        var prefs = new Prefs(s.P("prefs.json")) { SaveFolder = s.P("saves") };
        var library = new Library(s.P("library.json"));
        var thumbs = new ThumbCache(s.P("thumbs"));
        var team = new TeamActions(prefs, library, thumbs, dialogs, s.Dir, (m, _) => said.Add(m))
        {
            Handler = shelf,
            Launcher = () => "nas",
        };
        return (team, library, thumbs);
    }

    private static string SaveRock(string path)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["seed"] = JsonSerializer.SerializeToElement(13) };
        var h = new History();
        h.Push("Rock", new Settings(values));
        WorkFile.Save(path, new Piece("cut-rock", []), new Settings(values), h);
        return path;
    }

    /// <summary>The library service in memory, as Asset Shelf's ShelfServer answers it.</summary>
    private sealed class FakeShelf : HttpMessageHandler
    {
        public Dictionary<string, List<(int Version, string Name, byte[] Pack)>> Props { get; } = [];

        public string? Role { get; set; } = "member";

        public string? LastIfMatch { get; private set; }

        public bool BadHash { get; set; }

        public (HttpStatusCode Status, string Error)? Error { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken stop)
        {
            if (Error is { } err)
            {
                return Json(err.Status, new JsonObject { ["error"] = err.Error });
            }

            string path = req.RequestUri!.AbsolutePath;
            if (path == "/team/whoami")
            {
                return Json(HttpStatusCode.OK, new JsonObject { ["login"] = "ana@example.com", ["name"] = "Ana", ["role"] = Role, ["service"] = "shelf", ["version"] = "0.1.0" });
            }

            if (path == "/props" && req.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, new JsonArray([.. Props.Select(p => (JsonNode)RowOf(p.Key, p.Value[^1]))]));
            }

            string id = path.Split('/')[2];
            if (req.Method == HttpMethod.Put)
            {
                LastIfMatch = req.Headers.TryGetValues("If-Match", out var m) ? m.Single() : null;
                Assert.Equal("application/zip", req.Content!.Headers.ContentType!.MediaType);
                string name = Uri.UnescapeDataString(req.RequestUri.Query.Split("name=")[1]);
                byte[] body = await req.Content.ReadAsByteArrayAsync(stop);
                bool fresh = !Props.TryGetValue(id, out var versions);
                versions ??= Props[id] = [];
                if (LastIfMatch is not null && versions.Count > 0 && LastIfMatch != versions[^1].Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
                {
                    return Json(HttpStatusCode.Conflict, new JsonObject { ["error"] = "newer version" });
                }

                versions.Add((versions.Count + 1, name, body));
                return Json(fresh ? HttpStatusCode.Created : HttpStatusCode.OK, RowOf(id, versions[^1]));
            }

            (int _, string _, byte[] pack) = Props[id][^1];
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pack) };
            r.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue('"' + Convert.ToHexString(SHA256.HashData(BadHash ? [0] : pack)).ToLowerInvariant() + '"');
            return r;
        }

        private static JsonObject RowOf(string id, (int Version, string Name, byte[] Pack) v) => new()
        {
            ["id"] = id,
            ["name"] = v.Name,
            ["version"] = v.Version,
            ["size"] = v.Pack.Length,
            ["sha256"] = Convert.ToHexString(SHA256.HashData(v.Pack)).ToLowerInvariant(),
            ["publishedBy"] = new JsonObject { ["login"] = "ana@example.com", ["name"] = "Ana" },
            ["publishedAt"] = "2026-11-16T12:00:00Z",
        };

        private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
            new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
