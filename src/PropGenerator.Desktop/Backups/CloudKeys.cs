namespace PropGenerator.Desktop.Backups;

/// <summary>The keys of the cloud storage: kept in the system keychain, never in the prefs.</summary>
/// <param name="AccessKey">The access key id.</param>
/// <param name="SecretKey">The secret key.</param>
internal sealed record CloudKeys(string AccessKey, string SecretKey)
{
    /// <inheritdoc/>
    public override string ToString() => $"CloudKeys {{ AccessKey = {AccessKey}, SecretKey = (hidden) }}";
}
