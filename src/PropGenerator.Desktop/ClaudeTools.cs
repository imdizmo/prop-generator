using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PropGenerator.Desktop.Commands;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Status;
using PropGenerator.Engine;

namespace PropGenerator.Desktop;

/// <summary>
/// What Claude can do in the app (served by <see cref="ClaudeServer"/>): start a new piece from a preset, read every
/// control, set any of them as the user would, undo, redo, save, export, take a picture of the 3D view and run any command of the app's command list. Every
/// tool runs on the window's thread. A changed control goes through the settings panel exactly as a click or a typed value
/// does, so the floor repaints (or the rock or cliff rebuilds) live and each change is one visible, undoable step. No tool
/// opens a card: Claude cannot answer one.
/// </summary>
/// <remarks>Each tool's description for Claude is its <c>[Description]</c> attribute.</remarks>
[McpServerToolType]
public sealed class ClaudeTools
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    // Commands that open a dialog or a card only the user can answer, or that have a tool of their own.
    private static readonly HashSet<string> NeedsTheUser =
        [
            "open", "save", "save-copy", "export", "send-groundwork", "map-folder", "settings", "palette", "shortcuts", "about", "use-seed",
            "my-props", "recently-removed", "backup-file", "restore-file", "backup-folder", "cloud-setup", "save-picture",
            "report-problem", "save-palette", "save-preset",
        ];
    private readonly MainWindow _w;

    /// <summary>Makes the tools for this window.</summary>
    /// <param name="window">The window whose controls, history and view the tools work on.</param>
    public ClaudeTools(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _w = window;
        Capture = window.View.CaptureAsync;
    }

    /// <summary>Gets or sets what takes the picture of the view (the OpenGL view's own; the tests give theirs).</summary>
    internal Func<Task<(byte[] Rgba, int Width, int Height)>> Capture { get; set; }

    /// <summary>Lists every control.</summary>
    /// <returns>A JSON list.</returns>
    [McpServerTool(Name = "list_controls")]
    [Description("Lists every control of the settings panel of the piece being made (the floor's wood, a rock's shape and surface, or a cliff's proportions, blocks, cuts and the rest): key (what set_control takes), label, tooltip (what it does to the piece), group (such as a rock's Shape or Surface, or a cliff's Proportions or Blocks), type (float, int, bool, colour, choice or seed), kind (tone: only recolours the floor, instant; paint: repaints the floor tile or rebuilds the rock or cliff), the current value and, for numbers, min, max and step. A colour's value is three numbers 0 to 1 and its hex. A choice's value is one of its options' value strings (listed with their labels). A seed is a whole number from 0 to 4294967295.")]
    public Task<CallToolResult> ListControls() => Guard(() => OnUi(() =>
    {
        var list = new JsonArray();
        Settings now = _w.Panel.Current;
        foreach (ParamSpec s in _w.Panel.Specs)
        {
            var o = new JsonObject { ["key"] = s.Key, ["label"] = _w.Panel.LabelOf(s.Key), ["tooltip"] = s.Tooltip, ["type"] = s.Type, ["kind"] = s.Kind };
            switch (s.Type)
            {
                case "colour":
                    Rgb c = now.GetColour(s.Key);
                    o["value"] = new JsonArray(c.R, c.G, c.B);
                    o["hex"] = $"#{(int)Math.Round(c.R * 255):X2}{(int)Math.Round(c.G * 255):X2}{(int)Math.Round(c.B * 255):X2}";
                    break;
                case "float":
                    o["value"] = Math.Round(now.GetFloat(s.Key), 4);
                    break;
                case "int":
                    o["value"] = now.GetInt(s.Key);
                    break;
                case "choice":
                    o["value"] = now.GetString(s.Key);
                    o["options"] = new JsonArray([.. (s.Options ?? []).Select(x => (JsonNode)new JsonObject { ["value"] = x.Value, ["label"] = x.Label })]);
                    break;
                case "seed":
                    o["value"] = (long)now.GetSeed(s.Key);
                    break;
                default:
                    o["value"] = now.GetBool(s.Key);
                    break;
            }

            if (_w.Panel.ShownGroupOf(s.Key) is string group)
            {
                o["group"] = group;
            }

            if (s.Type is "float" or "int")
            {
                o["min"] = s.Min;
                o["max"] = s.Max;
                o["step"] = s.Step;
            }

            o["default"] = JsonNode.Parse(s.Default.GetRawText());
            list.Add(o);
        }

        return Task.FromResult(Ok(list.ToJsonString(Compact)));
    }));

    /// <summary>Sets one control.</summary>
    /// <param name="key">The control's key.</param>
    /// <param name="value">The new value.</param>
    /// <returns>What the control shows now, or the problem.</returns>
    [McpServerTool(Name = "set_control")]
    [Description("Sets one control exactly as the user would: the control moves in the panel, a floor's tone key (colour, contrast, drift) recolours at once, a paint key repaints the floor at full detail or rebuilds the rock or cliff, and the change is one undoable history step. value: a number for float and int controls (inside min..max; ints must be whole), true or false for bool controls, an option's value string for choice controls, a whole number 0 to 4294967295 for seed controls, and for the colour either \"#RRGGBB\" or [r, g, b] with each 0 to 1. Returns the value now shown; setting the value it already has changes nothing and adds no step.")]
    public Task<CallToolResult> SetControl(
        [Description("The control's key from list_controls, for example gap_scale.")] string key,
        [Description("The new value: a number, true/false, an option value (choice), a seed number, or for the colour \"#RRGGBB\" or [r, g, b].")] JsonElement value) =>
        Guard(() => OnUi(() =>
        {
            Settings before = _w.Panel.Current;
            string? problem = _w.Panel.SetFromOutside(key, value);
            if (problem is not null)
            {
                return Task.FromResult(Fail(problem));
            }

            string now = Describe(key);
            bool changed = !_w.Panel.Current.Equals(before);
            return Task.FromResult(Ok(changed
                ? $"{now} (one history step added: {_w.Files.History.Steps[^1].Label})"
                : $"{now} (already that value; nothing changed)"));
        }));

    /// <summary>Takes back the last change.</summary>
    /// <returns>The history now.</returns>
    [McpServerTool(Name = "undo")]
    [Description("Takes back the last change to the settings of the piece, like Ctrl+Z. The panel and the 3D view follow. Returns the history position.")]
    public Task<CallToolResult> Undo() => Guard(() => OnUi(() =>
    {
        _w.Files.Undo();
        return Task.FromResult(Ok(HistoryText()));
    }));

    /// <summary>Brings back the change just taken back.</summary>
    /// <returns>The history now.</returns>
    [McpServerTool(Name = "redo")]
    [Description("Brings back the change that undo took back, like Ctrl+Shift+Z. Does nothing when there is nothing to bring back. Returns the history position.")]
    public Task<CallToolResult> Redo() => Guard(() => OnUi(() =>
    {
        _w.Files.Redo();
        return Task.FromResult(Ok(HistoryText()));
    }));

    /// <summary>Saves the work file.</summary>
    /// <param name="path">Where to save, or none for the current file.</param>
    /// <param name="overwrite">True to replace a file that is already there.</param>
    /// <returns>The file saved, or the problem.</returns>
    [McpServerTool(Name = "save")]
    [Description("Saves the piece as a .propgen work file. With a path (absolute; .propgen is added when it has no extension) it saves there and refuses to replace an existing file unless overwrite is true. Without a path it saves to the file the piece already has; an untitled piece needs a path. Never opens a dialog.")]
    public Task<CallToolResult> Save(
        [Description("Absolute path of the work file, or omit for the current file.")] string? path = null,
        [Description("True to replace the file if it already exists.")] bool overwrite = false) =>
        Guard(() => OnUi(() =>
        {
            string? problem = _w.Files.SaveWithoutAsking(path, overwrite);
            return Task.FromResult(problem is null ? Ok($"Saved {_w.Files.Name}.") : Fail(problem));
        }));

    /// <summary>Exports to a Groundwork map.</summary>
    /// <param name="mapFolder">The map folder, or none for the remembered one.</param>
    /// <returns>The model names written, or the problem.</returns>
    [McpServerTool(Name = "export")]
    [Description("Exports the piece to a Groundwork map with its catalog entries, like the Export to Groundwork button: a floor as two GLB models (it waits for the full-detail paint of the current settings), a rock as one GLB model, pg_<name>_rock, and a cliff as one, pg_<name>_cliff, both in the Rocks group. The piece must be saved first (the models are named after the file). mapFolder is the map's folder; omit it to use the one remembered from the last export. Returns the names of the models written, or the error text. Never opens a dialog.")]
    public Task<CallToolResult> Export(
        [Description("The Groundwork map folder, or omit for the remembered one.")] string? mapFolder = null) =>
        Guard(() => OnUi(async () =>
        {
            (string[]? models, string? error) = await _w.Files.ExportWithoutAskingAsync(mapFolder);
            return error is null ? Ok("Exported: " + string.Join(", ", models!)) : Fail(error);
        }));

    /// <summary>Sends the piece to the map open in Groundwork.</summary>
    /// <returns>The models sent and Groundwork's answer, or the problem.</returns>
    [McpServerTool(Name = "send_to_groundwork")]
    [Description("Sends the piece to the map open in Groundwork through Groundwork's inbox, like File > Send to Groundwork: a floor as two GLB models, pg_<name>_floor_tile and pg_<name>_floor_tri (it waits for the full-detail paint of the current settings), any other prop as one, such as pg_<name>_rock. No map folder is needed. The piece must be saved first (the models are named after the file). It waits up to about 5 seconds for Groundwork's answer. Returns the models sent and then either \"In Groundwork map <name>\", Groundwork's refusal, or \"Waiting in Groundwork's inbox (open a map in Groundwork)\" when no map took them yet; or the error text. Never opens a dialog.")]
    public Task<CallToolResult> SendToGroundwork() =>
        Guard(() => OnUi(async () =>
        {
            (string[]? models, InboxAnswer? answer, string? error) = await _w.Files.SendToGroundworkWithoutAskingAsync();
            if (error is not null)
            {
                return Fail(error);
            }

            string text = $"Sent: {string.Join(", ", models!)}. {answer!.Text}.";
            return answer.Ok || answer.Waiting ? Ok(text) : Fail(text);
        }));

    /// <summary>A picture of the 3D view.</summary>
    /// <returns>A PNG, or the problem.</returns>
    [McpServerTool(Name = "screenshot")]
    [Description("Takes a picture (PNG) of the 3D view of the piece (the floor, the rock or the cliff) as it is drawn now, at the camera angle the user has. Only the 3D view, not the panel.")]
    public Task<CallToolResult> Screenshot() => Guard(async () =>
    {
        (byte[] rgba, int width, int height) = await OnUi(() => Capture());
        byte[] png = await Task.Run(() => PngEncoder.Encode(rgba, width, height));
        return new CallToolResult { Content = { ImageContentBlock.FromBytes(png, "image/png") } };
    });

    /// <summary>What the app shows now.</summary>
    /// <returns>A JSON object.</returns>
    [McpServerTool(Name = "get_state")]
    [Description("Reports the app's state: the recipe of the piece being made (floor_tile, cut-rock or slab-cliff), the file name, whether there are unsaved changes, the history (every step's label and which one is current), and the text on the status line: the latest message (progress and errors show there), or the mouse hint once a message's 6 seconds are over. It also reports how the view shows the piece: shading (lit, flat or wireframe), lod (0 to 2), scene (studio, studio-sky or grey), budget (hero, standard, background or mobile), faces (full: the faces at full detail, shown: the faces at the detail on screen, allowed: the budget's faces, over: whether full is above allowed) maximized (the view fills the window) and build (painted or low-poly, for a kind that has the Normal / Low-poly switch; null otherwise). The engine block reports easy-props' engine: tag, started (whether a build engine is running; the rest is left out until one is), generation (runtimes loaded: 1, plus one per recovery or recycle), heap_bytes and external_bytes (the build engine's V8 heap and ArrayBuffers), texture_bytes (the textures kept), or error when it could not be read.")]
    public Task<CallToolResult> GetState() => Guard(async () =>
    {
        JsonObject engine = await EngineState(EngineHost.Default);
        return await OnUi(() => State(engine));
    });

    /// <summary>
    /// The engine block of get_state, read off the UI thread (it waits on the build engine's queue, at most 5 s).
    /// </summary>
    /// <param name="host">The engine host.</param>
    /// <returns>The block, or one with the error when the engine could not be read.</returns>
    internal static async Task<JsonObject> EngineState(EngineHost host)
    {
        try
        {
            return await Task.Run(async () =>
            {
                var wait = TimeSpan.FromSeconds(5);
                string tag = host.Catalog.Info.Tag;
                if (!host.BuildsStarted)
                {
                    // Nothing has built yet: a state read does not load a build engine of its own.
                    return new JsonObject { ["tag"] = tag, ["started"] = false };
                }

                EngineHeap heap = await host.Builds.HeapAsync().WaitAsync(wait).ConfigureAwait(false);
                int generation = await host.Builds.GenerationAsync().WaitAsync(wait).ConfigureAwait(false);
                return new JsonObject
                {
                    ["tag"] = tag,
                    ["started"] = true,
                    ["generation"] = generation,
                    ["heap_bytes"] = heap.UsedBytes,
                    ["external_bytes"] = heap.ExternalBytes,
                    ["texture_bytes"] = host.Textures.Bytes,
                };
            }).ConfigureAwait(false);
        }
        catch (Exception e) when (e is EngineException or TimeoutException or OperationCanceledException or ObjectDisposedException)
        {
            return new JsonObject { ["error"] = e.Message };
        }
    }

    private Task<CallToolResult> State(JsonObject engine)
    {
        FileActions f = _w.Files;
        int full = _w.StatusBar.FullFaces();
        var o = new JsonObject
        {
            ["recipe"] = f.Recipe,
            ["file"] = f.Name,
            ["unsaved"] = f.Dirty,
            ["question_open"] = f.Asking,
            ["history_index"] = f.History.Index,
            ["history"] = new JsonArray([.. f.History.Steps.Select(s => (JsonNode?)s.Label)]),
            ["status_line"] = _w.StatusLine.Text,
            ["shading"] = _w.ViewState.Shading,
            ["lod"] = _w.ViewState.Lod,
            ["scene"] = _w.ViewState.Scene,
            ["budget"] = _w.ViewState.Budget,
            ["faces"] = new JsonObject
            {
                ["full"] = full,
                ["shown"] = _w.StatusBar.ShownFaces(),
                ["allowed"] = FaceBudget.Find(_w.ViewState.Budget).Faces,
                ["over"] = FaceBudget.IsOver(full, _w.ViewState.Budget),
            },
            ["maximized"] = _w.ViewState.Maximized,
            ["build"] = _w.Panel.Current.Values.TryGetValue("build", out JsonElement build) ? build.GetString() : null,
            ["engine"] = engine,
        };
        return Task.FromResult(Ok(o.ToJsonString(Compact)));
    }

    /// <summary>Starts a new untitled piece from a preset, as a click in the drawer does, without asking anything.</summary>
    /// <param name="id">The preset's id.</param>
    /// <returns>What was started, or the problem.</returns>
    [McpServerTool(Name = "start_from_preset")]
    [Description("Starts a new untitled piece from a preset, as a click on it in the app's drawer does: the panel then shows that recipe's controls (call list_controls again) with the preset's values, the history starts fresh, and the view shows the piece. The ids: floor-v5 (the wooden floor tile), the rock presets (boulder and the others), the cliff presets (long-cliff, tall-cliff, cliff-top and layered-cliff), the stylized rock presets (stylized-pebble, stylized-rock and stylized-boulder), easy-props' grass presets (for example grass-tuft and long-grass), flower presets (flower-clump, daisies), bush presets (round-bush, low-hedge), cactus presets (saguaro, golden-barrel), tree presets (oak, birch, scots-pine, coconut-palm-painted) and old-style tree presets (round-canopy; an old-style palm has no preset: run_command new-palm starts one); an unknown id is answered with the list. Refused while the piece has unsaved changes (save it first), a question is open in the app, or an export or a send to Blender is running. Never opens a dialog.")]
    public Task<CallToolResult> StartFromPreset(
        [Description("The preset's id, for example floor-v5 or boulder.")] string id) =>
        Guard(() => OnUi(async () =>
        {
            IReadOnlyList<Recipes.StartingPoint> points = _w.StartingPoints;
            Recipes.StartingPoint? point = points.FirstOrDefault(p => p.Id == id);
            if (point is null)
            {
                return Fail($"There is no preset '{id}'. The valid ids are: {string.Join(", ", points.Select(p => p.Id))}.");
            }

            FileActions f = _w.Files;
            if (f.Asking)
            {
                return Fail("A question is open in the app (a card is waiting for the user). It must be answered first.");
            }

            if (f.BusyReason is string busy)
            {
                return Fail(busy);
            }

            if (f.Dirty)
            {
                return Fail("The piece has unsaved changes; save it first.");
            }

            if (!await f.StartFromAsync(point))
            {
                return Fail($"{point.Name} was not started (the app asked a question first).");
            }

            return Ok($"Started a new untitled {point.Name} ({point.Recipe}, group {point.Group}). Call list_controls for its controls.");
        }));

    /// <summary>Runs one command of the app's command list.</summary>
    /// <param name="id">The command's id.</param>
    /// <returns>What was run, or why it was not.</returns>
    [McpServerTool(Name = "run_command")]
    [Description("Runs any command of the app's command list by its id, as choosing it in a menu does (the same list the menus, the shortcuts and the Ctrl+K palette use): for example surprise (a new seed), restore-preset, undo, redo, frame, maximize, hide-panels, stats, person, collision, next-shading, shading-flat, shading-wireframe, lod-1, scene-grey, camera-top. A command that is greyed in the app is refused with the reason it shows (for example 'Nothing to undo yet'); an unknown id is answered with the list of valid ids; a list of choices (shading, lod, camera-angle, scene) is answered with its choices' ids; a command that fails answers with the failure it says on the status bar. Commands that open a dialog or a card only the user can answer (open, save-copy, map-folder, settings, palette, shortcuts, about, use-seed, and save or export, which have their own tools) are refused. Returns what ran; call get_state to see the view afterwards.")]
    public Task<CallToolResult> RunCommand(
        [Description("The command's id, for example surprise or shading-flat.")] string id) =>
        Guard(() => OnUi(async () =>
        {
            Command? command = CommandList.Find(_w.Commands, id);
            if (command is null)
            {
                return Fail($"There is no command '{id}'. The valid ids are: {string.Join(", ", AllCommands().Select(c => c.Id))}.");
            }

            if (command.Children is { Count: > 0 } choices)
            {
                return Fail($"{command.Label} is a list of choices, not a command: run one of them by its id ({string.Join(", ", choices.Select(c => c.Id))}).");
            }

            if (command.Unavailable() is string reason)
            {
                return Fail($"{command.Label} cannot run now: {reason}.");
            }

            if (NeedsTheUser.Contains(command.Id) || command.Id.StartsWith("rename-preset-", StringComparison.Ordinal))
            {
                return Fail($"{command.Label} opens a dialog or a card that only the user can answer, so it is not run from here.{(command.Id is "save" or "export" ? $" Use the {command.Id} tool." : command.Id == "send-groundwork" ? " Use the send_to_groundwork tool." : string.Empty)}");
            }

            if (_w.Files.Asking)
            {
                return Fail("A question is open in the app (a card is waiting for the user). It must be answered first.");
            }

            if (command.Id is "new-floor" or "new-rock" && _w.Files.Dirty)
            {
                return Fail("The piece has unsaved changes; save it first.");
            }

            int errors = _w.ErrorsSaid;
            await command.Run();
            if (_w.ErrorsSaid != errors)
            {
                // The command said it failed, on the status bar.
                return Fail($"{command.Label.TrimEnd('…')} did not work: {_w.LastError}");
            }

            return Ok($"Ran {command.Label.TrimEnd('…')}.{(command.Checked() is bool on ? (on ? " It is on now." : " It is off now.") : string.Empty)}");
        }));

    /// <summary>Lists the prop's parts.</summary>
    /// <returns>A JSON list.</returns>
    [McpServerTool(Name = "list_parts")]
    [Description("Lists the parts of the prop being made (a bush's core and leaves, a tree's trunk, branches and leaf clusters): id (what pick_part takes), type, the part generator, whether the user changed it (moved, turned, resized, restored or copied), its position (x, y, z in metres, Z up, as the view shows it) and which part is selected. The floor, the stylized rocks and the cliffs have no parts.")]
    public Task<CallToolResult> ListParts() => Guard(() => OnUi(() =>
    {
        if (_w.Editing.Scene is not { } scene)
        {
            return Task.FromResult(Fail(_w.Editing.WhyNoParts() ?? "There are no parts"));
        }

        var list = new JsonArray();
        foreach (Scenes.SceneComponent c in scene.Components)
        {
            Nature.Kernel.Vec3d p = c.Transform.Position;
            list.Add(new JsonObject
            {
                ["id"] = c.Id,
                ["type"] = c.Type,
                ["generator"] = c.GeneratorId,
                ["changed"] = c.Slot is null || c.Edited || c.Moved is not null || c.Turned is not null || c.Sized is not null,
                ["position"] = new JsonArray(Math.Round(p.X, 4), Math.Round(-p.Z, 4), Math.Round(p.Y, 4)),
                ["selected"] = c.Id == _w.Editing.Selected,
            });
        }

        return Task.FromResult(Ok(list.ToJsonString(Compact)));
    }));

    /// <summary>Selects a part.</summary>
    /// <param name="id">The part's id, or empty to let go.</param>
    /// <returns>What is selected now.</returns>
    [McpServerTool(Name = "pick_part")]
    [Description("Selects a part of the prop by its id from list_parts, as a click on it in the view does (it is drawn lit up); an empty id lets go of the selection. The part commands of run_command (copy-part, remove-part, restore-part) and move_part, turn_part and resize_part work on the selected part.")]
    public Task<CallToolResult> PickPart(
        [Description("The part's id from list_parts, or \"\" to select nothing.")] string id) =>
        Guard(() => OnUi(() =>
        {
            if (_w.Editing.WhyNoParts() is string why)
            {
                return Task.FromResult(Fail(why));
            }

            if (id.Length > 0 && _w.Editing.Scene!.Components.All(c => c.Id != id))
            {
                return Task.FromResult(Fail($"There is no part '{id}'. list_parts lists them."));
            }

            _w.Editing.Select(id.Length == 0 ? null : id);
            return Task.FromResult(Ok(id.Length == 0 ? "Nothing is selected." : $"Selected {id}."));
        }));

    /// <summary>Moves the selected part.</summary>
    /// <param name="x">Metres along x.</param>
    /// <param name="y">Metres along y.</param>
    /// <param name="z">Metres up.</param>
    /// <returns>Done, or why not.</returns>
    [McpServerTool(Name = "move_part")]
    [Description("Moves the selected part by an offset in metres (x, y across the ground, z up, as the view shows them), as the Move tool does. One undoable history step; saved in the work file, and kept when the prop's controls change.")]
    public Task<CallToolResult> MovePart(
        [Description("Metres along x.")] double x,
        [Description("Metres along y.")] double y,
        [Description("Metres up (z).")] double z) =>
        PartChange(_w.Editing.WhyNotMove, () => _w.Editing.MoveBy(new System.Numerics.Vector3((float)x, (float)y, (float)z)));

    /// <summary>Turns the selected part.</summary>
    /// <param name="axis">x, y or z.</param>
    /// <param name="degrees">How far.</param>
    /// <returns>Done, or why not.</returns>
    [McpServerTool(Name = "turn_part")]
    [Description("Turns the selected part about an axis through its own origin (x, y or z; z is up, as the view shows them) by an angle in degrees, as the Turn tool does; a painted tree's branch or root turns about where it leaves its parent instead, and its leaf clumps do not turn. One undoable history step; saved, and kept when the prop's controls change.")]
    public Task<CallToolResult> TurnPart(
        [Description("The axis: x, y or z (up).")] string axis,
        [Description("How far, in degrees; negative turns the other way.")] double degrees) =>
        PartChange(_w.Editing.WhyNotTurn, () => _w.Editing.TurnBy(
            axis switch
            {
                "x" => System.Numerics.Vector3.UnitX,
                "y" => System.Numerics.Vector3.UnitY,
                "z" => System.Numerics.Vector3.UnitZ,
                _ => throw new ArgumentException("The axis is x, y or z.", nameof(axis)),
            },
            degrees));

    /// <summary>Resizes the selected part.</summary>
    /// <param name="factor">How many times larger.</param>
    /// <returns>Done, or why not.</returns>
    [McpServerTool(Name = "resize_part")]
    [Description("Makes the selected part a number of times larger (below 1 smaller), about its own origin, as the Resize tool does; it stays between a tenth and ten times the size the prop's controls give it (a painted tree's branch or root grows from where it leaves its parent, between 0.25 and 3 times, and a leaf clump about its middle, between 0.3 and 2.5 times, to a hundredth). One undoable history step; saved, and kept when the prop's controls change.")]
    public Task<CallToolResult> ResizePart(
        [Description("How many times larger, above 0: 1.5 makes it half as large again, 0.5 halves it.")] double factor) =>
        PartChange(_w.Editing.WhyNotResize, () =>
        {
            if (!(factor > 0) || !double.IsFinite(factor))
            {
                throw new ArgumentException("The factor must be a number above 0.", nameof(factor));
            }

            _w.Editing.ResizeBy(factor);
        });

    private Task<CallToolResult> PartChange(Func<string?> why, Action change) => Guard(() => OnUi(() =>
    {
        if (why() is string reason)
        {
            return Task.FromResult(Fail(reason));
        }

        Settings before = _w.Panel.Current;
        change();
        return Task.FromResult(Ok(_w.Panel.Current.Equals(before) ? "Nothing changed." : $"Done. {HistoryText()}"));
    }));

    private IEnumerable<Command> AllCommands() =>
        _w.Commands.SelectMany(c => new[] { c }.Concat(c.Children ?? []));

    private static CallToolResult Ok(string text) => new() { Content = { new TextContentBlock { Text = text } } };

    private static CallToolResult Fail(string text) => new() { IsError = true, Content = { new TextContentBlock { Text = text } } };

    // On the window's thread (straight away when already there). A failure becomes an error answer, never a dropped call.
    private static Task<T> OnUi<T>(Func<Task<T>> f) =>
        Dispatcher.UIThread.CheckAccess() ? f() : Dispatcher.UIThread.InvokeAsync(f);

    private static async Task<CallToolResult> Guard(Func<Task<CallToolResult>> f)
    {
        try
        {
            return await f();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return Fail(e.Message);
        }
    }

    private string Describe(string key)
    {
        string label = _w.Panel.LabelOf(key);
        return $"{label} ({key}) is now {_w.Panel.DescribeChange(label)[label.Length..].Trim()}";
    }

    private string HistoryText()
    {
        History h = _w.Files.History;
        string at = h.Index >= 0 ? h.Steps[h.Index].Label : "start";
        return string.Create(CultureInfo.InvariantCulture, $"History step {h.Index + 1} of {h.Steps.Count}: {at}.");
    }
}
