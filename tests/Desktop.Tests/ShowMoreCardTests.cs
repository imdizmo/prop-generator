using System.Diagnostics;
using Avalonia.Controls;
using PropGenerator.Desktop.Batch;
using PropGenerator.Desktop.Cards;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Tests.Support;
using PropGenerator.Engine;
using PropGenerator.Nature.Random;
using Xunit;

namespace PropGenerator.Desktop.Tests;

/// <summary>
/// Show me more's damaged versions (plan E10): a prop with damage settings gets a New seeds / Damaged switch, the
/// damaged page is the prop on its own seed broken in upstream's eight ways, and picking one is one undo step.
/// </summary>
public class ShowMoreCardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool Waited(Func<bool> done, int ms = 60000)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < ms)
        {
            Ui.Wait(20);
        }

        return done();
    }

    private static MainWindow Start(string pointId)
    {
        MainWindow w = Ui.Open(new FakePainter());
        Assert.True(CommandListTests.RunAsync(w.Files.StartFromAsync(YourPropsTests.Point(pointId))));
        Assert.True(Waited(() => w.Rocks.ShownSettings?.Equals(w.Panel.Current) == true));
        return w;
    }

    // The oak's damage settings and its damaged page, as the rules engine gives them.
    private static (string SettingsJson, IReadOnlyList<EngineVersion> Page) Damages(Settings s, uint seed, int page)
    {
        EngineRecipe oak = EngineKit.Recipe("painted-tree");
        string settings = EngineDamageSetting.ToJson(EngineHost.Default.Rules.DamageSettings(oak.Id, oak.ValuesJson(s), Ct));
        return (settings, EngineHost.Default.Rules.PageDamages(seed, page, settings, Ct));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DamagedIsOfferedForOakNotForGrass()
    {
        MainWindow oak = Start("oak");
        CommandListTests.Run(oak, "show-more");
        ShowMoreCard card = Assert.IsType<ShowMoreCard>(oak.Cards.Shown);
        Assert.True(card.OffersDamaged);
        Assert.True(card.ModeSwitch.IsVisible);
        Assert.Equal(["New seeds", "Damaged"], card.ModeSwitch.Children.OfType<Avalonia.Controls.Primitives.ToggleButton>().Select(b => b.Content as string));
        Assert.Equal(ShowMoreMode.Seeds, card.Mode);
        card.Close();
        oak.Close();

        MainWindow grass = Start("short-lawn");
        CommandListTests.Run(grass, "show-more");
        ShowMoreCard lawn = Assert.IsType<ShowMoreCard>(grass.Cards.Shown);
        Assert.False(lawn.OffersDamaged);
        Assert.False(lawn.ModeSwitch.IsVisible);
        lawn.ShowMode(ShowMoreMode.Damaged);
        Assert.Equal(ShowMoreMode.Seeds, lawn.Mode);
        lawn.Close();
        grass.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ADamagedPageKeepsTheSeed()
    {
        MainWindow w = Start("oak");
        uint seed = w.Panel.Current.GetSeed("seed");
        CommandListTests.Run(w, "show-more");
        var card = (ShowMoreCard)w.Cards.Shown!;
        card.ShowPage(2);

        // The Damaged chip goes to the first damaged page.
        card.DamagedButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ShowMoreMode.Damaged, card.Mode);
        Assert.Equal(0, card.Page);
        Assert.True(card.DamagedButton.IsChecked);
        Assert.False(card.NewSeedsButton.IsChecked);

        (string settings, IReadOnlyList<EngineVersion> page0) = Damages(w.Panel.Current, seed, 0);
        Assert.Equal(9, card.Versions.Count);
        Assert.All(card.Versions, v => Assert.Equal(seed, v.Seed));
        Assert.Null(card.Versions[4].Damage);
        EngineVersion[] others = [.. card.Versions.Where((_, i) => i != 4).Select(v => new EngineVersion { Seed = v.Seed, Damage = v.Damage })];
        Assert.Equal(page0.Select(v => v.ToJson()), others.Select(v => v.ToJson()));

        // Each is named by its damage, in upstream's words; the middle one is the prop as it is.
        Assert.Equal(EngineHost.Default.Rules.DamageNames(page0[0].ToJson(), settings, Ct), card.Captions[0]);
        Assert.False(string.IsNullOrWhiteSpace(card.Captions[0]));
        Assert.Equal(9, card.Versions.Select(card.KeyOf).Distinct().Count());
        Assert.Equal(EngineHost.Default.Rules.VersionKey(page0[0].ToJson(), Ct), card.KeyOf(card.Versions[0]));

        card.ShowPage(1);
        Assert.All(card.Versions, v => Assert.Equal(seed, v.Seed));
        Assert.Equal(Damages(w.Panel.Current, seed, 1).Page.Select(v => v.ToJson()), card.Versions.Where((_, i) => i != 4).Select(v => new EngineVersion { Seed = v.Seed, Damage = v.Damage }.ToJson()));

        // New seeds again: upstream's page seeds around the prop, from the first page.
        card.ShowMode(ShowMoreMode.Seeds);
        Assert.Equal(0, card.Page);
        Assert.Equal(Variations.Grid(seed, 0), card.Seeds);
        Assert.All(card.Versions, v => Assert.Null(v.Damage));
        card.Close();
        w.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void PickingADamagedVersionIsOneUndoStep()
    {
        MainWindow w = Start("oak");
        Settings before = w.Panel.Current;
        uint seed = before.GetSeed("seed");
        int steps = w.Files.History.Steps.Count;
        CommandListTests.Run(w, "show-more");
        var card = (ShowMoreCard)w.Cards.Shown!;
        card.ShowMode(ShowMoreMode.Damaged);
        PropVersion picked = card.Versions[0];
        Assert.NotNull(picked.Damage);

        card.Tiles[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Ui.Pump();
        Assert.Null(w.Cards.Shown);
        Assert.Equal(steps + 1, w.Files.History.Steps.Count);
        Assert.Equal("Use a damaged version", w.Files.History.Steps[^1].Label);
        Assert.Equal(seed, w.Panel.Current.GetSeed("seed"));
        Assert.All(picked.Damage!, d => Assert.Equal(d.Value, w.Panel.Current.Values[d.Key].GetDouble()));

        w.Files.Undo();
        Assert.Equal(before, w.Panel.Current);
        w.Close();
    }
}
