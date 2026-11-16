namespace PropGenerator.Desktop.Props;

/// <summary>How Your props are put in order (easy-props' sort choices, in its order).</summary>
internal enum PropSort
{
    /// <summary>The one changed last first.</summary>
    LastChanged,

    /// <summary>By name, A to Z, numbers in number order.</summary>
    Name,

    /// <summary>The one made last first.</summary>
    DateMade,
}
