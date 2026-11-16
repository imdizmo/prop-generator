namespace PropGenerator;

/// <summary>
/// The undo history. Each step holds the settings after a change; <see cref="Index"/> is the step that is current.
/// Callers push once per finished change (a slider drag pushes on release, not on every frame).
/// </summary>
public sealed class History
{
    private readonly List<HistoryStep> _steps = [];
    private readonly int _cap;

    /// <summary>Starts an empty history.</summary>
    /// <param name="cap">The most steps kept; the oldest go first.</param>
    public History(int cap = 200)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cap, 1);
        _cap = cap;
        Index = -1;
    }

    /// <summary>Every step, oldest first.</summary>
    public IReadOnlyList<HistoryStep> Steps => _steps;

    /// <summary>The current step, or -1 when there are none.</summary>
    public int Index { get; private set; }

    /// <summary>Adds a step after the current one. Steps that were undone are dropped.</summary>
    /// <param name="label">What changed.</param>
    /// <param name="s">The settings after the change.</param>
    public void Push(string label, Settings s)
    {
        _steps.RemoveRange(Index + 1, _steps.Count - Index - 1);
        _steps.Add(new HistoryStep(label, s));
        if (_steps.Count > _cap)
        {
            _steps.RemoveRange(0, _steps.Count - _cap);
        }

        Index = _steps.Count - 1;
    }

    /// <summary>Goes back one step.</summary>
    /// <returns>The settings there, or null when already at the oldest step.</returns>
    public Settings? Undo()
    {
        if (Index <= 0)
        {
            return null;
        }

        Index--;
        return _steps[Index].Settings;
    }

    /// <summary>Goes forward one step.</summary>
    /// <returns>The settings there, or null when already at the newest step.</returns>
    public Settings? Redo()
    {
        if (Index >= _steps.Count - 1)
        {
            return null;
        }

        Index++;
        return _steps[Index].Settings;
    }

    /// <summary>
    /// Goes straight to a step, as that many undos or redos would, but with one answer: the history strip's click or
    /// drag shows the settings there once, not once per step passed. No step is added or dropped.
    /// </summary>
    /// <param name="index">The step to go to.</param>
    /// <returns>The settings there, or null when it is already the current step.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no such step.</exception>
    public Settings? Go(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _steps.Count);
        if (index == Index)
        {
            return null;
        }

        Index = index;
        return _steps[index].Settings;
    }

    /// <summary>Rebuilds a history read from a file.</summary>
    /// <param name="steps">The steps, oldest first.</param>
    /// <param name="index">The current step.</param>
    /// <param name="cap">The most steps kept.</param>
    /// <returns>The history.</returns>
    internal static History Restore(IEnumerable<HistoryStep> steps, int index, int cap = 200)
    {
        var h = new History(cap);
        h._steps.AddRange(steps);
        if (h._steps.Count > cap)
        {
            int drop = h._steps.Count - cap;
            h._steps.RemoveRange(0, drop);
            index -= drop;
        }

        if (index < -1 || index >= h._steps.Count || (index == -1 && h._steps.Count > 0))
        {
            throw new WorkFileException($"The history position {index} is outside its {h._steps.Count} steps.");
        }

        h.Index = index;
        return h;
    }
}
