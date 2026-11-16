// Adapted from easy-props (MIT, © 2026 AHS12).
using PropGenerator.Nature.Export;

namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// The cut rock, easy-props' engine's recipe (<see cref="Engine"/>), which the app reaches through
/// <see cref="MeshRecipes"/>; its name, label and the engine's members as statics for direct use. The build, export and
/// shapes block on the engine, so they are never called on the UI thread.
/// </summary>
internal static class RockRecipe
{
    /// <summary>The recipe's name, as the drawer and the Claude tools know it.</summary>
    internal const string Id = "cut-rock";

    /// <summary>What the panel's header calls a rock.</summary>
    internal const string Label = "Rock";

    /// <summary>Gets the rock as easy-props' engine makes it: what the app and the statics below use.</summary>
    internal static EngineRecipe Engine => EngineRecipes.For(Id);

    /// <summary>The rock's settings in panel order: the cut rock's own, then the seed (kept and saved, not shown as a row).</summary>
    internal static IReadOnlyList<ParamSpec> Specs => Engine.Specs;

    /// <summary>The rock presets offered in the drawer, in easy-props' order.</summary>
    internal static IReadOnlyList<StartingPoint> StartingPoints => Engine.StartingPoints;

    /// <summary>Gets the share of full detail each detail level is built at: 1, 0.4 and 0.12, as the export's levels.</summary>
    internal static IReadOnlyList<double> DetailShares => Engine.DetailShares;

    /// <summary>The rows these settings have no use for: the Style (a switch over the view), and in Low-poly the edges
    /// and bend; the stone colours unless the stone is My own colours.</summary>
    /// <param name="s">The rock settings.</param>
    /// <returns>The keys to hide.</returns>
    internal static IReadOnlyCollection<string> HiddenKeys(Settings s) => Engine.HiddenKeys(s);

    /// <summary>A switch to My own colours starts the stone colours from the palette that was showing.</summary>
    /// <param name="before">The settings before the change.</param>
    /// <param name="after">The settings with the change.</param>
    /// <returns>The settings to keep.</returns>
    internal static Settings Adjust(Settings before, Settings after) => Engine.Adjust(before, after);

    /// <summary>Builds the rock for these settings at full detail as one coloured mesh, Z-up, in metres.</summary>
    /// <param name="s">The rock settings.</param>
    /// <returns>The mesh.</returns>
    internal static ColouredMesh Build(Settings s) => Engine.Build(s, 1);

    /// <summary>Builds the rock at a share of its full detail, as one coloured mesh, Z-up, in metres.</summary>
    /// <param name="s">The rock settings.</param>
    /// <param name="detail">The share of full detail: 1, or one of the lower levels' <see cref="DetailShares"/>.</param>
    /// <returns>The mesh.</returns>
    internal static ColouredMesh Build(Settings s, double detail) => Engine.Build(s, detail);

    /// <summary>Builds the rock's GLB file with easy-props' export, collision shapes left out.</summary>
    /// <param name="s">The rock settings.</param>
    /// <param name="modelName">The model's name, already safe as a file name.</param>
    /// <param name="look">How the game will draw it: Lit, Cartoon or Comic (cel).</param>
    /// <returns>The bytes of the .glb.</returns>
    internal static byte[] Glb(Settings s, string modelName, string look = ExportLook.Lit) => Engine.Glb(s, modelName, new ExportOptions(look));

    /// <summary>The shapes a player bumps into, as the export would choose them: one tapered shape per chunk, heaviest
    /// first, in the prop's own Y-up space.</summary>
    /// <param name="s">The rock settings.</param>
    /// <returns>The shapes; empty when the rock offers none.</returns>
    internal static IReadOnlyList<CollisionShape> Collision(Settings s) => Engine.Collision(s);
}
