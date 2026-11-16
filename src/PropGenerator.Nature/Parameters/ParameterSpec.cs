// Ported from easy-props frontend/src/assets/shared/parameters.ts (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Parameters;

/// <summary>
/// One editable value of a generator: its key, its text for the inspector, and how a raw value is turned into a legal
/// one. A spec never rejects a value. A bad one becomes the default or is pulled into range, and the caller reports it.
/// </summary>
public abstract class ParameterSpec
{
    /// <summary>A spec with its inspector text. <paramref name="label"/> is empty for a value the inspector does not show.</summary>
    protected ParameterSpec(string key, string label, string? help, string? group)
    {
        Key = key;
        Label = label;
        Help = help;
        Group = group;
    }

    /// <summary>The name of the value in a parameter object.</summary>
    public string Key { get; }

    /// <summary>The control's name in the inspector.</summary>
    public string Label { get; }

    /// <summary>A sentence saying what the control does, or null.</summary>
    public string? Help { get; }

    /// <summary>The heading it sits under in the inspector, or null.</summary>
    public string? Group { get; }

    /// <summary>The value used when the raw value is missing or wrong: a double or a string.</summary>
    public abstract object DefaultValue { get; }

    /// <summary>The legal value for a raw one: a double or a string. Never throws.</summary>
    public abstract object Coerce(object? raw);
}
