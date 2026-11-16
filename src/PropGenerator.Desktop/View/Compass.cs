// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using PropGenerator.Desktop.Look;
using T = PropGenerator.Desktop.Look.Theme;

namespace PropGenerator.Desktop.View;

/// <summary>
/// easy-props' navigation compass, bottom left of the view: a 76 px dark circle in which the app's own axes (+X red,
/// +Y green, +Z blue, Z up) turn as the camera orbits. Each axis is a line from the middle to a ball 30 px out (r 8
/// with its letter for the positive end, r 4.5 for the negative), bolder as it points at the viewer (opacity
/// 0.34 + facing × 0.66). A click on a ball looks along that axis, keeping the camera's distance and target.
/// </summary>
internal sealed class Compass : Control
{
    /// <summary>The compass's width and height.</summary>
    internal const double Size = 76;

    /// <summary>How far from the middle a ball sits.</summary>
    internal const double Reach = 30;

    /// <summary>How near a ball a click must be to hit it (easy-props' generous r 13 target).</summary>
    internal const double HitRadius = 13;

    /// <summary>What the compass says when the pointer is not on a ball.</summary>
    internal const string Tip = "Which way the view faces. Click a ball to look along that axis.";

    private static readonly Vector3[] Directions = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitX, -Vector3.UnitY, -Vector3.UnitZ];
    private static readonly string[] Names = ["+X", "+Y", "+Z", "-X", "-Y", "-Z"];
    private static readonly Color[] Colours = [Color.Parse("#e0776a"), Color.Parse("#9ed070"), Color.Parse("#6f9ee0")];
    private static readonly IBrush Face = new ImmutableSolidColorBrush(Color.FromArgb(115, 24, 25, 27));
    private static readonly IPen Edge = new ImmutablePen(T.OverlayBorder.ToImmutable(), 1);
    private static readonly IPen Ring = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(26, 255, 255, 255)), 1);
    private static readonly IPen BallEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(89, 0, 0, 0)), 1);
    private static readonly IPen BallHover = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(217, 255, 255, 255)), 1.6);
    private static readonly IBrush Origin = new ImmutableSolidColorBrush(Color.FromArgb(140, 255, 255, 255));
    private static readonly IBrush[] Fills = [.. Colours.Select(c => (IBrush)new ImmutableSolidColorBrush(c))];
    private static readonly IPen[] Lines = [.. Colours.Select(c => (IPen)new ImmutablePen(new ImmutableSolidColorBrush(c), 1.6, lineCap: PenLineCap.Round))];
    private static readonly FormattedText[] Letters = [.. "XYZ".Select(Letter)];

    private readonly OrbitCamera _camera;
    private readonly Action<Vector3> _lookFrom;
    private Vector3 _eye;
    private Vector3 _target;
    private int? _hover;

    /// <summary>Makes the compass.</summary>
    /// <param name="camera">The view's camera, read to turn the axes.</param>
    /// <param name="lookFrom">Looks at the target from a direction, keeping the distance (the view's own snap).</param>
    internal Compass(OrbitCamera camera, Action<Vector3> lookFrom)
    {
        _camera = camera;
        _lookFrom = lookFrom;
        Width = Size;
        Height = Size;
        Cursor = new Cursor(StandardCursorType.Arrow);
        Chrome.Tip(this, Tip);
    }

    /// <summary>The tooltip of an axis's ball: "Look along +X" and so on.</summary>
    /// <param name="axis">0 to 5: +X, +Y, +Z, −X, −Y, −Z.</param>
    /// <returns>The tooltip.</returns>
    internal static string TipFor(int axis) => "Look along " + Names[axis];

    /// <summary>Where an axis's ball is drawn, from the middle (x right, y down), and how opaque it is, for the camera now.</summary>
    /// <param name="axis">0 to 5: +X, +Y, +Z, −X, −Y, −Z.</param>
    /// <returns>The offset in pixels and the opacity.</returns>
    internal (double X, double Y, double Opacity) Place(int axis)
    {
        var view = Matrix4x4.CreateLookAt(_camera.Eye, _camera.Target, Vector3.UnitZ);
        var d = Vector3.TransformNormal(Directions[axis], view);
        double facing = (d.Z + 1) / 2;
        return (d.X * Reach, -d.Y * Reach, 0.34 + (facing * 0.66));
    }

    /// <summary>The ball under a point of the compass (the one drawn on top when two overlap), or null for none.</summary>
    /// <param name="p">The point, in the compass's own coordinates.</param>
    /// <returns>The axis, 0 to 5, or null.</returns>
    internal int? AxisAt(Point p)
    {
        var centre = new Point(Size / 2, Size / 2);
        for (int i = Directions.Length - 1; i >= 0; i--)
        {
            (double x, double y, _) = Place(i);
            double dx = p.X - (centre.X + x), dy = p.Y - (centre.Y + y);
            if ((dx * dx) + (dy * dy) <= HitRadius * HitRadius)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>Looks along an axis, keeping the camera's distance and target (as a click on its ball does).</summary>
    /// <param name="axis">0 to 5: +X, +Y, +Z, −X, −Y, −Z.</param>
    internal void Snap(int axis)
    {
        _lookFrom(Directions[axis]);
        InvalidateVisual();
    }

    /// <summary>Draws the compass again if the camera moved since it was last drawn.</summary>
    internal void Follow()
    {
        if (_camera.Eye != _eye || _camera.Target != _target)
        {
            InvalidateVisual();
        }
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _eye = _camera.Eye;
        _target = _camera.Target;
        var centre = new Point(Size / 2, Size / 2);
        context.DrawEllipse(Face, Edge, centre, (Size / 2) - 0.5, (Size / 2) - 0.5);
        context.DrawEllipse(null, Ring, centre, 36, 36);
        for (int i = 0; i < Directions.Length; i++)
        {
            (double x, double y, double opacity) = Place(i);
            using (context.PushOpacity(0.2 + ((opacity - 0.34) / 0.66 * 0.5)))
            {
                context.DrawLine(Lines[i % 3], centre, new Point(centre.X + x, centre.Y + y));
            }
        }

        for (int i = 0; i < Directions.Length; i++)
        {
            (double x, double y, double opacity) = Place(i);
            var at = new Point(centre.X + x, centre.Y + y);
            bool named = i < 3;
            using (context.PushOpacity(opacity))
            {
                context.DrawEllipse(Fills[i % 3], _hover == i ? BallHover : BallEdge, at, named ? 8 : 4.5, named ? 8 : 4.5);
                if (named)
                {
                    FormattedText letter = Letters[i];
                    context.DrawText(letter, new Point(at.X - (letter.Width / 2), at.Y - (letter.Height / 2)));
                }
            }
        }

        context.DrawEllipse(Origin, null, centre, 2, 2);
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerMoved(e);
        Hover(AxisAt(e.GetPosition(this)));
    }

    /// <inheritdoc/>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Hover(null);
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerPressed(e);
        e.Handled = true;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && AxisAt(e.GetPosition(this)) is int axis)
        {
            Snap(axis);
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerReleased(e);
        e.Handled = true;
    }

    private static FormattedText Letter(char c) =>
        new(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, weight: FontWeight.SemiBold), 9, new ImmutableSolidColorBrush(Color.Parse("#12140f")));

    private void Hover(int? axis)
    {
        if (axis == _hover)
        {
            return;
        }

        _hover = axis;
        Chrome.Tip(this, axis is int a ? TipFor(a) : Tip);
        InvalidateVisual();
    }
}
