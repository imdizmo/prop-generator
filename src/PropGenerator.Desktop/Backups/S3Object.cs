namespace PropGenerator.Desktop.Backups;

/// <summary>One object a bucket listed.</summary>
/// <param name="Key">Its key.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="LastModified">When it was written.</param>
internal sealed record S3Object(string Key, long Size, DateTimeOffset LastModified);
