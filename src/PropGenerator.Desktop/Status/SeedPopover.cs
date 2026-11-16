// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Status;

/// <summary>
/// The seed in the status bar, as easy-props has it: a "Seed n" button that opens a small panel to type, paste or copy
/// the seed, with Surprise me under it. The field takes what is typed when Enter is pressed or the field is left; text
/// that is not a seed in range keeps the old seed and says so in red. Esc puts the old seed back and closes the panel.
/// </summary>
internal sealed partial class SeedPopover
{
    private const string Caption = "Same settings and seed give the same prop.";

    private readonly SettingsPanel _panel;
    private readonly Func<string, Task<bool>> _copy;
    private readonly DispatcherTimer _copied = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private bool _closing;

    /// <summary>Builds the button and the panel it opens.</summary>
    /// <param name="panel">The settings; the seed is read from it and set through it (one undo step).</param>
    /// <param name="commands">The window's commands; Surprise me runs its own.</param>
    /// <param name="copy">Puts text on the clipboard; false when it could not.</param>
    internal SeedPopover(SettingsPanel panel, IReadOnlyList<Command> commands, Func<string, Task<bool>> copy)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(copy);
        _panel = panel;
        _copy = copy;

        Trigger = new Button
        {
            Height = 20,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new TextBlock { FontSize = Theme.Label, VerticalAlignment = VerticalAlignment.Center },
        };
        Trigger.Classes.Add("ep");
        Trigger.Classes.Add("ep-text");
        AutomationProperties.SetName(Trigger, "Seed");
        Chrome.Tip(Trigger, "Type, paste or copy the seed");
        Trigger.Click += (_, _) => Toggle();

