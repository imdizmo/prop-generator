namespace PropGenerator.Desktop.View;

/// <summary>
/// Where the history strip's boxes lie (the prototype's .hs boxes: up to 11 wide, 2 apart, shrinking to 3): every step
/// gets a box while they fit; past that the boxes stay 3 wide and a window of them shows around the current step, the
/// way the prototype's strip scrolls. A position across the strip maps back to the step under it.
/// </summary>
/// <param name="Count">How many steps the history has.</param>
/// <param name="First">The first step shown.</param>
/// <param name="Visible">How many steps are shown, from <paramref name="First"/>.</param>
/// <param name="Box">Each box's width.</param>
internal readonly record struct HistoryLayout(int Count, int First, int Visible, double Box)
{
    /// <summary>The widest a box gets.</summary>
    internal const double MaxBox = 11;

    /// <summary>The narrowest a box gets; past that the strip scrolls.</summary>
    internal const double MinBox = 3;

    /// <summary>The space between two boxes.</summary>
    internal const double Gap = 2;

    /// <summary>Lays the boxes out.</summary>
    /// <param name="width">The room for the boxes.</param>
    /// <param name="count">How many steps.</param>
    /// <param name="at">The current step, kept in sight.</param>
    /// <param name="first">The first step to show, when the window is held (a drag is going on); null places it.</param>
    /// <returns>The layout.</returns>
    internal static HistoryLayout Of(double width, int count, int at, int? first = null)
    {
        if (count <= 0 || width <= 0)
        {
            return new HistoryLayout(Math.Max(0, count), 0, 0, MaxBox);
        }

        double box = Math.Clamp(((width + Gap) / count) - Gap, MinBox, MaxBox);
        int visible = Math.Clamp((int)Math.Floor((width + Gap) / (box + Gap)), 1, count);
        int last = count - visible;
        int start = first ?? (at - (visible / 2));
        return new HistoryLayout(count, Math.Clamp(start, 0, last), visible, box);
    }

    /// <summary>Gets where step <paramref name="index"/>'s box starts, from the left of the boxes.</summary>
    /// <param name="index">The step (one of those shown).</param>
    /// <returns>Its left edge.</returns>
    internal double X(int index) => (index - First) * (Box + Gap);

    /// <summary>Gets the step under a position across the boxes; past either end, the step shown at that end.</summary>
    /// <param name="x">The position, from the left of the boxes.</param>
    /// <returns>The step, or -1 when none is shown.</returns>
    internal int IndexAt(double x)
    {
        if (Visible == 0)
        {
            return -1;
        }

        int i = (int)Math.Floor(x / (Box + Gap));
        return First + Math.Clamp(i, 0, Visible - 1);
    }
}
