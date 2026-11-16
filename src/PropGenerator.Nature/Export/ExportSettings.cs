// Ported from easy-props frontend/src/core/export/pipeline/types.ts (ExportSettings) (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Export;

/// <summary>What an export writes (easy-props' <c>ExportSettings</c>).</summary>
/// <param name="Name">The prop's name, used for every file; it must already be safe as one (easy-props' <c>exportNameOf</c>).</param>
/// <param name="Engine">
/// <c>unreal</c>, <c>godot</c> or <c>plain</c>: how the collision shapes are named, and for Unreal with
/// <paramref name="Join"/>, that they ride inside the first level's file.
/// </param>
/// <param name="Levels">How many detail levels to write, 1 to 3.</param>
/// <param name="Collision">Whether to write collision shapes.</param>
/// <param name="Wind">Asks for wind data; a prop that does not move gets none regardless.</param>
/// <param name="Join">Writes the prop as one mesh, each material still its own part of it.</param>
/// <param name="Far">Asks for a far-away card; a prop that offers none gets none regardless.</param>
/// <param name="Look">How the game will draw the prop: one of <see cref="ExportLook.All"/>.</param>
/// <param name="Materials">Puts the look's Godot materials beside the files; only Godot has them.</param>
public sealed record ExportSettings(
    string Name,
    string Engine,
    int Levels,
    bool Collision = true,
    bool Wind = true,
    bool Join = false,
    bool Far = false,
    string Look = ExportLook.Lit,
    bool Materials = false);
