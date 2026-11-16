// Adapted from easy-props frontend/src/core/commands/commands.ts (show-more, make-it-fit and their reasons) and
// frontend/src/store/editor-store.ts (makeItFit, openBatchWith) (MIT, © 2026 AHS12).
using System.Globalization;
using System.Text.Json.Nodes;
using PropGenerator.Desktop.Cards;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Status;
using PropGenerator.Engine;
using PropGenerator.Nature.Random;

namespace PropGenerator.Desktop.Batch;

/// <summary>
/// The prop's versions and budget: Show me more (nine versions side by side: new seeds, or damaged versions for a prop
/// with damage settings), Make it fit (lower the controls that cost
/// the most until the prop is under its face budget) and Export many (several versions at once). Make it fit and Export
/// many's names are upstream's, run on easy-props' engine (D1): the controls from the rules engine, the fit and the
/// names as jobs on the engine's export turn, never on the window's thread.
/// </summary>
internal sealed class PropBatch
{
    // The style every kind is built in (as EngineRecipe's).
    private const string Style = "low-poly";

    private readonly MainWindow _w;

    // Stops a fit still running when the window closes.
    private CancellationTokenSource? _fitting;

    /// <summary>Works on the window's prop.</summary>
    /// <param name="window">The window.</param>
    internal PropBatch(MainWindow window) => _w = window;

    /// <summary>Gets the card shown by Show me more while it is open, else null.</summary>
    internal ShowMoreCard? ShowMore { get; private set; }

    /// <summary>Gets the card shown by Export many while it is open, else null.</summary>
    internal ExportManyCard? ExportMany { get; private set; }

    /// <summary>Gets a value indicating whether Make it fit is working.</summary>
    internal bool Fitting { get; private set; }

    /// <summary>
    /// Gets or sets how long Make it fit may try: null for upstream's own five seconds (the app's),
    /// <see cref="Timeout.InfiniteTimeSpan"/> for no limit (tests, whose outcome must not depend on a busy machine).
    /// </summary>
    internal TimeSpan? FitDeadline { get; set; }

    private IMeshRecipe? Recipe => MeshRecipes.Find(_w.Files.Recipe);

    /// <summary>Why Show me more and Export many cannot run, or null when they can.</summary>
    /// <returns>The reason.</returns>
    internal string? WhyNoVersions() => Recipe is null ? "The floor tile has one version: change its seed instead" : null;

    /// <summary>The controls Make it fit may lower on the prop as it is now (upstream's <c>fitControlsOf</c>); none for a kind of ours.</summary>
    /// <returns>The controls; empty too when the rules engine cannot answer.</returns>
    internal IReadOnlyList<EngineFitControl> FitControls()
    {
        if (Recipe is not EngineRecipe engine)
        {
            return [];
        }

        try
        {
            return engine.FitControls(_w.Panel.Current);
        }
        catch (Exception e) when (EngineFailed(e))
        {
            return [];
        }
    }

    /// <summary>Why Make it fit cannot run, or null when it can.</summary>
    /// <returns>The reason.</returns>
    internal string? WhyNotFit()
    {
        if (Fitting)
        {
            return "Make it fit is working";
        }

        if (FitControls().Count == 0)
        {
            return "This kind of prop has to be lowered by hand";
        }

        return FaceBudget.IsOver(_w.Rocks.FullDetailFaces, _w.ViewState.Budget) ? null : "This prop already fits its budget";
    }

    /// <summary>Stops a fit still running (the window is closing); it then changes nothing.</summary>
    internal void Stop() => _fitting?.Cancel();

    /// <summary>Opens Show me more on the prop as it is now.</summary>
    internal void OpenShowMore()
    {
        if (Recipe is not { } recipe)
        {
            return;
        }

        string name = PropName();
        Settings now = _w.Panel.Current;
        DamagedVersions? damaged;
        try
        {
            damaged = DamagedVersions.Of(recipe, now);
        }
        catch (Exception e) when (EngineFailed(e))
        {
            // New seeds need nothing of the engine's rules: the card opens without the Damaged switch.
            TraceLog.Write($"Show me more: no damaged versions, easy-props' engine did not answer: {e.Message}");
            damaged = null;
        }

        ShowMoreCard? card = null;
        card = new ShowMoreCard(
            recipe,
            now,
            name,
            Use,
            OpenExportMany,
            () =>
            {
                if (ReferenceEquals(_w.Cards.Shown, card))
                {
                    _w.Cards.Close();
                }

                card?.Dispose();
                ShowMore = null;
            },
            damaged);
        ShowMore = card;
        _w.Cards.Show(card, nearTop: true);
    }

