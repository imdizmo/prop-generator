using PropGenerator.Desktop.Recipes;

namespace PropGenerator.Desktop;

/// <summary>Send to Groundwork: the piece (or saved props) delivered to Groundwork's inbox, for its open map.</summary>
public sealed partial class FileActions
{
    /// <summary>
    /// Gets or sets Groundwork's data folder, whose <c>inbox</c> the sends go to (<see cref="GroundworkInbox.DefaultDataDir()"/>;
    /// the tests give a scratch one).
    /// </summary>
    internal string GroundworkData { get; set; } = GroundworkInbox.DefaultDataDir();

    /// <summary>Gets or sets how long a send waits for Groundwork's answer before saying it waits in the inbox.</summary>
    internal TimeSpan GroundworkAnswerTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Sends the piece to Groundwork's open map (File, Send to Groundwork): the piece must be saved (named) first, as for
    /// Export. A floor waits for the full-detail paint and goes as its two tiles; any other kind as its one model. The
    /// status line says what Groundwork answered, or that the models wait in its inbox.
    /// </summary>
    /// <returns>Done when sent and answered (or not answered in time), failed or cancelled.</returns>
    public async Task SendToGroundworkAsync()
    {
        if (_exporting || Asking)
        {
            return;
        }

        (string[]? Models, string? Id, string? Error) sent;
        _exporting = true;
        try
        {
            if (FilePath is null && !await SaveAsAsync())
            {
                return;
            }

            sent = await DeliverPieceAsync();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _say($"Send to Groundwork failed: {e.Message}", true);
            return;
        }
        finally
        {
            _exporting = false;
        }

        // The piece is in the inbox: the wait for the answer does not hold the app busy.
        if (sent.Id is { } id)
        {
            _ = await AnswerAsync(id, Path.GetFileNameWithoutExtension(FilePath!));
        }
    }

    /// <summary>
    /// Sends the piece to Groundwork for Claude, who cannot answer a card: the piece must already be saved. Nothing is
    /// ever asked.
    /// </summary>
    /// <returns>The models sent and Groundwork's answer (or the waiting answer), or the problem.</returns>
    public async Task<(string[]? Models, InboxAnswer? Answer, string? Error)> SendToGroundworkWithoutAskingAsync()
    {
        if (Asking)
        {
            return (null, null, "A question is open in the app (a card is waiting for the user). It must be answered first.");
        }

        if (_exporting)
        {
            return (null, null, "An export is already running.");
        }

        if (FilePath is null)
        {
            return (null, null, "This piece is untitled: save it first (save with a path), the models are named after the file.");
        }

        string name = Path.GetFileNameWithoutExtension(FilePath);
        (string[]? Models, string? Id, string? Error) sent;
        _exporting = true;
        try
        {
            sent = await DeliverPieceAsync();
        }
        finally
        {
            _exporting = false;
        }

        return sent.Id is { } id ? (sent.Models, await AnswerAsync(id, name), null) : (null, null, sent.Error);
    }

    /// <summary>
    /// Sends saved props to Groundwork without opening them (Your props, Send to Groundwork), all in one delivery, in
    /// the export look. A floor tile is left out (it is painted by the painter: open it to send it), and so is a file that
    /// cannot be read or built; the status line says which.
    /// </summary>
    /// <param name="paths">The work files.</param>
    /// <returns>How many models were sent.</returns>
    internal async Task<int> SendFilesToGroundworkAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (_exporting || Asking || paths.Count == 0)
        {
            return 0;
        }

