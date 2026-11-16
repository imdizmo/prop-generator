namespace PropGenerator;

/// <summary>One saved state in the history.</summary>
/// <param name="Label">What the change was, for the history list.</param>
/// <param name="Settings">The settings after the change.</param>
public sealed record HistoryStep(string Label, Settings Settings);
