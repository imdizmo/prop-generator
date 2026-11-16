// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Details;

/// <summary>
/// The head of the controls panel, as easy-props' Details header: the kind of prop ("Rock", "Floor tile"), "Whole
/// prop", then Break (only while a tree's heavy limb that can break is selected, as easy-props' Details offers it),
/// Surprise me, Show me more and, for a painted tree, Make pieces on the right, and the filter box under them.
/// </summary>
internal sealed class PanelHeader : Border
{
    private readonly Command _more;
    private readonly Command _break;
    private readonly Command _pieces;

    /// <summary>Builds the header.</summary>
    /// <param name="filter">The panel's filter box, shown under the title row.</param>
    /// <param name="commands">The window's commands; Surprise me and Show me more run theirs.</param>
    internal PanelHeader(FilterBox filter, IReadOnlyList<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(commands);
        Padding = new Thickness(Look.Theme.Space3, Look.Theme.Space3, Look.Theme.Space3, Look.Theme.Space2);

        Title = new TextBlock
        {
            FontSize = Look.Theme.Title,
            FontWeight = FontWeight.Bold,
            LetterSpacing = Tokens.TitleLetterSpacing,
            Foreground = Look.Theme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var scope = new TextBlock
        {
            Text = "Whole prop",
            FontSize = Look.Theme.Label,
            Foreground = Look.Theme.TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Command surprise = commands.Single(c => c.Id == "surprise");
        Surprise = new Button
        {
            Height = 22,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children =
                {
                    new Icon("dices", 13) { VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "Surprise me", FontSize = Look.Theme.Caption, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        Surprise.Classes.Add("ep");
        Surprise.Classes.Add("ep-outline");
        AutomationProperties.SetName(Surprise, "Surprise me");
        Chrome.Tip(Surprise, "Try a different one with the same shape (R)");
        Surprise.Click += async (_, _) => await surprise.Run();

        _break = commands.Single(c => c.Id == "break-part");
        Command breaks = _break;
        Break = new Button
        {
            Height = 22,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children =
                {
                    new Icon("zap", 13) { VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "Break", FontSize = Look.Theme.Caption, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        Break.Classes.Add("ep");
        Break.Classes.Add("ep-outline");
        AutomationProperties.SetName(Break, "Break branch");
        Chrome.Tip(Break, "Break this branch partway, leaving a jagged stub");
        Break.Click += async (_, _) => await breaks.Run();

        _more = commands.Single(c => c.Id == "show-more");
        Command more = _more;
        ShowMore = new Button
        {
            Width = 24,
            Height = 24,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new Icon("layout-grid", 13),
        };
        ShowMore.Classes.Add("ep");
        AutomationProperties.SetName(ShowMore, "Show me more");
        ToolTip.SetShowOnDisabled(ShowMore, true);
        ShowMore.Click += async (_, _) => await more.Run();

        _pieces = commands.Single(c => c.Id == "make-pieces");
        Command pieces = _pieces;
        Pieces = new Button
        {
            Width = 24,
            Height = 24,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new Icon("axe", 13),
            IsVisible = false,
        };
        Pieces.Classes.Add("ep");
        AutomationProperties.SetName(Pieces, "Make pieces from this tree");
        Chrome.Tip(Pieces, "Make pieces from this tree: its stump, a fallen log, a fallen branch and its leaves on the ground");
        Pieces.Click += async (_, _) => await pieces.Run();
        Refresh();

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto,Auto"),
            ColumnSpacing = Look.Theme.Space2,
            Height = 24,
            Margin = new Thickness(2, 0, 0, 6),
        };
        row.Children.Add(Title);
        Grid.SetColumn(scope, 1);
        row.Children.Add(scope);
        Grid.SetColumn(Break, 3);
        row.Children.Add(Break);
        Grid.SetColumn(Surprise, 4);
        row.Children.Add(Surprise);
        Grid.SetColumn(ShowMore, 5);
        row.Children.Add(ShowMore);
        Grid.SetColumn(Pieces, 6);
        row.Children.Add(Pieces);
        Child = new StackPanel { Children = { row, filter } };
    }

    /// <summary>Gets the title: the kind of prop shown.</summary>
    internal TextBlock Title { get; }

    /// <summary>Gets the Surprise me button (a new seed, the settings kept).</summary>
    internal Button Surprise { get; }

    /// <summary>Gets the Break button: shown only while the selected part is a heavy limb that can break.</summary>
    internal Button Break { get; }

    /// <summary>Gets the Show me more button (greyed until the nine-up view comes).</summary>
    internal Button ShowMore { get; }

    /// <summary>Gets the Make pieces button (the axe): shown for a painted tree only, as easy-props' Details has it.</summary>
    internal Button Pieces { get; }

    /// <summary>
    /// Greys Show me more, with its reason, while its command is unavailable, and lights it up when it is not; shows the
    /// axe while Make pieces can run (from the recipe alone: no engine call).
    /// </summary>
    internal void Refresh()
    {
        Pieces.IsVisible = _pieces.Unavailable() is null;
        string? reason = _more.Unavailable();
        Chrome.Tip(ShowMore, reason is null ? "Show me more: nine versions side by side" : $"Show me more. {reason}.");
        ShowMore.IsEnabled = reason is null;
        ShowMore.Opacity = reason is null ? 1 : 0.5;
    }

    /// <summary>Shows Break while its command can run (a heavy limb that is not broken is selected), and hides it otherwise.</summary>
    internal void RefreshBreak() => Break.IsVisible = _break.Unavailable() is null;

    /// <summary>Shows which kind of prop the panel holds.</summary>
    /// <param name="kind">"Rock" or "Floor tile".</param>
    internal void ShowKind(string kind) => Title.Text = kind;
}
