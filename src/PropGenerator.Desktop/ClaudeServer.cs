using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PropGenerator.Desktop;

/// <summary>
/// Claude's connection: an MCP server inside the app (<see cref="ClaudeTools"/>) that Claude Code can drive the
/// controls through. It listens on 127.0.0.1 only, answers only requests addressed to this computer's own names (so a
/// web page cannot reach it under another name) and only those that send the token as "Authorization: Bearer".
/// </summary>
/// <remarks>Derived from Groundwork's editor/Desktop/ClaudeServer.cs (MIT License, Thibault Bene): see NOTICE.md.</remarks>
public sealed class ClaudeServer
{
    private static readonly HashSet<string> LocalHosts = new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost" };

    private readonly WebApplication _app;

    private ClaudeServer(WebApplication app, int port)
    {
        _app = app;
        Port = port;
    }

    /// <summary>Gets the port it listens on.</summary>
    public int Port { get; }

    /// <summary>The address Claude connects to.</summary>
    /// <param name="port">The port.</param>
    /// <returns>For example http://127.0.0.1:47120/mcp.</returns>
    public static string Url(int port) => $"http://127.0.0.1:{port}/mcp";

    /// <summary>What to type to connect Claude Code.</summary>
    /// <param name="port">The port.</param>
    /// <param name="token">The token.</param>
    /// <returns>The claude mcp add command.</returns>
    public static string ClaudeCodeCommand(int port, string token) =>
        $"claude mcp add --transport http prop-generator {Url(port)} --header \"Authorization: Bearer {token}\"";

    /// <summary>A new secret: 32 random bytes as lower-case hex.</summary>
    /// <returns>64 hex digits.</returns>
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>Starts the server.</summary>
    /// <param name="port">The port on 127.0.0.1.</param>
    /// <param name="token">The secret callers must send; read at each request, so a new token works at once.</param>
    /// <param name="tools">What Claude is given.</param>
    /// <returns>The running server.</returns>
    /// <exception cref="IOException">The port could not be listened on (taken).</exception>
    internal static async Task<ClaudeServer> Start(int port, Func<string> token, ClaudeTools tools)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory, Args = ["--hostBuilder:reloadConfigOnChange=false"] });
        builder.Logging.ClearProviders();

        // The host's console lifetime would take Ctrl+C from the app and stop only this server.
        builder.Services.AddSingleton<IHostLifetime, NoSignals>();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, port));
        builder.Services.AddMcpServer(o =>
            {
                o.ServerInfo = new() { Name = "prop-generator", Version = BuildInfo.Version };
                o.ServerInstructions = Guide;
            })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools(tools);
        WebApplication app = builder.Build();
        app.Use(async (ctx, next) =>
        {
            if (!LocalHosts.Contains(ctx.Request.Host.Host))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            byte[] expected = Encoding.UTF8.GetBytes("Bearer " + token());
            byte[] given = Encoding.UTF8.GetBytes(ctx.Request.Headers.Authorization.ToString());
            if (!CryptographicOperations.FixedTimeEquals(given, expected))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("The token is missing or wrong: copy it again from the app's Settings window.");
                return;
            }

            await next();
        });
        app.MapMcp("/mcp");
        try
        {
            await app.StartAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw new IOException($"Could not listen on 127.0.0.1:{port}: {e.Message}", e);
        }

        return new ClaudeServer(app, port);
    }

    /// <summary>Stops the server and frees its port.</summary>
    /// <param name="grace">How long a request still running may take to finish; then it is cut off.</param>
    /// <returns>Done when the listener is gone.</returns>
    internal async Task Stop(TimeSpan grace)
    {
        using var cut = new CancellationTokenSource(grace);
        try
        {
            await _app.StopAsync(cut.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The requests still running were cut off; the listener is closed.
        }

        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private static string Guide =>
        "This is Prop Generator, which makes game props live: a painted wooden floor tile, or a rock (easy-props' cut "
        + "rocks). get_state names the recipe being made (floor_tile or cut-rock); start_from_preset starts a new piece from a "
        + "preset (floor-v5 or a rock preset such as boulder) when nothing is unsaved. Call list_controls next: it gives every "
        + "control's key, what it does and its range. set_control changes one control exactly as the user would (visible in "
        + "the panel, one undo step each); on the floor colour, contrast and drift only recolour and every other control "
        + "repaints the wood (a moment at full detail); on a rock every control rebuilds the rock. Use screenshot to look at "
        + "the result. run_command(id) runs any command of the app's command list (the one behind the menus, the shortcuts and "
        + "the Ctrl+K palette), such as surprise, frame, maximize, next-shading or scene-grey; a greyed command is refused with its "
        + "reason and an unknown id is answered with the valid ids. get_state also tells the shading, lod, scene, budget, faces "
        + "and whether the view is maximized. undo and redo move through the history. save and export never open a dialog: save needs an absolute "
        + "path for a new piece, export needs a saved piece and a Groundwork map folder the first time; send_to_groundwork sends a "
        + "saved piece to the map open in Groundwork, with no folder to give. get_state shows the "
        + "status line, where errors appear: it holds the latest message, and once that message's 6 seconds are over it holds the "
        + "mouse hint instead (an error stays until the user's next action).";

    private sealed class NoSignals : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
