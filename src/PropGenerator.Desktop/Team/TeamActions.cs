using PropGenerator.Desktop.Pictures;
using PropGenerator.Desktop.Props;

namespace PropGenerator.Desktop.Team;

/// <summary>
/// What the app does with the team server: say who you are, list the team's props, publish one of yours and take one
/// of theirs. The server is the one typed in Settings, else the Launcher's <c>JATIFY_TEAM</c>; with neither the app is
/// offline, as without a team. When the server cannot be reached the person is asked to retry or carry on offline.
/// </summary>
internal sealed class TeamActions
{
    private readonly Prefs _prefs;
    private readonly Library _library;
    private readonly ThumbCache _thumbs;
    private readonly IDialogs _dialogs;
    private readonly Action<string, bool> _say;

    /// <summary>Makes the actions.</summary>
    /// <param name="prefs">Where the typed server is kept, and the folder taken props go to.</param>
    /// <param name="library">Your props, which a taken prop joins.</param>
    /// <param name="thumbs">The card pictures.</param>
    /// <param name="dialogs">The questions.</param>
    /// <param name="configFolder">Where team-links.json is kept.</param>
    /// <param name="say">Writes the status line (message, is an error).</param>
    internal TeamActions(Prefs prefs, Library library, ThumbCache thumbs, IDialogs dialogs, string configFolder, Action<string, bool> say)
    {
        _prefs = prefs;
        _library = library;
        _thumbs = thumbs;
        _dialogs = dialogs;
        _say = say;
        Links = new TeamLinks(Path.Combine(configFolder, "team-links.json"));
        library.Renamed += Links.Move;
    }

    /// <summary>Gets what sends the requests (a stub in the tests); null for the system's.</summary>
    internal HttpMessageHandler? Handler { get; init; }

    /// <summary>Gets what reads the Launcher's address (the tests give theirs).</summary>
    internal Func<string?> Launcher { get; init; } = () => Environment.GetEnvironmentVariable("JATIFY_TEAM");

    /// <summary>Gets which of your props are which team prop.</summary>
    internal TeamLinks Links { get; }

    /// <summary>Gets the server in use, or null when offline.</summary>
    internal Uri? Server => TeamClient.Address(_prefs.TeamServer, Launcher());

    /// <summary>Gets the Launcher's address, or null.</summary>
    internal string? LauncherAddress => Launcher() is { Length: > 0 } a ? a.Trim() : null;

    /// <summary>Asks who the server says you are, without asking anything on failure.</summary>
    /// <returns>You, or why not (offline, unreachable, not a team server).</returns>
    internal async Task<(TeamMe? Me, string? Problem)> CheckAsync()
    {
        if (Server is not { } server)
        {
            return (null, "No team server is set: Prop Generator works offline.");
        }

        using var c = new TeamClient(server, Handler);
        try
        {
            return (await c.WhoAmIAsync(), null);
        }
        catch (TeamException e)
        {
            return (null, e.Message);
        }
    }

    /// <summary>Lists the team's props.</summary>
    /// <returns>The props, or null when offline or the server failed (the status line says why).</returns>
    internal Task<IReadOnlyList<TeamProp>?> ListAsync() => RunAsync<IReadOnlyList<TeamProp>>(async c => await c.ListAsync());