        Field = new TextBox
        {
            Height = 24,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(6, 0),
            FontSize = Theme.Label,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Theme.Radius2),
            Background = Theme.SurfaceSunken,
            BorderBrush = Theme.BorderSubtle,
        };
        AutomationProperties.SetName(Field, "Seed");
        Chrome.Tip(Field, "The seed: a whole number. The same settings and seed always give the same prop. Enter applies it, Esc puts the old one back.");
        Field.KeyDown += OnFieldKey;
        Field.LostFocus += (_, _) => Apply();

        CopyButton = Chrome.IconTextButton("clipboard-copy", "Copy", "Copy the seed to the clipboard, to paste it somewhere else or keep it.");
        CopyButton.Height = 24;
        CopyButton.Click += async (_, _) => await CopyAsync();

        Surprise = Chrome.IconTextButton("dices", "Surprise me", commands.Single(c => c.Id == "surprise").Tooltip ?? "Try a different one with the same shape: a new seed.");
        Surprise.Height = 24;
        Surprise.HorizontalAlignment = HorizontalAlignment.Left;
        Command surprise = commands.Single(c => c.Id == "surprise");
        Surprise.Click += async (_, _) =>
        {
            await surprise.Run();
            Refresh(force: true);
        };

        Message = new TextBlock
        {
            FontSize = Theme.Caption,
            Foreground = Theme.TextTertiary,
            TextWrapping = TextWrapping.Wrap,
            Text = Caption,
        };
        Chrome.Tip(Message, "What the seed does, or what is wrong with the text in the field.");

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 4, Height = 24 };
        var label = new TextBlock { Text = "Seed", FontSize = Theme.Label, Foreground = Theme.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        Chrome.Tip(label, "The number that picks which random version of the piece you get.");
        Grid.SetColumn(Field, 1);
        Grid.SetColumn(CopyButton, 2);
        row.Children.Add(label);
        row.Children.Add(Field);
        row.Children.Add(CopyButton);

        Content = Chrome.StylePopover(new Border
        {
            Width = 250,
            Padding = new Thickness(Theme.Space2),
            Child = new StackPanel { Spacing = Theme.Space2, Children = { row, Surprise, Message } },
        });
        Content.MinWidth = 0;

        Popup = new Popup
        {
            Child = Content,
            PlacementTarget = Trigger,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopRight,
            PlacementGravity = PopupGravity.TopLeft,
            VerticalOffset = -6,
            IsLightDismissEnabled = true,
            OverlayDismissEventPassThrough = true,
        };
        Popup.Closed += (_, _) => _closing = false;
        _copied.Tick += (_, _) =>
        {
            _copied.Stop();
            CopyText.Text = "Copy";
        };
        Refresh();
    }

    /// <summary>Gets the "Seed n" button in the status bar.</summary>
    internal Button Trigger { get; }

    /// <summary>Gets the popup the button opens (the status bar holds it).</summary>
    internal Popup Popup { get; }

    /// <summary>Gets the popover's box, the content of <see cref="Popup"/>.</summary>
    internal Border Content { get; }

    /// <summary>Gets the field with the seed.</summary>
    internal TextBox Field { get; }

    /// <summary>Gets the Copy button.</summary>
    internal Button CopyButton { get; }

    /// <summary>Gets the Surprise me button.</summary>
    internal Button Surprise { get; }

    /// <summary>Gets the caption under the buttons: what the seed does, or what is wrong with the field's text.</summary>
    internal TextBlock Message { get; }

    /// <summary>Gets a value indicating whether the panel is open.</summary>
    internal bool IsOpen => Popup.IsOpen;

    /// <summary>Gets the text of the Copy button ("Copy", or "Copied" for 1.5 s after copying).</summary>
    internal string CopyLabel => CopyText.Text ?? string.Empty;

    /// <summary>Gets a value indicating whether the clock that puts "Copy" back after "Copied" is running.</summary>
    internal bool CopiedShowing => _copied.IsEnabled;

    /// <summary>Stops the clock that puts "Copy" back after "Copied" (the window is closing).</summary>
    internal void Stop() => _copied.Stop();

    /// <summary>Opens the panel with the seed selected, ready to type or paste over.</summary>
    internal void Open()
    {
        if (Popup.IsOpen)
        {
            return;
        }

        Refresh(force: true);
        Popup.IsOpen = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                Field.Focus();
                Field.SelectAll();
            },
            DispatcherPriority.Background);
    }

    /// <summary>Closes the panel.</summary>
    internal void Close() => Popup.IsOpen = false;

    /// <summary>
    /// Shows the seed as it is now on the button and, unless it is being typed in (the panel is open or the field has
    /// the keyboard), in the field, clearing any complaint.
    /// </summary>
    /// <param name="force">True to rewrite the field even while it is being typed in.</param>
    internal void Refresh(bool force = false)
    {
        string seed = Current;
        ((TextBlock)Trigger.Content!).Text = "Seed " + seed;
        if (!force && (Popup.IsOpen || Field.IsFocused))
        {
            return;
        }

        Field.Text = seed;
        Say(Caption, error: false);
    }

    /// <summary>Takes the field's text as the seed: a seed in range is set (one undo step); anything else is refused and said.</summary>
    internal void Apply()
    {
        if (_closing)
        {
            return;
        }

        ParamSpec? spec = _panel.SeedSpec;
        if (spec is null)
        {
            return;
        }

        (long min, long max) = Range(spec);
        long? seed = Parse(Field.Text ?? string.Empty, min, max);
        if (seed is null)
        {
            Say(NotASeed(min, max), error: true);
            return;
        }

        string? problem = _panel.SetFromOutside("seed", JsonSerializer.SerializeToElement(seed.Value));
        if (problem is not null)
        {
            Say(problem, error: true);
            return;
        }

        ((TextBlock)Trigger.Content!).Text = "Seed " + seed.Value.ToString(CultureInfo.InvariantCulture);
        Field.Text = seed.Value.ToString(CultureInfo.InvariantCulture);
        Say(Caption, error: false);
    }

    /// <summary>Reads a seed from text the way easy-props does: spaces and commas are dropped, and text around one number is ignored.</summary>
    /// <param name="text">What was typed or pasted.</param>
    /// <param name="min">The smallest seed.</param>
    /// <param name="max">The largest seed.</param>
    /// <returns>The seed, or null when the text does not hold exactly one whole number in range.</returns>
    internal static long? Parse(string text, long min, long max)
    {
        ArgumentNullException.ThrowIfNull(text);
        MatchCollection numbers = Number().Matches(SpacesAndCommas().Replace(text, string.Empty));
        if (numbers.Count != 1 || !WholeNumber().IsMatch(numbers[0].Value)
            || !long.TryParse(numbers[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long seed)
            || seed < min || seed > max)
        {
            return null;
        }

        return seed;
    }

    /// <summary>The words for text that is not a seed, naming the range.</summary>
    /// <param name="min">The smallest seed.</param>
    /// <param name="max">The largest seed.</param>
    /// <returns>The sentence.</returns>
    internal static string NotASeed(long min, long max) =>
        string.Create(CultureInfo.InvariantCulture, $"That isn't a seed: use a whole number from {min} to {max}.");

    [GeneratedRegex(@"[\s,]")]
    private static partial Regex SpacesAndCommas();

    [GeneratedRegex(@"[-+]?\d+(\.\d+)?")]
    private static partial Regex Number();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex WholeNumber();

    private TextBlock CopyText => (TextBlock)((StackPanel)CopyButton.Content!).Children[1];

    private string Current => _panel.SeedValue.ToString(CultureInfo.InvariantCulture);

    private static (long Min, long Max) Range(ParamSpec spec) =>
        spec.Type == "seed" ? (0, uint.MaxValue) : ((long)(spec.Min ?? 0), (long)(spec.Max ?? int.MaxValue));

    private void Toggle()
    {
        if (Popup.IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    private void Say(string text, bool error)
    {
        Message.Text = text;
        Message.Foreground = error ? Theme.Danger : Theme.TextTertiary;
        Field.BorderBrush = error ? Theme.Danger : Theme.BorderSubtle;
        AutomationProperties.SetHelpText(Field, text);
    }

    private void OnFieldKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Apply();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // The old seed goes back first, so leaving the field does not apply what was typed.
            _closing = true;
            Field.Text = Current;
            Say(Caption, error: false);
            Close();
            e.Handled = true;
        }
    }

    private async Task CopyAsync()
    {
        if (await _copy(Current))
        {
            CopyText.Text = "Copied";
            _copied.Stop();
            _copied.Start();
            Say(Caption, error: false);
            return;
        }

        Field.Text = Current;
        Say("Couldn't copy it. The number is selected, so you can copy it yourself.", error: true);
        Field.Focus();
        Field.SelectAll();
    }
}
