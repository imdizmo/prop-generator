namespace PropGenerator.Desktop.Team;

/// <summary>A picture attached to a suggestion: its file name and its bytes (PNG or JPEG).</summary>
/// <param name="Name">The name it is sent under, such as "window.png".</param>
/// <param name="Bytes">The picture file.</param>
internal sealed record SuggestionPicture(string Name, byte[] Bytes);

/// <summary>What a teammate writes with Suggestion, and the pictures they attach.</summary>
/// <param name="Text">What they wrote.</param>
/// <param name="Pictures">The attached pictures, in the order they were added.</param>
internal sealed record Suggestion(string Text, IReadOnlyList<SuggestionPicture> Pictures)
{
    /// <summary>The most pictures one suggestion carries.</summary>
    internal const int MaxPictures = 10;

    /// <summary>The largest picture file attached, in bytes.</summary>
    internal const long MaxPictureBytes = 10L * 1024 * 1024;

    /// <summary>The most all pictures together may be, in bytes (the server takes a zip of up to 20 MB).</summary>
    internal const long MaxAllBytes = 19L * 1024 * 1024;

    /// <summary>The longest text, in characters.</summary>
    internal const int MaxText = 20_000;

    /// <summary>Whether a file is a picture that can be attached: a PNG or a JPEG, by its first bytes.</summary>
    /// <param name="bytes">The file.</param>
    /// <returns>True for a PNG or JPEG.</returns>
    internal static bool IsPicture(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
        || bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]);
}
