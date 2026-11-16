namespace PropGenerator.Desktop.Backups;

/// <summary>
/// Where cloud backups go: the address, the region, the bucket and the folder in it. Every service but Amazon S3 is
/// addressed path-style (https://host/bucket/key); Amazon S3 by the bucket's own host. http only for this computer.
/// </summary>
/// <param name="Host">The host, with a port if any, lower case.</param>
/// <param name="Region">The region signed for.</param>
/// <param name="Bucket">The bucket.</param>
/// <param name="Folder">The folder in the bucket, without slashes at its ends.</param>
/// <param name="PathStyle">True for https://host/bucket/key, false for https://bucket.host/key (Amazon S3).</param>
internal sealed record CloudTarget(string Host, string Region, string Bucket, string Folder, bool PathStyle)
{
    /// <summary>Makes the target from the setup's fields, tidying the address (no scheme or path, lower case).</summary>
    /// <param name="service">The service's id.</param>
    /// <param name="endpoint">The address as typed; null or blank for Amazon S3's own.</param>
    /// <param name="region">The region; null or blank for the service's usual one.</param>
    /// <param name="bucket">The bucket.</param>
    /// <param name="folder">The folder in the bucket.</param>
    /// <returns>The target.</returns>
    /// <exception cref="BackupException">No address is typed, or no web address can be made of it.</exception>
    internal static CloudTarget From(string? service, string? endpoint, string? region, string bucket, string folder)
    {
        ArgumentNullException.ThrowIfNull(bucket);
        ArgumentNullException.ThrowIfNull(folder);
        CloudService s = CloudServices.Find(service);
        string reg = string.IsNullOrWhiteSpace(region) ? s.Region : region.Trim();
        string host = Tidy(endpoint);
        bool aws = s.Id == "aws";
        if (host.Length == 0)
        {
            host = aws ? $"s3.{reg}.amazonaws.com" : throw new BackupException("Type the storage's address (its endpoint) first.");
        }

        var target = new CloudTarget(host, reg, bucket.Trim(), folder.Trim().Trim('/'), PathStyle: !aws);
        try
        {
            // An address (or, on Amazon S3, a bucket) no web address can be made of is said here, not sent.
            _ = target.UriFor(string.Empty);
        }
        catch (UriFormatException e)
        {
            throw new BackupException($"Prop Generator can't make a web address of \"{target.Host}\" and the bucket {target.Bucket}. Check the address (such as {s.EndpointHint ?? "s3.<region>.amazonaws.com"}) and the bucket's name.", e);
        }

        return target;
    }

    /// <summary>An address as typed, tidied: the scheme and any path taken off, lower case.</summary>
    /// <param name="endpoint">The address.</param>
    /// <returns>The host (with its port), or empty.</returns>
    internal static string Tidy(string? endpoint)
    {
        string e = (endpoint ?? string.Empty).Trim().ToLowerInvariant();
        int scheme = e.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            e = e[(scheme + 3)..];
        }

        int slash = e.IndexOf('/', StringComparison.Ordinal);
        return slash >= 0 ? e[..slash] : e;
    }

    /// <summary>The address of an object (or of the bucket, for an empty key).</summary>
    /// <param name="key">The object's key, not encoded.</param>
    /// <returns>The address.</returns>
    internal Uri UriFor(string key)
    {
        string bare = Host.Split(':')[0];
        string scheme = bare is "localhost" or "127.0.0.1" or "[::1]" ? "http" : "https";
        string path = PathStyle ? $"/{Bucket}/{key}" : $"/{key}";
        string host = PathStyle ? Host : $"{Bucket}.{Host}";
        return new Uri($"{scheme}://{host}{SigV4.EncodePath(path)}");
    }

    /// <summary>The path signed for an object: the same as in <see cref="UriFor"/>, not encoded.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The path.</returns>
    internal string PathFor(string key) => PathStyle ? $"/{Bucket}/{key}" : $"/{key}";
}
