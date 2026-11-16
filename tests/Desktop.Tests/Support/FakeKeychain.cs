using PropGenerator.Desktop.Backups;

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>A keychain in memory: the tests never touch the real one.</summary>
internal sealed class FakeKeychain : IKeychain
{
    /// <summary>Gets or sets the keys kept.</summary>
    public CloudKeys? Keys { get; set; }

    /// <inheritdoc/>
    public Task<CloudKeys?> GetAsync() => Task.FromResult(Keys);

    /// <inheritdoc/>
    public Task SetAsync(CloudKeys keys)
    {
        Keys = keys;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ForgetAsync()
    {
        Keys = null;
        return Task.CompletedTask;
    }
}
