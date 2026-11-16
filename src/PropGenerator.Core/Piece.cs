namespace PropGenerator;

/// <summary>One thing being made: which recipe builds it and what each of its faces is.</summary>
/// <param name="Recipe">The recipe name, "floor_tile" for floor tiles.</param>
/// <param name="Faces">The marked faces.</param>
public sealed record Piece(string Recipe, IReadOnlyList<FaceMark> Faces);
