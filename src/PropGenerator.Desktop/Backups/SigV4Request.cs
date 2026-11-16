namespace PropGenerator.Desktop.Backups;

/// <summary>One request as Signature Version 4 signs it: nothing in it is encoded yet.</summary>
/// <param name="Method">The HTTP method, such as "GET".</param>
/// <param name="Path">The path, not encoded, starting with "/" (for S3, "/bucket/key" or "/key").</param>
/// <param name="Query">The query's names and values, not encoded, in any order.</param>
/// <param name="Headers">The headers to sign, Host and X-Amz-Date among them.</param>
/// <param name="PayloadHash">The body's SHA-256, as lowercase hex.</param>
internal sealed record SigV4Request(
    string Method,
    string Path,
    IReadOnlyList<(string Name, string Value)> Query,
    IReadOnlyList<(string Name, string Value)> Headers,
    string PayloadHash);
