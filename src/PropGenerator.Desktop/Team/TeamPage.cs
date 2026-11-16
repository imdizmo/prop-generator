using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Cards;

namespace PropGenerator.Desktop.Team;

/// <summary>
/// Team props (Your props, Team props…): who the team server says you are, and the newest version of every prop the
/// team published, each with who published it and when, and Take, which saves a copy into Your props.
/// </summary>
internal sealed class TeamPage : Border
{
    private readonly TeamActions _team;
    private readonly Action<string> _taken;
    private readonly TextBlock _who = new() { TextWrapping = TextWrapping.Wrap, FontSize = Look.Theme.Label, Foreground = Look.Theme.TextSecondary };
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private IReadOnlyList<TeamProp> _props = [];

    /// <summary>Builds the card and starts reading the team's props.</summary>
    /// <param name="team">The team server's actions.</param>
    /// <param name="close">Closes the card.</param>
    /// <param name="taken">Called with the new work file when a prop is taken.</param>
    internal TeamPage(TeamActions team, Action close, Action<string> taken)
    {
        _team = team;
        _taken = taken;
        CardParts.Dress(this);
        Width = 640;
        Height = 480;
        Button reload = Look.Chrome.TextButton("Reload", "Read the team's props again from the team server.");
        AutomationProperties.SetName(reload, "Reload");
        reload.Click += async (_, _) => await LoadAsync();
        var bar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 8) };
        DockPanel.SetDock(reload, Dock.Right);
        bar.Children.Add(reload);
        bar.Children.Add(_who);
        DockPanel head = CardParts.Header("Team props", "Close Team props (Esc).", close);
        var page = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(bar, Dock.Top);
        page.Children.Add(head);
        page.Children.Add(bar);
        page.Children.Add(new ScrollViewer { Content = _rows });
        Child = page;
        Loading = LoadAsync();
    }

    /// <summary>Gets the last read of the team's props (for the tests).</summary>
    internal Task Loading { get; private set; }

    /// <summary>Gets the names of the props shown, in order.</summary>
    internal IReadOnlyList<string> Names => [.. _props.Select(p => p.Name)];

    /// <summary>Gets who the card says you are, or why it cannot say.</summary>
    internal string Who => _who.Text ?? string.Empty;

    /// <summary>Takes a prop, by its name, as its Take button does.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The new work file, or null.</returns>
    internal Task<string?> TakeAsync(string name) => TakeAsync(_props.First(p => p.Name == name));

    // Takes the very row a Take button is on (two props may share a name).
    private async Task<string?> TakeAsync(TeamProp prop)
    {
        string? path = await _team.TakeAsync(prop);
        if (path is not null)
        {
            _taken(path);
        }

        return path;
    }

    private static TextBlock Line(string text, IBrush brush) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = Look.Theme.Label, Foreground = brush, VerticalAlignment = VerticalAlignment.Center };

    private async Task LoadAsync()
    {
        _who.Text = _team.Server is { } at ? $"Asking {at.Authority}…" : "No team server is set (Settings, Team): Prop Generator works offline.";
        _rows.Children.Clear();
        if (_team.Server is null)
        {
            return;
        }

        (TeamMe? me, string? problem) = await _team.CheckAsync();
        _who.Text = me?.Said ?? problem;
        if (me is null)
        {
            return;
        }

        _props = [.. (await _team.ListAsync() ?? []).OrderByDescending(p => p.PublishedAt)];
        if (_props.Count == 0)
        {
            _rows.Children.Add(Line("Nobody on the team has published a prop yet. Publish one from Your props.", Look.Theme.TextSecondary));
        }

        foreach (TeamProp p in _props)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
            grid.Children.Add(Line(p.Name, Look.Theme.TextPrimary));
            TextBlock note = Line(string.Create(CultureInfo.InvariantCulture, $"{p.ByName} · version {p.Version} · {p.PublishedAt.ToLocalTime():d MMM yyyy}"), Look.Theme.TextTertiary);
            Grid.SetColumn(note, 1);
            grid.Children.Add(note);
            Button take = Look.Chrome.TextButton("Take", "Save a copy of this prop into Your props, with its picture. Publishing it again later makes a new version of the team's prop.");
            AutomationProperties.SetName(take, $"Take {p.Name}");
            take.Click += async (_, _) => await TakeAsync(p);
            Grid.SetColumn(take, 2);
            grid.Children.Add(take);
            _rows.Children.Add(new Border { Child = grid, Padding = new Thickness(8, 5) });
        }
    }
}
