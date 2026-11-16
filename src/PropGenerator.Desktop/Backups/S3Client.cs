using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PropGenerator.Desktop.Backups;

/// <summary>
/// The four S3 calls the cloud backup needs (PUT, GET, LIST and DELETE), over HttpClient, each signed with SigV4 by
/// hand: no AWS SDK. Every failure becomes a <see cref="BackupException"/> saying in plain words what to check.
/// </summary>
internal sealed class S3Client : IDisposable
{
    /// <summary>
    /// The most bytes a backup download may have: Restore refuses props over 4 GB in all anyway, so a real backup is
    /// far below this; a storage sending more (or sending for ever) is stopped here, before it fills the disk.
    /// </summary>
    internal const long MaxDownload = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// The most bytes of an answer read into memory (the check's small file, a list page, an error): a list page of
    /// 1000 keys is about 300 KB, so this is far above any real answer.
    /// </summary>
    internal const int MaxReply = 4 * 1024 * 1024;

    /// <summary>
    /// How many seconds the storage may send nothing while an answer's body is read: the client's own timeout covers
    /// only the wait for the headers, and a real storage never pauses this long mid-body.
    /// </summary>
    internal const int IdleSeconds = 60;

    /// <summary>
    /// The most list pages one listing reads: at 1000 keys a page, 200 000 objects, far more than a backup folder
    /// (which keeps at most 100 backups) holds, and few enough to keep in memory.
    /// </summary>
    internal const int MaxListPages = 200;

    private readonly HttpClient _http;
    private readonly CloudTarget _target;
    private readonly CloudKeys _keys;
    private readonly Func<DateTime> _now;

    /// <summary>Makes a client.</summary>
    /// <param name="handler">What sends the requests (the system's in the app, a store in memory in the tests); not disposed here.</param>
    /// <param name="target">Where the backups go.</param>
    /// <param name="keys">The storage's keys.</param>
    /// <param name="now">The clock, in UTC (the tests may give theirs).</param>
    internal S3Client(HttpMessageHandler handler, CloudTarget target, CloudKeys keys, Func<DateTime>? now = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(60) };
        _target = target;
        _keys = keys;
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>Gets how long the storage may send nothing mid-body (the tests' shorter one).</summary>
    internal TimeSpan Idle { get; init; } = TimeSpan.FromSeconds(IdleSeconds);

    /// <summary>Gets the most bytes a download may have (the tests' smaller one).</summary>
    internal long DownloadLimit { get; init; } = MaxDownload;

    /// <summary>Writes an object.</summary>
    /// <param name="key">Its key.</param>
    /// <param name="body">Its bytes.</param>
    /// <returns>Done when it is written.</returns>
    internal async Task PutAsync(string key, byte[] body)
    {
        using HttpResponseMessage r = await SendAsync(HttpMethod.Put, key, [], body);
    }

    /// <summary>Writes a file as an object, read as it is sent (its hash is taken first).</summary>
    /// <param name="key">Its key.</param>
    /// <param name="path">The file.</param>
    /// <returns>Done when it is written.</returns>
    internal async Task PutFileAsync(string key, string path)
    {
        string hash;
        await using (FileStream s = File.OpenRead(path))
        {
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(s));
        }

        await using FileStream body = File.OpenRead(path);
        using HttpResponseMessage r = await SendAsync(HttpMethod.Put, key, [], new StreamContent(body), hash);
    }

    /// <summary>Reads an object.</summary>
    /// <param name="key">Its key.</param>
    /// <returns>Its bytes.</returns>
    internal async Task<byte[]> GetAsync(string key)
    {
        using HttpResponseMessage r = await SendAsync(HttpMethod.Get, key, [], null);
        return await ReadAllAsync(r, MaxReply);
    }

