using System.Globalization;

namespace PropGenerator.Desktop.Backups;

/// <summary>
/// What the backup commands do in the window: back up every prop to a file and restore one, the daily folder backup
/// (when a folder is chosen: at start and after saving, at most once a day; written off the window's thread), and the S3-compatible cloud backup with
/// its keys in the system keychain. Every outcome is said on the status line.
/// </summary>
internal sealed class BackupActions : IDisposable
{
    /// <summary>The app's own lists kept in a backup beside the props, by file name in the config folder.</summary>
    internal static readonly IReadOnlyList<string> ExtraFiles = ["colours.json", "presets.json"];

    private readonly Library _library;
    private readonly Prefs _prefs;
    private readonly IDialogs _dialogs;
    private readonly string _config;
    private readonly Action<string, bool> _say;
    private HttpMessageHandler? _ownHttp;
    private readonly SemaphoreSlim _turn = new(1, 1);
    private bool _toldFolderMissing;

    /// <summary>Makes the backup actions of a window.</summary>
    /// <param name="library">The props.</param>
    /// <param name="prefs">Where the backup folder and the cloud's place (never its keys) are kept.</param>
    /// <param name="dialogs">Asks the user.</param>
    /// <param name="configFolder">The app's config folder (the lists kept with a backup, and the work files of a transfer).</param>
    /// <param name="say">Shows a line on the status bar; true for a failure.</param>
    internal BackupActions(Library library, Prefs prefs, IDialogs dialogs, string configFolder, Action<string, bool> say)
    {
        _library = library;
        _prefs = prefs;
        _dialogs = dialogs;
        _config = configFolder;
        _say = say;
    }

    /// <summary>Gets or sets where the cloud's keys are kept (the system keychain until the tests give theirs).</summary>
    internal IKeychain Keychain { get; set; } = new SecretToolKeychain();

    /// <summary>Gets or sets what sends the cloud's requests (the system's until the tests give theirs).</summary>
    internal HttpMessageHandler Http
    {
        get => _ownHttp ??= new SocketsHttpHandler { AllowAutoRedirect = false };
        set => _ownHttp = value;
    }

    /// <summary>Gets or sets the clock.</summary>
    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;

    /// <summary>Gets or sets what merges a backup's lists into the app's own (colours, palettes, My presets).</summary>
    internal Action<IReadOnlyDictionary<string, byte[]>>? MergeExtras { get; set; }

    /// <summary>Gets a value indicating whether the cloud backup is set up.</summary>
    internal bool CloudOn => _prefs.CloudService is not null && _prefs.CloudBucket is not null;

    /// <summary>Asks where, then backs up every prop to one zip.</summary>
    /// <returns>Done when it is written or the problem is said.</returns>
    internal async Task BackUpToFileAsync()
    {
        string? path = await _dialogs.AskSaveFileAsync("Back up your props", FolderBackup.FileName(Now()), "zip", _prefs.SaveFolder);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        int n = await InTurnAsync(() => Write(path));
        _say($"Backed up {Props(n)} to {path}.", false);
    }

    /// <summary>Asks which backup, then restores it, merging (nothing here is replaced).</summary>
    /// <returns>Done when it is restored or the problem is said.</returns>
    internal async Task RestoreFromFileAsync()
    {
        string? path = await _dialogs.AskOpenFileAsync("Restore from a backup", "zip", _prefs.SaveFolder);
        if (path is not null)
        {
            await RestoreAsync(path);
        }
    }

    /// <summary>Asks for the daily backup's folder, then backs up into it at once.</summary>
    /// <returns>Done when it is chosen (or not).</returns>
    internal async Task ChooseFolderAsync()
    {
        string? folder = await _dialogs.AskFolderAsync("Choose the folder for the daily backup", _prefs.BackupFolder);
        if (folder is null)
        {
            return;
        }

        _prefs.BackupFolder = folder;
        _prefs.Save();
        _toldFolderMissing = false;
        _say(await RunDailyAsync() is string made ? $"Daily backup on: backed up to {made}." : $"Daily backup on, into {folder}.", false);
    }

