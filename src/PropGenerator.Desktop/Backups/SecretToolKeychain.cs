using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PropGenerator.Desktop.Backups;

/// <summary>
/// The system keychain through libsecret's secret-tool (GNOME Keyring, KWallet): the keys are one secret, the JSON
/// {accessKey, secretKey}, under service "prop-generator" and account "cloud-backup". The secret goes in on stdin,
/// never on the command line.
/// </summary>
internal sealed class SecretToolKeychain : IKeychain
{
    private static readonly string[] Attributes = ["service", "prop-generator", "account", "cloud-backup"];

    /// <inheritdoc/>
    public async Task<CloudKeys?> GetAsync()
    {
        (int code, string output) = await RunAsync(["lookup", .. Attributes], null);
        if (code != 0 || output.Length == 0)
        {
            return null;
        }

        try
        {
            var n = JsonNode.Parse(output);
            string? access = n?["accessKey"]?.GetValue<string>();
            string? secret = n?["secretKey"]?.GetValue<string>();
            return access is null || secret is null ? null : new CloudKeys(access, secret);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync(CloudKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        string secret = new JsonObject { ["accessKey"] = keys.AccessKey, ["secretKey"] = keys.SecretKey }.ToJsonString();
        (int code, string output) = await RunAsync(["store", "--label=Prop Generator cloud backup", .. Attributes], secret);
        if (code != 0)
        {
            throw new BackupException($"The system keychain did not keep the keys ({output.Trim()}). Is a keyring (GNOME Keyring or KWallet) running?");
        }
    }

    /// <inheritdoc/>
    public async Task ForgetAsync() => await RunAsync(["clear", .. Attributes], null);

    private static async Task<(int Code, string Output)> RunAsync(string[] args, string? input)
    {
        var start = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in args)
        {
            start.ArgumentList.Add(a);
        }

        try
        {
            using Process p = Process.Start(start) ?? throw new BackupException("secret-tool could not be started.");
            if (input is not null)
            {
                await p.StandardInput.WriteAsync(input);
            }

            p.StandardInput.Close();
            Task<string> output = p.StandardOutput.ReadToEndAsync();
            Task<string> error = p.StandardError.ReadToEndAsync();
            using var cut = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await p.WaitForExitAsync(cut.Token);
            }
            catch (OperationCanceledException)
            {
                p.Kill(entireProcessTree: true);
                throw;
            }

            return (p.ExitCode, p.ExitCode == 0 ? await output : await error);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new BackupException("The system keychain is not reachable: secret-tool is not installed (the libsecret tools).", e);
        }
        catch (OperationCanceledException e)
        {
            throw new BackupException("The system keychain did not answer (it may be locked).", e);
        }
    }
}
