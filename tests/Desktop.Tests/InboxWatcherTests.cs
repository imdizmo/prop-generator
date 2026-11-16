using PropGenerator.Desktop.Tests.Support;
using Xunit;
using static PropGenerator.Desktop.Tests.Support.BridgeKit;

namespace PropGenerator.Desktop.Tests;

public class InboxWatcherTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AGlbDroppedInTheInboxShowsAsAPieceWithItsFaceIds()
    {
        using var s = new Scratch();
        MainWindow w = Open(s);
        int[] ids = DropMesh(s, "Crate", 20);

        Ui.Wait(1600);

        Assert.NotNull(w.Inbox.Piece);
        Assert.Equal(ids.Distinct().Order(), w.Inbox.Piece!.Faces.Select(f => f.FaceId));
        Assert.All(w.Inbox.Piece.Faces, f => Assert.Equal("unmarked", f.Kind));
        Assert.True(w.View.ShowingPiece);
        Assert.Contains("Painting shapes from Blender comes in v2", w.StatusLine.Text);
        Assert.Contains("Crate", w.StatusLine.Text);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ManyWritesInARowAreLoadedOnceAfterTheyStop()
    {
        using var s = new Scratch();
        MainWindow w = Open(s);
        int loads = 0;
        w.Inbox.Loaded += _ => loads++;
        for (int i = 0; i < 4; i++)
        {
            DropMesh(s, "Crate", 20);
            Ui.Wait(100);
        }

        Ui.Wait(1500);

        Assert.Equal(1, loads);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AGlbThatCannotBeReadShowsAnErrorAndKeepsTheWindowAlive()
    {
        using var s = new Scratch();
        MainWindow w = Open(s);
        Directory.CreateDirectory(Inbox(s));
        File.WriteAllText(Path.Combine(Inbox(s), "broken.glb"), "not a model");

        Ui.Wait(1600);

        Assert.Contains("broken.glb", w.StatusLine.Text);
        Assert.Null(w.Inbox.Piece);
        Assert.False(w.View.ShowingPiece);
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheWatcherStopsWhenTheWindowCloses()
    {
        using var s = new Scratch();
        MainWindow w = Open(s);
        InboxWatcher watcher = w.Inbox;
        w.Close();
        Ui.Pump();

        Assert.True(watcher.IsDisposed);
    }
}
