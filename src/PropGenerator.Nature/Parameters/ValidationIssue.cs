// Ported from easy-props frontend/src/assets/shared/parameters.ts (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Parameters;

/// <summary>One thing validation changed: the parameter's key and a sentence saying what happened.</summary>
public sealed record ValidationIssue(string Key, string Message);
