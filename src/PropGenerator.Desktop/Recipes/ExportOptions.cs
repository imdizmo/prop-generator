// Picture sizes and their help from easy-props frontend/src/core/export/pipeline/types.ts and
// export-preferences.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Export;

namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// How a prop is exported (decision E6): the look the game draws it in, how large the pictures of the closest level
/// are (bark, stone and cut ends), and the set it belongs to, if any. Our own kinds use the look alone.
/// </summary>
/// <param name="Look">How the game will draw it: one of <see cref="ExportLook.All"/>.</param>
/// <param name="PictureSize">How many pixels across the closest level's pictures are: one of <see cref="PictureSizes"/>.</param>
/// <param name="SetName">The set's export name (upstream's <c>exportNameOf</c>), or null for a prop on its own.</param>
internal sealed record ExportOptions(string Look, int PictureSize = ExportOptions.DefaultPictureSize, string? SetName = null)
{
    /// <summary>The picture size until another is chosen (upstream's default).</summary>
    public const int DefaultPictureSize = 512;

    /// <summary>What the picture size does, in upstream's words (<c>PICTURE_SIZE_HELP</c>).</summary>
    public const string PictureSizeHelp =
        "How sharp bark, stone and cut ends are up close. Larger pictures make larger files. Only the closest level uses them.";

    /// <summary>Gets the lit look at the usual picture size, outside any set.</summary>
    public static ExportOptions Default { get; } = new(ExportLook.Lit);

    /// <summary>Gets the picture sizes offered, smallest first (upstream's <c>PICTURE_SIZES</c>).</summary>
    public static IReadOnlyList<int> PictureSizes { get; } = [512, 1024, 2048];

    /// <summary>A picture size as read from anywhere: one of <see cref="PictureSizes"/>, anything else the default.</summary>
    /// <param name="size">The size read.</param>
    /// <returns>The size kept.</returns>
    public static int KeptPictureSize(int size) => PictureSizes.Contains(size) ? size : DefaultPictureSize;
}
