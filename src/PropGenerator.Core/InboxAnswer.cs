namespace PropGenerator;

/// <summary>Groundwork's answer to a delivery, or that none came in time.</summary>
/// <param name="Ok">True when the open map took the models in.</param>
/// <param name="Waiting">True when no answer came in time: the delivery waits in the inbox for a map.</param>
/// <param name="Map">The map that answered, when it said.</param>
/// <param name="Models">The models the map took in.</param>
/// <param name="Error">Why the map refused the delivery, when it did.</param>
public sealed record InboxAnswer(bool Ok, bool Waiting, string? Map, IReadOnlyList<string> Models, string? Error)
{
    /// <summary>Gets the answer when none came in time.</summary>
    public static InboxAnswer StillWaiting { get; } = new(false, true, null, [], null);

    /// <summary>Gets what the status line says: "In Groundwork map X", the refusal, or that it waits.</summary>
    public string Text => Waiting
        ? GroundworkInbox.WaitingText
        : Ok ? $"In Groundwork map {Map}" : $"Groundwork refused it: {Error}";
}
