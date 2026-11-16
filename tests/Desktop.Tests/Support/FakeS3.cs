using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using PropGenerator.Desktop.Backups;

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>
/// An S3-compatible store in memory behind an HttpMessageHandler: no network is ever used. It checks every request's
/// SigV4 signature with the keys it was given (so a request signed with other keys, or with a header changed after
/// signing, is refused as S3 would), and answers PUT, GET, DELETE and ListObjectsV2 for one bucket.
/// </summary>
internal sealed class FakeS3 : HttpMessageHandler
{
    private readonly string _accessKey;
    private readonly string _secret;
    private readonly string _bucket;

    /// <summary>Makes a store for one bucket that accepts one pair of keys.</summary>
    /// <param name="accessKey">The access key it accepts.</param>
    /// <param name="secret">The secret key it accepts.</param>
    /// <param name="bucket">Its one bucket.</param>
    public FakeS3(string accessKey, string secret, string bucket)
    {
        _accessKey = accessKey;
        _secret = secret;
        _bucket = bucket;
    }

    /// <summary>Gets the objects, by key.</summary>
    public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets each request as "METHOD /path?query".</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Gets or sets the region requests must be signed for.</summary>
    public string Region { get; set; } = "auto";

    /// <summary>Gets or sets a value indicating whether the network is down (every request throws).</summary>
    public bool Unreachable { get; set; }

    /// <summary>Gets or sets how many keys a list answers at most before it continues (to test continuing).</summary>
    public int PageSize { get; set; } = 1000;

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Unreachable)
        {
            throw new HttpRequestException("Name or service not known");
        }

        Uri uri = request.RequestUri!;
        Requests.Add($"{request.Method} {uri.AbsolutePath}{uri.Query}");
        byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!SignedRight(request, body))
        {
            return Error(HttpStatusCode.Forbidden, "SignatureDoesNotMatch");
        }

        string[] parts = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Split('/', 2);
        if (parts[0] != _bucket)
        {
            return Error(HttpStatusCode.NotFound, "NoSuchBucket");
        }

        string key = parts.Length > 1 ? parts[1] : string.Empty;
        if (request.Method == HttpMethod.Put)
        {
            Objects[key] = body;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        if (request.Method == HttpMethod.Delete)
        {
            Objects.Remove(key);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (key.Length == 0)
        {
            var q = HttpUtility.ParseQueryString(uri.Query);
            return List(q["prefix"] ?? string.Empty, q["continuation-token"]);
        }

        return Objects.TryGetValue(key, out byte[]? found)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(found) }
            : Error(HttpStatusCode.NotFound, "NoSuchKey");
    }

    private static HttpResponseMessage Error(HttpStatusCode code, string what) => new(code)
    {
        Content = new StringContent($"<?xml version=\"1.0\"?><Error><Code>{what}</Code><Message>{what}</Message></Error>"),
    };

    private HttpResponseMessage List(string prefix, string? after)
    {
        string[] keys = [.. Objects.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            .Where(k => after is null || string.CompareOrdinal(k, after) > 0)];
        string[] page = [.. keys.Take(PageSize)];
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">");
        foreach (string k in page)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<Contents><Key>{k}</Key><LastModified>2026-11-12T09:00:00.000Z</LastModified><Size>{Objects[k].Length}</Size></Contents>");
        }

        bool more = keys.Length > page.Length;
        sb.Append(CultureInfo.InvariantCulture, $"<IsTruncated>{(more ? "true" : "false")}</IsTruncated>");
        if (more)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<NextContinuationToken>{page[^1]}</NextContinuationToken>");
        }

        sb.Append("</ListBucketResult>");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sb.ToString()) };
    }

    // The signature is made again here from what arrived, with the keys this store accepts.
    private bool SignedRight(HttpRequestMessage request, byte[] body)
    {
        string? auth = request.Headers.TryGetValues("Authorization", out var a) ? string.Join(",", a) : null;
        string? date = request.Headers.TryGetValues("x-amz-date", out var d) ? d.Single() : null;
        string? hash = request.Headers.TryGetValues("x-amz-content-sha256", out var h) ? h.Single() : null;
        if (auth is null || date is null || hash != Convert.ToHexStringLower(SHA256.HashData(body)))
        {
            return false;
        }

        Uri uri = request.RequestUri!;
        var query = HttpUtility.ParseQueryString(uri.Query);
        (string, string)[] pairs = [.. query.AllKeys.Select(k => (k!, query[k] ?? string.Empty))];
        var signed = new SigV4Request(
            request.Method.Method, Uri.UnescapeDataString(uri.AbsolutePath), pairs,
            [("host", uri.Authority), ("x-amz-content-sha256", hash), ("x-amz-date", date)], hash);
        var when = DateTime.ParseExact(date, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        return auth == SigV4.Authorization(signed, _accessKey, _secret, when, Region, "s3");
    }
}
