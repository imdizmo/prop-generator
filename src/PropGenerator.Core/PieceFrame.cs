namespace PropGenerator;

/// <summary>
/// How easy-props frames a new piece (its frameScene): by the Height and Width the piece was made with, not by its
/// mesh, so a tree's spreading branches do not push the camera back.
/// </summary>
/// <param name="Height">Its Height parameter, in metres (4.2 when it has none).</param>
/// <param name="Width">Its Width parameter, in metres; null when it has none (a tree), which keeps the camera at least 8 m away rather than 2 m.</param>
public sealed record PieceFrame(float Height, float? Width);
