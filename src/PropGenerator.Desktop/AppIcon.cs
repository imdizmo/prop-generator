using Avalonia.Media.Imaging;

namespace PropGenerator.Desktop;

/// <summary>The app's icon, built into the app, for its windows.</summary>
internal static class AppIcon
{
    private static readonly Lazy<Bitmap?> Loaded = new(() =>
    {
        using Stream? s = typeof(AppIcon).Assembly.GetManifestResourceStream("icon.png");
        return s is null ? null : new Bitmap(s);
    });

    /// <summary>The icon, or null when the build left it out.</summary>
    /// <returns>The picture.</returns>
    internal static Bitmap? Bitmap() => Loaded.Value;
}
