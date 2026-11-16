using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.View;
using PropGenerator.Nature.Export;
using Silk.NET.OpenGL;

namespace PropGenerator.Desktop;

/// <summary>
/// The 3D view, in easy-props' scene: its sky (a gradient, or flat grey), fog, and its orbit camera (OrbitCamera) in
/// every mode. Three things can be shown:
/// <list type="bullet">
/// <item>The floor tiles laid out as a small floor, coloured from the painter's tone layers by the shaders (TileShader),
/// on the sky with easy-props' grid. When the layers, the palette or the tone change, the layers are first composed into
/// a colour texture of their own size, 1:1, and mipmaps are made from it; the tiles then sample only that colour texture,
/// mipmapped (and with anisotropic filtering where the driver has it), so distant rows and thin seams stay smooth. The
/// painting keeps its own light and shade. The layout: a 3 x 3 grid of squares, turned 0 and 180 degrees in a checker,
/// then two columns of triangle pairs (one at 0 and one at 180 degrees, making a square) against them at x = 3S and
/// 4S.</item>
/// <item>A rock, on easy-props' ground disc and grid (tilted by the view's slope), lit by its hemisphere, sun and fill
/// lights with the sun's soft shadow, tone mapped (Khronos PBR Neutral at 1.15) and fogged; or drawn flat or as a wireframe.</item>
/// <item>A mesh from Blender, in plain grey, on the sky with the grid.</item>
/// </list>
/// Left drag turns the camera, right drag (or Shift with the left) pans, the wheel or a middle drag zooms; the turn
/// and the pan glide to a stop. Drawn only when something changed or the camera is moving: there is no idle loop.
/// </summary>
public sealed class TileView : OpenGlControlBase
{
    private static readonly Vector3 FloorTarget = new(2 * TileMesh.S, TileMesh.S, TileMesh.T / 2);

    // The floor's first view: from the front, 55° above the ground.
    private static readonly Vector3 FloorDirection = new(0, -MathF.Cos(55f * MathF.PI / 180f), MathF.Sin(55f * MathF.PI / 180f));

    // How far below the floor tiles' undersides the ground disc sits, in metres.
    private const float FloorGroundDrop = 0.01f;

    private static readonly string[] SamplerNames = ["uMarks", "uRest", "uDrift", "uRegion", "uBaseMul", "uSeam", "uAa"];

    // GL_EXT_texture_filter_anisotropic (also core in OpenGL 4.6 as ARB_texture_filter_anisotropic).
    private const TextureParameterName MaxAnisotropy = (TextureParameterName)0x84FE;
    private const GetPName MaxAnisotropyLimit = (GetPName)0x84FF;

    private static readonly bool Trace = Environment.GetEnvironmentVariable("PROPGEN_TRACE") == "1";

    // A development check: the first 1024 compose is read back and written there as raw RGBA, rows bottom-up.
    private static readonly string? DumpComposedTo = Environment.GetEnvironmentVariable("PROPGEN_DUMP_COMPOSED");

    // The person for scale is the same figure for every piece: built once, uploaded with each context.
    private static readonly Lazy<ColouredMesh> Person = new(PersonMesh.Build);

    // The floor's corner nearest the camera's start (front left), where the person stands beside the floor.
    private static readonly Vector3 FloorLow = new(-TileMesh.S / 2, -TileMesh.S / 2, 0);

    // The collision prisms' violet, and how see-through their faces and outline are.
    private static readonly Vector3 PrismColour = SceneLook.Srgb("#b46cff");
    private const float PrismFill = 0.22f;
    private const float PrismEdges = 0.85f;

    private readonly uint[] _textures = new uint[Layers.Count];
    private readonly Sky _sky = new();
    private readonly GroundGrid _ground = new();
    private readonly ShadowMap _shadow = new();
    private GL? _gl;
    private uint _composeProgram;
    private uint _floorProgram;
    private uint _pieceProgram;
    private GlMesh _pieceGl;
    private uint _rockProgram;
    private RockUniforms _rockAt;
    private GlMesh _rockGl;

    // The picture the rock's printed points read (a bush's leaf atlas), and where its alpha cuts; 0 for none.
    private uint _rockMap;
    private PieceFrame? _frame;
    private uint _rockBark;
    private uint _rockRelief;
    private float _rockAlphaCut;
    private GlMesh _personGl;
    private GlMesh _prismFaces;
    private GlMesh _prismLines;
    private bool _prismsStale;
    private Vector3 _lo;
    private Vector3 _hi;
    private float _framedHeight;
    private bool _pointerHeld;
    private bool _floorFramePending = true;
    private ColouredMesh? _rockPending;
    private bool _rockStale;
    private bool _pieceStale;
    private bool _shadowFailed;
    private uint _emptyVao;
    private uint _colourFbo;
    private uint _colourTexture;
    private float _anisotropy;
    private bool _composeStale;
    private bool _dumped;
    private GlMesh _square;
    private GlMesh _triangle;
    private bool _texturesStale;
    private int _textureSize;
    private Palette? _palette;
    private float _drift;
    private Lighting _lighting = Lighting.Default;
    private DragKind _drag;
    private Point _dragFrom;
    private Point _pressedAt;
    private TaskCompletionSource<(byte[] Rgba, int Width, int Height)>? _shot;

    private Control? _surface;
    private readonly FarGlPainter _far = new();
    private bool _farReady;
    private FarRequest? _farWanted;

    /// <summary>
    /// Gets or sets what a left drag does to the selected part while a part tool is on; null (or one that declines the
    /// press) leaves the left button to the camera.
    /// </summary>
    internal IPartDrag? PartDrag { get; set; }

    /// <summary>
    /// The ray through a point of the view, as the camera sees it now: where it starts (on the near plane) and its unit
    /// direction, Z-up, in metres. Null before the view has a size.
    /// </summary>
    /// <param name="at">The point, in the view's surface's own coordinates.</param>
    /// <returns>The ray, or null.</returns>
    internal (Vector3 From, Vector3 Direction)? RayAt(Point at)
    {
        Size size = _surface?.Bounds.Size ?? Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        Matrix4x4 vp = Orbit.ViewProjection((float)(size.Width / size.Height));
        if (!Matrix4x4.Invert(vp, out Matrix4x4 inverse))
        {
            return null;
        }

        float x = (float)((2 * at.X / size.Width) - 1), y = (float)(1 - (2 * at.Y / size.Height));
        var near = Vector4.Transform(new Vector4(x, y, 0, 1), inverse);
        var far = Vector4.Transform(new Vector4(x, y, 1, 1), inverse);
        Vector3 from = new Vector3(near.X, near.Y, near.Z) / near.W;
        Vector3 to = new Vector3(far.X, far.Y, far.Z) / far.W;
        return (from, Vector3.Normalize(to - from));
    }

    /// <summary>Where a point of the scene shows in the view, as the camera sees it now; null before the view has a size or behind the camera.</summary>
    /// <param name="p">The point, Z-up, in metres.</param>
    /// <returns>The point in the view's surface's own coordinates, or null.</returns>
    internal Point? ScreenAt(Vector3 p)
    {
        Size size = _surface?.Bounds.Size ?? Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        var clip = Vector4.Transform(new Vector4(p, 1), Orbit.ViewProjection((float)(size.Width / size.Height)));
        return clip.W <= 0 ? null : new Point((clip.X / clip.W + 1) / 2 * size.Width, (1 - (clip.Y / clip.W)) / 2 * size.Height);
    }

