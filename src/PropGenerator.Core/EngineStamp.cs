namespace PropGenerator;

/// <summary>
/// The engine a work file's prop was made with (easy-props' release), written at the top of the file for support and
/// for later versions; nothing reads it to decide anything yet.
/// </summary>
/// <param name="Tag">The release's tag, such as "v0.4.5".</param>
/// <param name="Commit">Its commit.</param>
public sealed record EngineStamp(string Tag, string Commit);
