namespace PropGenerator.Desktop;

/// <summary>
/// Save a picture (File, Save a picture…): the 3D view as it is, at its size, as a PNG named after the piece (easy-props'
/// rule: marks a file name cannot have and control characters become spaces, leading dots go, "Prop picture" when
/// nothing is left).
/// </summary>
internal static class PictureFile
{
    /// <summary>A piece's name made safe as a file name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The safe name, without an extension.</returns>
    internal static string SafeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        char[] chars = [.. name.Select(c => c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(c) ? ' ' : c)];
        string safe = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimStart('.').Trim();
        return safe.Length == 0 ? "Prop picture" : safe;
    }

    /// <summary>Asks where, takes the picture and writes it; every outcome is said on the status line.</summary>
    /// <param name="w">The window.</param>
    /// <returns>Done when written or the problem is said.</returns>
    internal static async Task SaveAsync(MainWindow w)
    {
        ArgumentNullException.ThrowIfNull(w);
        string name = SafeName(Path.GetFileNameWithoutExtension(w.Files.Name)) + ".png";
        string? path = await w.Files.Dialogs.AskSaveFileAsync("Save a picture", name, "png", w.Files.Prefs.SaveFolder);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        (byte[] Rgba, int Width, int Height) shot;
        try
        {
            shot = await (w.Files.Capture ?? throw new InvalidOperationException("there is no view"))();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            w.Say($"The picture couldn't be drawn ({e.Message}). Try again with the view showing.", error: true);
            return;
        }

        try
        {
            byte[] png = await Task.Run(() => PngEncoder.Encode(shot.Rgba, shot.Width, shot.Height));
            await File.WriteAllBytesAsync(path, png);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            w.Say($"The picture couldn't be saved. Check the folder can be written to. ({e.Message})", error: true);
            return;
        }

        w.Say($"Saved {Path.GetFileName(path)}");
    }
}