    /// <summary>
    /// Draws the far-away card's pictures of a prop with this view's OpenGL (see <see cref="FarGlPainter"/>), waiting for
    /// the view's next frame. Called off the window's thread (a build); null when the view is not running, cannot draw
    /// them, or draws no frame within ten seconds.
    /// </summary>
    /// <param name="mesh">The prop at full detail, Z-up.</param>
    /// <param name="frame">The card's box.</param>
    /// <returns>The pictures, or null.</returns>
    internal FarPictures? PaintFar(ColouredMesh mesh, FarFrame frame)
    {
        if (_gl is null || !_farReady)
        {
            return null;
        }

        var wanted = new FarRequest(mesh, frame, new TaskCompletionSource<FarPictures?>(TaskCreationOptions.RunContinuationsAsynchronously));
        Interlocked.Exchange(ref _farWanted, wanted)?.Done.TrySetResult(null);
        Dispatcher.UIThread.Post(RequestNextFrameRendering);
        return wanted.Done.Task.Wait(TimeSpan.FromSeconds(10)) ? wanted.Done.Task.Result : null;
    }

    // Draws the far card's pictures asked for, before the frame's own drawing.
    private unsafe void PaintFarWanted(GL g)
    {
        if (Interlocked.Exchange(ref _farWanted, null) is not { } wanted)
        {
            return;
        }

        GlMesh mesh = default;
        List<(int Unit, string Slot, uint Texture)> pictures = [];
        try
        {
            mesh = GlMesh.UploadRock(g, wanted.Mesh, edges: false);
            foreach ((int unit, string slot, bool srgb, bool repeat) in FarGlPainter.Binds(wanted.Mesh))
            {
                MeshTexture texture = slot switch
                {
                    nameof(ColouredMesh.Bark) => wanted.Mesh.Bark!,
                    nameof(ColouredMesh.Relief) => wanted.Mesh.Relief!,
                    _ => wanted.Mesh.Texture!,
                };
                InternalFormat format = srgb ? InternalFormat.Srgb8Alpha8 : InternalFormat.Rgba8;
                pictures.Add((unit, slot, repeat ? Repeating(g, texture, format) : Plain(g, texture, format)));
            }

            wanted.Done.TrySetResult(_far.Paint(g, mesh, pictures, wanted.Mesh.Texture?.AlphaCut ?? 0, wanted.Frame, FarCard.ViewSize));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            TraceLog.Write($"The far-away card was not drawn: {e.Message}");
            wanted.Done.TrySetResult(null);
        }
        finally
        {
            mesh.Delete(g);
            foreach ((int _, string _, uint texture) in pictures)
            {
                g.DeleteTexture(texture);
            }
        }
    }

