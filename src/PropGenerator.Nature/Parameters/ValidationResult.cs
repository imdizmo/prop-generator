// Ported from easy-props frontend/src/assets/shared/parameters.ts (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Parameters;

/// <summary>
/// The outcome of validating a parameter object. <see cref="Valid"/> is true when nothing had to change.
/// <see cref="Parameters"/> is always usable: the legal values, or the defaults when the input was not an object.
/// </summary>
public sealed record ValidationResult(bool Valid, ParameterDictionary Parameters, IReadOnlyList<ValidationIssue> Issues);
