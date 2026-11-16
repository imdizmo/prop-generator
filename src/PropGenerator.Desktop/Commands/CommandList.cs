// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Globalization;
using System.Numerics;
using PropGenerator.Desktop.Recipes;
using NatureHash = PropGenerator.Nature.Random.Hash;

namespace PropGenerator.Desktop.Commands;

/// <summary>
/// Every command of the app, built for one window: the top bar's menus in easy-props' order, the view's tools and the
/// shading cycle. Features not ported yet are there too, greyed with what they wait for.
/// </summary>
internal sealed class CommandList
{
    private const string NoLowPoly = "This kind has no low-poly style";
    private const string DemoScene = "Comes with the demo scenery around the prop; the Grove also waits for painted trees";

    private static readonly Func<string?> Always = () => null;
    private static readonly Func<bool?> NotACheck = () => null;
    private static readonly Func<Task> Nothing = () => Task.CompletedTask;

    // Every Run made here is wrapped: an unavailable command only says why, and a failure is said too.
    private readonly MainWindow _w;

    private CommandList(MainWindow window) => _w = window;

    /// <summary>Gets the top bar's menus: their titles and their items, in easy-props' order with our own items.</summary>
    internal static IReadOnlyList<MenuLayout> Menus { get; } =
    [
        new("File", ["new-floor", "new-rock", "new-grass", "new-cactus", "new-tree", null, "my-props", "recently-removed", "open", null, "save", "save-copy", null, "export", "send-groundwork", "send-blender", "map-folder", "save-picture", null, "export-many", null, "backup-file", "restore-file", "backup-folder", "backup-folder-off", "cloud-setup", "backup-cloud", "restore-cloud", "cloud-off", null, "settings"]),
        new("Edit", ["undo", "redo", "undo-remove", null, "copy-part", "remove-part", "restore-part", "break-part", null, "clear-selection"]),
        new("View", ["shading", "lod", null, "frame", "back-to-piece", "camera-angle", null, "stats", "person", "collision", "wind", null, "hide-panels", "maximize", "drawer", "drawer-left", "reset-layout"]),
        new("Prop", ["surprise", "show-more", "make-pieces", "use-seed", "copy-seed", "restore-preset", null, "save-preset", "my-presets", "rename-preset", "remove-preset", null, "save-palette", "palettes", "remove-palette", null, "build-normal", "build-low-poly"]),
        new("Help", ["palette", "shortcuts", null, "report-problem", "about"]),
    ];