    // The far card's printed picture: filtered linearly with no mipmaps, kept to its edges.
    private static unsafe uint Plain(GL g, MeshTexture texture, InternalFormat format)
    {
        uint id = g.GenTexture();
        g.BindTexture(TextureTarget.Texture2D, id);
        fixed (byte* pixels = texture.Rgba)
        {
            g.TexImage2D(TextureTarget.Texture2D, 0, format, (uint)texture.Width, (uint)texture.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        }

        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        g.BindTexture(TextureTarget.Texture2D, 0);
        return id;
    }

    /// <summary>Gets the view's height in pixels, for turning a drag into metres (1 before it has a size).</summary>
    internal double ViewHeight => Math.Max(1, _surface?.Bounds.Height ?? Bounds.Height);

    /// <summary>Starts on the floor, from the front and above.</summary>
    public TileView()
    {
        Orbit.Target = FloorTarget;
        Orbit.LookFrom(FloorDirection);
    }

    /// <summary>
    /// The newest layers were drawn: their size, and the milliseconds their upload and compose took (with
    /// PROPGEN_TRACE=1 the GPU is waited for, so the time is the real one).
    /// </summary>
    public event Action<int, double>? LayersDrawn;

    /// <summary>A tone or palette change was composed: the milliseconds the compose and mipmaps took (as above).</summary>
    public event Action<double>? Recomposed;

    /// <summary>OpenGL could not set up the view (the message says why).</summary>
    public event Action<string>? Failed;

    /// <summary>
    /// The OpenGL view was set up again while a rock was shown, and the rock (which the view does not keep once it is
    /// uploaded) is gone from it: whoever has the rock shows it again. Raised on the window's thread.
    /// </summary>
    public event Action? RockLost;

    /// <summary>
    /// A press in the view moved no more than 5 pixels before it was let go: a click (for picking), not a drag. Gives
    /// where, in the surface's coordinates.
    /// </summary>
    internal event Action<Point>? Clicked;

    /// <summary>Gets the layers shown (or about to be, on the next frame); null before the first paint.</summary>
    public Layers? Layers { get; private set; }

    /// <summary>Gets a value indicating whether a mesh (from Blender) is shown instead of the floor.</summary>
    public bool ShowingPiece => PieceMesh is not null;

    /// <summary>Gets a value indicating whether a vertex-coloured rock mesh is shown instead of the floor.</summary>
    public bool ShowingRock { get; private set; }

    /// <summary>Gets the mesh shown instead of the floor, or null when the floor is shown.</summary>
    public Mesh? PieceMesh { get; private set; }

    /// <summary>Gets how strongly the marks light and darken the wood.</summary>
    public float Contrast { get; private set; }

    /// <summary>Gets the camera (the toolbar's angles and the compass move it; the tests read it).</summary>
    internal OrbitCamera Orbit { get; } = new();

    /// <summary>Gets how the rock is drawn: lit, flat or wireframe.</summary>
    internal string Shading { get; private set; } = ViewState.Lit;

    /// <summary>Gets the scene around the piece.</summary>
    internal string Scene { get; private set; } = ViewState.DefaultScene;

    /// <summary>Gets a value indicating whether the 1.8 m person stands beside the piece.</summary>
    internal bool ShowPerson { get; private set; }

    /// <summary>
    /// Gets the ground's turn: the disc and both grids tilt by it about the prop's base (the view state's slope, plan
    /// E9). The identity while the floor tile is shown (it stays level) and on level ground; the prop, the person, the
    /// collision shapes and part drags never turn.
    /// </summary>
    internal Matrix4x4 GroundModel => ShowingFloor ? Matrix4x4.Identity : GroundTilt.Matrix(_slope, _downhill);

    // The floor tiles are shown (no rock and no mesh from Blender).
    private bool ShowingFloor => !ShowingRock && PieceMesh is null;

    // The view state's slope and the way it falls, in degrees.
    private double _slope;
    private double _downhill;

    /// <summary>Gets the wind in the view: on or off, its strength now, and how the prop on show moves.</summary>
    internal LiveWind Wind { get; } = new();

    // The view state followed, for the wind's strength, which Settings changes without a Changed.
    private ViewState? _state;

    // When the last frame was drawn, for the wind's easing and waves (0 before the first).
    private long _lastFrame;

    /// <summary>
    /// Gets where the person for scale stands: 0.6 m out from the front-left corner of what is shown (the rock, the mesh
    /// from Blender, or the floor), on the ground.
    /// </summary>
    internal Vector3 PersonAt => ShowingRock || PieceMesh is not null ? PersonMesh.Place(_lo) : PersonMesh.Place(FloorLow);

    /// <summary>Gets the shapes you can walk into, drawn over the piece; null when none are shown.</summary>
    internal CollisionPrisms? Prisms { get; private set; }

    /// <summary>Gets how many draw calls the last frame made (0 before the first).</summary>
    internal int DrawCalls { get; private set; }

    /// <summary>
    /// Gets the size of the piece shown, in metres along X, Y and Z: the rock's or the Blender mesh's bounds, or one
    /// floor tile.
    /// </summary>
    internal Vector3 ShownSize => ShowingRock || PieceMesh is not null ? _hi - _lo : new Vector3(TileMesh.S, TileMesh.S, TileMesh.T);

    /// <summary>
    /// Raised on the window's thread after each frame is drawn, so what follows the camera (the compass) can catch up.
    /// </summary>
    internal event Action? FrameDrawn;

    /// <summary>Shows the shapes you can walk into over the piece, or none; uploaded on the next frame.</summary>
    /// <param name="prisms">The prisms, or null to show none.</param>
    internal void SetPrisms(CollisionPrisms? prisms)
    {
        Prisms = prisms;
        _prismsStale = true;
        RequestNextFrameRendering();
    }

    /// <summary>Follows the window's view state: its shading, scene, sun and the person for scale are drawn from now on.</summary>
    /// <param name="state">The view state.</param>
    internal void Follow(ViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        void Take()
        {
            Shading = state.Shading;
            Scene = state.Scene;
            ShowPerson = state.ShowPerson;
            Wind.On = state.Wind;
            Wind.Wanted = state.WindStrength;
            _lighting = state.Lighting;
            _slope = state.GroundSlope;
            _downhill = state.GroundDownhill;
            _shadow.Stale = true;
            RequestNextFrameRendering();
        }

        state.Changed += Take;
        _state = state;
        Take();
    }

    /// <summary>
    /// Says whether a mouse button is down anywhere in the window. While one is, a rock that changed height is not
    /// framed again (the camera must not jump during a drag); once it is let go, a rock whose height changed by more
    /// than a fifth since it was framed is framed again.
    /// </summary>
    /// <param name="held">True while a button is down.</param>
    internal void SetPointerHeld(bool held)
    {
        _pointerHeld = held;
        if (!held && ShowingRock && OrbitCamera.ShouldReframe(_framedHeight, _hi.Z - _lo.Z))
        {
            FrameNewPiece();
            RequestNextFrameRendering();
        }
    }

    /// <summary>Shows new layers: they replace the textures of the ones before on the next frame.</summary>
    /// <param name="l">The layers.</param>
    public void SetLayers(Layers l)
    {
        ArgumentNullException.ThrowIfNull(l);
        Layers = l;
        _texturesStale = true;
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Shows a mesh instead of the floor, in plain neutral grey, framed as easy-props frames a new piece. The mesh before
    /// it, if any, is freed on the next frame.
    /// </summary>
    /// <param name="mesh">The mesh, in the Z-up frame.</param>
    /// <exception cref="ArgumentException">The mesh's arrays do not fit together or it has no triangles.</exception>
    public void ShowPiece(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        int n = mesh.Positions.Length / 3;
        if (mesh.Positions.Length % 3 != 0 || mesh.Uvs.Length != n * 2 || mesh.Indices.Length == 0
            || mesh.Indices.Length % 3 != 0 || mesh.Indices.Any(i => i < 0 || i >= n))
        {
            throw new ArgumentException("The mesh's positions, texture coordinates and triangles do not fit together.", nameof(mesh));
        }

        LeaveRock();
        (_lo, _hi) = BoundsOf(mesh.Positions);
        PieceMesh = mesh;
        _pieceStale = true;
        FrameNewPiece();
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Shows a vertex-coloured rock mesh instead of the floor, standing on the ground disc. The mesh is uploaded on the
    /// next frame and not kept; only its bounds are. The mesh shown before it is freed on the next frame.
    /// </summary>
    /// <param name="mesh">The rock, Z-up, in metres.</param>
    /// <param name="frame">
    /// True to frame the camera on the rock as easy-props frames a new piece; false to leave the camera as it is (a
    /// slider is being dragged), unless no mouse button is down and the rock's height changed by more than a fifth since
    /// it was framed. Coming from the floor or a Blender piece, the camera is framed either way, as its old target means
    /// nothing for a rock.
    /// </param>
    /// <exception cref="ArgumentException">The mesh's arrays do not fit together or it has no triangles.</exception>
    public void ShowRock(ColouredMesh mesh, bool frame)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        int n = mesh.Positions.Length / 3;
        if (mesh.Positions.Length % 3 != 0 || mesh.Normals.Length != n * 3 || mesh.Colours.Length != n * 3
            || mesh.Indices.Length == 0 || mesh.Indices.Length % 3 != 0 || mesh.Indices.Any(i => i < 0 || i >= n)
            || (mesh.Roughness is { } rough && rough.Length != n))
        {
            throw new ArgumentException("The rock's positions, normals, colours, roughness and triangles do not fit together.", nameof(mesh));
        }

        (Vector3 lo, Vector3 hi) = BoundsOf(mesh.Positions);
        bool reframe = frame || !ShowingRock || (!_pointerHeld && OrbitCamera.ShouldReframe(_framedHeight, hi.Z - lo.Z));
        PieceMesh = null;
        _pieceStale = true;
        ShowingRock = true;
        _rockPending = mesh;
        _rockStale = true;
        _shadow.Stale = true;
        (_lo, _hi) = (lo, hi);
        _frame = mesh.Frame;
        Wind.Measure(lo, hi);
        if (reframe)
        {
            FrameNewPiece();
        }

        RequestNextFrameRendering();
    }

    private static (Vector3 Lo, Vector3 Hi) BoundsOf(float[] positions)
    {
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (int i = 0; i + 2 < positions.Length; i += 3)
        {
            var p = new Vector3(positions[i], positions[i + 1], positions[i + 2]);
            lo = Vector3.Min(lo, p);
            hi = Vector3.Max(hi, p);
        }

        return (lo, hi);
    }

    private void FrameNewPiece()
    {
        Orbit.FrameNew(_lo, _hi, ShowingRock ? _frame : null);
        _framedHeight = _hi.Z - _lo.Z;
        _floorFramePending = false;
    }

    private void LeaveRock()
    {
        if (ShowingRock)
        {
            ShowingRock = false;
            _rockPending = null;
            _rockStale = true;
        }
    }

    /// <summary>
    /// Fits what is shown in the view again, looking from where the camera looks now: a rock or a mesh from Blender by
    /// easy-props' Frame (its bounding sphere), the floor by its own fit (all of it in the picture).
    /// </summary>
    internal void Frame()
    {
        if (ShowingRock || PieceMesh is not null)
        {
            Orbit.FrameFit(_lo, _hi);
        }
        else
        {
            Orbit.Target = FloorTarget;
            _floorFramePending = true;
        }

        RequestNextFrameRendering();
    }

    /// <summary>
    /// Looks at what is shown from a direction (Z up), keeping the camera's target and distance; the camera's own limits
    /// keep it between 0.05 and 1.52 rad from straight up.
    /// </summary>
    /// <param name="from">From the target towards the camera; need not be of length one.</param>
    internal void LookFrom(Vector3 from)
    {
        Orbit.LookFrom(from);
        RequestNextFrameRendering();
    }

    /// <summary>Shows the floor again; the mesh shown before it is freed on the next frame.</summary>
    public void ShowFloor()
    {
        if (PieceMesh is null && !ShowingRock)
        {
            return;
        }

        LeaveRock();
        PieceMesh = null;
        _pieceStale = true;
        Orbit.Target = FloorTarget;
        Orbit.LookFrom(FloorDirection);
        _floorFramePending = true;
        RequestNextFrameRendering();
    }

    /// <summary>Sets the colours the layers are turned into.</summary>
    /// <param name="p">The palette.</param>
    public void SetPalette(Palette p)
    {
        _palette = p;
        _composeStale = true;
        RequestNextFrameRendering();
    }

    /// <summary>Sets the contrast and the colour drift.</summary>
    /// <param name="contrast">How strongly the marks light and darken the wood.</param>
    /// <param name="drift">How strongly the red/grey drift shows.</param>
    public void SetTone(float contrast, float drift)
    {
        Contrast = contrast;
        _drift = drift;
        _composeStale = true;
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Takes the mouse from a surface laid over the view (Avalonia cannot hit-test the OpenGL picture itself), with
    /// easy-props' bindings: left drag turns the camera (with Shift, Ctrl or Meta it pans), right drag pans (with one
    /// of those it turns), middle drag zooms, and so does the wheel. A press let go within 5 pixels is a click.
    /// </summary>
    /// <param name="surface">The transparent control over the view.</param>
    public void Attach(Control surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surface = surface;
        surface.PointerPressed += (_, e) =>
        {
            PointerUpdateKind kind = e.GetCurrentPoint(surface).Properties.PointerUpdateKind;
            bool other = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            _drag = kind switch
            {
                PointerUpdateKind.LeftButtonPressed => other ? DragKind.Pan : DragKind.Orbit,
                PointerUpdateKind.RightButtonPressed => other ? DragKind.Orbit : DragKind.Pan,
                PointerUpdateKind.MiddleButtonPressed => DragKind.Zoom,
                _ => DragKind.None,
            };
            if (_drag == DragKind.None)
            {
                return;
            }

            _dragFrom = _pressedAt = e.GetPosition(surface);

            // With a part tool on and a part selected, the left button works the part instead of the camera.
            if (kind == PointerUpdateKind.LeftButtonPressed && PartDrag is { } part && part.Begin(_pressedAt))
            {
                _drag = DragKind.Part;
            }

            e.Pointer.Capture(surface);
        };
        surface.PointerMoved += (_, e) =>
        {
            if (_drag == DragKind.None)
            {
                return;
            }

            Point at = e.GetPosition(surface);
            double dx = at.X - _dragFrom.X, dy = at.Y - _dragFrom.Y, height = surface.Bounds.Height;
            switch (_drag)
            {
                case DragKind.Part:
                    PartDrag?.Move(at, _pressedAt, (e.KeyModifiers & KeyModifiers.Shift) != 0);
                    break;
                case DragKind.Orbit:
                    Orbit.Rotate(dx, dy, height);
                    break;
                case DragKind.Pan:
                    Orbit.Pan(dx, dy, height);
                    break;
                default:
                    Orbit.DragZoom(dy);
                    break;
            }

            _dragFrom = at;
            RequestNextFrameRendering();
        };
        surface.PointerReleased += (_, e) =>
        {
            if (_drag == DragKind.None)
            {
                return;
            }

            bool part = _drag == DragKind.Part;
            _drag = DragKind.None;
            e.Pointer.Capture(null);
            Point at = e.GetPosition(surface);
            if (part)
            {
                PartDrag?.End();
            }

            if (OrbitCamera.IsClick(at.X - _pressedAt.X, at.Y - _pressedAt.Y))
            {
                Clicked?.Invoke(at);
            }
        };
        surface.PointerCaptureLost += (_, _) =>
        {
            if (_drag == DragKind.Part)
            {
                PartDrag?.End();
            }

            _drag = DragKind.None;
        };
        surface.PointerWheelChanged += (_, e) =>
        {
            Orbit.Wheel(e.Delta.Y);
            RequestNextFrameRendering();
        };
    }

    /// <inheritdoc/>
    protected override void OnOpenGlInit(GlInterface gl)
    {
        _gl = GL.GetApi(gl.GetProcAddress);
        string head = GlVersion.Type == GlProfileType.OpenGLES ? TileShader.EsHead : TileShader.DesktopHead;
        try
        {
            _composeProgram = Link(_gl, head + TileShader.ComposeVertex, head + TileShader.ComposeFragment);
            _floorProgram = Link(_gl, head + TileShader.Vertex, head + TileShader.Fragment);
        }
        catch (InvalidOperationException e)
        {
            DeletePrograms(_gl);
            string message = "The 3D view could not start: " + e.Message;
            Dispatcher.UIThread.Post(() => Failed?.Invoke(message));
            return;
        }

        try
        {
            _pieceProgram = Link(_gl, head + TileShader.PieceVertex, head + TileShader.PieceFragment);
        }
        catch (InvalidOperationException e)
        {
            // Only meshes from Blender cannot be shown; the floor still works.
            string message = "Meshes from Blender cannot be shown: " + e.Message;
            Dispatcher.UIThread.Post(() => Failed?.Invoke(message));
        }

        _farReady = _far.Init(_gl, head);
        try
        {
            _rockProgram = Link(_gl, head + TileShader.RockVertex, head + TileShader.Finish + TileShader.RockFragment);
            _rockAt = RockUniforms.Find(_gl, _rockProgram);
        }
        catch (InvalidOperationException e)
        {
            // Only rocks cannot be shown; the floor still works.
            string message = "Rocks cannot be shown: " + e.Message;
            Dispatcher.UIThread.Post(() => Failed?.Invoke(message));
        }

        try
        {
            _sky.Init(_gl, head);
            _ground.Init(_gl, head);
            _shadow.Init(_gl, head);
        }
        catch (InvalidOperationException e)
        {
            // The pieces still show, without the sky, the ground or the sun's shadow.
            string message = "The scene around the piece cannot be drawn: " + e.Message;
            Dispatcher.UIThread.Post(() => Failed?.Invoke(message));
        }

        _shadowFailed = false;
        _square = GlMesh.Upload(_gl, TileMesh.Square());
        _triangle = GlMesh.Upload(_gl, TileMesh.Triangle());
        _personGl = GlMesh.UploadRock(_gl, Person.Value, edges: false);
        _prismsStale = Prisms is not null;

        // The compose pass draws one triangle from the vertex number; a core context still wants a vertex array bound.
        _emptyVao = _gl.GenVertexArray();
        _anisotropy = 0;
        if (_gl.IsExtensionPresent("GL_EXT_texture_filter_anisotropic") || _gl.IsExtensionPresent("GL_ARB_texture_filter_anisotropic"))
        {
            _anisotropy = Math.Min(16f, _gl.GetFloat(MaxAnisotropyLimit));
        }

        // The layers (if any) go up again in this context.
        _texturesStale = Layers is not null;
        _pieceStale = PieceMesh is not null;

        // The rock's managed mesh is not kept after upload, so a lost context cannot get it back: whoever has the rock is
        // told, and shows it again. A rock not yet uploaded still goes up.
        _rockGl = default;
        _rockMap = 0;
        _rockBark = 0;
        _rockRelief = 0;
        _rockStale = _rockPending is not null;
        if (ShowingRock && _rockPending is null)
        {
            Dispatcher.UIThread.Post(() => RockLost?.Invoke());
        }
    }

    /// <summary>
    /// The OpenGL context is gone without a deinit (Avalonia sets the view up again after): every handle belonged to it,
    /// so they are forgotten, not freed, and made afresh in the next init (the layers and a pending mesh go up again
    /// there; a rock already uploaded is asked for again through <see cref="RockLost"/>).
    /// </summary>
    protected override void OnOpenGlLost()
    {
        _sky.Forget();
        _ground.Forget();
        _shadow.Forget();
        Array.Clear(_textures);
        _colourFbo = 0;
        _colourTexture = 0;
        _textureSize = 0;
        _composeProgram = 0;
        _floorProgram = 0;
        _pieceProgram = 0;
        _rockProgram = 0;
        _pieceGl = default;
        _rockGl = default;
        _rockMap = 0;
        _rockBark = 0;
        _rockRelief = 0;
        _personGl = default;
        _prismFaces = default;
        _prismLines = default;
        _square = default;
        _triangle = default;
        _emptyVao = 0;
        _gl = null;
        base.OnOpenGlLost();
    }

    /// <inheritdoc/>
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl is null)
        {
            return;
        }

        DeleteTextures(_gl);
        _far.Delete(_gl);
        _farReady = false;
        _square.Delete(_gl);
        _triangle.Delete(_gl);
        _pieceGl.Delete(_gl);
        _pieceGl = default;
        _rockGl.Delete(_gl);
        _rockGl = default;
        UploadRockMap(_gl, null);
        UploadBark(_gl, null);
        _personGl.Delete(_gl);
        _personGl = default;
        DeletePrisms(_gl);
        _sky.Delete(_gl);
        _ground.Delete(_gl);
        _shadow.Delete(_gl);
        DeletePrograms(_gl);
        if (_emptyVao != 0)
        {
            _gl.DeleteVertexArray(_emptyVao);
        }

        _emptyVao = 0;
        _square = default;
        _triangle = default;
        _gl = null;
    }

    /// <inheritdoc/>
    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl is not GL g || _floorProgram == 0)
        {
            return;
        }

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int w = Math.Max(1, (int)(Bounds.Width * scaling));
        int h = Math.Max(1, (int)(Bounds.Height * scaling));
        float aspect = w / (float)h;
        bool moving = Orbit.Step();
        if (_state is { } followed)
        {
            Wind.Wanted = followed.WindStrength;
        }

