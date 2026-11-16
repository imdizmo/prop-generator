using Xunit;
using static PropGenerator.Tests.Support.Fixtures;

namespace PropGenerator.Tests;

public class HistoryTests
{
    [Fact]
    public void HistoryKeepsTheLast200Of205()
    {
        var h = new History();
        Settings s = Defaults();
        for (int i = 0; i < 205; i++)
        {
            h.Push("step " + i, s.With("contrast", i));
        }

        Assert.Equal(200, h.Steps.Count);
        Assert.Equal("step 5", h.Steps[0].Label);
        Assert.Equal("step 204", h.Steps[^1].Label);
        Assert.Equal(199, h.Index);
    }

    [Fact]
    public void PushAfterUndoDropsTheRedoSteps()
    {
        var h = new History();
        Settings s = Defaults();
        h.Push("a", s);
        h.Push("b", s.With("contrast", 1));
        h.Undo();
        h.Push("c", s.With("contrast", 2));
        Assert.Equal(["a", "c"], h.Steps.Select(x => x.Label));
        Assert.Null(h.Redo());
        Assert.Null(new History().Undo());
    }

    [Fact]
    public void GoMovesStraightToAStepAndKeepsTheSteps()
    {
        var h = new History();
        Settings s = Defaults();
        h.Push("a", s);
        h.Push("b", s.With("contrast", 1));
        h.Push("c", s.With("contrast", 2));

        Assert.Equal(s, h.Go(0));
        Assert.Equal(0, h.Index);
        Assert.Equal(s.With("contrast", 2), h.Go(2));
        Assert.Equal(2, h.Index);
        Assert.Null(h.Go(2));
        Assert.Equal(3, h.Steps.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => h.Go(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => h.Go(-1));

        // Undo and redo carry on from where Go left the history.
        h.Go(1);
        Assert.Equal(s, h.Undo());
        Assert.Equal(s.With("contrast", 1), h.Redo());
    }
}
