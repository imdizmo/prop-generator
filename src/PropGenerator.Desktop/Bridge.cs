namespace PropGenerator.Desktop;

/// <summary>Where Blender and the app swap files: <c>inbox/</c> (Blender to app) and <c>outbox/</c> (app to Blender).</summary>
public static class Bridge
{
    /// <summary>The environment variable that moves the root, so the app and a Blender add-on set to the same folder agree.</summary>
    public const string RootVariable = "PROPGEN_BRIDGE_ROOT";

    /// <summary>The root: PROPGEN_BRIDGE_ROOT if set, else ~/.cache/prop-generator.</summary>
    /// <returns>The folder.</returns>
    public static string DefaultRoot()
    {
        string? set = Environment.GetEnvironmentVariable(RootVariable);
        return !string.IsNullOrWhiteSpace(set)
            ? Path.GetFullPath(set)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "prop-generator");
    }

    /// <summary>The folder Blender writes into.</summary>
    /// <param name="root">The root.</param>
    /// <returns>The inbox folder.</returns>
    public static string Inbox(string root) => Path.Combine(root, "inbox");

    /// <summary>The folder the app writes into.</summary>
    /// <param name="root">The root.</param>
    /// <returns>The outbox folder.</returns>
    public static string Outbox(string root) => Path.Combine(root, "outbox");
}