    /// <summary>Reads an object into a file (written beside it first, then moved into place).</summary>
    /// <param name="key">Its key.</param>
    /// <param name="path">The file.</param>
    /// <returns>Done when it is written.</returns>
    internal async Task GetFileAsync(string key, string path)
    {
        using HttpResponseMessage r = await SendAsync(HttpMethod.Get, key, [], null);
        string temp = path + ".tmp";
        try
        {
            await using (FileStream to = File.Create(temp))
            {
                await CopyAsync(r, to, DownloadLimit);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>Lists the objects whose keys start with a prefix (ListObjectsV2, page after page).</summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The objects, in key order.</returns>
    internal async Task<IReadOnlyList<S3Object>> ListAsync(string prefix)
    {
        var all = new List<S3Object>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        int pages = 0;
        do
        {
            if (++pages > MaxListPages)
            {
                throw new BackupException(string.Create(CultureInfo.InvariantCulture, $"The storage's list goes on past {MaxListPages} pages. Check the endpoint, or give the backups a folder of their own."));
            }

            List<(string, string)> query = [("list-type", "2"), ("prefix", prefix)];
            if (token is not null)
            {
                query.Add(("continuation-token", token));
            }

            using HttpResponseMessage r = await SendAsync(HttpMethod.Get, string.Empty, query, null);
            using var page = new MemoryStream(await ReadAllAsync(r, MaxReply));
            XDocument doc;
            try
            {
                doc = XDocument.Load(page);
            }
            catch (XmlException e)
            {
                throw new BackupException("The storage's list could not be read. Check the endpoint.", e);
            }

            XElement root = doc.Root ?? throw new BackupException("The storage's list could not be read.");
            foreach (XElement c in root.Elements().Where(e => e.Name.LocalName == "Contents"))
            {
                all.Add(new S3Object(
                    Child(c, "Key") ?? string.Empty,
                    long.TryParse(Child(c, "Size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long size) ? size : 0,
                    DateTimeOffset.TryParse(Child(c, "LastModified"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at) ? at : default));
            }

            token = Child(root, "IsTruncated") == "true" ? Child(root, "NextContinuationToken") : null;
            if (token is not null && !seen.Add(token))
            {
                throw new BackupException("The storage's list repeats itself. Check the endpoint.");
            }
        }
        while (token is not null);

        return all;
    }

    /// <summary>Removes an object.</summary>
    /// <param name="key">Its key.</param>
    /// <returns>Done when it is removed.</returns>
    internal async Task DeleteAsync(string key)
    {
        using HttpResponseMessage r = await SendAsync(HttpMethod.Delete, key, [], null);
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();

    private static string? Child(XElement e, string name) => e.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value;

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string key, List<(string Name, string Value)> query, byte[]? body) =>
        SendAsync(method, key, query, body is null ? null : new ByteArrayContent(body), Convert.ToHexStringLower(SHA256.HashData(body ?? [])));

    // Signs and sends one request; anything but success is said as what to check.
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string key, List<(string Name, string Value)> query, HttpContent? content, string hash)
    {
        Uri baseUri = _target.UriFor(key);
        string q = string.Join('&', query.Select(p => SigV4.Encode(p.Name) + "=" + SigV4.Encode(p.Value)));
        var uri = new Uri(q.Length == 0 ? baseUri.AbsoluteUri : baseUri.AbsoluteUri + "?" + q);
        DateTime when = _now();
        string stamp = SigV4.Stamp(when);
        var signed = new SigV4Request(
            method.Method, _target.PathFor(key), query,
            [("host", uri.Authority), ("x-amz-content-sha256", hash), ("x-amz-date", stamp)], hash);
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.TryAddWithoutValidation("x-amz-date", stamp);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", hash);
        request.Headers.TryAddWithoutValidation("Authorization", SigV4.Authorization(signed, _keys.AccessKey, _keys.SecretKey, when, _target.Region, "s3"));
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new BackupException($"Prop Generator couldn't reach {_target.Host}. Check the address and your connection. ({e.Message})", e);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            string text;
            try
            {
                text = Encoding.UTF8.GetString(await ReadAllAsync(response, MaxReply));
            }
            catch (BackupException)
            {
                // An error whose body is too long or never ends is said by its status alone.
                text = string.Empty;
            }

            throw new BackupException(Explain(response.StatusCode, text));
        }
    }

    // An answer's body in memory, at most so many bytes.
    private async Task<byte[]> ReadAllAsync(HttpResponseMessage response, long limit)
    {
        using var m = new MemoryStream();
        await CopyAsync(response, m, limit);
        return m.ToArray();
    }

    // Copies an answer's body, at most so many bytes, waiting at most Idle for each piece of it: the client's timeout
    // covers only the headers.
    private async Task CopyAsync(HttpResponseMessage response, Stream to, long limit)
    {
        string tooMuch = $"The storage at {_target.Host} sent more than Prop Generator expects. Check the endpoint.";
        if (response.Content.Headers.ContentLength > limit)
        {
            throw new BackupException(tooMuch);
        }

        await using Stream from = await ReadPieceAsync(response.Content.ReadAsStreamAsync);
        byte[] buffer = new byte[81920];
        long total = 0;
        int n;
        while ((n = await ReadPieceAsync(token => from.ReadAsync(buffer, token).AsTask())) > 0)
        {
            total += n;
            if (total > limit)
            {
                throw new BackupException(tooMuch);
            }

            await to.WriteAsync(buffer.AsMemory(0, n));
        }
    }

    // One read from the storage, given up after Idle; a failure is said as what to check.
    private async Task<T> ReadPieceAsync<T>(Func<CancellationToken, Task<T>> read)
    {
        using var idle = new CancellationTokenSource(Idle);
        try
        {
            return await read(idle.Token);
        }
        catch (OperationCanceledException e)
        {
            throw new BackupException($"The storage at {_target.Host} stopped answering. Try again later, or check your connection.", e);
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            throw new BackupException($"The connection to {_target.Host} broke off. Try again later, or check your connection. ({e.Message})", e);
        }
    }

    private string Explain(HttpStatusCode code, string body)
    {
        string what = body.Contains("<Code>", StringComparison.Ordinal)
            ? body[(body.IndexOf("<Code>", StringComparison.Ordinal) + 6)..].Split('<')[0]
            : string.Empty;
        return (code, what) switch
        {
            (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized, "AccessDenied") =>
                "The storage didn't accept these keys for this. If they can read but not write, give them write access too.",
            (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized, _) =>
                "The storage didn't accept these keys. Check the access key and the secret key, and the region.",
            (HttpStatusCode.NotFound, "NoSuchBucket") =>
                $"There's no bucket called {_target.Bucket} at this address. Make it first on the storage's own page, or check its name.",
            (HttpStatusCode.NotFound, _) => "That backup is no longer in the bucket.",
            (HttpStatusCode.MovedPermanently or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect, _) =>
                "That address sends Prop Generator somewhere else. Check the endpoint and the region.",
            _ => string.Create(CultureInfo.InvariantCulture, $"The storage answered {(int)code} {what}. Try again later, or check the setup."),
        };
    }
}
