using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PropGenerator.Desktop.Backups;

/// <summary>
/// AWS Signature Version 4, written by hand (the cloud backup's only signing; no AWS SDK): the canonical request, the
/// string to sign, the signing key and the Authorization header. Checked against AWS's published test suite.
/// </summary>
internal static class SigV4
{
    /// <summary>The algorithm's name, as the header and the string to sign start.</summary>
    internal const string Algorithm = "AWS4-HMAC-SHA256";

    /// <summary>The signing key for a day, a region and a service.</summary>
    /// <param name="secret">The secret key.</param>
    /// <param name="day">The day, as yyyyMMdd.</param>
    /// <param name="region">The region.</param>
    /// <param name="service">The service, such as "s3".</param>
    /// <returns>The key.</returns>
    internal static byte[] SigningKey(string secret, string day, string region, string service)
    {
        byte[] date = Hmac(Encoding.UTF8.GetBytes("AWS4" + secret), day);
        byte[] reg = Hmac(date, region);
        byte[] svc = Hmac(reg, service);
        return Hmac(svc, "aws4_request");
    }

    /// <summary>The canonical request: method, encoded path, sorted query, sorted headers, their names, the payload's hash.</summary>
    /// <param name="r">The request.</param>
    /// <returns>Its canonical form.</returns>
    internal static string CanonicalRequest(SigV4Request r)
    {
        ArgumentNullException.ThrowIfNull(r);
        string query = string.Join(
            '&',
            r.Query.Select(q => (Name: Encode(q.Name), Value: Encode(q.Value)))
                .OrderBy(q => q.Name, StringComparer.Ordinal)
                .ThenBy(q => q.Value, StringComparer.Ordinal)
                .Select(q => q.Name + "=" + q.Value));
        List<(string Name, string Value)> headers = Canonical(r.Headers);
        var sb = new StringBuilder();
        sb.Append(r.Method).Append('\n');
        sb.Append(EncodePath(r.Path)).Append('\n');
        sb.Append(query).Append('\n');
        foreach ((string name, string value) in headers)
        {
            sb.Append(name).Append(':').Append(value).Append('\n');
        }

        sb.Append('\n');
        sb.Append(SignedHeaders(headers)).Append('\n');
        sb.Append(r.PayloadHash);
        return sb.ToString();
    }

    /// <summary>The string to sign: the algorithm, the time, the scope and the canonical request's hash.</summary>
    /// <param name="r">The request.</param>
    /// <param name="when">When it is signed (UTC).</param>
    /// <param name="region">The region.</param>
    /// <param name="service">The service.</param>
    /// <returns>The string.</returns>
    internal static string StringToSign(SigV4Request r, DateTime when, string region, string service)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalRequest(r))));
        return $"{Algorithm}\n{Stamp(when)}\n{Scope(when, region, service)}\n{hash}";
    }

    /// <summary>The Authorization header's value.</summary>
    /// <param name="r">The request.</param>
    /// <param name="accessKey">The access key id.</param>
    /// <param name="secret">The secret key.</param>
    /// <param name="when">When it is signed (UTC).</param>
    /// <param name="region">The region.</param>
    /// <param name="service">The service.</param>
    /// <returns>The value.</returns>
    internal static string Authorization(SigV4Request r, string accessKey, string secret, DateTime when, string region, string service)
    {
        ArgumentNullException.ThrowIfNull(r);
        byte[] key = SigningKey(secret, Day(when), region, service);
        string signature = Convert.ToHexStringLower(Hmac(key, StringToSign(r, when, region, service)));
        return $"{Algorithm} Credential={accessKey}/{Scope(when, region, service)}, SignedHeaders={SignedHeaders(Canonical(r.Headers))}, Signature={signature}";
    }

    /// <summary>The time as X-Amz-Date has it: yyyyMMddTHHmmssZ.</summary>
    /// <param name="when">The time (UTC).</param>
    /// <returns>The stamp.</returns>
    internal static string Stamp(DateTime when) => when.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Encodes a path, each part once, the slashes kept (S3 never normalises a key).</summary>
    /// <param name="path">The path.</param>
    /// <returns>The encoded path.</returns>
    internal static string EncodePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return string.Join('/', path.Split('/').Select(Encode));
    }

    /// <summary>Encodes text as SigV4 wants: every UTF-8 byte but A-Z, a-z, 0-9, '-', '.', '_' and '~' as %XX.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The encoded text.</returns>
    internal static string Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sb = new StringBuilder(text.Length);
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            char c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static string Day(DateTime when) => when.ToUniversalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Scope(DateTime when, string region, string service) => $"{Day(when)}/{region}/{service}/aws4_request";

    // Lowercase names, values trimmed with their inner runs of spaces made one, sorted by name.
    private static List<(string Name, string Value)> Canonical(IReadOnlyList<(string Name, string Value)> headers) =>
        [.. headers
            .Select(h => (Name: h.Name.Trim().ToLowerInvariant(), Value: string.Join(' ', h.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))))
            .OrderBy(h => h.Name, StringComparer.Ordinal)];

    private static string SignedHeaders(List<(string Name, string Value)> headers) => string.Join(';', headers.Select(h => h.Name));

    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
}