        long now = Stopwatch.GetTimestamp();
        Wind.Step(_lastFrame == 0 ? 0 : Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds);
        _lastFrame = now;
        if (_floorFramePending && !ShowingRock && PieceMesh is null)
        {
            Orbit.Place(FloorTarget, FrameDistance(aspect));
            _floorFramePending = false;
        }

        long t = Stopwatch.GetTimestamp();
        bool uploaded = false;
        if (_texturesStale && Layers is Layers l)
        {
            Upload(g, l);
            _texturesStale = false;
            _composeStale = true;
            uploaded = true;
        }

        bool composed = false;
        if (_composeStale && _textureSize > 0 && _palette is not null)
        {
            Compose(g);
            _composeStale = false;
            composed = true;
        }

        double ms = -1;
        if (uploaded || composed)
        {
            if (Trace)
            {
                g.Finish();
            }

            ms = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        }

        if (_pieceStale)
        {
            _pieceGl.Delete(g);
            _pieceGl = PieceMesh is Mesh shownPiece ? GlMesh.Upload(g, shownPiece) : default;
            _pieceStale = false;
        }

        if (_rockStale)
        {
            _rockGl.Delete(g);
            _rockGl = _rockPending is ColouredMesh shownRock ? GlMesh.UploadRock(g, shownRock, edges: true) : default;
            UploadRockMap(g, _rockPending?.Texture);
            UploadBark(g, _rockPending);
            _rockPending = null;
            _rockStale = false;
            _shadow.Stale = true;
        }

