using Avalonia.Threading;

namespace PropGenerator.Desktop;

/// <summary>
/// Runs an action on the UI thread once a delay has passed since the last <see cref="Restart"/>. Built on Task.Delay
/// and a post to the dispatcher rather than a DispatcherTimer, so it also runs in the headless tests' dispatcher.
/// </summary>
/// <param name="delay">How long things must stay still.</param>
/// <param name="action">What to run then.</param>
internal sealed class Debounce(TimeSpan delay, Action action)
{
    private long _generation;

    // The generation that ran or was dropped last: a wait is pending while it differs from _generation.
    private long _settled;

    /// <summary>Gets a value indicating whether a wait is running (the action is still to come).</summary>
    public bool Pending => Interlocked.Read(ref _settled) != Interlocked.Read(ref _generation);

    /// <summary>Starts the wait again from now; a wait already running is dropped.</summary>
    public void Restart()
    {
        long mine = Interlocked.Increment(ref _generation);
        _ = RunAsync(mine);
    }

    /// <summary>Drops the wait running, if any.</summary>
    public void Cancel() => Interlocked.Exchange(ref _settled, Interlocked.Increment(ref _generation));

    /// <summary>Runs the action now if a wait is running (the window is closing), and drops the wait.</summary>
    public void Flush()
    {
        if (Pending)
        {
            Cancel();
            action();
        }
    }

    private async Task RunAsync(long mine)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() =>
        {
            if (Interlocked.Read(ref _generation) == mine)
            {
                Interlocked.Exchange(ref _settled, mine);
                action();
            }
        });
    }
}