    /// <summary>
    /// Takes a version picked in Show me more, one undo step: a new seed (the hand edits stay), or a damaged version's
    /// damage settings on the prop as it is (upstream's <c>updateTreeParameters(version.damage, 'damaged version')</c>).
    /// </summary>
    /// <param name="version">The version.</param>
    internal void Use(PropVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        Settings now = _w.Panel.Current;
        if (version.Damage is null)
        {
            _w.Files.ChangeAll(now.With("seed", version.Seed), string.Create(CultureInfo.InvariantCulture, $"Use seed {version.Seed}"));
        }
        else
        {
            _w.Files.ChangeAll(version.Damaged(now), "Use a damaged version");
        }
    }

    /// <summary>Opens Export many: the prop in several versions, or these versions of it (ticked in Show me more).</summary>
    /// <param name="chosen">The versions ticked in Show me more, or null to choose how many versions.</param>
    internal void OpenExportMany(IReadOnlyList<PropVersion>? chosen = null)
    {
        if (Recipe is null)
        {
            return;
        }

        int most;
        try
        {
            most = MostVersions();
        }
        catch (Exception e) when (EngineFailed(e))
        {
            _w.Say($"Export many is not available: easy-props' engine did not answer ({e.Message}).", error: true);
            return;
        }

        ExportManyCard? card = null;
        card = new ExportManyCard(
            PropName(),
            chosen,
            most,
            variations => _ = ExportAsync(chosen, variations),
            () =>
            {
                if (ReferenceEquals(_w.Cards.Shown, card))
                {
                    _w.Cards.Close();
                }

                ExportMany = null;
                card?.Dispose();
            },
            Recipe is EngineRecipe ? _w.Files.Prefs.ExportPictureSize : null,
            size =>
            {
                if (_w.Files.Prefs.ExportPictureSize != size)
                {
                    _w.Files.Prefs.ExportPictureSize = size;
                    _w.Files.Prefs.Save();
                }
            },
            Recipe is EngineRecipe ? count => SetOf(chosen, count) : null,
            (count, set, folder) => _ = ExportSetAsync(chosen, count, set, folder),
            SetFolder(),
            ChooseSetFolderAsync);
        ExportMany = card;
        _w.Cards.Show(card, nearTop: true);
    }

    /// <summary>
    /// The batch Export many writes, named by upstream's <c>batchItems</c> on the engine (oak_01, oak_02, ...): names
    /// only, one job; each version's prop is made at its turn when it is exported, so the batch never holds them all.
    /// </summary>
    /// <param name="chosen">The versions (new seeds or damaged), or null for <paramref name="variations"/> versions.</param>
    /// <param name="variations">
    /// How many versions (1 to upstream's <c>MAX_VARIATIONS</c>) when none are chosen: the first is the prop as it is,
    /// the rest the same settings on upstream's variation seeds, so exporting twice makes the same props.
    /// </param>
    /// <param name="cancel">Stops the naming.</param>
    /// <returns>The items, in order.</returns>
    /// <exception cref="EngineException">The engine failed to name them.</exception>
    internal async Task<IReadOnlyList<BatchVersion>> ItemsAsync(IReadOnlyList<PropVersion>? chosen, int variations, CancellationToken cancel = default)
    {
        Settings now = _w.Panel.Current;
        uint own = now.Values.ContainsKey("seed") ? now.GetSeed("seed") : 0;
        PropVersion[] versions = chosen is null
            ? [.. Enumerable.Range(0, Math.Clamp(variations, 1, MostVersions())).Select(v => new PropVersion((uint)Variations.VariationSeed(own, v)))]
            : [.. chosen];
        var choice = new JsonObject
        {
            ["kind"] = "versions",
            ["name"] = PropName(),
            ["versions"] = new JsonArray([.. versions.Select(static v => JsonNode.Parse(v.ToJson()))]),
        };
        string choices = new JsonArray(choice).ToJsonString();
        IReadOnlyList<EngineBatchItem> named = await EngineHost.Default.Builds
            .BatchItemsAsync(choices, versions.Length, new JsonObject { ["style"] = Style }.ToJsonString(), cancel)
            .ConfigureAwait(true);
        if (named.Count != versions.Length)
        {
            throw new EngineScriptException(string.Create(CultureInfo.InvariantCulture, $"batchItems named {named.Count} props for {versions.Length} versions."));
        }

        return [.. named.Zip(versions, static (item, version) => new BatchVersion(item.Name, version))];
    }