        if (_prismsStale)
        {
            DeletePrisms(g);
            if (Prisms is CollisionPrisms prisms && prisms.Count > 0)
            {
                _prismFaces = GlMesh.UploadTriangles(g, prisms.Positions, prisms.Triangles);
                _prismLines = GlMesh.UploadLines(g, prisms.Lines);
            }

            _prismsStale = false;
        }

        SceneColours colours = SceneLook.Colours(Scene);
        bool rock = ShowingRock && _rockProgram != 0;
        bool piece = PieceMesh is not null && _pieceProgram != 0;
        SceneLights lights = SceneLook.Lights(Scene, _lighting);
        Matrix4x4 groundModel = GroundModel;
        var box = ShadowBox.For((_lo + _hi) / 2, Vector3.Distance(_lo, _hi) / 2, lights.SunDirection, Vector3.TransformNormal(Vector3.UnitZ, groundModel));
        if (rock && _shadow.Ready)
        {
            // The map is made with the first rock, so the rock's shadow sampler always has its depth texture.
            if (_shadow.Ensure(g) is string why && !_shadowFailed)
            {
                _shadowFailed = true;
                Dispatcher.UIThread.Post(() => Failed?.Invoke(why));
            }

            if (_shadow.Stale && Shading == ViewState.Lit && lights.CastsShadow)
            {
                _shadow.Render(g, box, _rockGl, ShowPerson ? _personGl : default, PersonAt, _rockMap, _rockAlphaCut);
            }
        }

        PaintFarWanted(g);
        g.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
        g.Viewport(0, 0, (uint)w, (uint)h);
        g.ClearColor(colours.SkyHorizon.X, colours.SkyHorizon.Y, colours.SkyHorizon.Z, 1f);
        g.ClearDepth(1);
        g.DepthMask(true);
        g.Disable(EnableCap.CullFace);
        g.Disable(EnableCap.Blend);
        g.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _sky.Draw(g, colours, h);
        int calls = 1;
        g.Enable(EnableCap.DepthTest);
        g.DepthFunc(DepthFunction.Lequal);

        Vector3 eye = Orbit.Eye;
        Vector3 forward = -Orbit.Back;
        Matrix4x4 viewProj = Orbit.ViewProjection(aspect);
        if (_rockProgram != 0)
        {
            calls += DrawGroundAndRock(g, viewProj, eye, forward, colours, lights, box, rock, groundModel);
        }

        if (piece)
        {
            g.UseProgram(_pieceProgram);
            g.Uniform3(g.GetUniformLocation(_pieceProgram, "uEye"), eye.X, eye.Y, eye.Z);
            Matrix4x4 mvp = viewProj;
            g.UniformMatrix4(g.GetUniformLocation(_pieceProgram, "uMvp"), 1, false, MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
            _pieceGl.Draw(g);
            calls++;
        }
        else if (!ShowingRock)
        {
            g.UseProgram(_floorProgram);
            SetFloorUniforms(g);
            int mvpAt = g.GetUniformLocation(_floorProgram, "uMvp");
            foreach ((bool triangle, Matrix4x4 model) in FloorLayout())
            {
                Matrix4x4 mvp = model * viewProj;
                g.UniformMatrix4(mvpAt, 1, false, MemoryMarshal.CreateReadOnlySpan(ref mvp.M11, 16));
                (triangle ? _triangle : _square).Draw(g);
                calls++;
            }
        }

        // The grid goes over everything, see-through, hidden where something is in front of it.
        _ground.Draw(g, viewProj, eye, forward, colours.Fog, groundModel);
        calls += _ground.Ready ? 2 : 0;

        // The shapes you can walk into go over everything, hidden by nothing, as easy-props draws them.
        if (_prismFaces.Vao != 0)
        {
            _ground.DrawOver(g, viewProj, eye, forward, colours.Fog, _prismFaces, _prismLines, PrismColour, PrismFill, PrismEdges);
            calls += 2;
        }

        DrawCalls = calls;

        g.BindVertexArray(0);
        g.ActiveTexture(TextureUnit.Texture4);
        g.BindTexture(TextureTarget.Texture2D, 0);
        g.ActiveTexture(TextureUnit.Texture3);
        g.BindTexture(TextureTarget.Texture2D, 0);
        g.ActiveTexture(TextureUnit.Texture2);
        g.BindTexture(TextureTarget.Texture2D, 0);
        g.ActiveTexture(TextureUnit.Texture1);
        g.BindTexture(TextureTarget.Texture2D, 0);
        g.ActiveTexture(TextureUnit.Texture0);
        g.BindTexture(TextureTarget.Texture2D, 0);
        g.UseProgram(0);
        g.Disable(EnableCap.DepthTest);
        g.Disable(EnableCap.Blend);
        g.DepthMask(true);
        TakeShot(g, w, h);

        if (uploaded && Layers is Layers shown)
        {
            int size = shown.Size;
            Dispatcher.UIThread.Post(() => LayersDrawn?.Invoke(size, ms));
        }
        else if (composed)
        {
            Dispatcher.UIThread.Post(() => Recomposed?.Invoke(ms));
        }

        if (moving || (Wind.Moving && ShowingRock))
        {
            // The camera is still gliding, or the prop moves in the wind: another frame.
            RequestNextFrameRendering();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            FrameDrawn?.Invoke();
        }
    }

