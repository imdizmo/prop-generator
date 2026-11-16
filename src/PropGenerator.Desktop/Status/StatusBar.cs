// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PropGenerator.Desktop.Look;
using PropGenerator.Desktop.Recipes;
using T = PropGenerator.Desktop.Look.Theme;

namespace PropGenerator.Desktop.Status;

/// <summary>
/// The bar along the bottom, as easy-props' status bar laid out to the shared design: on the left whether the piece is
/// saved, then what the mouse does in the view or the latest message (for 6 s; an error stays, in red, until the next
/// action), cut with an ellipsis before it would touch the right side; on the right the seed with its panel, the
/// "Over budget" warning chip with Make it fit, the detail level on screen (above level 0), the faces at full detail
/// against the budget and the budget picker. The right side never shrinks: when the bar is narrower than it, its items
/// drop from the left end.
/// </summary>
internal sealed class StatusBar : Border
{
    /// <summary>What the mouse does in the view, written from the view's real bindings.</summary>
    internal const string Hint = "Left drag to orbit · Right drag to pan · Scroll to zoom";

    /// <summary>How long a message shows before the hint comes back, in milliseconds.</summary>
    internal const int MessageMs = 6000;

    /// <summary>What the greyed Make it fit says: what it will do, and that it is not here yet.</summary>
    internal const string MakeItFitTip = "Make it fit: lowers the controls that cost the most until this prop fits its budget.";

    // The message line's tooltip while it has no text.
    private const string LineTip = "What the mouse does in the view, or the latest message from the app.";

    private static readonly int FloorFaces = TileMesh.Square().Indices.Length / 3;

    private readonly MainWindow _w;
    private readonly TextBlock _line;
    private readonly TextBlock _lod;
    private bool _syncing;

    /// <summary>Builds the bar and starts following the window.</summary>
    /// <param name="window">The window; its view state, file, rock and commands are followed.</param>
    /// <param name="line">The text block that shows the hint or the message (Claude's get_state reads it).</param>
    internal StatusBar(MainWindow window, TextBlock line)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(line);
        _w = window;
        _line = line;
        Height = T.StatusBarHeight;
        Padding = new Thickness(T.Space2, 0);
        Background = T.SurfacePanel;
        BorderBrush = T.BorderSubtle;
        BorderThickness = new Thickness(0, 1, 0, 0);
        TextElement.SetForeground(this, T.TextTertiary);
        TextElement.SetFontSize(this, T.Label);

