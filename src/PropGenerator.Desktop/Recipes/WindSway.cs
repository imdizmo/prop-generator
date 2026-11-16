namespace PropGenerator.Desktop.Recipes;

/// <summary>How a prop moves in the wind in the view, as the engine kits will move it.</summary>
/// <param name="Foliage">True when the whole prop flutters as well as sways (leaves, grass); false when it only sways (wood).</param>
/// <param name="Evenly">True when every part moves about alike, as across a patch of grass, so its rim swings no further than its middle.</param>
/// <param name="ByPart">
/// True when it flutters part by part, as easy-props' view does: its foliage parts flutter, its wood only sways and its
/// fruit swings whole (a tree). The mesh then carries how much each vertex flutters.
/// </param>
internal sealed record WindSway(bool Foliage, bool Evenly, bool ByPart = false);
