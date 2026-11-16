namespace PropGenerator.Nature.Js;

/// <summary>
/// JavaScript's <c>undefined</c> as a value, for places where C# <c>null</c> must stay JavaScript's <c>null</c>. A
/// parameter whose value is <see cref="Value"/> counts as missing, exactly as an <c>undefined</c> property does in
/// easy-props' validation.
/// </summary>
public sealed class JsUndefined
{
    private JsUndefined()
    {
    }

    /// <summary>The one instance.</summary>
    public static JsUndefined Value { get; } = new();

    /// <inheritdoc />
    public override string ToString() => "undefined";
}
