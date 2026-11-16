using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;

namespace PropGenerator.Engine;

/// <summary>
/// The host object the shims call, as <c>__host</c>: <c>cancelled()</c> (the running call's cancel flag, checked by
/// the MessageChannel shim at every yield), <c>png(rgba, w, h)</c> (OffscreenCanvas.convertToBlob, encoded by
/// <see cref="PngEncoder"/>), <c>log(level, text)</c> (console) and <c>progress(token, done, total)</c> (an
/// export's progress, which entry.ts reports).
/// </summary>
public sealed class HostBridge
{
    private readonly Action<string, string>? _log;
    private ScriptObject? _newBytes;

    internal HostBridge(Action<string, string>? log) => _log = log;

    /// <summary>The running call's stop signal (cancel or time out); none between calls.</summary>
    internal CancellationToken Stop { get; set; }

    /// <summary>Where the running call's progress goes (an export's); null for none.</summary>
    internal IProgress<double>? JobProgress { get; set; }

    /// <summary>The last share reported to <see cref="JobProgress"/> in the running call, or -1 before the first.</summary>
    internal double LastProgress { get; set; } = -1;

    /// <summary>Notes the runtime's memory (for <see cref="EasyPropsEngine.PeakExternalBytes"/>); null for none.</summary>
    internal Action? Sample { get; set; }

    /// <summary>Binds the bridge to a freshly loaded engine.</summary>
    internal void Attach(ScriptEngine engine) =>
        _newBytes = (ScriptObject)engine.Evaluate("(n) => new Uint8Array(n)");

    /// <summary>Whether the running call should stop.</summary>
    /// <returns>True once the call is cancelled or past its time.</returns>
    [ScriptMember("cancelled")]
    public bool Cancelled() => Stop.IsCancellationRequested;

    /// <summary>Whether the running call should stop (the token the bundle passes is not needed: one call runs at a time).</summary>
    /// <param name="_">The bundle's token, unused.</param>
    /// <returns>True once the call is cancelled or past its time.</returns>
    [ScriptMember("cancelled")]
    public bool Cancelled(object? _) => Stop.IsCancellationRequested;

    /// <summary>Encodes RGBA8 pixels, rows top-down, as a PNG.</summary>
    /// <param name="rgba">A Uint8Array of width x height x 4 bytes.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The PNG file, as a Uint8Array.</returns>
    [ScriptMember("png")]
    public object Png(object rgba, double width, double height)
    {
        Sample?.Invoke();
        byte[] pixels = ScriptData.Bytes(rgba);
        byte[] png = PngEncoder.Encode(pixels, (int)width, (int)height);
        ScriptObject make = _newBytes ?? throw new InvalidOperationException("The bridge is not attached.");
        var bytes = (ITypedArray<byte>)make.Invoke(false, png.Length);
        bytes.Write(png, 0, (ulong)png.Length, 0);
        return bytes;
    }

    /// <summary>
    /// An export's progress, reported as a share from 0 to 1 to the running call's <see cref="JobProgress"/>. It runs
    /// on the engine's thread, so the receiver must only report on (post), never wait for another thread.
    /// </summary>
    /// <param name="_">The job's token, unused: one call runs at a time.</param>
    /// <param name="done">Steps done.</param>
    /// <param name="total">Steps in all.</param>
    [ScriptMember("progress")]
    public void Progress(object? _, double done, double total)
    {
        IProgress<double>? progress = JobProgress;
        if (progress is null)
        {
            return;
        }

        double share = total > 0 && double.IsFinite(done) ? Math.Clamp(done / total, 0, 1) : 0;
        LastProgress = share;
        progress.Report(share);
    }

    /// <summary>A console line.</summary>
    /// <param name="text">The line (its level first).</param>
    [ScriptMember("log")]
    public void Log(object? text) => _log?.Invoke("log", text?.ToString() ?? "");

    /// <summary>A console line.</summary>
    /// <param name="level">log, info, warn or error.</param>
    /// <param name="text">The line.</param>
    [ScriptMember("log")]
    public void Log(object? level, object? text) => _log?.Invoke(level?.ToString() ?? "log", text?.ToString() ?? "");
}
