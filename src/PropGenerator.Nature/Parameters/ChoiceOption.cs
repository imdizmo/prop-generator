// Ported from easy-props frontend/src/assets/shared/parameters.ts (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Parameters;

/// <summary>One offered value of a <see cref="ChoiceSpec"/> and its name in the inspector.</summary>
public sealed record ChoiceOption(string Value, string Label);
