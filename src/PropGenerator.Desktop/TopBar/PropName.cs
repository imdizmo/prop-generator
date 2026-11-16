// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PropGenerator.Desktop.Look;

namespace PropGenerator.Desktop.Menus;

/// <summary>
/// The piece's name in the top bar, as easy-props shows the prop's: the work file's name without ".propgen", an orange
/// dot after it while there are unsaved changes. A click turns it into a box to rename the piece where it is: Enter
/// or leaving the box keeps the new name, Esc the old one.
/// </summary>
internal sealed class PropName : Panel
{
    private const int MostLetters = 60;

    private readonly FileActions _files;
    private readonly TextBlock _text;

    /// <summary>Makes the name for the piece being worked on.</summary>
    /// <param name="files">The piece as a file: its name, its unsaved mark and the rename.</param>
    internal PropName(FileActions files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
        _text = new TextBlock
        {
            FontSize = Look.Theme.Body,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Dot = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Fill = Brushes.Transparent };
        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(Dot, Dock.Right);
        Dot.Margin = new Thickness(6, 0, 0, 0);
        content.Children.Add(Dot);
        content.Children.Add(_text);
        Button = new Button
        {
            Content = content,
            Height = Look.Theme.ControlHeight,
            MinHeight = 0,
            MaxWidth = 220,
            Padding = new Thickness(8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Button.Classes.Add("ep");
        Button.Classes.Add("ep-name");
        AutomationProperties.SetName(Button, "Prop name");
        Chrome.Tip(Button, "The piece's name: click it to rename the piece. A dot after it means there are changes not saved yet.");
        Button.Click += (_, _) => StartRename();

        Box = new TextBox
        {
            Width = 180,
            Height = Look.Theme.ControlHeight,
            MinHeight = 0,
            Padding = new Thickness(8, 0),
            MaxLength = MostLetters,
            FontSize = Look.Theme.Body,
            FontWeight = FontWeight.SemiBold,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Look.Theme.SurfaceSunken,
            BorderBrush = Look.Theme.Accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Look.Theme.Radius2),
            IsVisible = false,
        };

        // The box keeps its accent border while it has the focus or the pointer.
        Box.Resources["TextControlBorderBrushFocused"] = Look.Theme.Accent;
        Box.Resources["TextControlBorderBrushPointerOver"] = Look.Theme.Accent;
        Box.Resources["TextControlBackgroundFocused"] = Look.Theme.SurfaceSunken;
        Box.Resources["TextControlBackgroundPointerOver"] = Look.Theme.SurfaceSunken;
        Box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        AutomationProperties.SetName(Box, "Prop name box");
        Chrome.Tip(Box, "Type the piece's new name (at most 60 characters) and press Enter to keep it, or Esc to leave it as it was.");
        Box.KeyDown += OnBoxKey;
        Box.LostFocus += (_, _) => Finish(keep: true);

        Margin = new Thickness(Look.Theme.Space1, 0, 0, 0);
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(Button);
        Children.Add(Box);
        files.Changed += Refresh;
        Refresh();
    }

    /// <summary>Gets the button that shows the name.</summary>
    internal Button Button { get; }

    /// <summary>Gets the box the name is typed in while renaming.</summary>
    internal TextBox Box { get; }

    /// <summary>Gets the unsaved-changes dot.</summary>
    internal Ellipse Dot { get; }

    /// <summary>Gets the name shown.</summary>
    internal string Text => _text.Text ?? string.Empty;

    /// <summary>Gets a value indicating whether the dot shows (there are unsaved changes).</summary>
    internal bool DotShown => ReferenceEquals(Dot.Fill, Look.Theme.Selection);

    /// <summary>Gets a value indicating whether the name is being typed.</summary>
    internal bool Editing => Box.IsVisible;

    /// <summary>Turns the name into the box, with the name selected.</summary>
    internal void StartRename()
    {
        Box.Text = Text;
        Button.IsVisible = false;
        Box.IsVisible = true;
        Box.Focus();
        Box.SelectAll();
    }

    private void OnBoxKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Finish(keep: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Finish(keep: false);
            e.Handled = true;
        }
    }

    private void Finish(bool keep)
    {
        if (!Editing)
        {
            return;
        }

        string draft = Box.Text?.Trim() ?? string.Empty;
        Box.IsVisible = false;
        Button.IsVisible = true;
        if (keep && draft.Length > 0 && draft != Text)
        {
            _ = _files.RenameAsync(draft);
        }
    }

    private void Refresh()
    {
        _text.Text = System.IO.Path.GetFileNameWithoutExtension(_files.Name);
        Dot.Fill = _files.Dirty ? Look.Theme.Selection : Brushes.Transparent;
        string state = _files.Dirty ? "Unsaved changes" : _files.FilePath is null ? "Not saved yet" : "Saved";
        AutomationProperties.SetName(Dot, state);
    }
}