        var inbox = new GroundworkInbox(GroundworkData);
        var left = new List<string>();
        string id;
        int sent;
        _exporting = true;
        try
        {
            Recipes.ExportOptions options = ExportOptionsNow;
            using InboxDelivery delivery = inbox.Begin();
            foreach (string path in paths)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    string recipe = WorkFile.Load(path).Piece.Recipe;
                    if (MeshRecipes.Find(recipe) is not { } mesh)
                    {
                        left.Add(recipe == FloorRecipe.Id ? $"{name} (a floor tile: open it to send it)" : $"{name} ({Props.PropSummary.UnknownKind(recipe)})");
                        continue;
                    }

                    InboxKind kind = GroundworkSend.MeshKind(name, mesh);
                    if (delivery.Kinds.Any(k => k.Model.Equals(kind.Model, StringComparison.OrdinalIgnoreCase)))
                    {
                        left.Add($"{name} (another ticked prop has its model name, {kind.Model})");
                        continue;
                    }

                    Settings settings = Complete(mesh.Specs, WorkFile.Load(path, mesh.Specs, x => MeshRecipes.Loaded(recipe, x)).Settings);
                    _say($"Making {name} for Groundwork…", false);
                    byte[] glb = await Task.Run(() => mesh.Glb(settings, kind.Model, options));
                    await Task.Run(() => delivery.Add(kind, glb));
                }
                catch (Exception e) when (e is WorkFileException or IOException or UnauthorizedAccessException or InboxException || EngineFailed(e))
                {
                    left.Add($"{name} ({e.Message})");
                }
            }

            sent = delivery.Kinds.Count;
            if (sent == 0)
            {
                _say($"Nothing was sent to Groundwork. Left out: {string.Join("; ", left)}.", true);
                return 0;
            }

            await Task.Run(delivery.Complete);
            id = delivery.Id;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InboxException)
        {
            _say($"Nothing was sent to Groundwork: {e.Message}", true);
            return 0;
        }
        finally
        {
            _exporting = false;
        }

        // The models are in the inbox: the wait for the answer does not hold the app busy.
        string said = sent == 1 ? "Sent 1 model to Groundwork" : $"Sent {sent} models to Groundwork";
        string leftOut = left.Count == 0 ? string.Empty : $" Left out: {string.Join("; ", left)}.";
        _say($"{said}…{leftOut}", false);
        InboxAnswer answer = await inbox.WaitForAnswerAsync(id, GroundworkAnswerTimeout);
        _say($"{said}: {answer.Text}.{leftOut}", !answer.Ok && !answer.Waiting);
        return sent;
    }

    // Waits for Groundwork's answer to a delivery and says it on the status line.
    private async Task<InboxAnswer> AnswerAsync(string id, string name)
    {
        InboxAnswer answer = await new GroundworkInbox(GroundworkData).WaitForAnswerAsync(id, GroundworkAnswerTimeout);
        _say($"Sent {name} to Groundwork: {answer.Text}.", !answer.Ok && !answer.Waiting);
        return answer;
    }

    // The one send of the open piece into the inbox. The name, recipe and settings are taken before any wait; a floor
    // waits for its full-detail paint. A problem is also said on the status line.
    private async Task<(string[]? Models, string? Id, string? Error)> DeliverPieceAsync()
    {
        string name = Path.GetFileNameWithoutExtension(FilePath!);
        Settings settings = _panel.Current;
        IMeshRecipe? mesh = MeshRecipes.Find(Recipe);
        var inbox = new GroundworkInbox(GroundworkData);
        try
        {
            if (!GroundworkSend.HasName(name))
            {
                string text = $"Cannot send \"{name}\": its file name has no letters or digits to name the models by. Rename the file and send again.";
                _say(text, true);
                return (null, null, text);
            }

            IReadOnlyList<(InboxKind Kind, byte[] Glb)> models;
            if (mesh is not null)
            {
                string what = ExportDialog.KindLabel(mesh);
                _say($"Making {name} {what} for Groundwork…", false);
                InboxKind kind = GroundworkSend.MeshKind(name, mesh);
                Recipes.ExportOptions options = ExportOptionsNow;
                byte[] glb = await Task.Run(() => mesh.Glb(settings, kind.Model, options));
                models = [(kind, glb)];
            }
            else
            {
                (byte[] Rgb, int Size)? painted = await ComposeAsync($"Making {name} for Groundwork…", settings);
                if (painted is null)
                {
                    const string Late = "The floor was not painted at full detail in time, so nothing was sent to Groundwork.";
                    _say(Late, true);
                    return (null, null, Late);
                }

                models = await Task.Run(() => GroundworkSend.Floor(name, painted.Value.Rgb, painted.Value.Size));
            }

            string id = await Task.Run(() =>
            {
                using InboxDelivery delivery = inbox.Begin();
                foreach ((InboxKind kind, byte[] glb) in models)
                {
                    delivery.Add(kind, glb);
                }

                delivery.Complete();
                return delivery.Id;
            });
            _say($"Sent {name} to Groundwork…", false);
            return ([.. models.Select(m => m.Kind.Model)], id, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InboxException || EngineFailed(e))
        {
            string text = $"Nothing was sent to Groundwork: {e.Message}";
            _say(text, true);
            return (null, null, text);
        }
    }
}
