namespace PropGenerator.Desktop.Backups;

/// <summary>Where the cloud backup's keys are kept: the system keychain in the app, a fake in the tests. Never the prefs.</summary>
internal interface IKeychain
{
    /// <summary>Reads the keys.</summary>
    /// <returns>The keys, or null when none are kept.</returns>
    Task<CloudKeys?> GetAsync();

    /// <summary>Keeps the keys, in place of any kept before.</summary>
    /// <param name="keys">The keys.</param>
    /// <returns>Done when they are kept.</returns>
    /// <exception cref="Backups.BackupException">The keychain could not keep them.</exception>
    Task SetAsync(CloudKeys keys);

    /// <summary>Forgets the keys.</summary>
    /// <returns>Done when they are gone.</returns>
    Task ForgetAsync();
}