    // The rock's printed picture, sRGB so it is read linear, with mipmaps, clamped at its edges (as the GLB's leaf
    // texture); the old one is freed. Null frees it and leaves none.
    private unsafe void UploadRockMap(GL g, MeshTexture? texture)
    {
        if (_rockMap != 0)
        {
            g.DeleteTexture(_rockMap);
            _rockMap = 0;
        }

        _rockAlphaCut = texture?.AlphaCut ?? 0;
        if (texture is null)
        {
            return;
        }

        _rockMap = g.GenTexture();
        g.BindTexture(TextureTarget.Texture2D, _rockMap);
        fixed (byte* pixels = texture.Rgba)
        {
            g.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Srgb8Alpha8, (uint)texture.Width, (uint)texture.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        }

        g.GenerateMipmap(TextureTarget.Texture2D);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        g.BindTexture(TextureTarget.Texture2D, 0);
    }

    // The bark's strokes (sRGB) and relief (linear), repeating with mipmaps and four-fold anisotropy as easy-props'
    // bark textures; the old ones are freed. Null, or a mesh without bark, leaves none.
    private void UploadBark(GL g, ColouredMesh? mesh)
    {
        foreach (uint old in (uint[])[_rockBark, _rockRelief])
        {
            if (old != 0)
            {
                g.DeleteTexture(old);
            }
        }

        _rockBark = mesh?.Bark is { } bark ? Repeating(g, bark, InternalFormat.Srgb8Alpha8) : 0;
        _rockRelief = mesh?.Relief is { } relief ? Repeating(g, relief, InternalFormat.Rgba8) : 0;
    }

    private static unsafe uint Repeating(GL g, MeshTexture texture, InternalFormat format)
    {
        const TextureParameterName maxAnisotropy = (TextureParameterName)0x84FE;
        uint id = g.GenTexture();
        g.BindTexture(TextureTarget.Texture2D, id);
        fixed (byte* pixels = texture.Rgba)
        {
            g.TexImage2D(TextureTarget.Texture2D, 0, format, (uint)texture.Width, (uint)texture.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        }

        g.GenerateMipmap(TextureTarget.Texture2D);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        if (g.IsExtensionPresent("GL_EXT_texture_filter_anisotropic") || g.IsExtensionPresent("GL_ARB_texture_filter_anisotropic"))
        {
            g.TexParameter(TextureTarget.Texture2D, maxAnisotropy, 4f);
        }

        g.BindTexture(TextureTarget.Texture2D, 0);
        return id;
    }

    private void DeletePrisms(GL g)
    {
        _prismFaces.Delete(g);
        _prismFaces = default;
        _prismLines.Delete(g);
        _prismLines = default;
    }

    // The ground disc with easy-props' lights in every mode, the person for scale when shown (always lit, in its own
    // grey), and the rock when one is shown; the sun's shadow is for the rock in the lit look only. Under the floor the
    // disc sits a centimetre low, so it never fights the tiles' undersides. The disc turns by the ground's slope
    // (groundModel); the person and the rock stay level. Gives how many draw calls it made.
    private int DrawGroundAndRock(GL g, Matrix4x4 viewProj, Vector3 eye, Vector3 forward, SceneColours colours, SceneLights lights, ShadowBox box, bool rock, Matrix4x4 groundModel)
    {
        RockUniforms u = _rockAt;
        g.UseProgram(_rockProgram);
        g.UniformMatrix4(u.Model, 1, false, MemoryMarshal.CreateReadOnlySpan(ref groundModel.M11, 16));
        u.Finish.Set(g, eye, forward, colours.Fog);
        g.UniformMatrix4(u.Mvp, 1, false, MemoryMarshal.CreateReadOnlySpan(ref viewProj.M11, 16));
        Matrix4x4 lookup = box.Lookup;
        g.UniformMatrix4(u.ShadowMatrix, 1, false, MemoryMarshal.CreateReadOnlySpan(ref lookup.M11, 16));
        g.Uniform3(u.Offset, 0f, 0f, 0f);
        g.Uniform1(u.NormalBias, box.NormalBias);
        g.Uniform1(u.WindStrength, 0f);
        g.Uniform1(u.WindTime, (float)(Wind.Time % 3600));
        g.Uniform1(u.WindHeight, Wind.Height);
        g.Uniform1(u.WindReach, Wind.Reach);
        g.Uniform1(u.WindFoliage, Wind.Sway?.Foliage == true ? 1f : 0f);
        Vector3 sky = lights.HemisphereSky * lights.HemisphereIntensity;
        Vector3 ground = lights.HemisphereGround * lights.HemisphereIntensity;
        Vector3 sun = lights.SunColour * lights.SunIntensity;
        Vector3 fill = lights.FillColour * lights.FillIntensity;
        g.Uniform3(u.HemiSky, sky.X, sky.Y, sky.Z);
        g.Uniform3(u.HemiGround, ground.X, ground.Y, ground.Z);
        g.Uniform3(u.SunDir, lights.SunDirection.X, lights.SunDirection.Y, lights.SunDirection.Z);
        g.Uniform3(u.SunColour, sun.X, sun.Y, sun.Z);
        g.Uniform3(u.FillDir, lights.FillDirection.X, lights.FillDirection.Y, lights.FillDirection.Z);
        g.Uniform3(u.FillColour, fill.X, fill.Y, fill.Z);
        bool lit = Shading == ViewState.Lit;
        bool shadowOn = rock && lit && lights.CastsShadow && _shadow.Texture != 0 && !_shadowFailed;
        g.Uniform1(u.ShadowOn, shadowOn ? 1 : 0);
        g.Uniform1(u.ShadowStrength, lights.Shadow);
        g.Uniform1(u.ShadowBias, box.Bias);
        g.Uniform1(u.ShadowRadius, ShadowBox.Radius);
        const float mapSize = ShadowBox.MapSize;
        g.Uniform2(u.ShadowSize, mapSize, mapSize);
        g.ActiveTexture(TextureUnit.Texture1);
        g.BindTexture(TextureTarget.Texture2D, _shadow.Texture);
        g.Uniform1(u.ShadowMap, 1);
        g.ActiveTexture(TextureUnit.Texture2);
        g.BindTexture(TextureTarget.Texture2D, _rockMap);
        g.Uniform1(u.Map, 2);
        g.Uniform1(u.AlphaCut, _rockAlphaCut);
        g.ActiveTexture(TextureUnit.Texture3);
        g.BindTexture(TextureTarget.Texture2D, _rockBark);
        g.Uniform1(u.Bark, 3);
        g.ActiveTexture(TextureUnit.Texture4);
        g.BindTexture(TextureTarget.Texture2D, _rockRelief);
        g.Uniform1(u.Relief, 4);
        g.ActiveTexture(TextureUnit.Texture0);
        Vector3 wire = SceneLook.ToLinear(SceneLook.WireframeColour);
        g.Uniform3(u.Wire, wire.X, wire.Y, wire.Z);

        // The ground is always lit (shadowed in the lit look only); the rock as the shading says.
        Vector3 groundColour = SceneLook.ToLinear(colours.Ground);
        g.Uniform1(u.Mode, 0);
        g.Uniform3(u.Base, groundColour.X, groundColour.Y, groundColour.Z);
        bool floor = !ShowingRock && PieceMesh is null;
        if (floor)
        {
            Matrix4x4 low = Matrix4x4.CreateTranslation(0, 0, -FloorGroundDrop) * viewProj;
            g.UniformMatrix4(u.Mvp, 1, false, MemoryMarshal.CreateReadOnlySpan(ref low.M11, 16));
        }

        _ground.DiscMesh.Draw(g);
        int calls = 1;
        Matrix4x4 level = Matrix4x4.Identity;
        g.UniformMatrix4(u.Model, 1, false, MemoryMarshal.CreateReadOnlySpan(ref level.M11, 16));
        g.UniformMatrix4(u.Mvp, 1, false, MemoryMarshal.CreateReadOnlySpan(ref viewProj.M11, 16));
        g.Uniform3(u.Base, 1f, 1f, 1f);
        if (ShowPerson && _personGl.Vao != 0)
        {
            Vector3 at = PersonAt;
            g.Uniform3(u.Offset, at.X, at.Y, at.Z);
            _personGl.Draw(g);
            g.Uniform3(u.Offset, 0f, 0f, 0f);
            calls++;
        }

        if (!rock)
        {
            return calls;
        }

        // ponytail: the sun's shadow stays where the still prop casts it; easy-props moves it too. Add the wind to the
        // shadow pass (TileShader.DepthVertex, ShadowMap.Render, made stale each frame) if a still shadow under a
        // swaying tree shows.
        g.Uniform1(u.WindStrength, (float)Wind.Strength);
        if (Shading == ViewState.Wireframe)
        {
            g.Uniform1(u.Mode, 2);
            _rockGl.DrawEdges(g);
        }
        else
        {
            g.Uniform1(u.Mode, lit ? 0 : 1);
            _rockGl.Draw(g);
        }

        return calls + 1;
    }

    /// <summary>
    /// A picture of the view as it is drawn now: the next frame is read back from the framebuffer in the OpenGL render
    /// callback. Fails when there is no OpenGL view running (the tests) or no frame is drawn within a few seconds.
    /// </summary>
    /// <returns>RGBA bytes, top row first, and the size.</returns>
    public async Task<(byte[] Rgba, int Width, int Height)> CaptureAsync()
    {
        if (_gl is null || _floorProgram == 0)
        {
            throw new InvalidOperationException("The 3D view is not running (there is no OpenGL view to take a picture of).");
        }

        var wanted = new TaskCompletionSource<(byte[], int, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _shot, wanted, null) is not null)
        {
            throw new InvalidOperationException("A screenshot is already being taken.");
        }

        RequestNextFrameRendering();
        try
        {
            return await wanted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            Interlocked.CompareExchange(ref _shot, null, wanted);
            throw new InvalidOperationException("The 3D view drew no frame in 5 seconds (is the window hidden or minimised?).");
        }
    }

