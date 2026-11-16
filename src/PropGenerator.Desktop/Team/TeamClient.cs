using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace PropGenerator.Desktop.Team;

/// <summary>Who the team server says you are: your Tailscale login and name, and your role on the team (null: not on it).</summary>
/// <param name="Login">The Tailscale login.</param>
/// <param name="Name">The display name.</param>
/// <param name="Role">The role (observer, member, admin, owner), or null for someone not on the team list.</param>
internal sealed record TeamMe(string Login, string Name, string? Role)
{
    /// <summary>Gets a value indicating whether this person may publish (member and up).</summary>
    internal bool CanPublish => Role is "member" or "admin" or "owner";

    /// <summary>Gets the line the app shows, such as "Connected as Ana (ana@example.com), member".</summary>
    internal string Said => Role is null
        ? $"{Name} ({Login}) is not on the team yet: ask the team's owner to add you."
        : $"Connected as {Name} ({Login}), {Role}";
}

/// <summary>The newest version of a prop on the team's library.</summary>
/// <param name="Id">The prop's id, the same across versions.</param>
/// <param name="Name">Its name.</param>
/// <param name="Version">Its version.</param>
/// <param name="Size">The pack's size in bytes.</param>
/// <param name="Sha256">The pack's SHA-256, in hex.</param>
/// <param name="ByName">Who published it (display name).</param>
/// <param name="PublishedAt">When.</param>
internal sealed record TeamProp(string Id, string Name, int Version, long Size, string Sha256, string ByName, DateTimeOffset PublishedAt);

/// <summary>Why a call to the team server failed, in words to show as they are.</summary>
internal sealed class TeamException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="TeamException"/> class.</summary>
    public TeamException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TeamException"/> class.</summary>
    /// <param name="message">What to show.</param>
    public TeamException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TeamException"/> class.</summary>
    /// <param name="message">What to show.</param>
    /// <param name="inner">The failure behind it.</param>
    public TeamException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Makes one.</summary>
    /// <param name="message">What to show.</param>
    /// <param name="unreachable">True when the server could not be reached at all.</param>
    /// <param name="conflict">True when someone published a newer version first (409).</param>
    internal TeamException(string message, bool unreachable, bool conflict = false)
        : base(message)
    {
        Unreachable = unreachable;
        Conflict = conflict;
    }

    /// <summary>Gets a value indicating whether the server could not be reached.</summary>
    internal bool Unreachable { get; }

    /// <summary>Gets a value indicating whether a newer version was published first.</summary>
    internal bool Conflict { get; }
}

/// <summary>
/// The calls Prop Generator makes to the team's library service (Asset Shelf's ShelfServer):
/// who am I, the list of props, publish a pack and take one. Plain http: Tailscale encrypts the connection and the server
/// knows the caller from it, so nothing is signed here.
/// </summary>
internal sealed partial class TeamClient : IDisposable
{
    /// <summary>The library service's port when the address names none.</summary>
    internal const int DefaultPort = 8781;

    /// <summary>The most bytes of an answer that is not a pack (whoami, the list, an error): far above a real one.</summary>
    internal const int MaxReply = 8 * 1024 * 1024;

    private readonly HttpClient _http;

    /// <summary>Makes a client for a server.</summary>
    /// <param name="server">The server, as <see cref="Address"/> makes it.</param>
    /// <param name="handler">What sends the requests (the system's in the app, a stub in the tests); null for the system's.</param>
    internal TeamClient(Uri server, HttpMessageHandler? handler = null)
    {
        Server = server;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = server;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>Gets the server.</summary>
    internal Uri Server { get; }

    /// <summary>
    /// The server to use: the one saved in the app's settings, else the Launcher's <c>JATIFY_TEAM</c>; null when neither
    /// is set (offline). A host or host:port, with the default port added; a scheme typed with it is kept.
    /// </summary>
    /// <param name="saved">The address saved in the settings, or null.</param>
    /// <param name="launcher">The <c>JATIFY_TEAM</c> value, or null.</param>
    /// <returns>The server, or null when offline or the text is not an address.</returns>
    internal static Uri? Address(string? saved, string? launcher)
    {
        string text = (string.IsNullOrWhiteSpace(saved) ? launcher : saved)?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return null;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? u) || u.Scheme is not ("http" or "https") || u.Host.Length == 0)
        {
            return null;
        }

