using System.IO.Compression;

namespace PropGenerator.Desktop.Team;

/// <summary>
/// A prop as the team's library keeps it: one zip with <c>prop.propgen</c> (the work file) and, when there is one,
/// <c>picture.png</c> (its card picture). Anything else in a zip is ignored.
/// </summary>
internal static class TeamPack
{
    /// <summary>The work file's name in the zip.</summary>
    internal const string WorkEntry = "prop.propgen";

    /// <summary>The picture's name in the zip.</summary>
    internal const string PictureEntry = "picture.png";

    /// <summary>The largest picture read: a card picture is a few hundred KB.</summary>
    internal const long MaxPicture = 16L * 1024 * 1024;

    /// <summary>The largest pack taken: a work file at its limit plus a picture at its own.</summary>
    internal const long MaxPack = WorkFile.MaxBytes + MaxPicture + (1024 * 1024);

    /// <summary>Packs a prop.</summary>
    /// <param name="work">The work file's bytes.</param>
    /// <param name="picture">Its picture (PNG), or null.</param>
    /// <returns>The zip.</returns>
    internal static byte[] Make(byte[] work, byte[]? picture)
    {
        using var m = new MemoryStream();
        using (var zip = new ZipArchive(m, ZipArchiveMode.Create, leaveOpen: true))
        {
            Put(zip, WorkEntry, work, CompressionLevel.Optimal);
            if (picture is not null)
            {
                Put(zip, PictureEntry, picture, CompressionLevel.NoCompression);
            }
        }

        return m.ToArray();
    }

    /// <summary>Unpacks a prop, refusing a zip without a work file or with parts over their limits.</summary>
    /// <param name="pack">The zip.</param>
    /// <returns>The work file's bytes and the picture, or null for none.</returns>
    internal static (byte[] Work, byte[]? Picture) Read(byte[] pack)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(pack), ZipArchiveMode.Read);
            ZipArchiveEntry work = zip.GetEntry(WorkEntry) ?? throw new TeamException("This is not a Prop Generator prop (it has no work file).");
            byte[]? picture = zip.GetEntry(PictureEntry) is { } p ? Bytes(p, MaxPicture) : null;
            return (Bytes(work, WorkFile.MaxBytes), picture);
        }
        catch (InvalidDataException e)
        {
            throw new TeamException($"The prop could not be unpacked: {e.Message}");
        }
    }

    private static void Put(ZipArchive zip, string name, byte[] bytes, CompressionLevel level)
    {
        using Stream s = zip.CreateEntry(name, level).Open();
        s.Write(bytes);
    }

    // Reads at most limit bytes, whatever the entry claims its size is.
    private static byte[] Bytes(ZipArchiveEntry e, long limit)
    {
        using Stream s = e.Open();
        using var m = new MemoryStream();
        byte[] buf = new byte[81920];
        int got;
        while ((got = s.Read(buf)) > 0)
        {
            if (m.Length + got > limit)
            {
                throw new TeamException($"The prop's {e.Name} is larger than {limit / (1024 * 1024)} MB: it was not taken.");
            }

            m.Write(buf, 0, got);
        }

        return m.ToArray();
    }
}
