using PropGenerator.Nature.Export;

namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// A recipe that builds its prop as one coloured mesh (a rock, a cliff), so the app treats it alike: the panel, the
/// drawer, the live view with its detail levels and stats, the shapes you can walk into, the export and Claude's tools.
/// </summary>
internal interface IMeshRecipe
{
    /// <summary>Gets the recipe's name, as work files, the drawer and the Claude tools know it.</summary>
    string Id { get; }

    /// <summary>Gets what the panel's header calls the prop.</summary>
    string Label { get; }

    /// <summary>Gets the part of the recipe's exported model names, stable whatever the label says: "rock", "cliff".</summary>
    string ExportName { get; }

    /// <summary>Gets the generator's version: a newer one draws the drawer's pictures of its starting points again.</summary>
    int Version { get; }

    /// <summary>Gets the settings in panel order, the seed last (kept and saved, not shown as a row).</summary>
    IReadOnlyList<ParamSpec> Specs { get; }

    /// <summary>Gets the starting points offered in the drawer, in drawer order.</summary>
    IReadOnlyList<StartingPoint> StartingPoints { get; }

    /// <summary>Gets the share of full detail each detail level is built at, full detail first.</summary>
    IReadOnlyList<double> DetailShares { get; }

    /// <summary>Builds the prop for these settings at a share of its full detail, as one coloured mesh, Z-up, in metres.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="detail">The share of full detail: 1, or one of the lower levels' <see cref="DetailShares"/>.</param>
    /// <param name="purpose">What the build is for, which decides the queue an engine-backed recipe puts it in.</param>
    /// <param name="cancel">Stops the build once its result is no longer wanted.</param>
    /// <param name="replaces">
    /// The caller's replace key: a newer build with the same key drops this one while it still waits (it then throws
    /// <see cref="OperationCanceledException"/>); null for none.
    /// </param>
    /// <returns>The mesh.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "The replace key follows the token, as EngineThread.BuildAsync and CollisionAsync take them.")]
    ColouredMesh Build(Settings s, double detail, BuildPurpose purpose = BuildPurpose.View, CancellationToken cancel = default, string? replaces = null);

    /// <summary>
    /// Builds the prop at a share of its full detail and, when asked, counts its faces at full detail (proxies left
    /// out): the faces of this build at full detail, else of a full build as well.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <param name="detail">The share of full detail.</param>
    /// <param name="countFull">True to count the full-detail faces; false to build the level alone.</param>
    /// <param name="purpose">What the build is for.</param>
    /// <param name="cancel">Stops the build once its result is no longer wanted.</param>
    /// <param name="replaces">The caller's replace key, or null for none.</param>
    /// <returns>The mesh, and the full-detail faces (null when <paramref name="countFull"/> is false).</returns>
    (ColouredMesh Mesh, int? FullFaces) BuildCounted(Settings s, double detail, bool countFull, BuildPurpose purpose, CancellationToken cancel, string? replaces = null)
    {
        ColouredMesh mesh = Build(s, detail, purpose, cancel, replaces);
        if (!countFull)
        {
            return (mesh, null);
        }

        ColouredMesh full = detail >= 1 ? mesh : Build(s, 1, purpose, cancel, replaces);
        return (mesh, full.Indices.Length / 3);
    }

    /// <summary>Builds the prop's GLB file with its export (easy-props' engine's, or ours for a cliff and a stylized rock), collision shapes left out.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="modelName">The model's name, already safe as a file name.</param>
    /// <param name="options">
    /// How to export it (null: <see cref="ExportOptions.Default"/>): the look (Lit, Cartoon or Comic, which bakes flat
    /// colours into the file), the closest level's picture size and the set; our own kinds read the look alone.
    /// </param>
    /// <param name="cancel">Stops the export once it is no longer wanted.</param>
    /// <returns>The bytes of the .glb.</returns>
    byte[] Glb(Settings s, string modelName, ExportOptions? options = null, CancellationToken cancel = default);

    /// <summary>The shapes a player bumps into, as the export chooses them, in the prop's own Y-up space.</summary>
    /// <param name="s">The settings.</param>
    /// <param name="cancel">Stops the work once its result is no longer wanted.</param>
    /// <param name="replaces">The caller's replace key, or null for none.</param>
    /// <returns>The shapes; empty when the prop offers none.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "The replace key follows the token, as EngineThread.BuildAsync and CollisionAsync take them.")]
    IReadOnlyList<CollisionShape> Collision(Settings s, CancellationToken cancel = default, string? replaces = null);

    /// <summary>Gets how the prop moves in the wind in the view, or null when it stands still (a rock, a cliff).</summary>
    WindSway? Wind => null;

    /// <summary>Gets a value indicating whether the prop can also be made low-poly, through its <c>build</c> setting.</summary>
    bool LowPoly => false;

    /// <summary>
    /// Gets the share of full detail the drawer's 128-pixel picture is built at: the full prop, unless it is heavy to
    /// build and its lighter level looks the same that small (a tree).
    /// </summary>
    double PictureDetail => 1;

    /// <summary>Whether the prop offers a far-away card for these settings (easy-props' farAway).</summary>
    /// <param name="s">The settings.</param>
    /// <returns>True when the Far away level is offered.</returns>
    bool FarAway(Settings s) => false;

    /// <summary>
    /// How the panel shows the rows these settings show, when a label or group differs by values (easy-props'
    /// <c>shown()</c>); null when every row shows as its setting says.
    /// </summary>
    /// <param name="s">The settings.</param>
    /// <returns>The rows shown, in upstream's order, or null.</returns>
    IReadOnlyList<ShownRow>? ShownRows(Settings s) => null;

    /// <summary>The settings these values have no use for, which the panel leaves out (easy-props' <c>shownControls</c>).</summary>
    /// <param name="s">The settings.</param>
    /// <returns>The keys of the rows to hide.</returns>
    IReadOnlyCollection<string> HiddenKeys(Settings s) => [];

    /// <summary>Gets the kind's own-colour rows, by the choice they belong to (what a saved palette holds); empty for none.</summary>
    IReadOnlyList<OwnColourGroup> OwnColourGroups => [];

    /// <summary>
    /// What else changes with a change just made in the panel: a choice switched to My own colours starts its colours
    /// from the named option that was showing (easy-props' <c>startOwnColours</c>).
    /// </summary>
    /// <param name="before">The settings before the change.</param>
    /// <param name="after">The settings with the change.</param>
    /// <returns>The settings to keep.</returns>
    Settings Adjust(Settings before, Settings after) => after;

    /// <summary>
    /// What a saved prop's settings were made with, before the defaults fill what the file lacks: easy-props' earlier
    /// looks (a rock saved before a setting existed takes the value that keeps its look, not a new rock's). Called by
    /// <see cref="MeshRecipes.Loaded"/> on the settings and every undo step of a file, and on a saved preset.
    /// </summary>
    /// <param name="s">The settings as saved.</param>
    /// <returns>The settings with what they lack filled; the same settings when nothing is.</returns>
    /// <exception cref="WorkFileException">What the prop looked like could not be found (the engine failed).</exception>
    Settings EarlierLooks(Settings s) => s;

    /// <summary>Gets the engine the prop is made with, written into its work file; null for our own kinds.</summary>
    EngineStamp? Engine => null;
}
