using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests.Cliffs;

/// <summary>The Long cliff builds fast enough to drag a slider live; timed on its own, after the other tests.</summary>
[Collection(TimedAlone.Name)]
public class CliffSpeedTests
{
    [Fact]
    public void TheLongCliffBuildsInUnder400ms()
    {
#if DEBUG
        return;
#else
        // Full detail, the seed check included: one build to warm up, then the median of five.
        var settings = CliffMeshes.Preset("long-cliff");
        PropGenerator.Nature.Cliffs.CliffGenerator.Build(settings, 1);
        long[] times = new long[5];
        for (int run = 0; run < times.Length; run++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            PropGenerator.Nature.Cliffs.CliffGenerator.Build(settings, 1);
            times[run] = watch.ElapsedMilliseconds;
        }

        Array.Sort(times);
        Assert.True(times[2] < 400, $"median build {times[2]} ms (all: {string.Join(", ", times)})");
        TestContext.Current.SendDiagnosticMessage($"Long cliff builds: {string.Join(", ", times)} ms");
#endif
    }
}
