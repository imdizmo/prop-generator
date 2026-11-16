// Adapted from easy-props frontend/src/panels/PiecesDialog.tsx (MIT, © 2026 AHS12).
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Engine;

namespace PropGenerator.Desktop.Cards;

/// <summary>
/// Make pieces from this tree: one row per piece (its stump, a fallen log, a fallen branch, its leaves on the ground),
/// each ticked when the tree can make it and greyed with upstream's reason when it cannot. Which ones it can is asked of
/// the engine as the card opens; until it answers the rows say "Checking…" and nothing can be made. Make saves each
/// piece as a prop of its own; meanwhile the card is busy (Cancel greyed, Esc does not close it). Then it lists what
/// was saved, to stay with the tree or go to Your props.
/// </summary>
internal sealed class PiecesCard : Border, IBusyCard
{
    /// <summary>What a kind the tree cannot make says (upstream's words: only a palm, which has no branch).</summary>
    internal const string NoBranches = "This tree has no big branches to drop";

    private const string CheckingText = "Checking…";

    private readonly EnginePieceLabels _labels;
    private readonly Func<IReadOnlyList<string>, Task<PiecesMade>> _make;
    private readonly Action _close;
    private readonly Action<string> _goToProps;
    private readonly Dictionary<string, CheckBox> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _helps = new(StringComparer.Ordinal);
    private readonly StackPanel _body = new() { Spacing = Look.Theme.Space3 };
    private readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Look.Theme.TextPrimary, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = Text(string.Empty, Look.Theme.TextSecondary);
    private readonly TextBlock _note = Text(string.Empty, Look.Theme.TextSecondary);
    private readonly Button _x;
    private IReadOnlyList<string>? _offered;
    private string? _firstPath;

    /// <summary>Makes the card for a tree.</summary>
    /// <param name="treeName">The tree's name (its file's, else its starting point's, else "tree").</param>
    /// <param name="labels">Each piece's name and help, upstream's.</param>
    /// <param name="offered">Which pieces the tree can make, as the engine answers.</param>
    /// <param name="make">Makes and saves the kinds ticked.</param>
    /// <param name="close">Closes the card.</param>
    /// <param name="goToProps">Closes the card and opens Your props on this work file.</param>
    internal PiecesCard(string treeName, EnginePieceLabels labels, Task<IReadOnlyList<string>> offered, Func<IReadOnlyList<string>, Task<PiecesMade>> make, Action close, Action<string> goToProps)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(offered);
        _labels = labels;
        _make = make;
        _close = close;
        _goToProps = goToProps;
        TreeName = treeName;
        Kinds = [.. labels.Kinds.Select(k => k.Kind)];

        _x = Look.Chrome.IconButton("x", "Close without making any pieces (Esc).");
        AutomationProperties.SetName(_x, "Close");
        _x.Click += (_, _) => Close();
        CancelButton = Look.Chrome.TextButton("Cancel", "Close without making any pieces (Esc).");
        CancelButton.Click += (_, _) => Close();
        MakeButton = Look.Chrome.PrimaryButton("axe", "Make pieces", "Make the ticked pieces, each saved as a prop of its own.");
        MakeButton.Click += (_, _) => _ = MakeAsync();
        StayButton = Look.Chrome.TextButton("Stay with this tree", "Close and carry on with the tree.");
        StayButton.Click += (_, _) => _close();
        GoToPropsButton = Look.Chrome.PrimaryButton("house", "Go to Your props", "Open Your props on the first piece made.");
        GoToPropsButton.Click += (_, _) =>
        {
            if (_firstPath is { } first)
            {
                _goToProps(first);
            }
        };

        foreach (string kind in Kinds)
        {
            EnginePieceLabel label = labels.Of(kind)!;
            var help = Text(CheckingText, Look.Theme.TextTertiary);
            help.FontSize = Look.Theme.Caption;
            var text = new StackPanel { Spacing = 1 };
            text.Children.Add(new TextBlock { Text = label.Name, Foreground = Look.Theme.TextPrimary });
            text.Children.Add(help);
            var row = new CheckBox { Content = text, IsChecked = false, IsEnabled = false };
            AutomationProperties.SetName(row, label.Name);
            Look.Chrome.Tip(row, $"{label.Name}: {label.Help}.");
            row.IsCheckedChanged += (_, _) => Refresh();
            _rows[kind] = row;
            _helps[kind] = help;
        }

