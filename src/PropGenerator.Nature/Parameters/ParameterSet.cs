// Ported from easy-props frontend/src/assets/shared/parameters.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Js;

namespace PropGenerator.Nature.Parameters;

/// <summary>
/// The parameters of one generator: the specs of its schema in schema order (the order of the validated values), and
/// the keys of the controls in the order the inspector shows them. <see cref="Validate(object?)"/> is easy-props'
/// <c>validateParameters</c>.
/// </summary>
public sealed class ParameterSet
{
    private readonly Dictionary<string, ParameterSpec> byKey;
    private readonly Func<IReadOnlyDictionary<string, object>, IReadOnlyCollection<string>>? hidden;

    /// <summary>A set. <paramref name="controlKeys"/> names the schema entries the inspector shows, in its order.</summary>
    /// <param name="generatorId">The generator's id.</param>
    /// <param name="schema">The specs in schema order.</param>
    /// <param name="controlKeys">The controls' keys, in the inspector's order.</param>
    /// <param name="lowPoly">Whether the prop can also be made low-poly, through a <c>build</c> choice.</param>
    /// <param name="hidden">The controls these values have no use for (easy-props' <c>shownControls</c>); null hides none.</param>
    public ParameterSet(
        string generatorId,
        IReadOnlyList<ParameterSpec> schema,
        IReadOnlyList<string> controlKeys,
        bool lowPoly = false,
        Func<IReadOnlyDictionary<string, object>, IReadOnlyCollection<string>>? hidden = null)
    {
        GeneratorId = generatorId;
        LowPoly = lowPoly;
        this.hidden = hidden;
        Schema = schema;
        byKey = schema.ToDictionary(spec => spec.Key, StringComparer.Ordinal);
        Controls = [.. controlKeys.Select(key => byKey[key])];
        var defaults = new ParameterDictionary();
        foreach (var spec in schema)
        {
            defaults.Set(spec.Key, spec.DefaultValue);
        }

        Defaults = defaults;
    }

    /// <summary>The generator's id.</summary>
    public string GeneratorId { get; }

    /// <summary>Gets a value indicating whether the prop can also be made low-poly, through a <c>build</c> choice.</summary>
    public bool LowPoly { get; }

    /// <summary>The controls that do something with these values, in the inspector's order (easy-props' <c>shownControls</c>).</summary>
    /// <param name="values">The prop's parameters.</param>
    public IReadOnlyList<ParameterSpec> ShownControls(IReadOnlyDictionary<string, object> values)
    {
        if (hidden is null)
        {
            return Controls;
        }

        var hide = hidden(values);
        return [.. Controls.Where(spec => !hide.Contains(spec.Key))];
    }

    /// <summary>Every validated value's spec, in the order the validated values come out.</summary>
    public IReadOnlyList<ParameterSpec> Schema { get; }

    /// <summary>The specs the inspector shows, in its order.</summary>
    public IReadOnlyList<ParameterSpec> Controls { get; }

    /// <summary>The default value of every parameter, in schema order.</summary>
    public ParameterDictionary Defaults { get; }

    /// <summary>The spec for a key, or null.</summary>
    public ParameterSpec? Find(string key) => byKey.GetValueOrDefault(key);

    /// <summary>Validates a parameter object. Never throws.</summary>
    public ValidationResult Validate(ParameterDictionary raw) => Validate((object?)raw);

    /// <summary>Validates a parameter object. Never throws.</summary>
    public ValidationResult Validate(IReadOnlyDictionary<string, object?> raw) => Validate((object?)raw);

    /// <summary>
    /// Validates anything. A raw value that is not an object (null, <see cref="JsUndefined"/>, a string, a number, an
    /// array) gives the defaults and the same issue zod gives. Otherwise out-of-range values are clamped, wrong ones
    /// reset, unknown keys dropped, and each change reported.
    /// </summary>
    public ValidationResult Validate(object? raw)
    {
        if (raw is not IReadOnlyDictionary<string, object?> dictionary)
        {
            var issue = new ValidationIssue("parameters", $"Invalid input: expected object, received {JsValue.ZodTypeName(raw)}");
            return new ValidationResult(false, Defaults, [issue]);
        }

        var values = new ParameterDictionary();
        var issues = new List<ValidationIssue>();
        foreach (var spec in Schema)
        {
            object? given = dictionary.TryGetValue(spec.Key, out object? found) ? found : JsUndefined.Value;
            object value = spec.Coerce(given);
            values.Set(spec.Key, value);

            // A missing value simply takes the default; a value that came through unchanged needs no report.
            if (given is JsUndefined || JsValue.StrictEquals(given, value))
            {
                continue;
            }

            string message = value is double number
                ? $"{spec.Key} was adjusted to {JsNumber.ToJsString(number)}"
                : $"{spec.Key} was reset to {value}";
            issues.Add(new ValidationIssue(spec.Key, message));
        }

        return new ValidationResult(issues.Count == 0, values, issues);
    }
}
