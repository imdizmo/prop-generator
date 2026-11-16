using System.Collections.Concurrent;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>The pictures of Show me more and the drawer: built for their own purpose, and stopped with their card or drawer.</summary>
[Collection(MeshRecipesAlone.Name)]
public class PictureJobsTests
{
    private static bool Waited(Func<bool> done, int ms = 20000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ClosingShowMeMoreCancelsItsToken()
    {
        var slow = new SlowRecipe(EngineKit.Recipe("bush"), TimeSpan.FromSeconds(20));
        StartingPoint round = EngineKit.Recipe("bush").StartingPoints.Single(p => p.Id == "round-bush");
        bool closed = false;
        using var card = new Cards.ShowMoreCard(slow, round.Settings, "bush", _ => { }, _ => { }, () => closed = true);
        Ui.Pump();
        Assert.True(Waited(() => slow.Calls.Count > 0));
        SlowRecipe.Call drawing = slow.Calls[0];
        Assert.Equal(BuildPurpose.ShowMore, drawing.Purpose);
        Assert.False(drawing.Cancel.IsCancellationRequested);

        card.Close();
        Assert.True(closed);
        Assert.True(drawing.Cancel.IsCancellationRequested);
        Assert.True(Waited(() => card.Drawing.IsCompleted, 5000));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DisposingTheDrawerWhilePicturesAreQueuedLogsNoObjectDisposedException()
    {
        // The slab cliff's pictures are always drawn by our painter (easy-props ships none for it).
        var slow = new SlowRecipe(CliffRecipe.Instance, TimeSpan.FromMilliseconds(300));
        using IDisposable _ = MeshRecipes.Override(slow);
        var lines = new ConcurrentQueue<string>();
        Action<string> listen = lines.Enqueue;
        TraceLog.Written += listen;
        try
        {
            // A picture cache of its own, so the cliff's pictures are drawn (by the double) rather than read.
            Scratch s = Ui.NewScratch();
            var w = new MainWindow(
                new FakePainter(), Ui.Specs(), new FakeDialogs(), new Prefs(s.P("prefs.json")), new Library(s.P("library.json")),
                s.P("bridge"), new Pictures.ThumbCache(s.P("thumbs")))
            {
                Width = 1400,
                Height = 900,
            };
            Ui.Track(w);
            w.Show();
            Ui.Pump();
            Assert.True(Waited(() => slow.Calls.Any(c => c.Purpose == BuildPurpose.Picture)));
            SlowRecipe.Call drawing = slow.Calls.First(c => c.Purpose == BuildPurpose.Picture);

            w.Drawer.Dispose();
            Assert.True(drawing.Cancel.IsCancellationRequested);
            Assert.True(Waited(() => w.Drawer.StopDrawing().IsCompleted, 10000));
            Ui.Wait(200);

            Assert.DoesNotContain(lines, l => l.Contains("disposed", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(lines, l => l.StartsWith("no picture drawn", StringComparison.Ordinal) && l.Contains("cancel", StringComparison.OrdinalIgnoreCase));
            w.Close();
        }
        finally
        {
            TraceLog.Written -= listen;
        }
    }
}