        ShowChoice();
        Width = 440;
        CardParts.Dress(this);
        Offered = TakeAsync(offered);
    }

    /// <summary>Raised when the card leaves the window (closed in any way): what it asks of the engine can stop.</summary>
    internal event Action? Detached;

    /// <summary>Gets the tree's name, as the pieces are named after it.</summary>
    internal string TreeName { get; }

    /// <summary>Gets every piece kind, in upstream's order (stump, log, branch, litter).</summary>
    internal IReadOnlyList<string> Kinds { get; }

    /// <summary>Gets the task that is done once the engine said which pieces the tree makes (or failed to).</summary>
    internal Task Offered { get; }

    /// <summary>Gets the task of the last Make (done at once until one is pressed).</summary>
    internal Task Making { get; private set; } = Task.CompletedTask;

    /// <inheritdoc/>
    public bool Busy { get; private set; }

    /// <summary>Gets the Make button.</summary>
    internal Button MakeButton { get; }

    /// <summary>Gets the Cancel button.</summary>
    internal Button CancelButton { get; }

    /// <summary>Gets the Stay with this tree button, shown once pieces are saved.</summary>
    internal Button StayButton { get; }

    /// <summary>Gets the Go to Your props button, shown once pieces are saved.</summary>
    internal Button GoToPropsButton { get; }

    /// <summary>Gets what the Make button says.</summary>
    internal string MakeLabel => Label(MakeButton).Text ?? string.Empty;

    /// <summary>Gets the card's title.</summary>
    internal string Title => _title.Text ?? string.Empty;

    /// <summary>Gets the line under the title.</summary>
    internal string Hint => _hint.Text ?? string.Empty;

    /// <summary>Gets the note above the buttons: what went wrong, if anything.</summary>
    internal string Note => _note.Text ?? string.Empty;

    /// <summary>Gets the names of the pieces saved (their props' names), or null until some are.</summary>
    internal IReadOnlyList<string>? SavedNames { get; private set; }

    /// <summary>The tick box of a piece.</summary>
    /// <param name="kind">stump, log, branch or litter.</param>
    /// <returns>Its box.</returns>
    internal CheckBox Row(string kind) => _rows[kind];

    /// <summary>A piece's name on its row.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>Upstream's name for it.</returns>
    internal string NameOf(string kind) => _labels.Of(kind)!.Name;

    /// <summary>What a piece's row says under its name: its help, "Checking…", or why the tree cannot make it.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The line.</returns>
    internal string HelpOf(string kind) => _helps[kind].Text ?? string.Empty;

    // The kinds ticked that the tree can make.
    private string[] Picked => [.. Kinds.Where(k => _offered?.Contains(k) == true && _rows[k].IsChecked == true)];

    /// <summary>Makes and saves the ticked pieces; the card is busy meanwhile.</summary>
    /// <returns>Done when they are saved, or it failed.</returns>
    internal Task MakeAsync()
    {
        if (Busy || Picked.Length == 0)
        {
            return Making;
        }

        Making = RunAsync(Picked);
        return Making;
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Detached?.Invoke();
    }

    private static TextBlock Text(string text, IBrush brush) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = Look.Theme.Label, Foreground = brush };

    private void Close()
    {
        if (!Busy)
        {
            _close();
        }
    }

    private async Task TakeAsync(Task<IReadOnlyList<string>> offered)
    {
        try
        {
            _offered = await offered.ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _offered = null;
            foreach (string kind in Kinds)
            {
                _helps[kind].Text = _labels.Of(kind)!.Help;
            }

            Say($"easy-props' engine could not say which pieces this tree makes ({Unwrap(e).Message}). Close the card and try again.", error: true);
            Refresh();
            return;
        }

        foreach (string kind in Kinds)
        {
            bool can = _offered.Contains(kind);
            _helps[kind].Text = can ? _labels.Of(kind)!.Help : NoBranches;
            _rows[kind].IsChecked = can;
        }

        Refresh();
    }

    private async Task RunAsync(string[] kinds)
    {
        Busy = true;
        Say(string.Empty, error: false);
        Refresh();
        PiecesMade made;
        try
        {
            made = await _make(kinds).ConfigureAwait(true);
        }
        finally
        {
            Busy = false;
        }

        if (made.Saved.Count == 0)
        {
            Say(made.Error is null ? string.Empty : $"No pieces were made: {made.Error}", error: made.Error is not null);
            Refresh();
            return;
        }

        SavedNames = [.. made.Saved.Select(p => p.Name)];
        _firstPath = made.Saved[0].Path;
        ShowSaved(made.Error);
    }

    private void Say(string text, bool error)
    {
        _note.Text = text;
        _note.Foreground = error ? Look.Theme.Danger : Look.Theme.TextSecondary;
        _note.IsVisible = text.Length > 0;
    }

    private void ShowChoice()
    {
        _title.Text = "Make pieces from this tree";
        _hint.Text = "Each piece is saved to Your props as a prop of its own, with this tree's bark and colours. They don't change when the tree does, so make them again after changing it.";
        _body.Children.Clear();
        _body.Children.Add(Head());
        _body.Children.Add(_hint);
        var list = new StackPanel { Spacing = Look.Theme.Space2 };
        list.Children.Add(new TextBlock { Text = $"Pieces of {TreeName}", FontSize = Look.Theme.Caption, Foreground = Look.Theme.TextTertiary });
        foreach (string kind in Kinds)
        {
            list.Children.Add(_rows[kind]);
        }

        _body.Children.Add(list);
        _body.Children.Add(_note);
        _body.Children.Add(Buttons(CancelButton, MakeButton));
        Child = _body;
        Say(string.Empty, error: false);
        Refresh();
    }

    private void ShowSaved(string? error)
    {
        int n = SavedNames!.Count;
        _title.Text = n == 1 ? "Piece saved" : string.Create(CultureInfo.InvariantCulture, $"{n} pieces saved");
        _hint.Text = $"{(n == 1 ? "It is" : "They are")} in Your props, each a prop of its own. Open one there to shape it, export it, or try Show me more.";
        _body.Children.Clear();
        _body.Children.Add(Head());
        _body.Children.Add(_hint);
        var list = new StackPanel { Spacing = 2, Margin = new Thickness(Look.Theme.Space3, 0, 0, 0) };
        foreach (string name in SavedNames)
        {
            list.Children.Add(new TextBlock { Text = name, Foreground = Look.Theme.TextPrimary });
        }

        _body.Children.Add(list);
        _body.Children.Add(_note);
        _body.Children.Add(Buttons(StayButton, GoToPropsButton));
        Say(error is null ? string.Empty : $"The rest were not made: {error}", error: error is not null);
        _x.IsEnabled = true;
        GoToPropsButton.Focus();
    }

    private DockPanel Head()
    {
        var row = new DockPanel { LastChildFill = true };
        if (_x.Parent is Panel old)
        {
            old.Children.Remove(_x);
        }

        DockPanel.SetDock(_x, Dock.Right);
        row.Children.Add(_x);
        if (_title.Parent is Panel was)
        {
            was.Children.Remove(_title);
        }

        row.Children.Add(_title);
        return row;
    }

    private static StackPanel Buttons(Button left, Button right)
    {
        foreach (Button b in new[] { left, right })
        {
            if (b.Parent is Panel old)
            {
                old.Children.Remove(b);
            }
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Look.Theme.Space2, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(left);
        buttons.Children.Add(right);
        return buttons;
    }

    private void Refresh()
    {
        bool known = _offered is not null;
        foreach (string kind in Kinds)
        {
            _rows[kind].IsEnabled = !Busy && known && _offered!.Contains(kind);
        }

        int picked = Picked.Length;
        Label(MakeButton).Text = Busy ? "Making pieces…" : picked == 1 ? "Make 1 piece" : string.Create(CultureInfo.InvariantCulture, $"Make {picked} pieces");
        MakeButton.IsEnabled = !Busy && picked > 0;
        Look.Chrome.Tip(MakeButton, !known ? "Wait until the card knows which pieces this tree makes." : picked == 0 ? "Tick the pieces to make first." : "Make the ticked pieces, each saved as a prop of its own.");
        CancelButton.IsEnabled = !Busy;
        _x.IsEnabled = !Busy;
    }

    // The label of a button made with an icon and a text.
    private static TextBlock Label(Button button) => ((StackPanel)button.Content!).Children.OfType<TextBlock>().Single();

    private static Exception Unwrap(Exception e) => e is AggregateException { InnerException: { } inner } ? inner : e;
}

/// <summary>What Make pieces saved, and why it stopped early, if it did.</summary>
/// <param name="Saved">The pieces saved, in the order made.</param>
/// <param name="Error">What went wrong, or null when every piece was saved.</param>
internal sealed record PiecesMade(IReadOnlyList<SavedPiece> Saved, string? Error);

/// <summary>One piece saved as a prop.</summary>
/// <param name="Kind">stump, log, branch or litter.</param>
/// <param name="Name">The prop's name (its file's, without .propgen).</param>
/// <param name="Path">Its work file.</param>
internal sealed record SavedPiece(string Kind, string Name, string Path);
