using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(PropGenerator.Desktop.Tests.Support.TestApp))]

// One Avalonia app for the whole run, as Groundwork's tests do: a new app per test was now and then set up on another
// thread than the dispatcher's.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>The headless app the window tests run in: the app's own themes, no screen, no OpenGL.</summary>
public static class TestApp
{
    /// <summary>Builds the headless app.</summary>
    /// <returns>The app builder.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
