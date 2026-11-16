using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PropGenerator.Desktop.Recipes;

namespace PropGenerator.Desktop;

/// <summary>
/// One picture of the objects drawer: a starting point or one of the user's own files, its picture (a plain swatch
/// until the picture is there) with its whole name under it, a star when it is a favourite, and an outline in the
/// selection colour (easy-props' amber) when it is the piece being worked on.
/// </summary>
internal sealed class DrawerTile
{
    private const double PictureSide = 64;

    private readonly Image _image = new() { Width = PictureSide, Height = PictureSide, IsVisible = false };
    private readonly Border _swatch = new() { Width = PictureSide, Height = PictureSide, CornerRadius = new CornerRadius(4) };
    private readonly TextBlock _star = new()
    {
        Text = "★",
        FontSize = 11,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        IsVisible = false,
    };

    /// <summary>Makes a tile.</summary>
    /// <param name="name">The name shown under the picture.</param>
    /// <param name="tip">What the tooltip says.</param>
    /// <param name="swatch">The colour shown until the picture is there.</param>
    /// <param name="background">The tile's own background.</param>
    /// <param name="point">The starting point it starts, or null for a file.</param>
    /// <param name="entry">The file it opens, or null for a starting point.</param>
    public DrawerTile(string name, string tip, Color swatch, IBrush background, StartingPoint? point, LibraryEntry? entry)
    {
        Name = name;
        Point = point;
        Entry = entry;
        Swatch = swatch;
        _swatch.Background = new SolidColorBrush(swatch);
        _star.Foreground = Look.Theme.TextPrimary;
        var picture = new Grid { Width = PictureSide, Height = PictureSide, HorizontalAlignment = HorizontalAlignment.Center };
        picture.Children.Add(_swatch);
        picture.Children.Add(_image);
        var content = new StackPanel { Width = 84, Spacing = 2 };
        content.Children.Add(picture);
        content.Children.Add(new TextBlock { Text = name, FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        Border = new Border
        {
            Child = new Grid { Children = { content, _star } },
            Padding = new Thickness(3),
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Background = background,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(Border, tip);
        Avalonia.Automation.AutomationProperties.SetName(Border, name);
    }

    /// <summary>Gets the tile itself, as it sits in the drawer.</summary>
    public Border Border { get; }

    /// <summary>Gets the name shown under the picture.</summary>
    public string Name { get; }

    /// <summary>Gets the starting point a click starts, or null for a file.</summary>
    public StartingPoint? Point { get; }

    /// <summary>Gets the file a click opens, or null for a starting point.</summary>
    public LibraryEntry? Entry { get; }

    /// <summary>Gets the colour shown while there is no picture.</summary>
    public Color Swatch { get; }

    /// <summary>Gets a value indicating whether the swatch is shown (no picture yet, or none could be decoded).</summary>
    public bool SwatchShown => _swatch.IsVisible;

    /// <summary>
    /// Gets the PNG last given to <see cref="Show"/>, decoded or not (the headless tests cannot decode one, so they
    /// compare these bytes); null when none was given or the picture was let go.
    /// </summary>
    public byte[]? ShownPng { get; private set; }

    /// <summary>Gets or sets a value indicating whether the star is shown.</summary>
    public bool Favourite
    {
        get => _star.IsVisible;
        set => _star.IsVisible = value;
    }

    /// <summary>Gets or sets a value indicating whether the tile is outlined in the selection colour (the piece being worked on).</summary>
    public bool Outlined
    {
        get;
        set
        {
            field = value;
            Border.BorderBrush = value ? Look.Theme.Selection : Brushes.Transparent;
        }
    }

    /// <summary>
    /// Shows a picture in place of the swatch; the picture shown before is let go. A PNG that cannot be decoded (or no
    /// platform to decode it, as in the headless tests) leaves the swatch.
    /// </summary>
    /// <param name="png">The picture.</param>
    public void Show(byte[] png)
    {
        Bitmap bitmap;
        try
        {
            using var stream = new MemoryStream(png, writable: false);
            bitmap = new Bitmap(stream);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            TraceLog.Write($"no picture for {Name}: {e.Message}");
            ShownPng = png;
            return;
        }

        Forget();
        ShownPng = png;
        _image.Source = bitmap;
        _image.IsVisible = true;
        _swatch.IsVisible = false;
    }

    /// <summary>Lets the picture go (the tile has left the drawer).</summary>
    public void Forget()
    {
        ShownPng = null;
        if (_image.Source is Bitmap old)
        {
            _image.Source = null;
            old.Dispose();
        }
    }
}
