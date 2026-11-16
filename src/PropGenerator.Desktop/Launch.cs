namespace PropGenerator.Desktop;

/// <summary>How the launch waits for the main window to have something to show.</summary>
internal static class Launch
{
    /// <summary>How long the loading window waits for the first picture before it shows the main window anyway.</summary>
    internal static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>Finishes when the window's first picture is ready, or after <paramref name="patience"/>, whichever is first.</summary>
    /// <param name="window">The main window, built and not yet shown.</param>
    /// <param name="patience">The longest to wait.</param>
    /// <returns>The wait.</returns>
    internal static Task FirstPicture(MainWindow window, TimeSpan patience)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.HasFirstPicture)
        {
            return Task.CompletedTask;
        }

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.FirstPictureReady += () => ready.TrySetResult();
        return Task.WhenAny(ready.Task, Task.Delay(patience));
    }
}
