// Adapted from easy-props frontend/src/viewport/scene/live-wind.ts and Viewport.tsx (MIT, © 2026 AHS12).
using System.Numerics;
using PropGenerator.Desktop.Recipes;

namespace PropGenerator.Desktop.View;

/// <summary>
/// Wind in the view: the prop moves as the engine kits' materials will move it. Its strength eases in and out, and
/// each point is weighed by its height over the prop's height and how far out it is against the prop's reach.
/// </summary>
internal sealed class LiveWind
{
    /// <summary>How fast the strength eases towards what is wanted, per second (easy-props' WIND_EASING).</summary>
    private const double Easing = 3;

    /// <summary>Below this the prop is held still.</summary>
    private const double Calm = 0.005;

    /// <summary>Further than any prop, so being far out adds nothing (easy-props' EVEN_REACH).</summary>
    private const float EvenReach = 1e6f;

    private double _eased;
    private Vector3 _lo;
    private Vector3 _hi = Vector3.One;

    /// <summary>Gets or sets a value indicating whether wind is switched on in the view.</summary>
    internal bool On { get; set; }

    /// <summary>Gets or sets the chosen strength: 1 is the engine kits' own, from 0.3 (calm) to 2 (a storm).</summary>
    internal double Wanted { get; set; } = 1;

    /// <summary>Gets or sets how the prop on show moves, or null when it stands still (a rock, the floor).</summary>
    internal WindSway? Sway
    {
        get;
        set
        {
            field = value;
            if (value is null)
            {
                // A still prop shown after a swaying one must not bend while the wind eases out.
                _eased = 0;
            }

            Measure(_lo, _hi);
        }
    }

    /// <summary>Gets or sets a value indicating whether a drag is under way: the wind calms, so a handle stays under the pointer.</summary>
    internal bool Calmed { get; set; }

    /// <summary>Gets the strength now, after easing; 0 holds the prop still.</summary>
    internal double Strength => _eased < Calm ? 0 : _eased;

    /// <summary>Gets a value indicating whether the prop is moving or easing, so the view must keep drawing frames.</summary>
    internal bool Moving => Strength > 0 || Target > 0;

    /// <summary>Gets the prop's height over its pivot (the origin).</summary>
    internal float Height { get; private set; } = 1;

    /// <summary>Gets how far out the prop reaches from its pivot, or a reach beyond any prop when it sways evenly.</summary>
    internal float Reach { get; private set; } = 1;

    /// <summary>Gets the seconds the wind has blown, for its waves.</summary>
    internal double Time { get; private set; }

    private double Target => On && Sway is not null && !Calmed ? Wanted : 0;

    /// <summary>Eases the strength towards what is wanted over <paramref name="seconds"/> (at most a tenth of a second counts).</summary>
    /// <param name="seconds">The time since the last frame.</param>
    internal void Step(double seconds)
    {
        double elapsed = Math.Clamp(seconds, 0, 0.1);
        _eased += (Target - _eased) * Math.Min(elapsed * Easing, 1);
        if (_eased < Calm && Target == 0)
        {
            _eased = 0;
        }

        Time += seconds;
    }

    /// <summary>Sizes the wind to the prop on show from its bounds (Z-up, pivot at the origin).</summary>
    /// <param name="lo">The lowest corner.</param>
    /// <param name="hi">The highest corner.</param>
    internal void Measure(Vector3 lo, Vector3 hi)
    {
        (_lo, _hi) = (lo, hi);
        Height = Math.Max(hi.Z, 1e-3f);
        Reach = Sway?.Evenly == true
            ? EvenReach
            : Math.Max(Math.Max(MathF.Sqrt((hi.X * hi.X) + (hi.Y * hi.Y)), MathF.Sqrt((lo.X * lo.X) + (lo.Y * lo.Y))), 1e-3f);
    }
}
