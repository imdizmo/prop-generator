// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Look;
using T = PropGenerator.Desktop.Look.Theme;

namespace PropGenerator.Desktop.View;

/// <summary>
/// easy-props' sun dial: the sky seen from above, 84 px across, with the prop (a small tree) in the middle and the sun
/// on it. Round the dial is the sun's direction (0° at the top, 90° on the right); nearer the middle is higher in the
/// sky: the radius is 1 − elevation / 90 × 0.72, so the rim is the horizon and the middle straight up, kept to 4° to
/// 88°. Drag the sun to move it; the arrow keys move it 5° round and up or down (15° with Shift).
/// </summary>
internal sealed class SunDial : Control
{
    /// <summary>The dial's width and height.</summary>
    internal const double Size = 84;

    /// <summary>The radius of the sky on the dial.</summary>
    internal const double Radius = (Size / 2) - 6;

    /// <summary>The lowest the dial puts the sun, in degrees.</summary>
    internal const double Lowest = 4;

    /// <summary>The highest the dial puts the sun, in degrees.</summary>
    internal const double Highest = 88;

    private static readonly IBrush SkyFill = new ImmutableSolidColorBrush(Color.FromArgb(41, 120, 140, 160));
    private static readonly IBrush TreeFill = new ImmutableSolidColorBrush(Color.FromArgb(153, 127, 177, 99));
    private static readonly IBrush SunFill = new ImmutableSolidColorBrush(Color.Parse("#ffc861"));
    private static readonly IPen RingPen = new ImmutablePen(T.BorderStrong.ToImmutable(), 1.5);
    private static readonly IPen TreePen = new ImmutablePen(TreeFill.ToImmutable(), 2, lineCap: PenLineCap.Round);
    private static readonly IPen SunPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#fff2d6")), 1.5);

    private readonly Func<Lighting> _get;
    private readonly Action<Lighting> _set;
    private bool _dragging;

    /// <summary>Makes the dial.</summary>
    /// <param name="get">Reads the light as it is now.</param>
    /// <param name="set">Sets the light the dial chose.</param>
    internal SunDial(Func<Lighting> get, Action<Lighting> set)
    {
        _get = get;
        _set = set;
        Width = Size;
        Height = Size;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(this, "Sun position");
        Chrome.Tip(this, "Drag the sun round the prop and nearer the middle to raise it. The arrow keys move it 5° (15° with Shift).");
    }

    /// <summary>Where the sun sits on the dial, each part from −1 to 1 (x to the right, y down), as easy-props' sunDialPosition.</summary>
    /// <param name="lighting">The light.</param>
    /// <returns>The sun's place on the dial.</returns>
    internal static (double X, double Y) Position(Lighting lighting)
    {
        double azimuth = lighting.Azimuth * Math.PI / 180;
        double radius = 1 - (Math.Clamp(lighting.Elevation, 0, 90) / 90 * 0.72);
        return (Math.Sin(azimuth) * radius, -Math.Cos(azimuth) * radius);
    }

    /// <summary>
    /// The light with the sun moved to a point of the dial (easy-props' sunDialToAngles, then kept to 4°–88°); its
    /// warmth and shadow are kept.
    /// </summary>
    /// <param name="x">Across, −1 to 1 at the rim (beyond it counts as the rim).</param>
    /// <param name="y">Down, −1 to 1 at the rim.</param>
    /// <param name="now">The light as it is.</param>
    /// <returns>The new light.</returns>
    internal static Lighting FromDial(double x, double y, Lighting now)
    {
        double radius = Math.Min(Math.Sqrt((x * x) + (y * y)), 1);
        double azimuth = ((Math.Atan2(x, -y) * 180 / Math.PI) + 360) % 360;
        double elevation = (1 - radius) * (90 / 0.72);
        return now with { Azimuth = azimuth, Elevation = Math.Clamp(elevation, Lowest, Highest) };
    }

    /// <summary>The light after an arrow key: right and up turn and raise the sun, left and down the other way.</summary>
    /// <param name="now">The light as it is.</param>
    /// <param name="key">The key.</param>
    /// <param name="shift">True when Shift is held (15° rather than 5°).</param>
    /// <returns>The new light, or null for a key the dial does not use.</returns>
    internal static Lighting? Nudge(Lighting now, Key key, bool shift)
    {
        double step = shift ? 15 : 5;
        return key switch
        {
            Key.Left or Key.Down => now with { Azimuth = (now.Azimuth - step + 360) % 360, Elevation = Math.Max(now.Elevation - step, Lowest) },
            Key.Right or Key.Up => now with { Azimuth = (now.Azimuth + step) % 360, Elevation = Math.Min(now.Elevation + step, Highest) },
            _ => null,
        };
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var centre = new Point(Size / 2, Size / 2);
        context.DrawEllipse(SkyFill, null, centre, Radius, Radius);
        context.DrawEllipse(null, RingPen, centre, Radius, Radius);

        // The tree the sun is shining on.
        context.DrawEllipse(TreeFill, null, new Point(centre.X, centre.Y - 4), 6, 6);
        context.DrawLine(TreePen, new Point(centre.X, centre.Y - 1), new Point(centre.X, centre.Y + 7));

        (double x, double y) = Position(_get());
        context.DrawEllipse(SunFill, SunPen, new Point(centre.X + (x * Radius), centre.Y + (y * Radius)), 6, 6);
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        e.Pointer.Capture(this);
        Focus();
        MoveTo(e.GetPosition(this));
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerMoved(e);
        if (_dragging)
        {
            MoveTo(e.GetPosition(this));
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (Nudge(_get(), e.Key, (e.KeyModifiers & KeyModifiers.Shift) != 0) is Lighting next)
        {
            _set(next);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void MoveTo(Point p)
    {
        double x = (p.X - (Size / 2)) / Radius, y = (p.Y - (Size / 2)) / Radius;
        _set(FromDial(x, y, _get()));
        InvalidateVisual();
    }
}
