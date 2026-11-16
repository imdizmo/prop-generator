using Avalonia.Threading;

namespace PropGenerator.Desktop;

/// <summary>
/// Claude's connection as the Settings window and the main window see it: on or off (remembered in the prefs, off to
/// begin with), the fixed port and the token, and a status line saying whether the server is listening or why not.
/// The server starts and stops with the toggle and is stopped, its port freed, when the window closes.
/// </summary>
public sealed class ClaudeConnection : IDisposable
{
    // How long a request still running is given to finish when the server stops.
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(1);

    private readonly MainWindow _window;
    private readonly Prefs _prefs;
    private readonly Action<string> _say;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClaudeServer? _server;
    private bool _disposed;

    /// <summary>Reads the remembered state and makes the token if there is none yet. Does not start the server.</summary>
    /// <param name="window">The window the tools work on.</param>
    /// <param name="prefs">Where the switch, port and token are kept.</param>
    /// <param name="say">Shows a line on the status line.</param>
    public ClaudeConnection(MainWindow window, Prefs prefs, Action<string> say)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(say);
        _window = window;
        _prefs = prefs;
        _say = say;
        if (string.IsNullOrEmpty(prefs.ClaudeToken))
        {
            prefs.ClaudeToken = ClaudeServer.NewToken();
            prefs.Save();
        }

        Token = prefs.ClaudeToken;
        Enabled = prefs.ClaudeEnabled;
        Status = Enabled ? "Starting…" : "Off.";
    }

    /// <summary>Gets or sets what starts the server (the tests give one that fails).</summary>
    internal Func<int, Func<string>, ClaudeTools, Task<ClaudeServer>> Starter { get; set; } = ClaudeServer.Start;

    /// <summary>Raised on the window's thread when the status, token or port changed.</summary>
    public event Action? Changed;

    /// <summary>Gets a value indicating whether Claude is allowed to connect (the toggle).</summary>
    public bool Enabled { get; private set; }

    /// <summary>Gets a value indicating whether the server is listening now.</summary>
    public bool Running => _server is not null;

    /// <summary>Gets the port on 127.0.0.1.</summary>
    public int Port => _prefs.ClaudePort;

    /// <summary>Gets the address Claude connects to, in full (http://127.0.0.1:port/mcp), as it is pasted.</summary>
    public string Address => ClaudeServer.Url(Port);

    /// <summary>Gets the secret Claude must send.</summary>
    public string Token { get; private set; }

    /// <summary>Gets what to type in a terminal to connect Claude Code.</summary>
    public string ClaudeCodeCommand => ClaudeServer.ClaudeCodeCommand(Port, Token);

    /// <summary>Gets whether the server is listening, or why not.</summary>
    public string Status { get; private set; }

    /// <summary>Starts the server if the prefs say it is on (called once, after the window is made).</summary>
    /// <returns>Done when started or failed.</returns>
    public Task StartIfEnabledAsync() => Enabled ? Apply() : Task.CompletedTask;

    /// <summary>Turns the connection on or off, remembers it, and starts or stops the server.</summary>
    /// <param name="on">True to allow Claude to connect.</param>
    /// <returns>Done when the server is listening, stopped, or failed (the status says which).</returns>
    public Task SetEnabledAsync(bool on)
    {
        Enabled = on;
        _prefs.ClaudeEnabled = on;
        _prefs.Save();
        return Apply();
    }

    /// <summary>Chooses another port (used from tests and later settings); restarts the server if it is on.</summary>
    /// <param name="port">A port from 1024 to 65535.</param>
    public void SetPort(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1024);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        _prefs.ClaudePort = port;
        _prefs.Save();
        if (Enabled)
        {
            _ = Apply();
        }
        else
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Makes a new token and remembers it; the old one stops working at once.</summary>
    public void NewToken()
    {
        Token = ClaudeServer.NewToken();
        _prefs.ClaudeToken = Token;
        _prefs.Save();
        Changed?.Invoke();
    }

    /// <summary>Stops the server and frees its port.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClaudeServer? s = _server;
        _server = null;
        if (s is not null)
        {
            try
            {
                Task.Run(() => s.Stop(Grace)).Wait(TimeSpan.FromSeconds(4));
            }
            catch (AggregateException)
            {
                // Going away anyway.
            }
        }
    }

    private async Task Apply()
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            if (_server is not null)
            {
                ClaudeServer old = _server;
                _server = null;
                await Task.Run(() => old.Stop(Grace));
            }

            if (!Enabled)
            {
                Status = "Off.";
                return;
            }

            try
            {
                _server = await Starter(Port, () => Token, new ClaudeTools(_window));
                Status = $"Listening on {Address}.";
                if (_disposed)
                {
                    ClaudeServer late = _server;
                    _server = null;
                    await Task.Run(() => late.Stop(Grace));
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // Callers do not await this: whatever goes wrong is said here, and the status never stays on Starting.
                Status = $"Claude cannot connect: the server could not start on port {Port} ({e.Message})";
                _say(Status);
            }
        }
        finally
        {
            _gate.Release();
            Dispatcher.UIThread.Post(() => Changed?.Invoke());
        }
    }
}
