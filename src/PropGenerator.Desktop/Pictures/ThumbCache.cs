using System.Security.Cryptography;
using System.Text;

namespace PropGenerator.Desktop.Pictures;

/// <summary>
/// A folder of small pictures kept between runs, one PNG per key, never more than a set number of files: when there
/// are too many the oldest go. A problem with the disk only ever means "no picture", it never throws.
/// </summary>
/// <param name="dir">The folder; made when the first picture is put.</param>
/// <param name="cap">The most files to keep.</param>
internal sealed class ThumbCache(string dir, int cap = 400)
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Where the app keeps its pictures: <c>~/.cache/prop-generator/thumbs</c>.</summary>
    /// <returns>The folder's path.</returns>
    public static string DefaultDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "prop-generator", "thumbs");

    /// <summary>The key of a work file's picture: the view as it was when the file was last saved.</summary>
    /// <param name="fullPath">The work file's full path.</param>
    /// <returns><c>file:</c> and the path.</returns>
    public static string FileKey(string fullPath) => "file:" + fullPath;

    /// <summary>
    /// The key of a starting point's picture drawn by our painter, for any recipe. The recipe and its generator's
    /// version are part of it, so a newer generator draws its pictures again; for an easy-props kind so is the
    /// engine's tag, so a newer bundle does too.
    /// </summary>
    /// <param name="presetId">The starting point's id, such as "long-cliff".</param>
    /// <param name="recipe">The recipe it makes, such as "slab-cliff".</param>
    /// <param name="version">The recipe's generator version.</param>
    /// <param name="engineTag">The engine bundle's tag and commit for an easy-props kind, or null for one of our own.</param>
    /// <returns><c>preset:</c>, the id, the recipe and the generator's version, then the tag when given.</returns>
    public static string PresetKey(string presetId, string recipe, int version, string? engineTag = null) =>
        engineTag is null
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"preset:{presetId}:{recipe}:v{version}")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"preset:{presetId}:{recipe}:v{version}:{engineTag}");

    /// <summary>Reads a picture.</summary>
    /// <param name="key">What the picture is of.</param>
    /// <returns>The PNG's bytes, or null when it is missing, unreadable or not a PNG.</returns>
    public byte[]? Get(string key)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(PathFor(key));
            return bytes.Length > Signature.Length && bytes.AsSpan(0, Signature.Length).SequenceEqual(Signature) ? bytes : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Stores a picture, then deletes the oldest files beyond the cap.</summary>
    /// <param name="key">What the picture is of.</param>
    /// <param name="png">The PNG's bytes.</param>
    public void Put(string key, byte[] png)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string target = PathFor(key);
            string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, png);
            File.Move(temp, target, overwrite: true);
            Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // No picture kept; the caller draws it again next time.
        }
    }

    private string PathFor(string key) =>
        Path.Combine(dir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() + ".png");

    private void Trim()
    {
        FileInfo[] files = [.. new DirectoryInfo(dir).GetFiles("*.png").OrderBy(f => f.LastWriteTimeUtc)];
        for (int i = 0; i < files.Length - cap; i++)
        {
            try
            {
                files[i].Delete();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Someone else has it; it goes next time.
            }
        }
    }
}