        // ponytail: an explicit :80 reads as no port (8781); nobody runs the library on 80.
        return new UriBuilder(u.Scheme, u.Host, u.IsDefaultPort ? DefaultPort : u.Port).Uri;
    }

    /// <summary>A new id for a prop published for the first time: 22 letters, digits, - and _.</summary>
    /// <returns>The id.</returns>
    internal static string NewId() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Asks who the server says you are.</summary>
    /// <param name="stop">Stops the call.</param>
    /// <returns>You.</returns>
    internal async Task<TeamMe> WhoAmIAsync(CancellationToken stop = default)
    {
        using JsonDocument d = await GetJsonAsync("team/whoami", stop);
        JsonElement r = d.RootElement;
        return new TeamMe(Text(r, "login") ?? string.Empty, Text(r, "name") ?? Text(r, "login") ?? string.Empty, Text(r, "role"));
    }

    /// <summary>Lists the newest version of every prop on the team.</summary>
    /// <param name="stop">Stops the call.</param>
    /// <returns>The props.</returns>
    internal async Task<IReadOnlyList<TeamProp>> ListAsync(CancellationToken stop = default)
    {
        using JsonDocument d = await GetJsonAsync("props", stop);
        if (d.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new TeamException($"The team server at {Server.Authority} sent a list that is not a list.");
        }

        return [.. d.RootElement.EnumerateArray().Select(Row).OfType<TeamProp>()];
    }

    /// <summary>Publishes a pack as a prop's new version.</summary>
    /// <param name="id">The prop's id.</param>
    /// <param name="name">Its name.</param>
    /// <param name="pack">The zip.</param>
    /// <param name="lastVersion">The version this one follows (sent as If-Match), or null for a prop new to the team.</param>
    /// <param name="stop">Stops the call.</param>
    /// <returns>The prop as the server now has it.</returns>
    internal async Task<TeamProp> PublishAsync(string id, string name, byte[] pack, int? lastVersion, CancellationToken stop = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"props/{Uri.EscapeDataString(id)}?name={Uri.EscapeDataString(name)}")
        {
            Content = new ByteArrayContent(pack),
        };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        if (lastVersion is int v)
        {
            req.Headers.TryAddWithoutValidation("If-Match", v.ToString(CultureInfo.InvariantCulture));
        }

        using HttpResponseMessage r = await SendAsync(req, stop);
        if (r.StatusCode == HttpStatusCode.Conflict)
        {
            throw new TeamException($"Someone published a newer version of {name} first.", unreachable: false, conflict: true);
        }

        await Check(r, stop);
        using var d = JsonDocument.Parse(await ReadAsync(r, MaxReply, stop));
        return Row(d.RootElement) ?? throw new TeamException($"The team server at {Server.Authority} did not say what it kept.");
    }

    /// <summary>Downloads a prop's newest pack, checked against the hash the server sends.</summary>
    /// <param name="id">The prop's id.</param>
    /// <param name="stop">Stops the call.</param>
    /// <returns>The zip.</returns>
    internal async Task<byte[]> TakeAsync(string id, CancellationToken stop = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"props/{Uri.EscapeDataString(id)}/pack");
        using HttpResponseMessage r = await SendAsync(req, stop);
        await Check(r, stop);
        byte[] zip = await ReadAsync(r, TeamPack.MaxPack, stop);
        if (r.Headers.ETag?.Tag.Trim('"') is { Length: 64 } tag
            && !string.Equals(tag, Convert.ToHexString(SHA256.HashData(zip)), StringComparison.OrdinalIgnoreCase))
        {
            throw new TeamException("The prop arrived damaged (its hash does not match): try again.");
        }

        return zip;
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial System.Text.RegularExpressions.Regex IdForm();

    private static string? Text(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static TeamProp? Row(JsonElement o)
    {
        // An id outside the agreed form (1–64 of A-Z a-z 0-9 _ -) is not a prop: it never reaches a path or a URL.
        if (Text(o, "id") is not { } id || !IdForm().IsMatch(id) || Text(o, "name") is not { } name
            || !o.TryGetProperty("version", out JsonElement v) || !v.TryGetInt32(out int version))
        {
            return null;
        }

        long size = o.TryGetProperty("size", out JsonElement s) && s.TryGetInt64(out long n) ? n : 0;
        string by = o.TryGetProperty("publishedBy", out JsonElement p) ? Text(p, "name") ?? Text(p, "login") ?? "someone" : "someone";
        DateTimeOffset at = Text(o, "publishedAt") is { } t && DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset when) ? when : DateTimeOffset.MinValue;
        return new TeamProp(id, name, version, size, Text(o, "sha256") ?? string.Empty, by, at);
    }

    private static async Task<byte[]> ReadAsync(HttpResponseMessage r, long limit, CancellationToken stop)
    {
        if (r.Content.Headers.ContentLength > limit)
        {
            throw new TeamException($"The team server sent more than {limit / (1024 * 1024)} MB.");
        }

        await using Stream s = await r.Content.ReadAsStreamAsync(stop);
        using var m = new MemoryStream();
        byte[] buf = new byte[81920];
        int got;
        while ((got = await s.ReadAsync(buf, stop)) > 0)
        {
            if (m.Length + got > limit)
            {
                throw new TeamException($"The team server sent more than {limit / (1024 * 1024)} MB.");
            }

            m.Write(buf, 0, got);
        }

        return m.ToArray();
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken stop)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        using HttpResponseMessage r = await SendAsync(req, stop);
        await Check(r, stop);
        try
        {
            return JsonDocument.Parse(await ReadAsync(r, MaxReply, stop));
        }
        catch (JsonException)
        {
            throw new TeamException($"{Server.Authority} answered, but not as a team server does.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken stop)
    {
        try
        {
            return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stop);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !stop.IsCancellationRequested)
        {
            throw new TeamException($"Can't reach the team server at {Server.Authority} ({e.Message.TrimEnd('.')}).", unreachable: true);
        }
    }

    // A failure answer's {error} is shown as it is; without one, the status says what went wrong.
    private async Task Check(HttpResponseMessage r, CancellationToken stop)
    {
        if (r.IsSuccessStatusCode)
        {
            return;
        }

        string? said = null;
        try
        {
            using var d = JsonDocument.Parse(await ReadAsync(r, MaxReply, stop));
            said = Text(d.RootElement, "error");
        }
        catch (Exception e) when (e is JsonException or TeamException)
        {
            // No {error}: the status says it.
        }

        throw new TeamException(said ?? r.StatusCode switch
        {
            HttpStatusCode.Forbidden => "The team server says you may not do that (ask the team's owner for a role that can).",
            HttpStatusCode.NotFound => "The team server does not have that (or is not a library service).",
            HttpStatusCode.RequestEntityTooLarge => "The prop is too big for the team server.",
            _ => $"The team server at {Server.Authority} answered {(int)r.StatusCode} {r.ReasonPhrase}.",
        });
    }
}
