using System.Globalization;
using System.Text.RegularExpressions;

namespace PropGenerator.Desktop.Backups;

/// <summary>
/// The daily folder backup, as easy-props' (Settings, Backup): at most one backup a day into a folder the user chose
/// (a second drive, a synced folder), named prop-generator-backup-YYYY-MM-DD.zip, the oldest of those let go past the
/// number kept. Nothing else in the folder is touched.
/// </summary>
internal static partial class FolderBackup
{
    /// <summary>The backup's file name for a day.</summary>
    /// <param name="day">The day, in the user's own time.</param>
    /// <returns>The file name.</returns>
    internal static string FileName(DateTimeOffset day) =>
        "prop-generator-backup-" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".zip";

    /// <summary>Makes today's backup if there is none yet, then lets the oldest go past <paramref name="keep"/>.</summary>
    /// <param name="folder">The backup folder.</param>
    /// <param name="keep">How many daily backups to keep (1 to 100).</param>
    /// <param name="now">The time now, in the user's own time.</param>
    /// <param name="make">Writes the backup to the given path.</param>
    /// <returns>The backup made, or null when today's was already there.</returns>
    /// <exception cref="DirectoryNotFoundException">The folder is not there (a drive not plugged in).</exception>
    internal static string? RunIfDue(string folder, int keep, DateTimeOffset now, Action<string> make)
    {
        ArgumentNullException.ThrowIfNull(make);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(
                $"Couldn't back up to {folder}: the folder isn't there. Plug the drive in, or choose another folder (File, Backup folder…).");
        }

        string path = Path.Combine(folder, FileName(now));
        if (File.Exists(path))
        {
            return null;
        }

        make(path);
        foreach (string old in Directory.GetFiles(folder)
            .Where(f => Daily().IsMatch(Path.GetFileName(f)))
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            .Skip(Math.Clamp(keep, 1, 100)))
        {
            File.Delete(old);
        }

        return path;
    }

    [GeneratedRegex(@"^prop-generator-backup-\d{4}-\d{2}-\d{2}\.zip$")]
    private static partial Regex Daily();
}
