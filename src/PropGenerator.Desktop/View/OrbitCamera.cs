// Adapted from easy-props (MIT, © 2026 AHS12), whose camera is three.js's OrbitControls (MIT, © 2010–2026 three.js authors).
using System.Numerics;

namespace PropGenerator.Desktop.View;

/// <summary>
/// The view's camera, as easy-props sets up three.js's orbit controls, with Z up: it circles a target at a distance,
/// its height kept 0.05 to 1.52 rad from straight up (never under the ground) and its distance 1.2 to 40 m (more when a
/// big piece needs it to fit). Turning and panning are damped: each step takes 8 % of what is left of the move, so the
/// camera glides to a stop; zooming is immediate. Pure numbers, no OpenGL: the view steps it once a frame.
/// </summary>
internal sealed class OrbitCamera
{
    /// <summary>The vertical field of view, in radians (45°).</summary>
    internal const float FieldOfView = 45f * MathF.PI / 180f;

    /// <summary>The near clipping plane, in metres.</summary>
    internal const float Near = 0.05f;

    /// <summary>The far clipping plane, in metres.</summary>
    internal const float Far = 500f;

    /// <summary>The smallest angle from straight up, in radians.</summary>
    internal const float MinPolar = 0.05f;

    /// <summary>The largest angle from straight up, in radians: just above the ground.</summary>
    internal const float MaxPolar = 1.52f;

    /// <summary>The closest the camera comes to its target, in metres.</summary>
    internal const float MinDistance = 1.2f;

    /// <summary>The farthest the camera goes from its target, in metres, unless a big piece needs more.</summary>
    internal const float DefaultMaxDistance = 40f;

    /// <summary>The share of the move left that each step makes.</summary>
    internal const float Damping = 0.08f;

    /// <summary>The most a press may move, in pixels, and still be a click rather than a drag.</summary>
    internal const double ClickSlop = 5;

    /// <summary>How much one wheel notch (100 pixels in a browser) brings the camera closer.</summary>
    private const float WheelStep = 0.95f;

    // Below this the move left is not worth another frame.
    private const float Settled = 1e-5f;

    private float _deltaTheta;
    private float _deltaPhi;
    private Vector3 _pan;

    /// <summary>Starts at easy-props' perspective direction, 10 m from the origin.</summary>
    internal OrbitCamera()
    {
        LookFrom(Perspective);
    }

    /// <summary>Gets easy-props' three-quarter view, from the front left and a little above: (−0.62, 0.36, 0.62) in its Y up.</summary>
    internal static Vector3 Perspective { get; } = Vector3.Normalize(new Vector3(-0.62f, -0.62f, 0.36f));

    /// <summary>Gets or sets the point the camera circles and looks at.</summary>
    internal Vector3 Target { get; set; }

    /// <summary>Gets the camera's distance from its target.</summary>
    internal float Distance { get; private set; } = 10;

    /// <summary>Gets the farthest the camera may go now: 40 m, or what the piece shown needs to fit.</summary>
    internal float MaxDistance { get; private set; } = DefaultMaxDistance;

    /// <summary>Gets the angle around the up axis, in radians (0 looks from the front, −Y).</summary>
    internal float Theta { get; private set; }

    /// <summary>Gets the angle from straight up, in radians.</summary>
    internal float Phi { get; private set; }

    /// <summary>Gets the unit vector from the target towards the camera.</summary>
    internal Vector3 Back => new(MathF.Sin(Phi) * MathF.Sin(Theta), -MathF.Sin(Phi) * MathF.Cos(Theta), MathF.Cos(Phi));

    /// <summary>Gets where the camera is.</summary>
    internal Vector3 Eye => Target + (Distance * Back);

    /// <summary>Gets a value indicating whether a damped move is still under way.</summary>
    internal bool Moving => MathF.Abs(_deltaTheta) > Settled || MathF.Abs(_deltaPhi) > Settled || _pan.Length() > Settled * Math.Max(1, Distance);

