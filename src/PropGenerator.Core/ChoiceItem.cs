namespace PropGenerator;

/// <summary>One option of a "choice" setting.</summary>
/// <param name="Value">The text stored in the settings and sent to the painter.</param>
/// <param name="Label">The name shown to the user in the drop-down.</param>
public sealed record ChoiceItem(string Value, string Label);
