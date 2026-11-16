using PropGenerator.Desktop.Engine;
using Xunit;

[assembly: AssemblyFixture(typeof(PropGenerator.Desktop.Tests.Support.EngineShutdown))]

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>
/// Shuts the shared engine host down once every test has run, as the app's exit does: the tests share
/// <see cref="EngineHost.Default"/>, and only this fixture (never a test) shuts it down.
/// </summary>
public sealed class EngineShutdown : IDisposable
{
    /// <summary>Stops the shared engines, waiting at most 10 s for the build thread.</summary>
    public void Dispose() => EngineHost.Default.Shutdown(TimeSpan.FromSeconds(10));
}
