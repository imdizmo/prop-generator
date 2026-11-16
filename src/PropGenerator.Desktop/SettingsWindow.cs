using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace PropGenerator.Desktop;

/// <summary>
/// The app's settings: Claude's connection, the export look and picture size, and the viewport's wind strength and ground slope. For
/// Claude: the toggle "Allow Claude to connect" (off to begin with), the address Claude connects to and the token it
/// must send (each as text that can be selected, with a copy button), a button for a new token, the line to type into a
/// terminal to connect Claude Code, and whether the server is listening or why not.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly ClaudeConnection _claude;
    private readonly ToggleSwitch _allow;
    private readonly TextBlock _status;
    private readonly TextBox _address;
    private readonly TextBox _token;
    private readonly TextBox _command;
    private bool _syncing;

    /// <summary>Builds the window.</summary>
    /// <param name="claude">Claude's connection.</param>
    /// <param name="prefs">Where the export look is kept; without it the Export section is left out.</param>
    public SettingsWindow(ClaudeConnection claude, Prefs? prefs = null)
        : this(claude, prefs, null)
    {
    }

    /// <summary>Builds the window, the wind's strength going through the view state.</summary>
    /// <param name="claude">Claude's connection.</param>
    /// <param name="prefs">Where the export look is kept; without it the Export section is left out.</param>
    /// <param name="view">The view state the wind's strength goes through, so the toolbar follows; null writes the prefs alone.</param>
    /// <param name="team">The team server's actions; without them (or the prefs) the Team section is left out.</param>
    internal SettingsWindow(ClaudeConnection claude, Prefs? prefs, Commands.ViewState? view, Team.TeamActions? team = null)
    {
        ArgumentNullException.ThrowIfNull(claude);
        _claude = claude;
        Title = "Settings — Prop Generator";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Background = Look.Theme.SurfacePanel;

        _allow = new ToggleSwitch { OnContent = "On", OffContent = "Off", IsChecked = claude.Enabled };
        Tip(_allow, "Allow Claude to connect",
            "Lets Claude (Claude Code) drive this app from your own computer: read and set every control, save, export and take a picture. Off until you turn it on.");
        _allow.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing && _allow.IsChecked is bool on)
            {
                _ = _claude.SetEnabledAsync(on);
            }
        };

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        _address = ReadOnlyBox("Address", "The address Claude connects to. It only ever listens on this computer (127.0.0.1), never on the network.");
        _token = ReadOnlyBox("Token", "The secret Claude must send with every request. Anyone with it can change your wood settings while the connection is on, so keep it private.");
        _command = ReadOnlyBox("Command", "The line to type in a terminal to add this app to Claude Code with the right address and token. Run it once.");

        var newToken = new Button { Content = "New token" };
        Tip(newToken, "New token",
            "Makes a new secret token. The old one stops working at once, so Claude has to be given the new one.");
        newToken.Click += (_, _) => _claude.NewToken();

        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = "Claude connection", FontWeight = FontWeight.SemiBold, FontSize = 16 });
        stack.Children.Add(Labelled("Allow Claude to connect", _allow));
        stack.Children.Add(_status);
        stack.Children.Add(Labelled("Address", WithCopy(_address, "Copy address", "Copies the address to the clipboard so you can paste it where Claude is set up.")));
        stack.Children.Add(Labelled("Token", WithCopy(_token, "Copy token", "Copies the token to the clipboard so you can paste it where Claude is set up.", newToken)));
        stack.Children.Add(Labelled("Claude Code", WithCopy(_command, "Copy command", "Copies the whole line for Claude Code to the clipboard, ready to paste into a terminal.")));
        if (prefs is not null)
        {
            stack.Children.Add(new TextBlock { Text = "Export", FontWeight = FontWeight.SemiBold, FontSize = 16, Margin = new Thickness(0, 10, 0, 0) });
            stack.Children.Add(Labelled("Look", LookChoice(prefs)));
            stack.Children.Add(Labelled("Picture size", PictureSizeChoice(prefs)));
            stack.Children.Add(new TextBlock { Text = "Viewport", FontWeight = FontWeight.SemiBold, FontSize = 16, Margin = new Thickness(0, 10, 0, 0) });
            stack.Children.Add(Labelled("Wind strength", WindStrength(prefs, view)));
            stack.Children.Add(Labelled("Slope", Degrees(View.SlopePopover.SlopeSpec, View.SlopePopover.SlopeKeywords, prefs, view)));
            stack.Children.Add(Labelled("Downhill", Degrees(View.SlopePopover.DownhillSpec, View.SlopePopover.DownhillKeywords, prefs, view)));
        }

        if (prefs is not null && team is not null)
        {
            stack.Children.Add(new TextBlock { Text = "Team", FontWeight = FontWeight.SemiBold, FontSize = 16, Margin = new Thickness(0, 10, 0, 0) });
            TeamSection(stack, prefs, team);
        }

        Content = stack;

        _claude.Changed += Refresh;
        Closed += (_, _) => _claude.Changed -= Refresh;
        Refresh();
    }

    // The team server: typed here, else the Launcher's; Check asks who the server says you are.
    private static void TeamSection(StackPanel stack, Prefs prefs, Team.TeamActions team)
    {
        var server = new TextBox
        {
            Text = prefs.TeamServer,
            PlaceholderText = team.LauncherAddress is { } given ? $"{given} (from the Launcher)" : "Not set: Prop Generator works offline",
        };
        Tip(server, "Team server",
            "The team's server, as a Tailscale address or name (with :port when it is not 8781). Leave it empty to use the one the Launcher gives, or to work offline without one.");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        var check = new Button { Content = "Check" };
        Tip(check, "Check the team server", "Asks the team server who it says you are (by your Tailscale account), to see that the address works.");
        async Task CheckAsync()
        {
            prefs.TeamServer = string.IsNullOrWhiteSpace(server.Text) ? null : server.Text.Trim();
            prefs.Save();
            status.Text = team.Server is { } at ? $"Asking {at.Authority}…" : string.Empty;
            (Team.TeamMe? me, string? problem) = await team.CheckAsync();
            status.Text = me?.Said ?? problem;
        }

        check.Click += async (_, _) => await CheckAsync();
        server.LostFocus += (_, _) =>
        {
            string? typed = string.IsNullOrWhiteSpace(server.Text) ? null : server.Text.Trim();
            if (typed != prefs.TeamServer)
            {
                prefs.TeamServer = typed;
                prefs.Save();
            }
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        row.Children.Add(server);
        Grid.SetColumn(check, 1);
        row.Children.Add(check);
        stack.Children.Add(Labelled("Server", row));
        stack.Children.Add(status);
        status.Text = team.Server is null ? "No team server is set: Prop Generator works offline." : "Not checked yet.";
    }

    private static void Tip(Control c, string name, string tip)
    {
        AutomationProperties.SetName(c, name);
        ToolTip.SetTip(c, tip);
    }

    private static Grid Labelled(string label, Control input)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), ColumnSpacing = 8 };
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(input, 1);
        g.Children.Add(input);
        return g;
    }

    // easy-props 0.4.2's export look: how the game will draw the prop. Comic also makes the colours in the files flat.
    private static ComboBox LookChoice(Prefs prefs)
    {
        (string Value, string Label)[] looks = [("lit", "Lit"), ("cartoon", "Cartoon"), ("cel", "Comic")];
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 160,
        };
        foreach (var (_, label) in looks)
        {
            combo.Items.Add(new ComboBoxItem { Content = label });
        }

        combo.SelectedIndex = Math.Max(0, Array.FindIndex(looks, look => look.Value == prefs.ExportLook));
        Tip(combo, "Export look",
            "How your game will draw the props you export and send to Blender. Lit is the usual look. Cartoon and Comic need the toon materials in the engine kit; Comic also makes the colours in the files flat, a few tones per part.");
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && prefs.ExportLook != looks[combo.SelectedIndex].Value)
            {
                prefs.ExportLook = looks[combo.SelectedIndex].Value;
                prefs.Save();
            }
        };
        return combo;
    }

    // easy-props 0.4.5's picture size: how many pixels across the closest level's bark, stone and cut ends are.
    private static ComboBox PictureSizeChoice(Prefs prefs)
    {
        ComboBox combo = PictureSizeBox(prefs.ExportPictureSize);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && prefs.ExportPictureSize != Recipes.ExportOptions.PictureSizes[combo.SelectedIndex])
            {
                prefs.ExportPictureSize = Recipes.ExportOptions.PictureSizes[combo.SelectedIndex];
                prefs.Save();
            }
        };
        return combo;
    }

    /// <summary>
    /// A picture size choice (512, 1024, 2048) with upstream's help, showing <paramref name="size"/>; the Export many
    /// card offers the same one.
    /// </summary>
    /// <param name="size">The size chosen now (anything else shows 512).</param>
    /// <returns>The choice, named "Picture size".</returns>
    internal static ComboBox PictureSizeBox(int size)
    {
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 160,
        };
        foreach (int option in Recipes.ExportOptions.PictureSizes)
        {
            combo.Items.Add(new ComboBoxItem { Content = option.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        combo.SelectedIndex = Math.Max(0, Recipes.ExportOptions.PictureSizes.ToList().IndexOf(Recipes.ExportOptions.KeptPictureSize(size)));
        Tip(combo, "Picture size", Recipes.ExportOptions.PictureSizeHelp);
        return combo;
    }

    // A double as the decimal it reads as (0.3 -> 0.3m): a cast gives the binary value's digits on newer runtimes.
    private static decimal Dec(double d) => decimal.Parse(d.ToString("R", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);

    // easy-props' Wind strength: how hard the wind blows in the view when Wind in the view is on.
    private NumericUpDown WindStrength(Prefs prefs, Commands.ViewState? view)
    {
        var box = new NumericUpDown
        {
            Minimum = Dec(Prefs.MinWindStrength),
            Maximum = Dec(Prefs.MaxWindStrength),
            Increment = 0.1m,
            FormatString = "0.0",
            Value = Dec(prefs.WindStrength),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 160,
        };
        Tip(box, "Wind strength",
            "From a calm day (0.3) to a storm (2). The middle, 1, is how the engine kits start. Switch the wind on with View > Wind in the view.");
        bool syncing = false;
        box.ValueChanged += (_, e) =>
        {
            if (syncing || e.NewValue is not decimal value || (double)value == prefs.WindStrength)
            {
                return;
            }

            if (view is not null)
            {
                // Through the view state, so the toolbar's Wind popover follows.
                view.WindStrength = (double)value;
            }
            else
            {
                prefs.WindStrength = (double)value;
                prefs.Save();
            }
        };
        if (view is not null)
        {
            void Follow()
            {
                syncing = true;
                box.Value = Dec(view.WindStrength);
                syncing = false;
            }

            view.Changed += Follow;
            Closed += (_, _) => view.Changed -= Follow;
        }

        return box;
    }

    // easy-props' Viewport rows Slope and Downhill: the ground's tilt in the view, in degrees (only the view changes).
    // Upstream's search words go in the box's help text (this window has no search). Through the view state when there
    // is one, so the toolbar's Slope popover and the view follow.
    private NumericUpDown Degrees(ParamSpec spec, string keywords, Prefs prefs, Commands.ViewState? view)
    {
        bool slope = spec.Key == "groundSlope";
        double Current() => view is null ? (slope ? prefs.GroundSlope : prefs.GroundDownhill) : (slope ? view.GroundSlope : view.GroundDownhill);
        var box = new NumericUpDown
        {
            Minimum = Dec(spec.Min ?? 0),
            Maximum = Dec(spec.Max ?? 0),
            Increment = Dec(spec.Step ?? 1),
            FormatString = "0",
            Value = Dec(Current()),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 160,
        };
        Tip(box, spec.Label, spec.Tooltip);
        AutomationProperties.SetHelpText(box, keywords);
        bool syncing = false;
        box.ValueChanged += (_, e) =>
        {
            if (syncing || e.NewValue is not decimal value || (double)value == Current())
            {
                return;
            }

            if (view is not null)
            {
                if (slope)
                {
                    view.GroundSlope = (double)value;
                }
                else
                {
                    view.GroundDownhill = (double)value;
                }
            }
            else
            {
                if (slope)
                {
                    prefs.GroundSlope = Commands.ViewState.KeptSlope((double)value);
                }
                else
                {
                    prefs.GroundDownhill = Commands.ViewState.KeptDownhill((double)value);
                }

                prefs.Save();
            }
        };
        if (view is not null)
        {
            void Follow()
            {
                syncing = true;
                box.Value = Dec(Current());
                syncing = false;
            }

            view.Changed += Follow;
            Closed += (_, _) => view.Changed -= Follow;
        }

        return box;
    }

    private static TextBox ReadOnlyBox(string name, string tip)
    {
        var box = new TextBox { IsReadOnly = true, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("monospace") };
        Tip(box, name, tip);
        return box;
    }

    private Grid WithCopy(TextBox box, string name, string tip, Button? extra = null)
    {
        var copy = new Button { Content = "Copy" };
        Tip(copy, name, tip);
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(box.Text ?? string.Empty);
            }
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        row.Children.Add(box);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        if (extra is not null)
        {
            Grid.SetColumn(extra, 2);
            row.Children.Add(extra);
        }

        return row;
    }

    private void Refresh()
    {
        _syncing = true;
        _allow.IsChecked = _claude.Enabled;
        _syncing = false;
        _status.Text = _claude.Status;
        _address.Text = _claude.Address;
        _token.Text = _claude.Token;
        _command.Text = _claude.ClaudeCodeCommand;
    }
}