    /// <summary>Turns the daily folder backup off (the backups already there stay).</summary>
    internal void StopFolder()
    {
        _prefs.BackupFolder = null;
        _prefs.Save();
        _say("The daily backup is off. The backups already in the folder stay.", false);
    }

    /// <summary>
    /// Makes today's folder backup if a folder is chosen and there is none yet today: the zip is written off the
    /// window's thread, after any backup already being written; what happened is said back on the caller's thread.
    /// </summary>
    /// <returns>The backup made, or null.</returns>
    internal async Task<string?> RunDailyAsync()
    {
        if (_prefs.BackupFolder is not { } folder)
        {
            return null;
        }

        int keep = _prefs.BackupKeep;
        DateTimeOffset now = Now();
        try
        {
            return await InTurnAsync(() => FolderBackup.RunIfDue(folder, keep, now, p => Write(p)));
        }
        catch (DirectoryNotFoundException e)
        {
            if (!_toldFolderMissing)
            {
                _toldFolderMissing = true;
                _say(e.Message, true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _say($"Couldn't back up to {folder}: {e.Message}", true);
        }

        return null;
    }

    /// <summary>
    /// Checks the cloud's place and keys (write, read, list, delete a small file); when it works, keeps the keys in the
    /// keychain and the place in the prefs. A blank secret keeps the secret already kept for the same access key.
    /// </summary>
    /// <param name="service">The service's id.</param>
    /// <param name="endpoint">The address.</param>
    /// <param name="region">The region, or blank for the service's own.</param>
    /// <param name="bucket">The bucket.</param>
    /// <param name="folder">The folder in the bucket.</param>
    /// <param name="accessKey">The access key.</param>
    /// <param name="secretKey">The secret key, or blank to keep the one kept.</param>
    /// <returns>What to tell the user: "Connected…" or what to check.</returns>
    internal async Task<string> SetUpCloudAsync(
        string service, string? endpoint, string? region, string bucket, string folder, string accessKey, string? secretKey)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(accessKey))
            {
                return "Type the bucket and the access key first.";
            }

            CloudKeys? kept = await Keychain.GetAsync();
            string? secret = string.IsNullOrEmpty(secretKey) && kept?.AccessKey == accessKey.Trim() ? kept.SecretKey : secretKey;
            if (string.IsNullOrEmpty(secret))
            {
                return "Type the secret key first.";
            }

            var keys = new CloudKeys(accessKey.Trim(), secret);
            var target = CloudTarget.From(service, endpoint, region, bucket, string.IsNullOrWhiteSpace(folder) ? "prop-generator" : folder);
            using (var s3 = new S3Client(Http, target, keys))
            {
                await new CloudBackup(s3, target.Folder).CheckAsync();
            }

            await Keychain.SetAsync(keys);
            _prefs.CloudService = service;
            _prefs.CloudEndpoint = string.IsNullOrWhiteSpace(endpoint) ? null : CloudTarget.Tidy(endpoint);
            _prefs.CloudRegion = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
            _prefs.CloudBucket = target.Bucket;
            _prefs.CloudFolder = target.Folder;
            _prefs.Save();
            return $"Connected. Prop Generator can read and write in {target.Bucket}/{target.Folder}/";
        }
        catch (BackupException e)
        {
            return e.Message;
        }
    }

    /// <summary>Backs up every prop to the cloud, keeping the newest few there.</summary>
    /// <returns>Done when it is sent or the problem is said.</returns>
    internal async Task BackUpToCloudAsync()
    {
        string temp = Path.Combine(_config, "cloud-upload.zip");
        try
        {
            (S3Client s3, CloudTarget target) = await CloudAsync();
            using (s3)
            {
                Directory.CreateDirectory(_config);
                int n = await InTurnAsync(() => Write(temp));
                string key = await new CloudBackup(s3, target.Folder).UploadAsync(temp, Now(), _prefs.CloudKeep);
                _say($"Backed up {Props(n)} to the cloud ({target.Bucket}/{key}).", false);
            }
        }
        catch (BackupException e)
        {
            _say(e.Message, true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>Restores the newest cloud backup, merging (nothing here is replaced).</summary>
    /// <returns>Done when it is restored or the problem is said.</returns>
    internal async Task RestoreFromCloudAsync()
    {
        string temp = Path.Combine(_config, "cloud-restore.zip");
        try
        {
            (S3Client s3, CloudTarget target) = await CloudAsync();
            using (s3)
            {
                var cloud = new CloudBackup(s3, target.Folder);
                IReadOnlyList<S3Object> backups = await cloud.ListAsync();
                if (backups.Count == 0)
                {
                    _say($"There is no backup in {target.Bucket}/{target.Folder}/ yet.", true);
                    return;
                }

                Directory.CreateDirectory(_config);
                await cloud.DownloadAsync(backups[0].Key, temp);
            }

            await RestoreAsync(temp);
        }
        catch (BackupException e)
        {
            _say(e.Message, true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>Turns the cloud backup off: the keys leave the keychain; nothing in the bucket is touched.</summary>
    /// <returns>Done when it is off.</returns>
    internal async Task TurnOffCloudAsync()
    {
        await Keychain.ForgetAsync();
        _prefs.CloudService = null;
        _prefs.Save();
        _say("The cloud backup is off and its keys are forgotten. The backups in the bucket stay.", false);
    }

    /// <inheritdoc/>
    public void Dispose() => _ownHttp?.Dispose();

    private static string Props(int n) => n == 1 ? "1 prop" : string.Create(CultureInfo.InvariantCulture, $"{n} props");

    private async Task<(S3Client S3, CloudTarget Target)> CloudAsync()
    {
        if (!CloudOn)
        {
            throw new BackupException("Set up the cloud backup first (File, Cloud backup…).");
        }

        CloudKeys keys = await Keychain.GetAsync()
            ?? throw new BackupException("No keys are saved for cloud backup. Set them again in File, Cloud backup….");
        var target = CloudTarget.From(_prefs.CloudService, _prefs.CloudEndpoint, _prefs.CloudRegion, _prefs.CloudBucket!, _prefs.CloudFolder);
        return (new S3Client(Http, target, keys), target);
    }

    // Runs a backup job off the window's thread once the one before it is done: two never write at once (a daily
    // backup asked for again while it is being written then finds today's there).
    private async Task<T> InTurnAsync<T>(Func<T> job)
    {
        await _turn.WaitAsync();
        try
        {
            return await Task.Run(job);
        }
        finally
        {
            _turn.Release();
        }
    }

    private int Write(string path)
    {
        var extras = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in ExtraFiles)
        {
            string file = Path.Combine(_config, name);
            if (File.Exists(file))
            {
                extras[name] = File.ReadAllBytes(file);
            }
        }

        return BackupArchive.Write(path, [.. _library.List().Select(e => e.Path)], extras, Now());
    }

    private async Task RestoreAsync(string zip)
    {
        string[] trusted = [.. _library.List().Select(e => Path.GetDirectoryName(e.Path)!), .. _prefs.SaveFolder is { } save ? [save] : Array.Empty<string>()];
        try
        {
            BackupRestore r = await Task.Run(() => BackupArchive.Restore(zip, _library, Path.Combine(_config, "restored"), trusted));
            MergeExtras?.Invoke(r.Extras);
            int back = r.Added + r.Copies;
            string said = back == 0
                ? "Everything in that backup is already here."
                : $"Restored {Props(back)}." + (r.Copies > 0 ? $" {Props(r.Copies)} differed from yours and came back beside them as \"(from backup)\"." : string.Empty);
            _say(r.Refused > 0 ? $"{said} {r.Refused} entries were not let in." : said, false);
        }
        catch (BackupException e)
        {
            _say(e.Message, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _say($"The backup could not be restored: {e.Message}", true);
        }
    }
}