    // Reads the framebuffer just drawn, if a picture was asked for.
    private unsafe void TakeShot(GL g, int w, int h)
    {
        TaskCompletionSource<(byte[], int, int)>? wanted = Interlocked.Exchange(ref _shot, null);
        if (wanted is null)
        {
            return;
        }

        try
        {
            byte[] raw = new byte[w * h * 4];
            g.PixelStore(PixelStoreParameter.PackAlignment, 1);
            fixed (byte* at = raw)
            {
                g.ReadPixels(0, 0, (uint)w, (uint)h, PixelFormat.Rgba, PixelType.UnsignedByte, at);
            }

            // OpenGL gives the bottom row first.
            byte[] top = new byte[raw.Length];
            int row = w * 4;
            for (int y = 0; y < h; y++)
            {
                System.Buffer.BlockCopy(raw, (h - 1 - y) * row, top, y * row, row);
                for (int x = 3; x < row; x += 4)
                {
                    top[(y * row) + x] = 255;
                }
            }

            wanted.TrySetResult((top, w, h));
        }
        catch (Exception e)
        {
            wanted.TrySetException(e);
        }
    }

    /// <summary>
    /// How far the camera must be for the whole floor (x from -S/2 to 4.5 S, y from -S/2 to 2.5 S) to fit the picture,
    /// with a small margin, seen from the camera's current angle.
    /// </summary>
    /// <param name="aspect">The picture's width over its height.</param>
    /// <returns>The distance from the floor's centre.</returns>
    private float FrameDistance(float aspect)
    {
        const float s = TileMesh.S;
        Vector3 back = Orbit.Back;
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, back));
        var up = Vector3.Cross(back, right);
        float tanY = MathF.Tan(OrbitCamera.FieldOfView / 2), tanX = tanY * aspect;
        float need = OrbitCamera.MinDistance;
        foreach (float x in (float[])[-s / 2, 4.5f * s])
        {
            foreach (float y in (float[])[-s / 2, 2.5f * s])
            {
                foreach (float z in (float[])[0, TileMesh.T])
                {
                    Vector3 o = new Vector3(x, y, z) - FloorTarget;
                    float toward = Vector3.Dot(o, back);
                    need = MathF.Max(need, (MathF.Abs(Vector3.Dot(o, right)) / tanX) + toward);
                    need = MathF.Max(need, (MathF.Abs(Vector3.Dot(o, up)) / tanY) + toward);
                }
            }
        }

        return need * 1.06f;
    }

    /// <summary>
    /// Every tile of the floor: whether it is the triangle, and where it goes. Squares at (i S, j S), turned 180
    /// degrees where i + j is odd; triangle pairs at (3 S, j S) and (4 S, j S), one at 0 and one at 180 degrees.
    /// </summary>
    /// <returns>The tiles.</returns>
    internal static IEnumerable<(bool Triangle, Matrix4x4 Model)> FloorLayout()
    {
        const float s = TileMesh.S;
        static Matrix4x4 Place(float x, float y, bool turned) =>
            (turned ? Matrix4x4.CreateRotationZ(MathF.PI) : Matrix4x4.Identity) * Matrix4x4.CreateTranslation(x, y, 0);

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                yield return (false, Place(i * s, j * s, (i + j) % 2 == 1));
            }
        }

        for (int i = 3; i < 5; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                yield return (true, Place(i * s, j * s, false));
                yield return (true, Place(i * s, j * s, true));
            }
        }
    }

    /// <summary>Compiles and links a program from its two shaders.</summary>
    /// <param name="g">The OpenGL context.</param>
    /// <param name="vs">The vertex shader's source.</param>
    /// <param name="fs">The fragment shader's source.</param>
    /// <returns>The program.</returns>
    /// <exception cref="InvalidOperationException">A shader did not compile, or the program did not link (the log says why).</exception>
    internal static uint Link(GL g, string vs, string fs)
    {
        uint Compile(ShaderType t, string src)
        {
            uint sh = g.CreateShader(t);
            g.ShaderSource(sh, src);
            g.CompileShader(sh);
            g.GetShader(sh, ShaderParameterName.CompileStatus, out int ok);
            if (ok == 0)
            {
                string log = g.GetShaderInfoLog(sh);
                g.DeleteShader(sh);
                throw new InvalidOperationException($"{t}: {log}");
            }

            return sh;
        }

        uint v = Compile(ShaderType.VertexShader, vs);
        uint f;
        try
        {
            f = Compile(ShaderType.FragmentShader, fs);
        }
        catch (InvalidOperationException)
        {
            g.DeleteShader(v);
            throw;
        }

        uint p = g.CreateProgram();
        g.AttachShader(p, v);
        g.AttachShader(p, f);
        g.LinkProgram(p);
        g.DetachShader(p, v);
        g.DetachShader(p, f);
        g.DeleteShader(v);
        g.DeleteShader(f);
        g.GetProgram(p, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0)
        {
            string log = g.GetProgramInfoLog(p);
            g.DeleteProgram(p);
            throw new InvalidOperationException(log);
        }

        return p;
    }

    private void DeletePrograms(GL g)
    {
        if (_composeProgram != 0)
        {
            g.DeleteProgram(_composeProgram);
        }

        if (_floorProgram != 0)
        {
            g.DeleteProgram(_floorProgram);
        }

        if (_pieceProgram != 0)
        {
            g.DeleteProgram(_pieceProgram);
        }

        _pieceProgram = 0;
        if (_rockProgram != 0)
        {
            g.DeleteProgram(_rockProgram);
        }

        _rockProgram = 0;

        _composeProgram = 0;
        _floorProgram = 0;
    }

    // The layers turned into colour, 1:1 into the colour texture, then its mipmaps.
    private void Compose(GL g)
    {
        void V3(string name, Rgb c) => g.Uniform3(g.GetUniformLocation(_composeProgram, name), c.R, c.G, c.B);

        g.BindFramebuffer(FramebufferTarget.Framebuffer, _colourFbo);
        g.Viewport(0, 0, (uint)_textureSize, (uint)_textureSize);
        g.Disable(EnableCap.DepthTest);
        g.Disable(EnableCap.Blend);
        g.Disable(EnableCap.CullFace);
        g.UseProgram(_composeProgram);
        Palette p = _palette!;
        V3("uBase", p.Base);
        V3("uLitTint", p.LitTint);
        V3("uShadeTint", p.ShadeTint);
        V3("uRedder", p.Redder);
        V3("uGreyer", p.Greyer);
        V3("uEndMul", p.EndMul);
        V3("uSeamDeep", p.SeamDeep);
        V3("uSeamLight", p.SeamLight);
        g.Uniform1(g.GetUniformLocation(_composeProgram, "uContrast"), Contrast);
        g.Uniform1(g.GetUniformLocation(_composeProgram, "uDriftAmount"), _drift);
        g.Uniform1(g.GetUniformLocation(_composeProgram, "uSize"), (float)_textureSize);
        for (int i = 0; i < Layers.Count; i++)
        {
            g.ActiveTexture(TextureUnit.Texture0 + i);
            g.BindTexture(TextureTarget.Texture2D, _textures[i]);
            g.Uniform1(g.GetUniformLocation(_composeProgram, SamplerNames[i]), i);
        }

        g.BindVertexArray(_emptyVao);
        g.DrawArrays(PrimitiveType.Triangles, 0, 3);
        g.BindVertexArray(0);
        DumpOnce(g);
        for (int i = Layers.Count - 1; i >= 0; i--)
        {
            g.ActiveTexture(TextureUnit.Texture0 + i);
            g.BindTexture(TextureTarget.Texture2D, 0);
        }

        g.BindTexture(TextureTarget.Texture2D, _colourTexture);
        g.GenerateMipmap(TextureTarget.Texture2D);
        g.BindTexture(TextureTarget.Texture2D, 0);
    }

    private unsafe void DumpOnce(GL g)
    {
        if (DumpComposedTo is null || _dumped || _textureSize != 1024)
        {
            return;
        }

        _dumped = true;
        byte[] pixels = new byte[_textureSize * _textureSize * 4];
        g.PixelStore(PixelStoreParameter.PackAlignment, 4);
        fixed (byte* at = pixels)
        {
            g.ReadPixels(0, 0, (uint)_textureSize, (uint)_textureSize, PixelFormat.Rgba, PixelType.UnsignedByte, at);
        }

        File.WriteAllBytes(DumpComposedTo, pixels);
    }

    private void SetFloorUniforms(GL g)
    {
        bool has = _colourTexture != 0 && !_composeStale;
        g.Uniform1(g.GetUniformLocation(_floorProgram, "uHasColour"), has ? 1 : 0);
        if (!has)
        {
            return;
        }

        // The colour texture has the layers' own texel grid; a preview's (N < 1024) sits elsewhere than the full
        // paint's, so it is moved to line up: 0.5/N - 0.5/1024 on both coordinates.
        float shift = _textureSize == 1024 ? 0f : (0.5f / _textureSize) - (0.5f / 1024f);
        g.Uniform2(g.GetUniformLocation(_floorProgram, "uUvShift"), shift, shift);
        g.ActiveTexture(TextureUnit.Texture0);
        g.BindTexture(TextureTarget.Texture2D, _colourTexture);
        g.Uniform1(g.GetUniformLocation(_floorProgram, "uColour"), 0);
    }

    // One single-channel half-float texture per layer. The region and the seam blend are read without smoothing
    // (a region is a whole number); the others are smoothed between texels.
    private void Upload(GL g, Layers l)
    {
        DeleteTextures(g);
        float[][] data = [l.Marks, l.Rest, l.Drift, l.Region, l.BaseMul, l.Seam, l.Aa];
        g.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        for (int i = 0; i < Layers.Count; i++)
        {
            uint t = g.GenTexture();
            g.BindTexture(TextureTarget.Texture2D, t);
            bool exact = i is 3 or 6;
            int filter = (int)(exact ? TextureMinFilter.Nearest : TextureMinFilter.Linear);
            g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, filter);
            g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, filter);
            g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            g.TexImage2D<float>(TextureTarget.Texture2D, 0, InternalFormat.R16f, (uint)l.Size, (uint)l.Size, 0, PixelFormat.Red, PixelType.Float, data[i]);
            _textures[i] = t;
        }

        // The colour texture the layers are composed into: 8 bits per channel as the export, with a full mipmap chain.
        _colourTexture = g.GenTexture();
        g.BindTexture(TextureTarget.Texture2D, _colourTexture);
        g.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)l.Size, (uint)l.Size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ReadOnlySpan<byte>.Empty);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        g.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        if (_anisotropy > 1)
        {
            g.TexParameter(TextureTarget.Texture2D, MaxAnisotropy, _anisotropy);
        }

        g.BindTexture(TextureTarget.Texture2D, 0);
        _colourFbo = g.GenFramebuffer();
        g.BindFramebuffer(FramebufferTarget.Framebuffer, _colourFbo);
        g.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _colourTexture, 0);
        GLEnum status = g.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            string message = $"The 3D view cannot colour the floor: its picture buffer is not usable ({status}).";
            Dispatcher.UIThread.Post(() => Failed?.Invoke(message));
        }

        _textureSize = l.Size;
    }

    private void DeleteTextures(GL g)
    {
        for (int i = 0; i < _textures.Length; i++)
        {
            if (_textures[i] != 0)
            {
                g.DeleteTexture(_textures[i]);
                _textures[i] = 0;
            }
        }

        if (_colourFbo != 0)
        {
            g.DeleteFramebuffer(_colourFbo);
            _colourFbo = 0;
        }

        if (_colourTexture != 0)
        {
            g.DeleteTexture(_colourTexture);
            _colourTexture = 0;
        }

        _textureSize = 0;
    }
}
