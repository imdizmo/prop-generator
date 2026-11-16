// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Details;

/// <summary>
/// A folding group of the controls panel, drawn as the design's inspector section: a hairline above, a header button
/// (32 high, a 14 px chevron in the quiet text colour that turns down when open, the title 13 semibold) over the group's
/// rows. Clicking the header asks the panel to fold or unfold it; the panel decides and remembers.
/// </summary>
internal sealed class SettingsGroup : Border
{
    private readonly Icon _chevron;

    /// <summary>Builds a group, folded.</summary>
    /// <param name="title">The group's name, such as "Shape".</param>
    internal SettingsGroup(string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        Title = title;
        BorderBrush = Look.Theme.BorderSubtle;
        BorderThickness = new Thickness(0, 1, 0, 0);
        _chevron = new Icon("chevron-right", 14)
        {
            Foreground = Look.Theme.TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = TransformOperations.Parse("rotate(0deg)"),
            Transitions = [new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Tokens.Slow }],
        };
        Head = new Button
        {
            Height = Look.Theme.SectionHeight,
            MinHeight = 0,
            Padding = new Thickness(10, 0, Look.Theme.Space3, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    _chevron,
                    new TextBlock
                    {
                        Text = title,
                        FontSize = Look.Theme.Body,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Look.Theme.TextPrimary,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        Head.Classes.Add("ep");
        Head.Classes.Add("ep-group");
        AutomationProperties.SetName(Head, title);
        Chrome.Tip(Head, $"Show or hide the {title} controls. Which groups are open is remembered for each kind of prop.");
        Head.Click += (_, _) => Toggled?.Invoke(this);
        Body = new StackPanel { Margin = new Thickness(Look.Theme.Space3, 2, Look.Theme.Space3, Look.Theme.Space3), IsVisible = false };
        Child = new StackPanel { Children = { Head, Body } };
    }

    /// <summary>Raised when the header is clicked: the panel folds or unfolds the group.</summary>
    internal event Action<SettingsGroup>? Toggled;

    /// <summary>Gets the group's name.</summary>
    internal string Title { get; }

    /// <summary>Gets the header button.</summary>
    internal Button Head { get; }

    /// <summary>Gets the rows, shown only while the group is open.</summary>
    internal StackPanel Body { get; }

    /// <summary>Gets or sets a value indicating whether the group is open (its rows shown, the chevron turned down).</summary>
    internal bool IsOpen
    {
        get => Body.IsVisible;
        set
        {
            Body.IsVisible = value;
            _chevron.RenderTransform = TransformOperations.Parse(value ? "rotate(90deg)" : "rotate(0deg)");
            AutomationProperties.SetItemStatus(Head, value ? "expanded" : "collapsed");
        }
    }
}
