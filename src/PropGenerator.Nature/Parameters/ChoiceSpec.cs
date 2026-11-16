// Ported from easy-props frontend/src/assets/shared/parameters.ts (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Parameters;

/// <summary>
/// A choice that falls back to the default when the value is not one of the offered ones (easy-props'
/// <c>choiceSchema</c>). Anything that is not a string counts as unknown.
/// </summary>
public sealed class ChoiceSpec : ParameterSpec
{
    private readonly HashSet<string> allowed;

    /// <summary>A choice control.</summary>
    public ChoiceSpec(
        string key,
        string label,
        string? help,
        string? group,
        IReadOnlyList<ChoiceOption> options,
        string defaultValue)
        : base(key, label, help, group)
    {
        Options = options;
        DefaultChoice = defaultValue;
        allowed = new HashSet<string>(options.Select(option => option.Value), StringComparer.Ordinal);
    }

    /// <summary>The offered values, in the order the inspector lists them.</summary>
    public IReadOnlyList<ChoiceOption> Options { get; }

    /// <summary>The default as a string.</summary>
    public string DefaultChoice { get; }

    /// <inheritdoc />
    public override object DefaultValue => DefaultChoice;

    /// <summary>The raw value if it is an offered one, else the default.</summary>
    public string Clamp(object? raw) => raw is string text && allowed.Contains(text) ? text : DefaultChoice;

    /// <inheritdoc />
    public override object Coerce(object? raw) => Clamp(raw);
}
