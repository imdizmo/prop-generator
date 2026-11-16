using System.Globalization;

namespace PropGenerator.Desktop.Backups;

/// <summary>
/// The cloud backup: each backup is the same zip as Back up to a file, put in the bucket's folder as
/// prop-generator-backup-YYYY-MM-DD-HHMMSS.zip (UTC); the newest few are kept and older ones deleted. Restore takes one
/// of them (the newest, unless told) and merges it as a file restore does. Only PUT, GET, LIST and DELETE are used.
/// </summary>
/// <param name="s3">The storage.</param>
/// <param name="folder">The folder in the bucket.</param>
internal sealed class CloudBackup(S3Client s3, string folder)
{
    private const string Name = "prop-generator-backup-";
    private readonly string _prefix = folder.Trim('/').Length == 0 ? string.Empty : folder.Trim('/') + "/";

    /// <summary>Checks that the keys can write, read, list and delete in the folder (a small file, removed again).</summary>
    /// <returns>Done when the check passed.</returns>
    /// <exception cref="BackupException">It did not: the message says what to check.</exception>
    internal async Task CheckAsync()
    {
        string key = _prefix + ".prop-generator-check";
        byte[] probe = "Prop Generator checks it can write here."u8.ToArray();
        await s3.PutAsync(key, probe);
        byte[] back = await s3.GetAsync(key);
        if (!back.AsSpan().SequenceEqual(probe))
        {
            throw new BackupException("The storage gave back something else than was written. Check the endpoint.");
        }

        await s3.ListAsync(_prefix);
        await s3.DeleteAsync(key);
    }

    /// <summary>Puts a backup in the bucket, then deletes the oldest past <paramref name="keep"/>.</summary>
    /// <param name="zipPath">The backup made on this computer.</param>
    /// <param name="now">When it was made.</param>
    /// <param name="keep">How many backups the bucket keeps (1 to 100).</param>
    /// <returns>The backup's key.</returns>
    internal async Task<string> UploadAsync(string zipPath, DateTimeOffset now, int keep)
    {
        string key = _prefix + Name + now.UtcDateTime.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture) + ".zip";
        await s3.PutFileAsync(key, zipPath);
        foreach (S3Object old in (await ListAsync()).Skip(Math.Clamp(keep, 1, 100)))
        {
            await s3.DeleteAsync(old.Key);
        }

        return key;
    }

    /// <summary>Lists the backups in the folder, the newest first.</summary>
    /// <returns>The backups.</returns>
    internal async Task<IReadOnlyList<S3Object>> ListAsync() =>
        [.. (await s3.ListAsync(_prefix + Name))
            .Where(o => o.Key.EndsWith(".zip", StringComparison.Ordinal) && !o.Key[_prefix.Length..].Contains('/', StringComparison.Ordinal))
            .OrderByDescending(o => o.Key, StringComparer.Ordinal)];

    /// <summary>Fetches a backup into a file.</summary>
    /// <param name="key">The backup's key.</param>
    /// <param name="path">The file.</param>
    /// <returns>Done when it is written.</returns>
    internal Task DownloadAsync(string key, string path) => s3.GetFileAsync(key, path);
}
