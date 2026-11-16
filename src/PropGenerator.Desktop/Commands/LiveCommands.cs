using System.Collections;

namespace PropGenerator.Desktop.Commands;

/// <summary>
/// A sub-list of commands made again each time it is read, for a list that depends on the piece (the floor has one
/// detail level, a rock three).
/// </summary>
/// <param name="make">Makes the list as it is now.</param>
internal sealed class LiveCommands(Func<IReadOnlyList<Command>> make) : IReadOnlyList<Command>
{
    /// <inheritdoc/>
    public int Count => make().Count;

    /// <inheritdoc/>
    public Command this[int index] => make()[index];

    /// <inheritdoc/>
    public IEnumerator<Command> GetEnumerator() => make().GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
