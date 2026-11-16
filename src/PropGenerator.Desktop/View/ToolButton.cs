// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using PropGenerator.Desktop.Look;
using T = PropGenerator.Desktop.Look.Theme;

namespace PropGenerator.Desktop.View;

/// <summary>
/// One tool over the 3D view, as easy-props' <c>.tool</c> drawn as the design's ghost tool: 28 high and at least 28
/// wide, an icon of 16 (and a short label, for Shading and Detail level), a chevron of 14 when it opens a menu. Quiet
/// when off; when on (the chosen tool, a toggled view button) on the selected wash with the icon in the accent; faint
/// when it cannot be used yet (it can still be clicked, and then says why). A segment (Normal, Low-poly) is a 22 high
/// pill with its label only, the chosen one on the accent wash.
/// </summary>
internal sealed class ToolButton : Button
{
    private readonly StackPanel _row;
    private readonly TextBlock? _text;
    private string _tooltip;

    /// <summary>Makes the tool.</summary>
    /// <param name="icon">The lucide icon.</param>
    /// <param name="name">What a screen reader calls it (REF's aria-label).</param>
    /// <param name="tooltip">What its tooltip says while it can be used.</param>
    /// <param name="text">A short label after the icon, or null for none.</param>
    /// <param name="menu">True when it opens a menu (a chevron follows).</param>
    /// <param name="segment">True for a side of a segmented control: a pill with its label and no icon.</param>
    internal ToolButton(string icon, string name, string tooltip, string? text = null, bool menu = false, bool segment = false)
    {
        Classes.Add("ep");
        Classes.Add(segment ? "ep-seg" : "ep-tool");
        Height = segment ? SegmentHeight : T.ControlHeight;
        MinHeight = 0;
        MinWidth = segment ? 0 : T.ControlHeight;
        Padding = segment ? new Thickness(10, 0) : menu ? new Thickness(6, 0, 4, 0) : new Thickness(6, 0);
        VerticalContentAlignment = VerticalAlignment.Center;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IconName = icon;
        _row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (!segment)
        {
            _row.Children.Add(MakeIcon(icon));
        }

        if (text is not null)
        {
            _text = new TextBlock { Text = text, FontSize = T.Label, VerticalAlignment = VerticalAlignment.Center };
            _row.Children.Add(_text);
        }

        if (menu)
        {
            _row.Children.Add(new Icon("chevron-down", 14) { VerticalAlignment = VerticalAlignment.Center });
        }

        Content = _row;
        AutomationProperties.SetName(this, name);
        _tooltip = tooltip;
        Chrome.Tip(this, tooltip);
    }

    /// <summary>Gets the lucide icon shown.</summary>
    internal string IconName { get; private set; }

    /// <summary>Gets the label after the icon, or null when it has none.</summary>
    internal string? Text => _text?.Text;

    /// <summary>The height of a segment: the design's segmented control, 22.</summary>
    internal const double SegmentHeight = 22;

    /// <summary>Gets or sets a value indicating whether the tool is on (the selected wash, the icon in the accent).</summary>
    internal bool Pressed
    {
        get => Classes.Contains("ep-pressed");
        set => Classes.Set("ep-pressed", value);
    }

    /// <summary>Gets or sets why the tool cannot be used yet (it shows faint and its tooltip says why), or null when it can.</summary>
    internal string? Unavailable
    {
        get;
        set
        {
            field = value;
            Classes.Set("ep-unavailable", value is not null);
            Chrome.Tip(this, value ?? _tooltip);
        }
    }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>Changes what the tool shows and says (the Shading, Detail level and Maximize tools follow the view).</summary>
    /// <param name="icon">The lucide icon.</param>
    /// <param name="name">What a screen reader calls it.</param>
    /// <param name="tooltip">What its tooltip says.</param>
    /// <param name="text">The label after the icon (ignored when the tool has none).</param>
    internal void Show(string icon, string name, string tooltip, string? text = null)
    {
        if (icon != IconName && _row.Children.Count > 0 && _row.Children[0] is Icon)
        {
            IconName = icon;
            _row.Children[0] = MakeIcon(icon);
        }

        if (_text is not null && text is not null)
        {
            _text.Text = text;
        }

        AutomationProperties.SetName(this, name);
        _tooltip = tooltip;
        Chrome.Tip(this, Unavailable ?? tooltip);
    }

    private static Icon MakeIcon(string icon) => new(icon, 16) { VerticalAlignment = VerticalAlignment.Center };
}
