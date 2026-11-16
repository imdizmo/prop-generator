using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Documents;

namespace PropGenerator.Desktop.Look;

/// <summary>A control that draws one lucide icon, stroked, at a square size and in the <see cref="Foreground"/> colour.</summary>
public sealed class Icon : Control
{
    /// <summary>The colour of the stroke (unset: the inherited text colour).</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<Icon, IBrush?>(nameof(Foreground));

    private readonly Geometry _geometry;
    private readonly double _size;

    static Icon()
    {
        AffectsRender<Icon>(ForegroundProperty, TextElement.ForegroundProperty);
    }

    /// <summary>Makes an icon.</summary>
    /// <param name="name">The lucide name, for example <c>save</c>.</param>
    /// <param name="size">The width and height in pixels.</param>
    public Icon(string name, double size)
    {
        _geometry = Icons.Get(name);
        IconName = name;
        _size = size;
        Width = size;
        Height = size;
        IsHitTestVisible = false;
    }

    /// <summary>Gets the lucide name it draws, for example <c>save</c>.</summary>
    public string IconName { get; }

    /// <summary>The colour of the stroke; when not set, the text colour inherited from the parent (so an icon in a button follows the button).</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        double scale = _size / 24.0;
        var pen = new Pen(Foreground ?? TextElement.GetForeground(this), 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(null, pen, _geometry);
        }
    }
}
