// Adapted from easy-props (MIT, © 2026 AHS12).
namespace PropGenerator.Desktop.Commands;

/// <summary>
/// How the window shows the piece, apart from the piece itself: the shading, the detail level, the scene and its light,
/// the overlays (stats, a person for scale, what you can walk into), the layout (maximized, panels hidden, the drawer
/// open and its side) and the face budget. The menus, the shortcuts and the view toolbar change it; the 3D view, the status bar and
/// the layout follow <see cref="Changed"/>. The scene, the light, the budget and the drawer are remembered in the
/// prefs; the rest starts afresh with each window. Used on the UI thread only.
/// </summary>
internal sealed class ViewState
{
    /// <summary>The finished look, with light and shadow.</summary>
    internal const string Lit = "lit";

    /// <summary>One flat colour per part.</summary>
    internal const string Flat = "flat";

    /// <summary>The triangles the prop is made of.</summary>
    internal const string Wireframe = "wireframe";

    /// <summary>How long the light must stay still before it is written to the prefs (a sun dragged writes once).</summary>
    private static readonly TimeSpan LightingSettle = TimeSpan.FromMilliseconds(300);

    private readonly Prefs _prefs;
    private readonly Debounce _saveLighting;
    private readonly Debounce _saveGround;
    private string _scene;
    private Lighting _lighting;
    private bool _drawerOpen;
    private bool _drawerLeft;
    private string _budget;

    /// <summary>Starts from what the prefs remember.</summary>
    /// <param name="prefs">Where the scene, the light, the budget and the drawer are kept.</param>
    internal ViewState(Prefs prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        _prefs = prefs;
        _scene = Scenes.Contains(prefs.Scene) ? prefs.Scene : DefaultScene;
        _budget = Budgets.Contains(prefs.Budget) ? prefs.Budget : DefaultBudget;
        _lighting = prefs.Lighting.IsValid ? prefs.Lighting : Lighting.Default;
        _drawerOpen = prefs.DrawerOpen;
        _drawerLeft = prefs.DrawerLeft;
        _saveLighting = new Debounce(LightingSettle, _prefs.Save);
        _saveGround = new Debounce(LightingSettle, _prefs.Save);
    }

    /// <summary>Raised after any part changed (a value set to what it already was raises nothing).</summary>
    internal event Action? Changed;

    /// <summary>Gets the live shading modes, in the order Z steps through them.</summary>
    internal static IReadOnlyList<string> Shadings { get; } = [Lit, Flat, Wireframe];

    /// <summary>Gets the scenes that can be shown: Studio, Studio with sky and Neutral grey.</summary>
    internal static IReadOnlyList<string> Scenes { get; } = ["studio", "studio-sky", "grey"];

    /// <summary>Gets the face budgets: Hero, Standard, Background and Mobile.</summary>
    internal static IReadOnlyList<string> Budgets { get; } = ["hero", "standard", "background", "mobile"];

    /// <summary>The scene until another is chosen.</summary>
    internal const string DefaultScene = "studio-sky";

    /// <summary>The budget until another is chosen.</summary>
    internal const string DefaultBudget = "standard";

    /// <summary>Gets or sets how the piece is drawn: <see cref="Lit"/>, <see cref="Flat"/> or <see cref="Wireframe"/>.</summary>
    /// <exception cref="ArgumentException">Another name.</exception>
    internal string Shading
    {
        get;
        set => Set(ref field, OneOf(value, Shadings, nameof(Shading)));
    }

    = Lit;

    /// <summary>The Far away level: the prop as its far-away card, for a kind that offers one (easy-props' FAR_LEVEL).</summary>
    internal const int FarLevel = 3;

