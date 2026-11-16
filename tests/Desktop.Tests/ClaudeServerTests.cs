using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.ClaudeKit;

namespace PropGenerator.Desktop.Tests;

public class ClaudeServerTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ARequestWithoutTheTokenIsRefusedAndOneWithItIsServed()
    {
        var (_, tools, _, _, _) = Make();
        int port = FreePort();
        ClaudeServer server = Run(ClaudeServer.Start(port, () => "secret-token", tools));
        try
        {
            using var http = new HttpClient();
            HttpRequestMessage Req(string? token, string? host = null)
            {
                var r = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/mcp")
                {
                    Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"),
                };
                r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                if (token is not null)
                {
                    r.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                }

                if (host is not null)
                {
                    r.Headers.Host = host;
                }

                return r;
            }

            Assert.Equal(HttpStatusCode.Unauthorized, Run(http.SendAsync(Req(null))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, Run(http.SendAsync(Req("wrong"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, Run(http.SendAsync(Req("secret-token", "evil.example"))).StatusCode);

            HttpResponseMessage ok = Run(http.SendAsync(Req("secret-token")));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            string body = Run(ok.Content.ReadAsStringAsync());
            foreach (string tool in new[] { "list_controls", "set_control", "undo", "redo", "save", "export", "send_to_groundwork", "screenshot", "get_state", "start_from_preset", "run_command" })
            {
                Assert.Contains(tool, body, StringComparison.Ordinal);
            }
        }
        finally
        {
            Run(server.Stop(TimeSpan.FromSeconds(1)));
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ToolCallsOverHttpChangeTheControlsAndKeepWorkingCallAfterCall()
    {
        var (w, tools, _, _, _) = Make();
        int port = FreePort();
        ClaudeServer server = Run(ClaudeServer.Start(port, () => "tok", tools));
        try
        {
            using var http = new HttpClient();
            string Call(string name, string args)
            {
                var r = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/mcp")
                {
                    Content = new StringContent(
                        $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{name}}}","arguments":{{{args}}}}}""",
                        Encoding.UTF8,
                        "application/json"),
                };
                r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                r.Headers.TryAddWithoutValidation("Authorization", "Bearer tok");
                HttpResponseMessage resp = Run(http.SendAsync(r));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                return Run(resp.Content.ReadAsStringAsync());
            }

            for (int i = 0; i < 40; i++)
            {
                double v = 0.2 + ((i % 10) * 0.05);
                string said = Call("set_control", string.Create(System.Globalization.CultureInfo.InvariantCulture, $$$"""{"key":"gap_scale","value":{{{v}}}}"""));
                Assert.DoesNotContain("\"isError\":true", said, StringComparison.Ordinal);
                Assert.Equal(v, w.Panel.Current.GetFloat("gap_scale"), 3);
            }

            Assert.Contains("\"isError\":true", Call("set_control", """{"key":"nope","value":1}"""), StringComparison.Ordinal);
            Assert.Contains("history", Call("get_state", "{}"), StringComparison.Ordinal);
            string shot = Call("screenshot", "{}");
            Assert.Contains("\"isError\":true", shot, StringComparison.Ordinal);
            Assert.Contains("3D view", shot, StringComparison.Ordinal);
        }
        finally
        {
            Run(server.Stop(TimeSpan.FromSeconds(1)));
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AStoppedServerFreesItsPort()
    {
        var (_, tools, _, _, _) = Make();
        int port = FreePort();
        ClaudeServer server = Run(ClaudeServer.Start(port, () => "t", tools));
        Run(server.Stop(TimeSpan.FromSeconds(1)));

        var l = new TcpListener(IPAddress.Loopback, port);
        l.Start();
        l.Stop();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ClosingTheWindowDoesNotWaitOutARequestStillRunning()
    {
        var (w, tools, _, _, _) = Make();
        int port = FreePort();
        _ = tools;
        w.Claude.SetPort(port);
        Run(w.Claude.SetEnabledAsync(true));
        using var http = new HttpClient();
        var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_state","arguments":{}}}""", Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + w.Claude.Token);

        // A request that is slow on the window's thread: the window never pumps it, so it stays in flight.
        Task<HttpResponseMessage> pending = http.SendAsync(req);
        Thread.Sleep(300);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        w.Close();
        clock.Stop();

        Assert.True(clock.ElapsedMilliseconds < 3500, $"closing took {clock.ElapsedMilliseconds} ms");
        _ = pending;
    }
}
