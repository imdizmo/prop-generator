using System.Text.Json;

namespace PropGenerator;

/// <summary>
/// What one face of a piece is: a plank top, a plank side or a cut end. Version 2 paints face by face from these.
/// </summary>
/// <param name="FaceId">The face's number in the piece's mesh.</param>
/// <param name="Kind">"plank_top", "plank_side" or "cut_end".</param>
/// <param name="Settings">Settings that only this face overrides (empty when it follows the piece).</param>
public sealed record FaceMark(int FaceId, string Kind, IReadOnlyDictionary<string, JsonElement> Settings)
{
    /// <summary>The face kinds that exist.</summary>
    public static IReadOnlyList<string> Kinds { get; } = ["plank_top", "plank_side", "cut_end"];
}