        // The message's full text is its tooltip, since it is cut when the bar is narrow.
        _line.VerticalAlignment = VerticalAlignment.Center;
        _line.TextTrimming = TextTrimming.CharacterEllipsis;
        _line.Foreground = T.TextSecondary;
        _line.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty)
            {
                Chrome.Tip(_line, string.IsNullOrEmpty(_line.Text) ? LineTip : _line.Text);
            }
        };
        Chrome.Tip(_line, LineTip);

        Seed = new SeedPopover(window.Panel, window.Commands, window.CopyAsync);
        MakeItFit = new Button
        {
            Content = new TextBlock
            {
                Text = "Make it fit",
                FontSize = T.Keys,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            },
            MinHeight = 0,
            MinWidth = 0,
            Height = 16,
            Padding = new Thickness(7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        MakeItFit.Click += (_, _) => _ = Commands.CommandList.Find(window.Commands, "make-it-fit")?.Run();
        MakeItFit.Classes.Add("ep");
        MakeItFit.Classes.Add("ep-chip-action");
        AutomationProperties.SetName(MakeItFit, "Make it fit");
        Chrome.Tip(MakeItFit, MakeItFitTip);
        ToolTip.SetShowOnDisabled(MakeItFit, true);
        Warning = new Border
        {
            Height = 20,
            Padding = new Thickness(7, 0, 3, 0),
            CornerRadius = new CornerRadius(T.RadiusPill),
            Background = T.WarningSoft,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Icon("triangle-alert", 14) { Foreground = T.Warning, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "Over budget", Foreground = T.Warning, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                    MakeItFit,
                },
            },
        };
        _lod = Readout("Faces in the detail level on screen");
        FacesReadout = Readout("Faces at full detail, against this budget");
        SaveLabel = Readout("Whether this piece has changes that are not saved to its work file.");
        SaveLabel.Foreground = T.TextSecondary;
        SaveLabel.Margin = new Thickness(0, 0, T.Space4, 0);
        Picker = new ComboBox
        {
            MinHeight = 0,
            MinWidth = 0,
            Height = 20,
            Padding = new Thickness(6, 0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(T.Radius1),
            FontSize = T.Label,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ItemsSource = FaceBudget.Targets.Select(t => t.Label).ToArray(),
        };
        AutomationProperties.SetName(Picker, "What this prop is for");
        Chrome.Tip(Picker, "What this prop is for");
        Picker.SelectionChanged += (_, _) =>
        {
            if (!_syncing && Picker.SelectedIndex >= 0)
            {
                _w.ViewState.Budget = FaceBudget.Targets[Picker.SelectedIndex].Id;
            }
        };

        // The picker's list opens upward from it, right edges aligned, as the bar's popovers do.
        Picker.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<Popup>("PART_Popup") is { } list)
            {
                OpenUpward(list);
            }
        };

        Right = new StatusRight
        {
            Spacing = T.Space4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Seed.Trigger, Warning, _lod, FacesReadout, Picker },
        };
        var left = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(SaveLabel, Dock.Left);
        left.Children.Add(SaveLabel);
        left.Children.Add(_line);
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(Right, Dock.Right);
        Right.Margin = new Thickness(T.Space4, 0, 0, 0);
        row.Children.Add(Right);
        row.Children.Add(left);
        Child = new Panel { Children = { row, Seed.Popup } };

        MessageTimer.Tick += (_, _) => ShowHint();
        window.ViewState.Changed += Refresh;
        window.Files.Changed += Refresh;
        window.Files.RecipeChanged += _ => Refresh();
        window.Rocks.Showed += (_, _) => Refresh();
        window.Rocks.Counted += Refresh;
        window.Files.Renamed += Refresh;
        window.Files.FloorShown += Refresh;
        window.Panel.PaintChanged += _ => Refresh();
        window.Panel.ToneChanged += Refresh;
        window.Panel.Committed += _ =>
        {
            Acted();
            Refresh();
        };
        ShowHint();
        Refresh();
    }

    /// <summary>Gets the seed button and its panel.</summary>
    internal SeedPopover Seed { get; }

    /// <summary>Gets the budget picker.</summary>
    internal ComboBox Picker { get; }

    /// <summary>Gets a value indicating whether the line shows an error (in the danger colour, kept until the next action).</summary>
    internal bool IsError { get; private set; }

    /// <summary>Gets the timer that brings the hint back after a message.</summary>
    internal DispatcherTimer MessageTimer { get; } = new() { Interval = TimeSpan.FromMilliseconds(MessageMs) };

    /// <summary>Gets the "Over budget" warning chip (shown only while over), with Make it fit in it.</summary>
    internal Border Warning { get; }

    /// <summary>Gets the right side: the seed, the warning, the detail level, the faces and the budget picker.</summary>
    internal StatusRight Right { get; }

    /// <summary>Gets Make it fit: the chip's one action, greyed with the reason while it cannot run.</summary>
    internal Button MakeItFit { get; }

    /// <summary>Gets the text of the detail level readout, empty while the level is 0.</summary>
    internal string LodText => _lod.IsVisible ? _lod.Text ?? string.Empty : string.Empty;

    /// <summary>Gets the faces readout: "n / budget faces".</summary>
    internal TextBlock FacesReadout { get; }

    /// <summary>Gets the save state's label, first on the left.</summary>
    internal TextBlock SaveLabel { get; }

    /// <summary>Gets the save state's words: "Unsaved", "Not saved yet" or "Saved".</summary>
    internal string SaveText => SaveLabel.Text ?? string.Empty;

    /// <summary>Shows a message in place of the hint: for 6 s, or (an error) until the next action. A message does not replace an error showing.</summary>
    /// <param name="message">The message.</param>
    /// <param name="error">True for a failure, shown in the danger colour.</param>
    internal void Show(string message, bool error)
    {
        // Only an action (or a newer error) takes an error off the line.
        if (IsError && !error)
        {
            return;
        }

        _line.Text = message;
        IsError = error;
        _line.Foreground = error ? T.Danger : T.TextSecondary;
        MessageTimer.Stop();
        if (!error)
        {
            MessageTimer.Start();
        }
    }

    /// <summary>Brings the hint back in place of the message.</summary>
    internal void ShowHint()
    {
        MessageTimer.Stop();
        IsError = false;
        _line.Foreground = T.TextSecondary;
        _line.Text = Hint;
    }

    /// <summary>Stops the bar's clocks (the message's and the seed panel's Copied), as the window closes.</summary>
    internal void Stop()
    {
        MessageTimer.Stop();
        Seed.Stop();
    }

    /// <summary>The user did something: an error that was showing goes, and the hint comes back.</summary>
    internal void Acted()
    {
        if (IsError)
        {
            ShowHint();
        }
    }

    /// <summary>Shows the seed, the faces, the budget and the save state as they are now.</summary>
    internal void Refresh()
    {
        Seed.Refresh();
        int full = FullFaces();
        string budget = _w.ViewState.Budget;
        (string _, string label, int allowed) = FaceBudget.Find(budget);
        bool over = FaceBudget.IsOver(full, budget);
        bool lod = _w.ViewState.Lod > 0 && MeshRecipes.Find(_w.Files.Recipe) is not null;

        _lod.IsVisible = lod;
        _lod.Text = _w.ViewState.Lod == Commands.ViewState.FarLevel
            ? string.Create(CultureInfo.InvariantCulture, $"Far away: {ShownFaces():N0} faces")
            : string.Create(CultureInfo.InvariantCulture, $"LOD{_w.ViewState.Lod}: {ShownFaces():N0} faces");
        FacesReadout.Text = string.Create(CultureInfo.InvariantCulture, $"{(lod ? "Full detail: " : string.Empty)}{full:N0} / {allowed:N0} faces");
        FacesReadout.Foreground = over ? T.Warning : T.TextTertiary;
        Warning.IsVisible = over;
        Chrome.Tip(Warning, over ? FaceBudget.Warning(full, budget) : "Over budget");
        string? whyNotFit = _w.Versions.WhyNotFit();
        MakeItFit.IsEnabled = whyNotFit is null;
        Chrome.Tip(MakeItFit, whyNotFit is null ? MakeItFitTip : $"Make it fit: {char.ToLowerInvariant(whyNotFit[0])}{whyNotFit[1..]}");
        _syncing = true;
        Picker.SelectedIndex = Math.Max(0, FaceBudget.Targets.ToList().FindIndex(t => t.Label == label));
        _syncing = false;
        SaveLabel.Text = _w.Files.SaveState switch
        {
            SaveState.Unsaved => "Unsaved",
            SaveState.NotSavedYet => "Not saved yet",
            _ => "Saved",
        };
    }

    /// <summary>The faces of the piece at full detail: the rock's (or cliff's) triangles, the floor tile's, or a mesh from Blender's.</summary>
    /// <returns>The triangle count.</returns>
    internal int FullFaces() => _w.View.PieceMesh is { } blender
        ? blender.Indices.Length / 3
        : MeshRecipes.Find(_w.Files.Recipe) is not null ? _w.Rocks.FullDetailFaces : FloorFaces;

    /// <summary>The faces of the piece in the view at the detail shown: the rock's (or cliff's), the floor tile's, or a mesh from Blender's.</summary>
    /// <returns>The triangle count.</returns>
    internal int ShownFaces() => _w.View.PieceMesh is { } blender
        ? blender.Indices.Length / 3
        : MeshRecipes.Find(_w.Files.Recipe) is not null ? _w.Rocks.ShownFaces : FloorFaces;

    /// <summary>Makes a popup open upward from its item, the right edges aligned, 6 above it (the bar's popovers).</summary>
    /// <param name="popup">The popup.</param>
    internal static void OpenUpward(Popup popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        popup.Placement = PlacementMode.AnchorAndGravity;
        popup.PlacementAnchor = PopupAnchor.TopRight;
        popup.PlacementGravity = PopupGravity.TopLeft;
        popup.VerticalOffset = -6;
    }

    private static TextBlock Readout(string tip)
    {
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        Chrome.Tip(text, tip);
        return text;
    }
}