    /// <summary>The command with this id, looking inside the sub-lists too.</summary>
    /// <param name="commands">The list.</param>
    /// <param name="id">The id.</param>
    /// <returns>The command, or null when there is none.</returns>
    internal static Command? Find(IReadOnlyList<Command> commands, string id)
    {
        ArgumentNullException.ThrowIfNull(commands);
        foreach (Command c in commands)
        {
            if (c.Id == id)
            {
                return c;
            }

            if (c.Children?.FirstOrDefault(x => x.Id == id) is Command child)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>Makes every command for this window.</summary>
    /// <param name="w">The window the commands act on.</param>
    /// <returns>The commands: the menus' items in menu order, then the tools and the shading cycle.</returns>
    internal static IReadOnlyList<Command> For(MainWindow w)
    {
        ArgumentNullException.ThrowIfNull(w);
        var list = new CommandList(w);
        return [.. list.FileMenu(), .. list.EditMenu(), .. list.ViewMenu(), .. list.PropMenu(), .. list.HelpMenu(), .. list.Tools()];
    }

    private FileActions Files => _w.Files;

    private ViewState State => _w.ViewState;

    // A rock or a cliff: built as one mesh, with its detail levels.
    private bool Rock => MeshRecipes.Find(Files.Recipe) is not null;

    // A starting point by its recipe and id, from the recipe the app registers (easy-props' engine's).
    private static StartingPoint Preset(string recipe, string id) =>
        MeshRecipes.Find(recipe)!.StartingPoints.First(p => p.Id == id);

    private IEnumerable<Command> FileMenu()
    {
        yield return Make(
            "new-floor", "New floor tile", "File", "Ctrl+N", "grid-3x3",
            "Start a new untitled floor tile with the default wood. Asks first when this piece has unsaved changes.",
            Files.NewAsync, keywords: "start create floor tile wood");
        yield return Make(
            "new-rock", "New rock", "File", null, "box",
            "Start a new untitled rock from the Boulder starting point. The drawer has every other rock.",
            async () => await Files.StartFromAsync(Preset(RockRecipe.Id, "boulder")),
            unavailable: () => MeshRecipes.Unavailable(RockRecipe.Id), keywords: "start create rock stone boulder");
        yield return Make(
            "new-grass", "New grass", "File", null, "sprout",
            "Start a new untitled patch of grass from the first grass starting point. The drawer has the other grasses.",
            async () => await Files.StartFromAsync(MeshRecipes.Find("grass")!.StartingPoints[0]),
            unavailable: () => MeshRecipes.Unavailable("grass"), keywords: "start create grass meadow lawn tuft reeds");
        yield return Make(
            "new-cactus", "New cactus", "File", null, "sprout",
            "Start a new untitled cactus from the Saguaro starting point. The drawer has the other cacti.",
            async () => await Files.StartFromAsync(Preset("cactus", "saguaro")),
            unavailable: () => MeshRecipes.Unavailable("cactus"), keywords: "start create cactus cacti saguaro barrel prickly pear desert");
        yield return Make(
            "new-tree", "New tree", "File", null, "sprout",
            "Start a new untitled tree from the Old oak starting point. The drawer has the other trees, palms and conifers.",
            async () => await Files.StartFromAsync(Preset("painted-tree", "oak")),
            unavailable: () => MeshRecipes.Unavailable("painted-tree"), keywords: "start create tree oak painted palm conifer pine broadleaf wood forest");
        yield return Make(
            "new-palm", "New old-style palm", "File", null, "trees",
            "Start a new untitled old-style palm from its default settings. easy-props offers no palm starting point, so the drawer has none.",
            async () => await Files.StartFromAsync(EngineRecipes.PalmDefaults),
            unavailable: () => MeshRecipes.Unavailable("palm-tree"), keywords: "start create palm tree coconut tropical old style");
        yield return Make(
            "my-props", "My props", "File", null, "house",
            "Open Your props: every prop you saved, with its details, a search and the order, to open, rename, copy, remove or export one or several.",
            () => _w.ShowPropsAsync(), keywords: "home gallery library saved recent files your props rename duplicate copy delete");
        yield return Make(
            "recently-removed", "Recently removed", "File", null, "trash-2",
            "See the props you removed: they wait there for 30 days, to be put back or removed for good.",
            () => _w.ShowPropsAsync(bin: true), keywords: "bin trash deleted restore put back");
        yield return Make(
            "backup-file", "Back up to a file…", "File", null, "file-down",
            "Back up every prop (with your colours, palettes and presets) into one .zip file you choose.",
            _w.Backups.BackUpToFileAsync, keywords: "backup zip save all export archive");
        yield return Make(
            "restore-file", "Restore from a file…", "File", null, "folder-open",
            "Bring props back from a backup .zip. Nothing is replaced: a prop that differs comes back beside yours as \"(from backup)\".",
            _w.Backups.RestoreFromFileAsync, keywords: "backup zip restore import");
        yield return Make(
            "backup-folder", "Daily backup folder…", "File", null, "folder",
            "Choose a folder (a second drive, a synced folder) where every prop is backed up once a day, the last ten kept.",
            _w.Backups.ChooseFolderAsync, keywords: "backup automatic daily folder drive");
        yield return Make(
            "backup-folder-off", "Stop the daily backup", "File", null, null,
            "Stop backing up into the daily folder. The backups already there stay.",
            Sync(_w.Backups.StopFolder), unavailable: () => _w.Files.Prefs.BackupFolder is null ? "The daily backup is off" : null,
            keywords: "backup daily off");
        yield return Make(
            "cloud-setup", "Cloud backup…", "File", null, "settings-2",
            "Set up backups to S3-compatible storage of your own (Cloudflare R2, Backblaze B2, Amazon S3 and others). The keys stay in the system keychain.",
            Sync(_w.ShowCloudSetup), keywords: "backup cloud s3 r2 b2 bucket keys");
        yield return Make(
            "backup-cloud", "Back up to the cloud", "File", null, null,
            "Back up every prop to the cloud now; the newest ten backups are kept there.",
            _w.Backups.BackUpToCloudAsync, unavailable: CloudUnavailable, keywords: "backup cloud s3 upload");
        yield return Make(
            "restore-cloud", "Restore from the cloud", "File", null, null,
            "Bring props back from the newest cloud backup. Nothing is replaced: a prop that differs comes back beside yours.",
            _w.Backups.RestoreFromCloudAsync, unavailable: CloudUnavailable, keywords: "backup cloud s3 restore download");
        yield return Make(
            "cloud-off", "Turn off the cloud backup", "File", null, null,
            "Stop the cloud backup and forget its keys. Nothing in the bucket is touched.",
            _w.Backups.TurnOffCloudAsync, unavailable: CloudUnavailable, keywords: "backup cloud off forget keys");
        yield return Make(
            "undo-remove", "Undo remove", "Edit", null, "undo-2",
            "Put back the props you removed last, where they were.",
            Sync(_w.UndoRemove), unavailable: () => _w.LastRemoved is { Count: > 0 } ? null : "Nothing was removed",
            keywords: "restore put back bin deleted");
        yield return Make(
            "open", "Open a file…", "File", "Ctrl+O", "folder-open",
            "Open a saved work file with its settings and undo steps. Asks first when this piece has unsaved changes.",
            Files.OpenAsync, keywords: "load read disk work file");
        yield return Make(
            "save", "Save", "File", "Ctrl+S", "save",
            "Save this piece to its work file; a piece that was never saved asks where to save it first.",
            Files.SaveAsync, keywords: "write disk keep work file");
        yield return Make(
            "save-copy", "Save a copy…", "File", "Ctrl+Shift+S", null,
            "Save this piece under a new name, as a separate work file, and go on working in that copy.",
            Files.SaveAsAsync, keywords: "save as duplicate new version");
        yield return Make(
            "export", "Export…", "File", "Ctrl+E", "download",
            "Export to Groundwork: write this piece into a Groundwork map and its catalog, a floor as its square and triangle tiles, a rock or a cliff as one model in the Rocks group.",
            Files.ExportAsync, keywords: "groundwork map glb catalog");
        yield return Make(
            "send-groundwork", "Send to Groundwork", "File", null, "send",
            "Send this piece to the map open in Groundwork, through Groundwork's inbox: a floor as its square and triangle tiles, any other prop as one model. The piece must be saved first; the status line says which map took it, or that it waits until a map is opened.",
            Files.SendToGroundworkAsync, keywords: "groundwork open map inbox glb");
        yield return Make(
            "save-picture", "Save a picture…", "File", null, "camera",
            "Save the 3D view as it is now as a PNG picture, named after the piece.",
            () => PictureFile.SaveAsync(_w), keywords: "screenshot png image picture render");
        yield return Make(
            "send-blender", "Send to Blender", "File", null, "send",
            "Write this piece to the Blender outbox folder: a floor as its two tiles painted as they are now, a rock or a cliff as one GLB with its colours. In Blender's Prop Generator panel, Get painted brings it in.",
            _w.SendToBlenderAsync, keywords: "blender outbox glb");
        yield return Make(
            "map-folder", "Map folder…", "File", null, "folder",
            "Choose which Groundwork map folder Export writes into; the folder is remembered.",
            Files.ChangeMapFolderAsync, keywords: "groundwork map export folder");
        yield return Make(
            "export-many", "Export many…", "File", null, "file-down",
            "Export several versions of this piece at once.",
            Sync(() => _w.Versions.OpenExportMany()), unavailable: _w.Versions.WhyNoVersions, keywords: "batch variations seeds");
        yield return Make(
            "settings", "Settings", "File", "Ctrl+,", "settings",
            "Open the settings, where you can let Claude connect to this app and copy its address and token.",
            Sync(_w.ShowSettings), keywords: "preferences options claude token");
    }

    private IEnumerable<Command> EditMenu()
    {
        yield return Make(
            "undo", "Undo", "Edit", "Ctrl+Z", "undo-2", "Take back the last change to the settings of this piece.",
            Sync(Files.Undo), unavailable: () => CanUndo() ? null : "Nothing to undo yet");
        yield return Make(
            "redo", "Redo", "Edit", "Ctrl+Shift+Z", "redo-2", "Bring back the change you just took back.",
            Sync(Files.Redo), unavailable: () => Files.History.Index < Files.History.Steps.Count - 1 ? null : "Nothing to redo");
        yield return Make(
            "copy-part", "Copy part", "Edit", "Ctrl+D", "clipboard-copy", "Copy the selected part of the prop, with what hangs off it, and select the copy. One undo step.",
            Sync(_w.Editing.Copy), unavailable: _w.Editing.WhyNotCopy, keywords: "duplicate part");
        yield return Make(
            "remove-part", "Remove part", "Edit", "Delete", "trash-2", "Remove the selected part of the prop, with what hangs off it. One undo step.",
            Sync(_w.Editing.Remove), unavailable: _w.Editing.WhyNotRemove, keywords: "delete part");
        yield return Make(
            "restore-part", "Restore this part", "Edit", null, "rotate-ccw", "Put the selected part's own settings back to their defaults. One undo step.",
            Sync(_w.Editing.Restore), unavailable: _w.Editing.NeedsPart, keywords: "reset defaults selected part");
        yield return Make(
            "break-part", "Break branch", "Edit", null, "zap", "Break the selected heavy limb of a tree partway, leaving a jagged stub. One undo step.",
            Sync(_w.Editing.Break), unavailable: _w.Editing.WhyNotBreak, keywords: "snap damage limb stub");
        yield return Make(
            "clear-selection", "Clear selection", "Edit", "Esc", "x", "Put the part tool down, then let go of the selected part.",
            Sync(_w.Editing.Cancel), unavailable: () => _w.Editing.Selected is null && _w.Editing.Tool == Parts.PartEditing.SelectTool ? "Nothing is selected" : null,
            keywords: "deselect disarm tool cancel");
    }

    private IEnumerable<Command> ViewMenu()
    {
        yield return Make(
            "shading", "Shading", "View", null, "layers", "How the piece is drawn in the view: lit, flat or as its triangles.",
            Nothing, children:
            [
                Shading(ViewState.Lit, "Lit", "The finished look, with light and shadow."),
                Shading(ViewState.Flat, "Flat", "One flat colour per part. Good for checking the shape."),
                Shading(ViewState.Wireframe, "Wireframe", "Shows the triangles the prop is made of."),
                Make("shading-wind", "Wind", "View", null, null, "How much each part moves in the wind.", Nothing, unavailable: () => "Comes with wind"),
            ],
            keywords: "lit flat wireframe wind draw mode");
        yield return Make(
            "lod", "Detail level", "View", null, "grid-2x2", "Which detail level the view shows: full detail, or the lighter levels an export writes.",
            Nothing, children: new LiveCommands(Levels), keywords: "lod poly triangles faces");
        yield return Make(
            "frame", "Frame", "View", "F", "frame", "Move the camera so the whole piece fits in the view, looking from where it looks now.",
            Sync(_w.View.Frame), keywords: "focus zoom fit");
        yield return Make(
            "back-to-piece", "Back to the piece", "View", null, "arrow-left",
            "Go back to the piece you are making after a mesh from Blender was shown.",
            _w.BackToThePieceAsync, unavailable: () => _w.View.ShowingPiece ? null : "Nothing from Blender is shown",
            keywords: "blender mesh return");
        yield return Make(
            "camera-angle", "Camera angle", "View", null, "camera", "Look at the piece from straight in front, the side, the top, or the usual three-quarter view.",
            Nothing, children:
            [
                Angle("perspective", "Perspective", "The usual three-quarter view, from the front left and a little above.", new Vector3(-0.62f, -0.62f, 0.36f)),
                Angle("front", "Front", "Look at the piece from straight in front.", new Vector3(0, -1, 0)),
                Angle("side", "Side", "Look at the piece from its right side.", new Vector3(1, 0, 0)),
                Angle("top", "Top", "Look down on the piece from straight above.", new Vector3(0, 0, 1)),
            ],
            keywords: "front side top perspective view");
        yield return Toggle("stats", "Show stats", "info", "Show a card with the piece's faces, draw calls and size.", () => State.ShowStats, v => State.ShowStats = v, "faces draw calls size");
        yield return Toggle("wind", "Wind in the view", "wind", "Move the prop in the view as the wind materials will move it in your engine. Settings has its strength.", () => State.Wind, v => State.Wind = v, "sway animate move breeze");
        yield return Toggle("person", "Show a person for scale", "person-standing", "Stand a 1.8 m person beside the piece, to judge its size.", () => State.ShowPerson, v => State.ShowPerson = v, "size human reference");
        yield return Toggle("collision", "Show what you can walk into", "brick-wall", "Show the shapes a player bumps into, drawn over the piece.", () => State.ShowCollision, v => State.ShowCollision = v, "collision shapes solid");
        yield return Make(
            "hide-panels", "Hide or show panels", "View", "Ctrl+\\", "panel-left-close", "Hide the settings panel to see more of the view, or show it again.",
            Sync(() => State.PanelsHidden = !State.PanelsHidden), keywords: "sidebar collapse", clearsError: false);
        yield return Make(
            "maximize", "Maximize the view", "View", "Ctrl+Space", "maximize", "Let the 3D view fill the window: the panel, the drawer and the status bar go until you press it again.",
            Sync(() => State.Maximized = !State.Maximized), keywords: "fullscreen viewport", clearsError: false);
        yield return Toggle(
            "drawer", "Show the drawer", null, "Open or close the objects drawer (along the bottom of the view, or on its left), with every starting point and your own files.",
            () => State.DrawerOpen, ShowOrHideDrawer, "objects presets starting points", "Ctrl+G");
        yield return Toggle(
            "drawer-left", "Dock the drawer on the left", null,
            "Dock the objects drawer down the left of the window, its pictures in a column you can make wider; off, it lies along the bottom of the view.",
            () => State.DrawerLeft, DockDrawer, "objects drawer side left bottom dock column");
        yield return Make(
            "reset-layout", "Reset layout", "View", null, "layout-panel-left", "Bring back the panel at its first width, and leave the maximized view.",
            Sync(_w.ResetLayout), keywords: "panels sizes restore");
    }

    private IEnumerable<Command> PropMenu()
    {
        yield return Make(
            "surprise", "Surprise me", "Prop", "R", "dices", "Try a different one with the same shape: a new seed, the settings kept. One undo step.",
            Sync(Surprise), keywords: "randomize variation new seed");
        yield return Make(
            "show-more", "Show me more", "Prop", null, "layout-grid", "See nine versions of this piece side by side and pick one.",
            Sync(_w.Versions.OpenShowMore), unavailable: _w.Versions.WhyNoVersions, keywords: "variations seeds grid compare pick choose versions");
        yield return Make(
            "make-pieces", "Make pieces from this tree…", "Prop", null, "axe",
            "Make pieces from this tree: its stump, a fallen log, a fallen branch and its leaves on the ground, each saved as a prop of its own.",
            Sync(_w.Pieces.Open), unavailable: _w.Pieces.WhyNoPieces, keywords: "pieces stump log branch litter leaves fallen axe cut wood");
        yield return Make(
            "make-it-fit", "Make it fit", "Prop", null, "shrink", "Lower the controls that cost the most until this prop fits its face budget. One undo step.",
            _w.Versions.FitAsync, unavailable: _w.Versions.WhyNotFit, keywords: "budget faces lighter reduce mobile performance");
        yield return Make(
            "use-seed", "Use a seed…", "Prop", null, "hash", "Type or paste the seed that picks which random version of the piece you get.",
            Sync(_w.ShowSeed), keywords: "seed type paste number");
        yield return Make(
            "copy-seed", "Copy seed", "Prop", null, "clipboard-copy", "Copy this piece's seed, to paste it somewhere else or keep it.",
            CopySeedAsync, keywords: "seed clipboard number");
        yield return Make(
            "restore-preset", "Restore to preset", "Prop", null, "rotate-ccw", "Put every setting and the seed back as the starting point had them. One undo step.",
            Sync(RestorePreset), keywords: "reset defaults starting point");
        yield return Make(
            "save-preset", "Save as my preset…", "Prop", null, null,
            "Save this piece's settings and seed as a starting point of your own, shown in the drawer's My presets tab.",
            _w.SavePresetAsync, keywords: "preset starting point save mine favourite template");
        yield return Make(
            "my-presets", "Start from my preset", "Prop", null, null,
            "Start a new untitled piece from one of your own presets.",
            Nothing, unavailable: NoPresets,
            children: new LiveCommands(() => [.. _w.Presets.All.Select(p => Make(
                "preset-" + p.Id, p.Name, "Prop", null, null, $"Start a new untitled piece from your preset {p.Name}.",
                async () => await Files.StartFromAsync(p)))]),
            keywords: "preset starting point mine");
        yield return Make(
            "rename-preset", "Rename my preset", "Prop", null, null,
            "Give one of your own presets a new name.",
            Nothing, unavailable: NoPresets,
            children: new LiveCommands(() => [.. _w.Presets.All.Select(p => Make(
                "rename-preset-" + p.Id, p.Name, "Prop", null, null, $"Give your preset {p.Name} a new name.",
                () => _w.RenamePresetAsync(p)))]),
            keywords: "preset rename");
        yield return Make(
            "remove-preset", "Remove my preset", "Prop", null, null,
            "Remove one of your own presets. Pieces started from it keep their settings.",
            Nothing, unavailable: NoPresets,
            children: new LiveCommands(() => [.. _w.Presets.All.Select(p => Make(
                "remove-preset-" + p.Id, p.Name, "Prop", null, null, $"Remove your preset {p.Name}.",
                Sync(() => _w.RemovePreset(p))))]),
            keywords: "preset remove delete");
        var palettes = new Colours.PaletteActions(_w);
        yield return Make(
            "save-palette", "Save colours as a palette…", "Prop", null, "shapes",
            "Save the colours of My own colours under a name, to use them again on any prop with the same colours. The same name again updates it.",
            palettes.SaveAsync, unavailable: palettes.SaveUnavailable, keywords: "palette colours save my own colours swatches");
        yield return Make(
            "palettes", "Use a palette", "Prop", null, null,
            "Use one of your saved palettes on this prop: its colours become My own colours. One undo step.",
            Nothing, unavailable: () => palettes.Offered().Count > 0 ? null : "No saved palette for this kind yet",
            children: new LiveCommands(() => [.. palettes.Offered().Select(x => Make(
                "palette-" + x.Palette.Id, $"{x.Palette.Name} ({x.Group.Title})", "Prop", null, null,
                $"Use the palette {x.Palette.Name} on this prop's {x.Group.Title.ToLowerInvariant()} colours.",
                Sync(() => palettes.Apply(x.Palette))))]),
            keywords: "palette colours apply use my own colours");
        yield return Make(
            "remove-palette", "Remove a palette", "Prop", null, "trash-2",
            "Remove one of your saved palettes. Props using its colours keep them.",
            Nothing, unavailable: () => palettes.Offered().Count > 0 ? null : "No saved palette for this kind yet",
            children: new LiveCommands(() => [.. palettes.Offered().Select(x => Make(
                "remove-palette-" + x.Palette.Id, $"{x.Palette.Name} ({x.Group.Title})", "Prop", null, null,
                $"Remove the palette {x.Palette.Name}. Props using its colours keep them.",
                Sync(() => palettes.Remove(x.Palette))))]),
            keywords: "palette delete remove");
        yield return Build("build-normal", "Style: Normal", "1", "painted", "Make the prop in its normal style: soft, carved edges and painted shading.");
        yield return Build("build-low-poly", "Style: Low-poly", "2", "low-poly", "Make the same prop low-poly: big flat faces, each one colour, and fewer of them.");
    }

    private IEnumerable<Command> HelpMenu()
    {
        yield return Make(
            "palette", "Search or run a command", "Help", "Ctrl+K", "search", "Find any command, setting or starting point by typing part of its name, and run it.",
            Sync(_w.Palette.Open), keywords: "find commands palette");
        yield return Make(
            "shortcuts", "Keyboard shortcuts", "Help", "Ctrl+/", "keyboard", "List every keyboard shortcut of the app.",
            Sync(_w.ShowShortcuts), keywords: "help keys cheatsheet");
        yield return Make(
            "report-problem", "Report a problem…", "Help", null, "triangle-alert",
            "Fill in a report of a problem (the app's version, your system, the open prop's kind, seed and settings) to copy or save. Nothing is sent.",
            Sync(_w.ShowReport), keywords: "bug issue feedback report problem crash");
        yield return Make(
            "about", "About Prop Generator", "Help", null, "check", "Show the app's version and where its parts come from.",
            Sync(_w.ShowAbout), keywords: "version licences credits");
    }

    private IEnumerable<Command> Tools()
    {
        yield return Make(
            "select-tool", "Select tool", "Tools", "Q", "mouse-pointer-2", "Click a part in the view to pick it; drag to look around.",
            Sync(() =>
            {
                if (_w.Editing.Tool != Parts.PartEditing.SelectTool)
                {
                    _w.Editing.Toggle(_w.Editing.Tool);
                }
            }),
            keywords: "pointer click pick");
        yield return Make(
            "move-tool", "Move part", "Tools", "G", "move", "Drag in the view to move the selected part over the ground; hold Shift to move it up or down.",
            Sync(() => _w.Editing.Toggle(Parts.PartEditing.MoveTool)), unavailable: _w.Editing.WhyNotMove, keywords: "gizmo translate position axis");
        yield return Make(
            "turn-tool", "Turn part", "Tools", "E", "rotate-3d", "Drag in the view to turn the selected part about the upright; hold Shift to tip it.",
            Sync(() => _w.Editing.Toggle(Parts.PartEditing.TurnTool)), unavailable: _w.Editing.WhyNotTurn, keywords: "rotate gizmo tilt angle swing");
        yield return Make(
            "resize-tool", "Resize part", "Tools", "S", "scale-3d", "Drag up in the view to make the selected part larger, down to make it smaller.",
            Sync(() => _w.Editing.Toggle(Parts.PartEditing.ResizeTool)), unavailable: _w.Editing.WhyNotResize, keywords: "scale size bigger smaller shrink grow");
        yield return Make(
            "scene", "Scene", "View", null, "tree-pine", "The place around the piece in the view: a plain studio, the studio under a blue sky, or neutral grey.",
            Nothing, children:
            [
                Scene("studio", "Studio", "A plain place to work"),
                Scene("studio-sky", "Studio with sky", "A plain place to work, in bright daylight under a blue sky"),
                Make("scene-meadow", "Meadow demo", "View", null, null, "Grass, flowers, bushes, rocks and a cliff around your prop", Nothing, unavailable: () => DemoScene),
                Make("scene-grove", "Grove demo", "View", null, null, "The meadow, with trees as well", Nothing, unavailable: () => DemoScene),
                Scene("grey", "Neutral grey", "Best for judging colour"),
            ],
            keywords: "backdrop sky studio grey background");
        yield return Make(
            "next-shading", "Next shading", "View", "Z", null, "Step to the next way of drawing the rock: lit, flat, wireframe.",
            Sync(() => State.Shading = ViewState.Shadings[(ViewState.Shadings.ToList().IndexOf(State.Shading) + 1) % ViewState.Shadings.Count]),
            unavailable: () => _w.ShadingUnavailable, keywords: "cycle lit flat wireframe");
    }

    private static Func<Task> Sync(Action a) => () =>
    {
        a();
        return Task.CompletedTask;
    };

    private Command Make(
        string id, string label, string group, string? keys, string? icon, string tooltip, Func<Task> run,
        Func<string?>? unavailable = null, Func<bool?>? isChecked = null, IReadOnlyList<Command>? children = null,
        string? keywords = null, bool clearsError = true)
    {
        Func<string?> why = unavailable ?? Always;
        async Task Guarded()
        {
            // Changing the layout is no answer to a failure: maximizing or hiding the panels leaves it showing.
            if (clearsError)
            {
                _w.Acted();
            }

            if (why() is string reason)
            {
                _w.Say(reason);
                return;
            }

            try
            {
                await run();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _w.Say($"{label.TrimEnd('…')} did not work: {e.Message}", error: true);
            }
        }

        return new Command(id, label, group, keys, icon, why, isChecked ?? NotACheck, children, Guarded, keywords, tooltip);
    }

    private Command Toggle(string id, string label, string? icon, string tooltip, Func<bool> get, Action<bool> set, string keywords, string? keys = null) =>
        Make(id, label, "View", keys, icon, tooltip, Sync(() => set(!get())), isChecked: () => get(), keywords: keywords);

    // Flat and Wireframe draw rocks only: for the floor or a mesh from Blender they are greyed, and Lit shows checked
    // (the view state keeps the choice for the next rock).
    private Command Shading(string mode, string label, string tooltip) =>
        Make(
            "shading-" + mode, label, "View", null, null, tooltip, Sync(() => State.Shading = mode),
            unavailable: mode == ViewState.Lit ? null : () => _w.ShadingUnavailable, isChecked: () => _w.ShadingShown == mode);

    private Command Scene(string scene, string label, string tooltip) =>
        Make("scene-" + scene, label, "View", null, null, tooltip, Sync(() => State.Scene = scene), isChecked: () => State.Scene == scene);

    private IReadOnlyList<Command> Levels()
    {
        string[] help = ["Full detail, seen up close", "About 40% of the faces, for further away", "About 12% of the faces, for further away"];
        int count = Rock ? 3 : 1;
        IReadOnlyList<Command> levels = [.. Enumerable.Range(0, count).Select(level => Make(
            $"lod-{level}", string.Create(CultureInfo.InvariantCulture, $"Level {level} (LOD{level})"), "View", null, null, help[level] + ".",
            Sync(() => State.Lod = level), isChecked: () => State.Lod == level))];
        return MeshRecipes.Find(Files.Recipe)?.FarAway(_w.Panel.Current) == true
            ? [.. levels, Make(
                "lod-far", "Far away", "View", null, null, "The far-away card the export makes: two crossed pictures of the prop, drawn by the view.",
                Sync(() => State.Lod = ViewState.FarLevel), isChecked: () => State.Lod == ViewState.FarLevel)]
            : levels;
    }

    private Command Angle(string id, string label, string tooltip, Vector3 from) =>
        Make("camera-" + id, label, "View", null, null, tooltip, Sync(() => _w.View.LookFrom(Vector3.Normalize(from))));

    // easy-props 0.4.2: a kind that can be made low-poly switches its build setting, as one undo step; others have none.
    private Command Build(string id, string label, string keys, string build, string tooltip) =>
        Make(
            id, label, "Prop", keys, null, tooltip,
            Sync(() => _w.Panel.SetFromOutside("build", System.Text.Json.JsonSerializer.SerializeToElement(build))),
            unavailable: () => Recipes.MeshRecipes.Find(Files.Recipe) is { LowPoly: true } ? null : NoLowPoly,
            isChecked: () => _w.Panel.Current.Values.TryGetValue("build", out System.Text.Json.JsonElement now) && now.GetString() == build,
            keywords: "style low poly normal build look");

    private string? NoPresets() => _w.Presets.All.Count > 0 ? null : "No presets of your own yet";

    private string? CloudUnavailable() => _w.Backups.CloudOn ? null : "Set up the cloud backup first (File, Cloud backup…)";

    // Undo goes back to the last step first when something changed since it, as Ctrl+Z does.
    private bool CanUndo()
    {
        History h = Files.History;
        return h.Index > 0 || (h.Index >= 0 && !h.Steps[h.Index].Settings.Equals(_w.Panel.Current));
    }

    // Moving the drawer shows it, as its own Dock button does.
    private void DockDrawer(bool left)
    {
        State.Maximized = false;
        State.DrawerLeft = left;
        State.DrawerOpen = true;
    }

    // In the maximized view the drawer is out of sight: the view is restored and the drawer shown, as My props does.
    private void ShowOrHideDrawer(bool open)
    {
        if (State.Maximized)
        {
            State.Maximized = false;
            State.DrawerOpen = true;
            return;
        }

        State.DrawerOpen = open;
    }

    // easy-props' rule: the new seed is hashSeed(seed, seed + 1). A floor seed (a whole number with a range) is
    // folded into its range.
    private void Surprise()
    {
        ParamSpec spec = _w.Panel.Specs.First(s => s.Key == "seed");
        Settings now = _w.Panel.Current;
        double seed = spec.Type == "seed" ? now.GetSeed("seed") : now.GetInt("seed");
        uint next = NatureHash.Seed(seed, seed + 1);
        object value = spec.Type == "seed"
            ? next
            : (long)((next % (uint)((spec.Max ?? 9999) - (spec.Min ?? 0) + 1)) + (spec.Min ?? 0));
        Files.ChangeAll(now.With("seed", value), "Surprise me");
    }

    private void RestorePreset()
    {
        StartingPoint? point = _w.AllStartingPoints.FirstOrDefault(p => p.Id == Files.StartedFrom && p.Recipe == Files.Recipe);
        Files.ChangeAll(point?.Settings ?? _w.Panel.Defaults(), "Restore to preset");
    }

    private async Task CopySeedAsync()
    {
        string seed = _w.Panel.SeedValue.ToString(CultureInfo.InvariantCulture);
        bool copied = await _w.CopyAsync(seed);
        _w.Say(copied ? $"Copied seed {seed}" : "Couldn't copy the seed. Open the seed in the status bar to copy it yourself.", error: !copied);
    }
}
