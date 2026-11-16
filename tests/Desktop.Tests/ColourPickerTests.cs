using Avalonia.Controls;
using Avalonia.Interactivity;
using PropGenerator.Desktop.Details;
using PropGenerator.Desktop.Tests.Support;
using Xunit;

namespace PropGenerator.Desktop.Tests;

// The colour rows' picker: Recent, My colours and pasted lists of codes, kept in the colour book.
public class ColourPickerTests
{
    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
    }

    private static string Wood(MainWindow w)
    {
        Rgb c = w.Panel.Current.GetColour("colour");
        static int B(float x) => (int)Math.Round(x * 255);
        return $"#{B(c.R):x2}{B(c.G):x2}{B(c.B):x2}";
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ChosenColoursGoToRecentAndPickedFromThere()
    {
        MainWindow w = Ui.Open(new FakePainter());
        ColourRow row = w.Panel.ColourRow!;
        Click(row.Swatches[2]);
        string chosen = row.SwatchHexes[2].ToLowerInvariant();
        Assert.Equal(chosen, w.Colours.Recent[0]);
        Assert.Equal(chosen, row.RecentHexes[0]);

        Click(row.Swatches[4]);
        Assert.Equal(row.SwatchHexes[2].ToLowerInvariant(), row.RecentHexes[1]);
        Click(row.RecentSwatches[1]);
        Assert.Equal(chosen, Wood(w));
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void PastingSeveralCodesFillsMyColoursAndOneCodeIsUsed()
    {
        MainWindow w = Ui.Open(new FakePainter());
        ColourRow row = w.Panel.ColourRow!;

        row.Paste("#112233, rgb(68, 85, 102); abc");
        Assert.Equal(["#112233", "#445566", "#aabbcc"], w.Colours.Mine);
        Assert.Equal(["#112233", "#445566", "#aabbcc"], row.MineHexes);
        Assert.Equal("Added 3 to My colours", row.Note);

        row.Paste("#778899");
        Assert.Equal("#778899", Wood(w));
        Assert.Equal("#778899", w.Colours.Recent[0]);

        row.Paste("green");
        Assert.Equal("Not a colour code. Try #4e7d3e.", row.Note);

        Click(row.MineSwatches[1]);
        Assert.Equal("#445566", Wood(w));

        Click(row.AddMine);
        Assert.Equal(3, w.Colours.Mine.Count);
        row.Paste("#010203");
        Click(row.AddMine);
        Assert.Equal("#010203", w.Colours.Mine[^1]);
        Assert.Equal("Added to My colours", row.Note);

        row.RemoveFromMine("#112233");
        Assert.DoesNotContain("#112233", w.Colours.Mine);
        Assert.DoesNotContain("#112233", row.MineHexes);
        w.Close();
    }
}