    /// <summary>Gets or sets the detail level shown: 0 (full detail), 1, 2, or <see cref="FarLevel"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Below 0 or above <see cref="FarLevel"/>.</exception>
    internal int Lod
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, FarLevel);
            Set(ref field, value);
        }
    }

    /// <summary>Gets or sets the scene around the piece (one of <see cref="Scenes"/>); remembered.</summary>
    /// <exception cref="ArgumentException">Another name.</exception>
    internal string Scene
    {
        get => _scene;
        set
        {
            if (Set(ref _scene, OneOf(value, Scenes, nameof(Scene))))
            {
                _prefs.Scene = value;
                _prefs.Save();
            }
        }
    }

    /// <summary>Gets or sets the sun and the shadows; remembered once it stays still for a moment.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A part is outside its range.</exception>
    internal Lighting Lighting
    {
        get => _lighting;
        set
        {
            if (!value.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The sun's direction, height, warmth or shadow is outside its range.");
            }

            if (Set(ref _lighting, value))
            {
                _prefs.Lighting = value;
                _saveLighting.Restart();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether the stats card shows.</summary>
    internal bool ShowStats
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>Gets or sets a value indicating whether the view moves the prop in the wind, as the engine kits will; remembered.</summary>
    internal bool Wind
    {
        get => _prefs.Wind;
        set
        {
            if (_prefs.Wind != value)
            {
                _prefs.Wind = value;
                _prefs.Save();
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Gets or sets the wind's strength in the view (1 is the engine kits' own, 0.3 calm, 2 a storm); remembered.</summary>
    internal double WindStrength
    {
        get => _prefs.WindStrength;
        set
        {
            double strength = Math.Clamp(value, Prefs.MinWindStrength, Prefs.MaxWindStrength);
            if (_prefs.WindStrength != strength)
            {
                _prefs.WindStrength = strength;
                _prefs.Save();
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether a person stands beside the piece for scale.</summary>
    internal bool ShowPerson
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>Gets or sets a value indicating whether the shapes you can walk into show.</summary>
    internal bool ShowCollision
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>Gets or sets a value indicating whether the view fills the window (no panel, drawer or status bar).</summary>
    internal bool Maximized
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>Gets or sets a value indicating whether the settings panel is hidden.</summary>
    internal bool PanelsHidden
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>Gets or sets a value indicating whether the objects drawer is open (the one place this is kept); remembered.</summary>
    internal bool DrawerOpen
    {
        get => _drawerOpen;
        set
        {
            if (Set(ref _drawerOpen, value))
            {
                _prefs.DrawerOpen = value;
                _prefs.Save();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether the objects drawer is docked on the left of the view (else at its bottom); remembered.</summary>
    internal bool DrawerLeft
    {
        get => _drawerLeft;
        set
        {
            if (Set(ref _drawerLeft, value))
            {
                _prefs.DrawerLeft = value;
                _prefs.Save();
            }
        }
    }

    /// <summary>Gets or sets what the piece is for, which sets its face budget (one of <see cref="Budgets"/>); remembered.</summary>
    /// <exception cref="ArgumentException">Another name.</exception>
    internal string Budget
    {
        get => _budget;
        set
        {
            if (Set(ref _budget, OneOf(value, Budgets, nameof(Budget))))
            {
                _prefs.Budget = value;
                _prefs.Save();
            }
        }
    }

    /// <summary>The level ground's slope, in degrees.</summary>
    internal const double MinSlope = 0;

    /// <summary>The steepest slope the view offers, in degrees (easy-props' 35).</summary>
    internal const double MaxSlope = 35;

    /// <summary>The slope's step, in degrees.</summary>
    internal const double SlopeStep = 1;

    /// <summary>The last way round the ground can fall, in degrees (easy-props' 355).</summary>
    internal const double MaxDownhill = 355;

    /// <summary>The downhill direction's step, in degrees.</summary>
    internal const double DownhillStep = 5;

    /// <summary>
    /// Gets or sets how steep the view's ground is, in degrees from 0 (level) to 35 in whole degrees (kept to them);
    /// remembered once it stays still for a moment. Only the view: never in the scene, the work file or an export.
    /// </summary>
    internal double GroundSlope
    {
        get => _prefs.GroundSlope;
        set
        {
            double slope = KeptSlope(value);
            if (_prefs.GroundSlope != slope)
            {
                _prefs.GroundSlope = slope;
                _saveGround.Restart();
                Changed?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets which way the sloping ground falls, in degrees around the prop from 0 (towards the front) to 355 in
    /// fives (kept to them); remembered once it stays still for a moment.
    /// </summary>
    internal double GroundDownhill
    {
        get => _prefs.GroundDownhill;
        set
        {
            double downhill = KeptDownhill(value);
            if (_prefs.GroundDownhill != downhill)
            {
                _prefs.GroundDownhill = downhill;
                _saveGround.Restart();
                Changed?.Invoke();
            }
        }
    }

    /// <summary>A slope kept to 0..35 in whole degrees; anything not a number is level.</summary>
    /// <param name="value">The slope asked for.</param>
    /// <returns>The slope kept.</returns>
    internal static double KeptSlope(double value) =>
        double.IsFinite(value) ? Math.Clamp(Math.Round(value / SlopeStep) * SlopeStep, MinSlope, MaxSlope) : MinSlope;

    /// <summary>A downhill direction kept to 0..355 in fives; anything not a number is 0.</summary>
    /// <param name="value">The direction asked for.</param>
    /// <returns>The direction kept.</returns>
    internal static double KeptDownhill(double value) =>
        double.IsFinite(value) ? Math.Clamp(Math.Round(value / DownhillStep) * DownhillStep, 0, MaxDownhill) : 0;

    /// <summary>Writes the light and the ground to the prefs now if they are still waiting to settle (the window is closing).</summary>
    internal void Flush()
    {
        _saveLighting.Flush();
        _saveGround.Flush();
    }

    private static string OneOf(string value, IReadOnlyList<string> names, string what) =>
        names.Contains(value) ? value : throw new ArgumentException($"'{value}' is not a {what}: use one of {string.Join(", ", names)}.", nameof(value));

    private bool Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Changed?.Invoke();
        return true;
    }
}