    /// <summary>Exports the batch (see <see cref="FileActions.ExportManyAsync"/>).</summary>
    /// <param name="chosen">The versions, or null for <paramref name="variations"/> versions.</param>
    /// <param name="variations">How many versions when none are chosen.</param>
    /// <returns>How many were exported.</returns>
    internal async Task<int> ExportAsync(IReadOnlyList<PropVersion>? chosen, int variations)
    {
        IReadOnlyList<BatchVersion> items;
        try
        {
            items = await ItemsAsync(chosen, variations).ConfigureAwait(true);
        }
        catch (Exception e) when (EngineFailed(e))
        {
            _w.Say($"Nothing was exported: easy-props' engine could not name the versions ({e.Message}).", error: true);
            return 0;
        }

        return await _w.Files.ExportManyAsync(items).ConfigureAwait(true);
    }

    /// <summary>
    /// The versions Export many would export as a set's props (each made as Export many makes it), and the colour
    /// settings they share; null when the prop cannot be in a set or the rules engine did not answer.
    /// </summary>
    /// <param name="chosen">The versions ticked in Show me more, or null for <paramref name="count"/> versions.</param>
    /// <param name="count">How many versions when none are chosen.</param>
    /// <returns>The props and their groups.</returns>
    internal (IReadOnlyList<SetProp> Props, IReadOnlyList<EngineColourGroup> Groups)? SetOf(IReadOnlyList<PropVersion>? chosen, int count)
    {
        if (Recipe is not EngineRecipe engine)
        {
            return null;
        }

        try
        {
            IReadOnlyList<SetProp> props = SetPropsOf(engine, chosen, count);
            return (props, ExportSet.Groups(props));
        }
        catch (Exception e) when (EngineFailed(e))
        {
            TraceLog.Write($"Export as a set: easy-props' engine did not answer: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Export many as a set: the versions, named as upstream's batch names them, each made in the set's colours and
    /// exported into the set's folder (<see cref="FileActions.ExportSetAsync"/>).
    /// </summary>
    /// <param name="chosen">The versions, or null for <paramref name="count"/> versions.</param>
    /// <param name="count">How many versions when none are chosen.</param>
    /// <param name="set">The set's name and colours.</param>
    /// <param name="folder">The folder the set's own folder is made in.</param>
    /// <returns>How many were exported.</returns>
    internal async Task<int> ExportSetAsync(IReadOnlyList<PropVersion>? chosen, int count, SetChoice set, string folder)
    {
        if (Recipe is not EngineRecipe engine)
        {
            return 0;
        }

        IReadOnlyList<SetProp> props;
        try
        {
            props = await ExportSet.NamedAsync(SetPropsOf(engine, chosen, count)).ConfigureAwait(true);
        }
        catch (Exception e) when (EngineFailed(e))
        {
            _w.Say($"Nothing was exported: easy-props' engine could not name the versions ({e.Message}).", error: true);
            return 0;
        }

        return await _w.Files.ExportSetAsync(props, set, folder).ConfigureAwait(true);
    }

    /// <summary>The folder a set goes in until another is chosen: the last set's, else where work is saved, else Documents.</summary>
    /// <returns>The folder, or null when there is none.</returns>
    internal string? SetFolder()
    {
        Prefs prefs = _w.Files.Prefs;
        if ((prefs.SetFolder ?? prefs.SaveFolder) is { Length: > 0 } remembered)
        {
            return remembered;
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return documents.Length > 0 ? documents : null;
    }

    /// <summary>Asks where a set goes.</summary>
    /// <param name="start">The folder to start in.</param>
    /// <returns>The folder chosen, or null.</returns>
    internal Task<string?> ChooseSetFolderAsync(string? start) => _w.Files.Dialogs.AskFolderAsync("Choose where the set goes", start);

    /// <summary>
    /// Make it fit: upstream's <c>makeItFit</c> on the engine's export turn (off the window's thread) lowers the controls
    /// that cost the most, one after the other, until the prop is under its face budget. The changes are one undo step,
    /// and the status line names them in upstream's words.
    /// </summary>
    /// <returns>Done when the prop fits, or as well as it can.</returns>
    internal async Task FitAsync()
    {
        if (WhyNotFit() is not null || Recipe is not EngineRecipe engine)
        {
            return;
        }

        Fitting = true;
        using var fitting = new CancellationTokenSource();
        _fitting = fitting;
        try
        {
            Settings before = _w.Panel.Current;
            (string _, string label, int budget) = FaceBudget.Find(_w.ViewState.Budget);
            _w.Say($"Making it fit the {label} budget…");
            EngineFit result;
            try
            {
                result = await engine.MakeItFitAsync(before, budget, FitDeadline, fitting.Token).ConfigureAwait(true);
            }
            catch (Exception e) when (EngineFailed(e))
            {
                if (!fitting.IsCancellationRequested)
                {
                    _w.Say($"Make it fit left the prop as it was: easy-props' engine did not answer ({e.Message}).", error: true);
                }

                return;
            }

            if (fitting.IsCancellationRequested)
            {
                return;
            }

            if (!_w.Panel.Current.Equals(before))
            {
                _w.Say("The prop changed while Make it fit was working; nothing was changed. Try again.", error: true);
                return;
            }

            if (result.Changes.Count == 0)
            {
                _w.Say("Make it fit could not lower anything more. Lower the detail by hand.", error: true);
                return;
            }

            Settings target = before;
            foreach (EngineFitChange change in result.Changes)
            {
                target = target.With(change.Key, change.To);
            }

            _w.Files.ChangeAll(target, "Make it fit");
            _w.Say(result.Fits
                ? string.Create(CultureInfo.InvariantCulture, $"Fits the {label} budget now, at {result.Faces:N0} faces: {result.Description}")
                : string.Create(CultureInfo.InvariantCulture, $"Still over the {label} budget at {result.Faces:N0} faces after {result.Description}. Lower the rest by hand."), error: !result.Fits);
        }
        finally
        {
            _fitting = null;
            Fitting = false;
        }
    }

    // The versions as a set's props, each named after the prop (upstream's batch numbers them at the export).
    private IReadOnlyList<SetProp> SetPropsOf(EngineRecipe engine, IReadOnlyList<PropVersion>? chosen, int count)
    {
        Settings now = _w.Panel.Current;
        uint own = now.Values.ContainsKey("seed") ? now.GetSeed("seed") : 0;
        PropVersion[] versions = chosen is null
            ? [.. Enumerable.Range(0, Math.Clamp(count, 1, MostVersions())).Select(v => new PropVersion((uint)Variations.VariationSeed(own, v)))]
            : [.. chosen];
        string name = PropName();
        return [.. versions.Select(v => new SetProp(name, engine, v.Of(now)))];
    }

    // A failure easy-props' engine is expected to give (phase 2 D10).
    private static bool EngineFailed(Exception e) =>
        e is EngineException or TimeoutException or ObjectDisposedException or OperationCanceledException;

    // Upstream's MAX_VARIATIONS, from the engine; a bundle that does not say gives one version at a time.
    private static int MostVersions() => Math.Max(1, EngineHost.Default.Catalog.Info.MaxVariations);

    // What the prop is called: its file's name, or "prop" while untitled (easy-props' treeName || 'prop').
    private string PropName() => _w.Files.FilePath is string path ? Path.GetFileNameWithoutExtension(path) : "prop";
}