    /// <summary>The camera's view and projection for a picture of this shape.</summary>
    /// <param name="aspect">The picture's width over its height.</param>
    /// <returns>World to clip space, row vectors.</returns>
    internal Matrix4x4 ViewProjection(float aspect) =>
        Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitZ) * Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, Near, Far);

    /// <summary>A left drag (three.js: 2π × pixels / the view's height, the same both ways).</summary>
    /// <param name="dx">Pixels moved right.</param>
    /// <param name="dy">Pixels moved down.</param>
    /// <param name="viewHeight">The view's height in pixels.</param>
    internal void Rotate(double dx, double dy, double viewHeight)
    {
        float h = (float)Math.Max(1, viewHeight);
        _deltaTheta -= 2 * MathF.PI * (float)dx / h;
        _deltaPhi -= 2 * MathF.PI * (float)dy / h;
    }

    /// <summary>A right drag: the target moves with the hand across the screen, as far as the view shows at its distance.</summary>
    /// <param name="dx">Pixels moved right.</param>
    /// <param name="dy">Pixels moved down.</param>
    /// <param name="viewHeight">The view's height in pixels.</param>
    internal void Pan(double dx, double dy, double viewHeight)
    {
        float h = (float)Math.Max(1, viewHeight);
        float reach = Distance * MathF.Tan(FieldOfView / 2);
        Vector3 back = Back;
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, back));
        if (!float.IsFinite(right.X))
        {
            right = new Vector3(MathF.Cos(Theta), MathF.Sin(Theta), 0);
        }

        var up = Vector3.Cross(back, right);
        _pan += (-right * (2 * (float)dx * reach / h)) + (up * (2 * (float)dy * reach / h));
    }

    /// <summary>Moves the camera closer (below 1) or farther (above 1), at once, within the distance limits.</summary>
    /// <param name="scale">What the distance is multiplied by.</param>
    internal void Zoom(float scale) => Distance = Math.Clamp(Distance * scale, MinDistance, MaxDistance);

    /// <summary>The wheel: each notch towards the screen brings the camera 5 % closer.</summary>
    /// <param name="notches">Notches turned, positive away from the user (zooming in).</param>
    internal void Wheel(double notches) => Zoom(MathF.Pow(WheelStep, (float)notches));

    /// <summary>A middle drag: down moves the camera away, up brings it closer (5 % each 100 pixels).</summary>
    /// <param name="dy">Pixels moved down.</param>
    internal void DragZoom(double dy) => Zoom(MathF.Pow(WheelStep, -(float)dy * 0.01f));

    /// <summary>One frame of the damped move: turns and pans by 8 % of what is left, then keeps the angle in its limits.</summary>
    /// <returns>True while some of the move is left, so another frame is wanted.</returns>
    internal bool Step()
    {
        Theta += _deltaTheta * Damping;
        Phi = Math.Clamp(Phi + (_deltaPhi * Damping), MinPolar, MaxPolar);
        Target += _pan * Damping;
        _deltaTheta *= 1 - Damping;
        _deltaPhi *= 1 - Damping;
        _pan *= 1 - Damping;
        if (Moving)
        {
            return true;
        }

        Stop();
        return false;
    }

    /// <summary>Drops what is left of a damped move.</summary>
    internal void Stop()
    {
        _deltaTheta = 0;
        _deltaPhi = 0;
        _pan = Vector3.Zero;
    }

    /// <summary>
    /// Looks at the target from a direction, keeping the target and the distance; the angle is kept in its limits
    /// (straight down becomes 0.05 rad from straight up).
    /// </summary>
    /// <param name="from">From the target towards the camera; need not be of length one.</param>
    internal void LookFrom(Vector3 from)
    {
        if (from.LengthSquared() < 1e-12f)
        {
            return;
        }

        var d = Vector3.Normalize(from);
        if (MathF.Abs(d.X) > 1e-6f || MathF.Abs(d.Y) > 1e-6f)
        {
            Theta = MathF.Atan2(d.X, -d.Y);
        }

        Phi = Math.Clamp(MathF.Acos(Math.Clamp(d.Z, -1f, 1f)), MinPolar, MaxPolar);
        Stop();
    }

    /// <summary>
    /// easy-props' framing of a new piece: the target at 0.58 of its height above its base, over the middle of its
    /// footprint, and the camera max(2.7 × height, 2.4 × width, 8) m away along the perspective direction. The farthest
    /// distance grows to fit a piece too big for 40 m.
    /// </summary>
    /// <param name="lo">The piece's lowest corner.</param>
    /// <param name="hi">The piece's highest corner.</param>
    /// <param name="frame">The piece's Height and Width as made, when it has them: then it is framed by those, over its pivot.</param>
    internal void FrameNew(Vector3 lo, Vector3 hi, PieceFrame? frame = null)
    {
        float height = frame?.Height ?? hi.Z - lo.Z;
        float width = frame is null ? MathF.Max(hi.X - lo.X, hi.Y - lo.Y) : frame.Width ?? 0;
        float distance = NewPieceDistance(height, width, frame is { Width: not null } ? 2 : 8);
        Target = frame is null
            ? new Vector3((lo.X + hi.X) / 2, (lo.Y + hi.Y) / 2, lo.Z + (height * 0.58f))
            : new Vector3(0, 0, height * 0.58f);
        MaxDistance = MathF.Max(DefaultMaxDistance, distance);
        LookFrom(Perspective);
        Distance = distance;
    }

    /// <summary>
    /// easy-props' Frame: the target at the middle of the bounds and the camera far enough for their bounding sphere,
    /// max(r × 1.25 / sin(fov / 2), 1.2) m and no farther than allowed, looking from where it looks now.
    /// </summary>
    /// <param name="lo">The piece's lowest corner.</param>
    /// <param name="hi">The piece's highest corner.</param>
    internal void FrameFit(Vector3 lo, Vector3 hi)
    {
        float radius = Vector3.Distance(lo, hi) / 2;
        float distance = MathF.Max(radius * 1.25f / MathF.Sin(FieldOfView / 2), MinDistance);
        Stop();
        Target = (lo + hi) / 2;
        Distance = MathF.Min(distance, MaxDistance);
    }

    /// <summary>Places the camera exactly, without the distance limits' growth (the floor's own fit).</summary>
    /// <param name="target">What it looks at.</param>
    /// <param name="distance">How far it is, kept within the limits.</param>
    internal void Place(Vector3 target, float distance)
    {
        Stop();
        Target = target;
        MaxDistance = MathF.Max(DefaultMaxDistance, distance);
        Distance = Math.Clamp(distance, MinDistance, MaxDistance);
    }

    /// <summary>The distance easy-props frames a new piece at: max(2.7 × height, 2.4 × width, 8) m.</summary>
    /// <param name="height">The piece's height.</param>
    /// <param name="width">The piece's widest side.</param>
    /// <param name="nearest">The nearest it may be: 8 m, or 2 m for a piece that says how wide it is.</param>
    /// <returns>The distance.</returns>
    internal static float NewPieceDistance(float height, float width, float nearest = 8) => MathF.Max(MathF.Max(height * 2.7f, width * 2.4f), nearest);

    /// <summary>Whether a piece whose height changed should be framed again: when it changed by more than a fifth.</summary>
    /// <param name="framedHeight">The height it was framed at.</param>
    /// <param name="height">Its height now.</param>
    /// <returns>True to frame it again.</returns>
    internal static bool ShouldReframe(float framedHeight, float height) => MathF.Abs(height - framedHeight) > framedHeight * 0.2f;

    /// <summary>Whether a press that ended this far from where it began was a click: 5 pixels or less.</summary>
    /// <param name="dx">Pixels moved across.</param>
    /// <param name="dy">Pixels moved down.</param>
    /// <returns>True for a click.</returns>
    internal static bool IsClick(double dx, double dy) => Math.Sqrt((dx * dx) + (dy * dy)) <= ClickSlop;
}
