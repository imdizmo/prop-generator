using System.Net;
using System.Net.Sockets;
using PropGenerator.Desktop.Tests.Support;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

public class ClaudeConnectionTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ConnectionIsOffByDefaultKeepsAFixedPortAndAPersistedToken()
    {
        var (w, _, _, _, s) = Make();
        var prefs = new Prefs(s.P("prefs.json"));

        Assert.False(w.Claude.Enabled);
        Assert.Equal(47120, w.Claude.Port);
        Assert.Equal(64, w.Claude.Token.Length);
        Assert.Matches("^[0-9a-f]{64}$", w.Claude.Token);
        Assert.Equal(w.Claude.Token, prefs.ClaudeToken);
        Assert.False(prefs.ClaudeEnabled);

        string old = w.Claude.Token;
        w.Claude.NewToken();
        Assert.NotEqual(old, w.Claude.Token);
        Assert.Equal(w.Claude.Token, new Prefs(s.P("prefs.json")).ClaudeToken);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheToggleStartsAndStopsTheServerAndATakenPortIsReportedNotThrown()
    {
        var (w, _, _, _, s) = Make();
        int port = FreePort();
        w.Claude.SetPort(port);

        Run(w.Claude.SetEnabledAsync(true));
        Assert.True(w.Claude.Running);
        Assert.True(new Prefs(s.P("prefs.json")).ClaudeEnabled);
        Assert.Contains($"127.0.0.1:{port}", w.Claude.Status, StringComparison.Ordinal);

        Run(w.Claude.SetEnabledAsync(false));
        Assert.False(w.Claude.Running);
        var l = new TcpListener(IPAddress.Loopback, port);
        l.Start();
        try
        {
            Run(w.Claude.SetEnabledAsync(true));
            Assert.False(w.Claude.Running);
            Assert.Contains("port", w.Claude.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("port", w.StatusLine.Text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            l.Stop();
        }

        Run(w.Claude.SetEnabledAsync(true));
        Assert.True(w.Claude.Running);
        w.Close();
        Ui.Pump();
        var again = new TcpListener(IPAddress.Loopback, port);
        again.Start();
        again.Stop();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AnyFailureToStartIsOnTheStatusLineAndNeverLeavesStarting()
    {
        var (w, _, _, _, _) = Make();
        w.Claude.Starter = (_, _, _) => throw new InvalidCastException("odd failure");

        Run(w.Claude.SetEnabledAsync(true));

        Assert.False(w.Claude.Running);
        Assert.Contains("odd failure", w.Claude.Status, StringComparison.Ordinal);
        Assert.Contains("odd failure", w.StatusLine.Text, StringComparison.Ordinal);
    }
}
