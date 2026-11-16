using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Cards;

namespace PropGenerator.Desktop.Backups;

/// <summary>
/// Cloud backup (File, Cloud backup…): the service, its address, the bucket, the region, the folder in the bucket and
/// the keys, with Check the connection, which keeps the keys in the system keychain once they work. The secret is never
/// shown again: left blank, the one kept stays.
/// </summary>
internal sealed class CloudSetupCard : Border
{
    private readonly BackupActions _backups;
    private readonly ComboBox _service = new() { MinWidth = 220 };
    private readonly TextBox _endpoint = Box("Address", "The storage's address (its endpoint), such as <account-id>.r2.cloudflarestorage.com. Amazon S3 needs none.");
    private readonly TextBox _bucket = Box("Bucket", "The bucket the backups go into. It must already exist: make it on the storage's own page.");
    private readonly TextBox _region = Box("Region", "The bucket's region; leave it empty for the service's usual one.");
    private readonly TextBox _folder = Box("Folder in the bucket", "The folder in the bucket the backups go into; prop-generator until changed.");
    private readonly TextBox _access = Box("Access key", "The access key (key id) the storage gave you.");
    private readonly TextBox _secret = Box("Secret key", "The secret key the storage gave you. It is kept in the system keychain, never in the app's files. Leave it empty to keep the one kept.");
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, FontSize = Look.Theme.Label, Foreground = Look.Theme.TextTertiary };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = Look.Theme.Label, Foreground = Look.Theme.TextSecondary };

    /// <summary>Builds the card with what is set up now.</summary>
    /// <param name="backups">The window's backup actions.</param>
    /// <param name="prefs">Where the cloud's place is kept.</param>
    /// <param name="close">Closes the card.</param>
    internal CloudSetupCard(BackupActions backups, Prefs prefs, Action close)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        _backups = backups;
        CardParts.Dress(this);
        Width = 520;
        foreach (CloudService s in CloudServices.All)
        {
            _service.Items.Add(s.Label);
        }

        Look.Chrome.Tip(_service, "Which storage the backups go to. Any other S3-compatible storage works as Other, with its address.");
        AutomationProperties.SetName(_service, "Service");
        _service.SelectionChanged += (_, _) => ShowHint();
        _service.SelectedIndex = Math.Max(0, CloudServices.All.ToList().FindIndex(s => s.Id == (prefs.CloudService ?? "r2")));
        _endpoint.Text = prefs.CloudEndpoint;
        _bucket.Text = prefs.CloudBucket;
        _region.Text = prefs.CloudRegion;
        _folder.Text = prefs.CloudFolder;
        _secret.PasswordChar = '•';
        _status.Text = backups.CloudOn ? $"On: backups go to {prefs.CloudBucket}/{prefs.CloudFolder}/." : "Off.";

        Button check = Look.Chrome.TextButton("Check the connection", "Try the keys: write, read, list and remove a small file in the folder. When it works, the keys are kept and the cloud backup is on.");
        AutomationProperties.SetName(check, "Check the connection");
        check.Click += async (_, _) => await CheckAsync();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), RowSpacing = 6, ColumnSpacing = 8 };
        (string, Control)[] rows = [("Service", _service), ("Address", _endpoint), ("Bucket", _bucket), ("Region", _region), ("Folder in the bucket", _folder), ("Access key", _access), ("Secret key", _secret)];
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = rows[i].Item1, VerticalAlignment = VerticalAlignment.Center, Foreground = Look.Theme.TextSecondary };
            Grid.SetRow(label, i);
            Grid.SetRow(rows[i].Item2, i);
            Grid.SetColumn(rows[i].Item2, 1);
            grid.Children.Add(label);
            grid.Children.Add(rows[i].Item2);
        }

        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CardParts.Header("Cloud backup", "Close the cloud backup's setup (Esc).", close),
                new TextBlock { Text = "Back up every prop to S3-compatible storage of your own. The keys stay in the system keychain.", TextWrapping = TextWrapping.Wrap, Foreground = Look.Theme.TextSecondary },
                grid,
                _hint,
                check,
                _status,
            },
        };
        ShowHint();
    }

    /// <summary>Gets what the card says about the connection.</summary>
    internal string Status => _status.Text ?? string.Empty;

    /// <summary>Fills the fields (as typing does).</summary>
    /// <param name="service">The service's id.</param>
    /// <param name="endpoint">The address.</param>
    /// <param name="bucket">The bucket.</param>
    /// <param name="accessKey">The access key.</param>
    /// <param name="secretKey">The secret key.</param>
    internal void Fill(string service, string? endpoint, string bucket, string accessKey, string secretKey)
    {
        _service.SelectedIndex = CloudServices.All.ToList().FindIndex(s => s.Id == service);
        _endpoint.Text = endpoint;
        _bucket.Text = bucket;
        _access.Text = accessKey;
        _secret.Text = secretKey;
    }

    /// <summary>Checks the connection and, when it works, keeps the keys and turns the cloud backup on.</summary>
    /// <returns>Done when the card says how it went.</returns>
    internal async Task CheckAsync()
    {
        _status.Text = "Checking…";
        CloudService s = CloudServices.All[Math.Max(0, _service.SelectedIndex)];
        _status.Text = await _backups.SetUpCloudAsync(
            s.Id, _endpoint.Text, _region.Text, _bucket.Text ?? string.Empty, _folder.Text ?? string.Empty, _access.Text ?? string.Empty, _secret.Text);
        _secret.Text = string.Empty;
    }

    private static TextBox Box(string name, string tip)
    {
        var box = new TextBox();
        AutomationProperties.SetName(box, name);
        Look.Chrome.Tip(box, tip);
        return box;
    }

    private void ShowHint()
    {
        CloudService s = CloudServices.All[Math.Max(0, _service.SelectedIndex)];
        _endpoint.PlaceholderText = s.EndpointHint ?? "(none needed)";
        _endpoint.IsEnabled = s.EndpointHint is not null;
        _region.PlaceholderText = s.Region;
        _hint.Text = s.KeysHint;
    }
}
