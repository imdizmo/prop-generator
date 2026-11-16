using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Xunit;

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>Helpers for the tests of Claude's connection: a free port, waiting on a task, a window with its tools.</summary>
internal static class ClaudeKit
{
    internal static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // Waits for a task while the window's thread keeps running (the tools hop onto it).
    internal static T Run<T>(Task<T> t, int ms = 20000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!t.IsCompleted && clock.ElapsedMilliseconds < ms)
        {
            Ui.Pump();
            Thread.Sleep(5);
        }

        Assert.True(t.IsCompleted, "the call did not finish");
        return t.GetAwaiter().GetResult();
    }

    internal static void Run(Task t)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!t.IsCompleted && clock.ElapsedMilliseconds < 20000)
        {
            Ui.Pump();
            Thread.Sleep(5);
        }

        Assert.True(t.IsCompleted, "the call did not finish");
        t.GetAwaiter().GetResult();
    }

    internal static string Text(CallToolResult r) => string.Concat(r.Content.OfType<TextContentBlock>().Select(b => b.Text));

    internal static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    internal static (MainWindow Window, ClaudeTools Tools, FakePainter Painter, FakeDialogs Dialogs, Scratch Scratch) Make()
    {
        var painter = new FakePainter();
        var dialogs = new FakeDialogs();
        Scratch s = Ui.NewScratch();
        MainWindow w = Ui.Open(painter, dialogs, s);
        return (w, new ClaudeTools(w), painter, dialogs, s);
    }
}