    /// <summary>
    /// Publishes a saved prop (as it is on disk) to the team: a new prop the first time, then a new version of it. When
    /// someone published a newer version since, asks before publishing over it.
    /// </summary>
    /// <param name="path">The work file.</param>
    /// <returns>True when it was published.</returns>
    internal async Task<bool> PublishAsync(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        byte[] work;
        try
        {
            if (new FileInfo(path).Length > WorkFile.MaxBytes)
            {
                _say($"{name} is too large to publish.", true);
                return false;
            }

            work = await File.ReadAllBytesAsync(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _say($"{name} could not be read: {e.Message}", true);
            return false;
        }

        byte[] pack = TeamPack.Make(work, _thumbs.Get(ThumbCache.FileKey(Path.GetFullPath(path))));
        (string Id, int Version)? link = Links.Of(path);
        string id = link?.Id ?? TeamClient.NewId();
        TeamProp? row = await RunAsync(async c =>
        {
            try
            {
                return await c.PublishAsync(id, name, pack, link?.Version);
            }
            catch (TeamException e) when (e.Conflict)
            {
                if (!await _dialogs.ConfirmAsync($"Someone published a newer version of {name} since you last had it. Publish yours over it?", "Publish over it", "Keep theirs"))
                {
                    return null;
                }

                int? now = (await c.ListAsync()).FirstOrDefault(p => p.Id == id)?.Version;
                return await c.PublishAsync(id, name, pack, now);
            }
        });
        if (row is null)
        {
            return false;
        }

        Links.Set(path, row.Id, row.Version);
        _say(row.Version == 1 ? $"Published {name} to the team" : $"Published {name} to the team (version {row.Version})", false);
        return true;
    }

    /// <summary>
    /// Takes a team prop into Your props: saved under its name (or a free one beside it) in your save folder, with its
    /// picture, and remembered as that team prop so publishing it again makes a new version.
    /// </summary>
    /// <param name="prop">The prop.</param>
    /// <returns>The new work file, or null when it was not taken (the status line says why).</returns>
    internal async Task<string?> TakeAsync(TeamProp prop)
    {
        byte[]? pack = await RunAsync<byte[]>(async c => await c.TakeAsync(prop.Id));
        if (pack is null)
        {
            return null;
        }

        string folder = _prefs.SaveFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prop Generator");
        string name = YourProps.NameProblem(prop.Name, out string clean) is null && clean.Length > 0 ? clean : "Team prop";
        string tmp = Path.Combine(folder, $".{Path.GetRandomFileName()}.propgen.tmp");
        try
        {
            (byte[] work, byte[]? picture) = TeamPack.Read(pack);
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(tmp, work);
            _ = WorkFile.Load(tmp); // Refuses what is not a work file this build reads, before it is listed.
            string target = Path.Combine(folder, name + ".propgen");
            for (int n = 2; File.Exists(target) || Directory.Exists(target); n++)
            {
                target = Path.Combine(folder, $"{name} {n}.propgen");
            }

            File.Move(tmp, target);
            target = Path.GetFullPath(target);
            if (picture is not null)
            {
                _thumbs.Put(ThumbCache.FileKey(target), picture);
            }

            _library.Add(target);
            Links.Set(target, prop.Id, prop.Version);
            _say($"Took {prop.Name} from the team, as {Path.GetFileNameWithoutExtension(target)}", false);
            return target;
        }
        catch (Exception e) when (e is TeamException or WorkFileException or IOException or UnauthorizedAccessException)
        {
            _say($"{prop.Name} was not taken: {e.Message}", true);
            return null;
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover hidden .tmp is harmless.
            }
        }
    }

    /// <summary>Sends a crash report, once: no retry and no queue (the person can send the next one).</summary>
    /// <param name="zip">The report.</param>
    /// <returns>What to say: sent with its id, or why not.</returns>
    internal async Task<(bool Sent, string Said)> SendCrashAsync(byte[] zip)
    {
        if (Server is not { } server)
        {
            return (false, "The report could not be sent: no team server is set.");
        }

        using var c = new TeamClient(server, Handler);
        try
        {
            string id = await c.SendCrashAsync(zip, "prop-generator", Crash.RunLog.Version, Crash.RunLog.Os);
            return (true, $"Crash report sent to the team ({id}). Thank you.");
        }
        catch (TeamException e)
        {
            return (false, $"The report could not be sent: {e.Message}");
        }
    }

    /// <summary>Sends a suggestion, once: no retry and no queue (what was written stays in the card to send again).</summary>
    /// <param name="suggestion">The suggestion.</param>
    /// <returns>What to say: sent, or why not.</returns>
    internal async Task<(bool Sent, string Said)> SendSuggestionAsync(Suggestion suggestion)
    {
        if (Server is not { } server)
        {
            return (false, "The suggestion could not be sent: no team server is set.");
        }

        using var c = new TeamClient(server, Handler);
        try
        {
            await c.SendSuggestionAsync(suggestion, "prop-generator", Crash.RunLog.Version, Crash.RunLog.Os);
            return (true, "Suggestion sent to the team. Thank you.");
        }
        catch (TeamException e)
        {
            return (false, $"The suggestion could not be sent: {e.Message}");
        }
    }

    // One call to the server: offline says so; unreachable asks Retry or Carry on offline; any other failure is said.
    private async Task<T?> RunAsync<T>(Func<TeamClient, Task<T?>> call)
        where T : class
    {
        if (Server is not { } server)
        {
            _say("No team server is set (Settings, Team): Prop Generator is working offline.", true);
            return null;
        }

        using var c = new TeamClient(server, Handler);
        while (true)
        {
            try
            {
                return await call(c);
            }
            catch (TeamException e) when (e.Unreachable)
            {
                if (!await _dialogs.ConfirmAsync($"{e.Message} Retry, or carry on offline?", "Retry", "Carry on offline"))
                {
                    _say("Working offline.", false);
                    return null;
                }
            }
            catch (TeamException e)
            {
                _say(e.Message, true);
                return null;
            }
        }
    }
}
