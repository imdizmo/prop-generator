using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace PropGenerator.Desktop.Look;

/// <summary>
/// The design's tabs: one row of underline tabs that never wraps or scrolls. The tabs that do not fit collapse, from
/// the end, into a last "More (n)" tab whose menu lists them with their counts; the first tab ("All") and the chosen
/// one never collapse, so a tab picked from More shows as the last tab shown, chosen. A collapsed tab takes no room,
/// no clicks and no focus (it stays a child, so its width is known for the next fit).
/// </summary>
internal sealed class TabRow : Panel
{
    private const double Spacing = 2;
    private readonly List<ToggleButton> _tabs = [];
    private bool[] _shown = [];

    /// <summary>Initializes a new instance of the <see cref="TabRow"/> class, with no tabs.</summary>
    internal TabRow()
    {
        More = new Button
        {
            Content = "More",
            Height = Tokens.Tab,
            MinHeight = 0,
            FontSize = Tokens.FsBody,
            Padding = new Thickness(10, 0),
            Margin = new Thickness(Spacing, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        More.Classes.Add("ep");
        More.Classes.Add("ep-tab-more");
        Chrome.Tip(More, "Click to see the tabs that do not fit here, and pick one.");
        More.Click += (_, _) => OpenMore();
        Children.Add(More);
        ClipToBounds = true;
    }

    /// <summary>Gets the last tab, "More (n)", which lists the collapsed tabs.</summary>
    internal Button More { get; }

    /// <summary>Gets a value indicating whether the More tab is shown (some tabs do not fit).</summary>
    internal bool MoreShown { get; private set; }

    /// <summary>Gets the tabs collapsed into More, in order.</summary>
    internal IReadOnlyList<ToggleButton> Collapsed =>
        [.. _tabs.Where((t, i) => t.IsVisible && i < _shown.Length && !_shown[i])];

    /// <summary>Gets More's menu as it reads: the collapsed tabs' text ("Floors 1").</summary>
    internal IReadOnlyList<string> MoreItems => [.. Collapsed.Select(Text)];

    /// <summary>
    /// Works out which tabs are shown: all when they fit; otherwise the ones at the end collapse, one by one, until
    /// the rest and More fit, except the first and the chosen one, which always show.
    /// </summary>
    /// <param name="widths">Each tab's width.</param>
    /// <param name="spacing">The gap between two tabs (and before More).</param>
    /// <param name="more">The width of the More tab.</param>
    /// <param name="available">The room for the row.</param>
    /// <param name="chosen">The chosen tab's index, or -1.</param>
    /// <returns>Whether each tab is shown.</returns>
    internal static bool[] Fit(IReadOnlyList<double> widths, double spacing, double more, double available, int chosen)
    {
        ArgumentNullException.ThrowIfNull(widths);
        bool[] shown = [.. widths.Select(_ => true)];
        double Used(bool withMore)
        {
            int n = 0;
            double sum = 0;
            for (int i = 0; i < widths.Count; i++)
            {
                if (shown[i])
                {
                    sum += widths[i];
                    n++;
                }
            }

            sum += Math.Max(0, n - 1) * spacing;
            return withMore ? sum + spacing + more : sum;
        }

        if (Used(false) <= available + 0.5)
        {
            return shown;
        }

        for (int i = widths.Count - 1; i > 0 && Used(true) > available + 0.5; i--)
        {
            if (i != chosen)
            {
                shown[i] = false;
            }
        }

        return shown;
    }

    /// <summary>Makes an underline tab: its label (with a muted count after it) and the accent line under it when chosen.</summary>
    /// <returns>The tab, to add with <see cref="Add"/>.</returns>
    internal static ToggleButton MakeTab()
    {
        var line = new Border
        {
            Height = 2,
            CornerRadius = new CornerRadius(2),
            Background = Look.Theme.Accent,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsVisible = false,
        };
        var text = new TextBlock { FontSize = Tokens.FsBody, VerticalAlignment = VerticalAlignment.Center };
        var b = new ToggleButton
        {
            Content = new Panel { Children = { text, line } },
            Height = Tokens.Tab,
            MinHeight = 0,
            Padding = new Thickness(10, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        b.Classes.Add("ep-tab");
        b.IsCheckedChanged += (_, _) => line.IsVisible = b.IsChecked == true;
        return b;
    }

    /// <summary>Sets a tab's label: its name, and its count in the muted colour when it has one.</summary>
    /// <param name="tab">A tab from <see cref="MakeTab"/>.</param>
    /// <param name="name">Its name.</param>
    /// <param name="count">Its count, or null for none ("All").</param>
    internal static void SetText(ToggleButton tab, string name, int? count)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (Text(tab) == (count is null ? name : $"{name} {count}"))
        {
            return;
        }

        TextBlock text = Label(tab);
        text.Inlines = [new Run(name)];
        if (count is int n)
        {
            text.Inlines.Add(new Run(" " + n.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                Foreground = Look.Theme.TextTertiary,
                FontSize = Tokens.FsLabel,
                FontWeight = Avalonia.Media.FontWeight.Normal,
            });
        }
    }

    /// <summary>Gets a tab's label as it reads ("Trees 56").</summary>
    /// <param name="tab">A tab from <see cref="MakeTab"/>.</param>
    /// <returns>Its text.</returns>
    internal static string Text(ToggleButton tab) =>
        string.Concat(Label(tab).Inlines?.OfType<Run>().Select(r => r.Text) ?? []);

    /// <summary>Gets a tab's underline (shown while it is chosen).</summary>
    /// <param name="tab">A tab from <see cref="MakeTab"/>.</param>
    /// <returns>The line.</returns>
    internal static Border Underline(ToggleButton tab) => (Border)((Panel)tab.Content!).Children[1];

    /// <summary>Adds a tab at the end (before More).</summary>
    /// <param name="tab">The tab.</param>
    internal void Add(ToggleButton tab)
    {
        _tabs.Add(tab);
        Children.Insert(Children.Count - 1, tab);
    }

    /// <summary>Does what picking a collapsed tab from More's menu does: clicks it.</summary>
    /// <param name="index">Its place in <see cref="Collapsed"/>.</param>
    internal void PickFromMore(int index)
    {
        Collapsed[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        InvalidateMeasure();
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        Size child = new(double.PositiveInfinity, availableSize.Height);
        double height = 0;
        double[] widths = new double[_tabs.Count];
        int chosen = -1;
        for (int i = 0; i < _tabs.Count; i++)
        {
            ToggleButton t = _tabs[i];
            t.Measure(child);
            widths[i] = t.IsVisible ? t.DesiredSize.Width : 0;
            height = Math.Max(height, t.DesiredSize.Height);
            if (t.IsChecked == true && chosen < 0)
            {
                chosen = i;
            }
        }

        More.Measure(child);
        height = Math.Max(height, More.DesiredSize.Height);
        _shown = Fit(widths, Spacing, More.DesiredSize.Width - Spacing + 8, availableSize.Width, chosen);
        for (int i = 0; i < _shown.Length; i++)
        {
            // A tab hidden for another reason (IsVisible) counts as shown, so it is never listed under More.
            _shown[i] |= !_tabs[i].IsVisible;
        }

        MoreShown = _shown.Contains(false);
        double width = 0;
        int n = 0;
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_shown[i] && _tabs[i].IsVisible)
            {
                width += widths[i];
                n++;
            }
        }

        width += Math.Max(0, n - 1) * Spacing + (MoreShown ? More.DesiredSize.Width : 0);
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        bool first = true;
        for (int i = 0; i < _tabs.Count; i++)
        {
            ToggleButton t = _tabs[i];
            bool shown = i < _shown.Length && _shown[i] && t.IsVisible;
            Show(t, shown);
            if (!shown)
            {
                t.Arrange(new Rect(x, 0, 0, 0));
                continue;
            }

            x += first ? 0 : Spacing;
            first = false;
            t.Arrange(new Rect(x, 0, t.DesiredSize.Width, finalSize.Height));
            x += t.DesiredSize.Width;
        }

        Show(More, MoreShown);
        if (MoreShown)
        {
            string label = $"More ({Collapsed.Count})";
            if (More.Content as string != label)
            {
                More.Content = label;
            }

            More.Arrange(new Rect(x, 0, More.DesiredSize.Width, finalSize.Height));
        }
        else
        {
            More.Arrange(new Rect(x, 0, 0, 0));
        }

        return finalSize;
    }

    private static TextBlock Label(ToggleButton tab) => (TextBlock)((Panel)tab.Content!).Children[0];

    // A collapsed tab takes no clicks and no focus (setting these does not lay the row out again).
    private static void Show(Control c, bool shown)
    {
        c.Focusable = shown;
        c.IsHitTestVisible = shown;
    }

    private void OpenMore()
    {
        IReadOnlyList<ToggleButton> collapsed = Collapsed;
        var items = new List<MenuItem>(collapsed.Count);
        for (int i = 0; i < collapsed.Count; i++)
        {
            int at = i;
            var item = new MenuItem { Header = Text(collapsed[i]) };
            item.Click += (_, _) => PickFromMore(at);
            items.Add(item);
        }

        var menu = new ContextMenu { ItemsSource = items, Placement = PlacementMode.BottomEdgeAlignedLeft };
        menu.Open(More);
    }
}
