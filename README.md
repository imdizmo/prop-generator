# Prop Generator

A desktop app that paints wooden floor tiles in any wood colour, builds easy-props' props (run on easy-props' own code,
bundled as its engine) and our own slab cliffs and stylized rocks, live, and exports them to a Groundwork map. It is
written in C# (.NET 10, Avalonia); the floor is painted by a Python worker (numpy) that the app starts and stops.

## Install

Download the latest release from the repository's Releases page. Version 0.1.0 is for **Linux x64**; **Windows is
planned, not in this version.** There are two downloads, the same app in each:

- `PropGenerator-v0.1.0-linux-x64.AppImage`: make it executable (`chmod +x PropGenerator-*.AppImage`) and run it. If
  your system has no FUSE, run it with `--appimage-extract-and-run`.
- `PropGenerator-v0.1.0-linux-x64.tar.gz`: unpack it anywhere (`tar -xzf PropGenerator-*.tar.gz`) and run
  `PropGenerator-v0.1.0-linux-x64/PropGenerator`. `./install-desktop-entry.sh` in that folder adds it to your app menu
  with its icon (`--remove` takes it out again).

**Requirements:** a 64-bit x86 Linux with glibc 2.27 or newer and a graphical session (X11, or Wayland with XWayland)
with OpenGL (Mesa's drivers are enough). Nothing else needs installing: the app carries its own .NET runtime, and the
floor painter's Python, with numpy, is bundled in the package's `python/` folder, so the Python on your system (if any)
is never used. Where a systemd user session exists (most desktop distributions), the painter runs in a systemd scope
capped at 2 GB of memory; without one, it runs as a plain process.

The package also holds the Blender add-on (`blender/prop_generator_bridge.zip`, see [Blender add-on](#blender-add-on)),
the licences of what it bundles (`NOTICE.md` and `licenses/`) and this README.

## What version 1 does

- Paints the v5 floor tiles (a square and a triangle) in any wood colour. Colour, contrast and drift recolour at once;
  every other setting repaints the wood, first fast and rough, then at full detail.
- Builds easy-props' cut rocks (as easy-props 0.4.2 makes them): 12 settings and a seed in two groups (Shape,
  Surface), in two styles: Normal, with soft carved edges, or Low-poly, one shell of hard flat faces, each one colour.
  Switch with Style: Normal and Style: Low-poly over the view or the keys 1 and 2 (Edges and Bend hide in Low-poly).
  The stone's colour can be My own colours: four colour rows appear, starting from the palette that was showing. The
  rock is rebuilt while you drag a slider.
- Builds easy-props' grass (as easy-props 0.4.2 makes it): tufts, meadows, lawns, reeds and a flower meadow, nine
  starting points in the drawer's Grass group (File > New grass starts the first). 15 settings and a seed in Shape, Blades and
  Colour, two styles (Normal: fine curved blades; Low-poly: fewer, wider blades folded down the middle), and My own
  colours for the grass and for its flowers. Grass has nothing to walk into, and it sways in the wind (below).
- Builds easy-props' flowers (as easy-props 0.4.2 makes them): clumps of daisies, cups, spikes or plumes, six starting
  points in the drawer's Flowers group. 8 settings and a seed in Shape and Flower, two styles (Normal: thin flat petals;
  Low-poly: solid faceted shapes, each face one colour), and My own colours for the flowers. Flowers have nothing to
  walk into, and they sway in the wind like grass.
- Builds easy-props' bushes (as easy-props 0.4.2 makes them): round, spreading, tall and hedge bushes, open ones with twigs
  inside, and flowering ones, thirteen starting points in the drawer's Bushes group. A bush is a solid core under leaf
  cards printed with easy-props' leaf picture and cut out where it is clear, as the 3D view, the drawer's pictures and the
  sun's shadow show them; Low-poly makes it one shell of flat faces with faceted flowers. It has one capsule to walk into,
  and its leaves flutter as it sways in the wind.
- Builds easy-props' cacti (as easy-props 0.4.2 makes them): a saguaro with arms, a young saguaro, organ pipes, two
  barrels and a prickly pear, six starting points in the drawer's Cacti group (File > New cactus starts the Saguaro).
  Three kinds (a ribbed column with arms, a barrel, flat pads), each showing only the settings it uses, with ridges,
  spines and flowers; two styles (Normal and Low-poly: a few flat faces, each one colour), and My own colours for the
  cactus, its spines and its flowers. A cactus stands still in the wind; its export's collision comes from its body.
- Builds easy-props' painted trees (as easy-props 0.4.2 makes them): 54 starting points under easy-props' headings in the
  drawer (Broadleaf, Blossom & fruit, Palms & tropical, Conifers, Weeping, Old & bare, Shrubs & saplings, Fantasy, Ferns;
  File > New tree starts the Old oak). Three kinds (a branching crown with seven crown shapes, a palm with feather, fan or
  blade fronds, a conifer of tiers), each showing only the settings it uses. The wood is one sculpted surface whose
  limbs melt into the trunk, with ridges, knots, a hollow, moss and six bark patterns; the leaves are clumps of cards
  printed with easy-props' brush dabs and cut out where they are clear, with hanging strands and fruit. Low-poly makes
  the same tree in a few flat-sided blocks. In the wind the tree sways, its leaves flutter, its wood does not and its
  fruit swings whole; the trunk and heavy limbs are what you walk into. The 3D view draws the bark in its painted
  colours; the bark print and its relief are in the export.
- Builds easy-props' old-style trees and palms (as easy-props 0.4.2 makes them): a broadleaf tree of trunk, branches,
  leaf clumps and roots (Round canopy, the drawer's Old-style trees group), and a palm of ringed trunk, crown, fronds and
  coconuts (File > New old-style palm; easy-props offers no palm preset, its two are retired). 11 and 14 settings and a
  seed. Their lighter detail levels simplify the wood with three's SimplifyModifier, as easy-props does (on its engine),
  and keep fewer, larger leaf parts. They sway and flutter in the wind and have
  nothing to walk into.
- Builds stylized rocks (the one-minute Blender recipe: a sphere pushed about by Voronoi noise, thinned to flat facets):
  7 settings and a seed in three groups (Shape, Mesh, Colour), three starting points in the drawer's Stylized group.
  Exports like a cut rock, as `pg_<name>_rock`.
- Wears the Jatify look (jatify.com's near-black and orange, in Space Grotesk, as the Jatify launcher has it) and works
  like easy-props: its icons, a top bar with menus, one command list behind the
  menus, the shortcuts and a search box (Ctrl+K), a controls panel with a filter and folding groups, a status bar, and
  a 3D view with easy-props' sky, grid, light, shadows, tools and compass (all below).
- Tooltips on every setting and every command say what it does.
- An objects drawer along the bottom of the 3D view (below).
- Undo and redo, and work files (`.propgen`) that hold the settings and the undo steps, for a floor or a rock.
- Exports a floor as two GLB models (the square and the triangle tile), or a rock as one GLB model, plus catalog entries
  into a Groundwork map folder. A rock goes in the group "Rocks" as `pg_<name>_rock`. Settings > Export > Look says how
  your game will draw a rock, cliff or stylized rock you export or send to Blender (easy-props 0.4.2's looks): Lit,
  Cartoon (the same file; the engine's toon material does the rest) or Comic, which cuts each part's colours to a few
  flat tones in the file. It is remembered.
- Your props: every prop you saved on one page, with its details, a search, kinds and three orders, to open, rename,
  copy, remove (into Recently removed, 30 days, with Undo) or export one or several (below).
- Backups of every prop: to a file, a daily folder backup, and an S3-compatible cloud backup with restore (below).
- A colour picker in every colour row with Recent and My colours, and lists of colour codes pasted in; saved palettes
  for each kind's own colours; My presets, your own starting points (below).
- Save a picture of the view as a PNG, and Report a problem, which fills in a report to copy or save (below).
- Claude can drive the app (read and set every setting, start from a preset, run any command, undo, redo, save, export,
  screenshot) over a local connection that is off until you turn it on in Settings.
- A Blender add-on moves meshes between Blender and the app (see below).

## The window

**Top bar.** On the left the brand button (every starting point), the menus File, Edit, View, Prop and Help, and the
prop's name (click it to rename the work file); in the middle the "Search or run…" button (Ctrl+K); on the right Save
and Export. Every item has a tooltip, its key and its icon. An item that cannot run
yet is greyed and its tooltip says why ("Nothing to undo yet", "Select a part first").

- **File:** New floor tile, New rock, New grass, New cactus, New tree, New old-style palm, My props, Recently removed, Open, Save, Save a copy, Export,
  Send to Blender, Map folder, Save a picture, the backups (Back up to a file, Restore from a file, Daily backup folder,
  Stop the daily backup, Cloud backup, Back up to the cloud, Restore from the cloud, Turn off the cloud backup),
  Settings, and Export many (several versions of the prop at once, each its own model: `name_01` is the prop as it is,
  the rest the same settings on seeds drawn from it, up to 50; or the versions ticked in Show me more).
- **Edit:** Undo, Redo, Undo remove (puts back the props you removed last), Copy part, Remove part, Restore this part
  (its own settings back to their defaults) and Clear selection. Each part change is one undo step and is saved in the
  work file; a removed part stays removed when a control changes (easy-props brings it back), until undone.
- **View:** Shading (Lit, Flat, Wireframe; Flat and Wireframe are for rocks), Detail level (with Far away for the
  old-style trees and palms: the export's far-away card, its pictures drawn by the view), Frame, Back to the piece, Camera angle, Show stats, Show a
  person for scale, Show what you can walk into, Wind in the view, Hide or show panels, Maximize the view, Show the drawer, Dock the drawer on the left, Reset layout.
- **Prop:** Surprise me, Use a seed, Copy seed, Restore to preset, Save as my preset, Start from my preset, Rename my
  preset, Remove my preset, Save colours as a palette, Use a palette, Remove a palette, Style: Normal and Style: Low-poly (for a rock, grass, flowers, a bush, a cactus or a tree;
  greyed for a kind without a low-poly style), Show me more (the prop with eight others around it on new seeds: click
  one to take it, More for eight more, tick some and Export these) and Make it fit (in the palette and the status
  bar: lowers the controls that cost the most until the prop is under its face budget, one undo step).
- **Help:** Search or run a command, Keyboard shortcuts, Report a problem, About.

**History strip.** Along the top of the view, one box per change since the piece was opened: click a box, or drag
across the strip, to step back and forth (undone steps show dark); Undo and Redo move along it, and a box's tooltip
names its change.

**Objects drawer.** Drag its edge to make it bigger or smaller; "Dock on the left" moves it to the left side, where
it resizes in width. Its side and sizes are remembered.

**Search or run (Ctrl+K).** One box that finds anything: commands, settings, starting points and your saved props.
Empty, it suggests what is useful now. Typing searches in sections; Enter runs the top row, the arrow keys move, Esc
closes. A setting opens its options in a sub-list. A row that cannot run says why in the footer. Ctrl+K also works
while you type in a text box.

**Controls panel (right).** The head shows the kind of prop and "Whole prop" with Surprise me, and a filter box under
them. Below are the settings in folding groups (a rock has Shape and Surface); each row has its label, a slider or a
switch or a colour, a value you can type, and a reset arrow that puts that one setting back to the preset. At the foot,
Restore to preset puts every setting and the seed back (one undo step). Drag the edge between the view and the panel
(or press the left and right arrows on it) to make the panel 240 to 520 px wide; the small tab on the edge hides and
shows it. The width is remembered.

**Status bar (bottom).** On the left what the mouse does in the view, or the latest message (for 6 seconds; an error
stays, in red, until your next action, and brings the status bar back if the view is maximized). On the right: the
seed (click it to type, paste or copy it, or to try another), the detail level on screen (above level 0), the faces at
full detail against the budget (in a warning colour with "Over budget" when over, with Make it fit after it), the
budget picker (Hero, Standard, Background, Mobile) and whether the piece is saved
("Saved", "Unsaved", "Not saved yet").

**The 3D view.** Left drag orbits, right drag (or Shift and left drag) pans, the wheel zooms. The scene has a sky, a
ground grid, a sun and soft shadows. Click a part of a prop made of parts (a bush's core or leaves, a tree's branches)
to pick it: it is drawn lit up. Top left are the tools: Select, Move part (drag over the ground; Shift for up and down),
Turn part (drag to turn it about the upright; Shift to tip it) and Resize part (drag up or down, from a tenth to ten
times its size), each greyed with the reason until a part that can take it is picked. A part's turn and size are ours:
easy-props turns and resizes only a painted tree's branches; here any part that moves turns and resizes, through its
transform, and keeps them when a control changes. Top right, two groups: the look (Shading, Detail level) and the view (Scene: Studio, Studio with sky or
Neutral grey; Sun and shadow, where you drag the sun round the piece and set its height, warmth and shadow; Wind,
which moves grass and flowers in the view as the engine kits' wind materials will move it in your game, with a strength from calm
to storm (also in Settings; a rock or a floor holds still; the sun's shadow does not move with it yet);
Camera angle: Perspective, Front, Side, Top; Frame; Stats; A person for scale; What you can walk into; Maximize). The
scene, the sun and the budget are remembered. Bottom left is the compass: click a ball to look along that axis. Maximize
(Ctrl+Space) lets the view fill the window; press it again to bring the panel, the drawer and the status bar back.

## Your props

File > My props opens Your props: every work file you saved or opened (the library), one row each with its kind and when
it changed. A search box finds props by name, kind or the starting point they were made from; a kind list shows one kind
only; the order is Last changed (the default), Name (numbers in number order) or Date made, and it is remembered. Click a
prop to see its details on the right: its kind and starting point, when it was made and changed, its seed, size and
file. From there: Open, Export… (writes it to the Groundwork map without opening it; a floor tile is exported from the
open piece, as it needs painting), Copy (a copy beside it, "<name> copy", then "copy 2"…), Rename… (at most 60
characters; a name another file has, or with / or \ in it, is refused) and Remove. Ctrl+click, or a row's box, ticks
several props: Export… and Remove then act on every ticked one. The prop you are working on cannot be removed (start or
open another one first). The drawer's Mine tab still shows the same files as pictures.

**Recently removed.** A removed prop's file moves into `~/.config/prop-generator/removed/` and waits there for 30 days
(the app forgets older ones when it starts). Undo (on the page, or Edit > Undo remove) puts back what the last remove
took. File > Recently removed (or the page's button) lists them, newest first: Put back (refused while another file has
taken its place), Remove for good (asks first), Put all back and Empty the bin (asks first).

**Team props.** With a team server (the team's library service, reached over Tailscale), Publish to team sends the
picked prop, as it is saved, with its picture: the first time as a new team prop, then as a new version of it. If
someone published a newer version since you last had it, you are asked before yours goes over it. Team props (the
page's button) shows who the server says you are and every prop the team published; Take saves a copy into Your
props, beside your saved work. The server is the one typed in Settings > Team, else the one the Jatify Launcher gives
(`JATIFY_TEAM`, a host or host:port; the port is 8781 when none is given). With neither, the app works offline as
before. When the server cannot be reached you are asked to retry or carry on offline. There are no passwords: the
server knows you by your Tailscale account.

## Backups

- **Back up to a file** writes every prop into one zip (`prop-generator-backup-YYYY-MM-DD.zip` is offered): a
  `backup.json` listing each prop (where it was, its SHA-256 and when it was saved), the work files under `props/`, and
  under `extras/` your colours, palettes and My presets.
- **Restore from a file** merges and never replaces: a prop that is here as it is in the backup is left alone, a missing
  one comes back where it was, and one with other contents comes back beside it as "<name> (from backup)". Where it
  was is only a hint: a prop goes back there only when that folder already exists and is one of your props' folders (or
  the save folder); otherwise it comes back in `~/.config/prop-generator/restored/`. A file that is not a backup, or one
  made by a newer app, is refused; colours, palettes and presets are merged too.
- **Daily backup folder** (a second drive, a synced folder): once chosen, the app backs up into it at once, then at most
  once a day (at start and after a save), as `prop-generator-backup-YYYY-MM-DD.zip`, keeping the last ten and touching
  nothing else in the folder. A folder that is not there (a drive not plugged in) is said once.
- **Cloud backup** sends the same zip to S3-compatible storage of your own: Cloudflare R2, Backblaze B2, Amazon S3,
  Wasabi, DigitalOcean Spaces, MinIO, or Other with its address. File > Cloud backup… takes the address, bucket,
  region, folder in the bucket (`prop-generator`) and keys; Check the connection writes, reads, lists and removes a
  small file, and only then keeps the keys, in the system keychain through libsecret's `secret-tool` (service
  `prop-generator`, account `cloud-backup`), never in the prefs. Back up to the cloud puts
  `<folder>/prop-generator-backup-YYYY-MM-DD-HHMMSS.zip` and keeps the newest ten there; Restore from the cloud merges
  the newest one as a file restore does. The requests are signed with AWS Signature Version 4, written by hand (checked
  against AWS's published test suite), and only PUT, GET, LIST and DELETE are used. Turn off the cloud backup forgets
  the keys and leaves the bucket alone.

## Colours, palettes and My presets

**The colour picker.** A colour row's custom circle opens the colour square with its #RRGGBB box, then Recent (the last
eight colours chosen, newest first), My colours (the + adds the colour shown; right click one to take it out) and a box
for pasted codes: #4e7d3e, 4e7d3e, #abc or rgb(78, 125, 62), split by spaces, commas or semicolons. One code is used at
once; several are added to My colours. They are kept in `~/.config/prop-generator/colours.json`.

**Saved palettes.** With a kind's colours on My own colours, Prop > Save colours as a palette… keeps those colours
under a name (the same name again updates it). Use a palette offers the palettes that fit the open kind (a rock's stone
palettes fit every rock) and sets them as one undo step; Remove a palette removes one (props keep their colours).

**My presets.** Prop > Save as my preset… keeps the piece's settings and seed as a starting point of your own, in the
drawer's My presets tab (right click to rename or remove) and in Start from my preset. Restore to preset goes back to
it. They are kept in `~/.config/prop-generator/presets.json`.

## Save a picture and Report a problem

File > Save a picture… writes the 3D view as it is, at its size, as a PNG named after the piece. Help > Report a
problem… fills in a report (the app's version, the system, the open prop's kind, starting point, seed and every
setting, and the last failure shown) to edit, then copy or save to a file. Nothing is sent from the app.

## Keyboard shortcuts

Ctrl is Cmd on a Mac. No shortcut runs while a text box has the focus, except Ctrl+K. Help > Keyboard shortcuts
(Ctrl+/) shows this list in the app.

| Keys | Does |
|---|---|
| Ctrl+N | New floor tile |
| Ctrl+O | Open a file |
| Ctrl+S | Save |
| Ctrl+Shift+S | Save a copy |
| Ctrl+E | Export |
| Ctrl+G | Show or hide the drawer |
| Ctrl+, | Settings |
| Ctrl+Z | Undo |
| Ctrl+Shift+Z (or Ctrl+Y) | Redo |
| F | Frame the piece |
| Z | Next shading for a rock (Lit, Flat, Wireframe) |
| Ctrl+\ | Hide or show the panels |
| Ctrl+Space | Maximize the view |
| R | Surprise me |
| 1, 2 | Style: Normal, Style: Low-poly (a rock, grass, flowers, a bush, a cactus or a tree) |
| Ctrl+K (or Ctrl+Shift+P) | Search or run a command |
| Ctrl+/ | Keyboard shortcuts |
| Q | Select tool |
| G, E, S | Move part, Turn part, Resize part (with a part picked) |
| Ctrl+D, Delete, Esc | Copy part, Remove part, Clear selection (Esc puts the tool down first) |

## What version 1 does not do (planned for version 2)

- GPU painting (the painter runs on the CPU).
- Painting the faces of a shape from Blender one by one: v1 shows such a shape in plain grey, but keeps its face numbers.
- The Meadow and Grove demo scenery around the prop (the Grove also needs the painted trees).

## Build from source

Want to help? See [CONTRIBUTING.md](CONTRIBUTING.md).

Needs the .NET 10 SDK and `python3` with numpy and Pillow (no other packages; a build from source uses the Python on
PATH, a release package its own).

    dotnet build PropGenerator.slnx -c Release

The app is built to `src/PropGenerator.Desktop/bin/Release/net10.0/`, with the painter copied beside it.
`scripts/package.sh linux-x64 v0.1.0 dist` makes the release packages (the .tar.gz and the AppImage) in `dist/`.

## Run from source

    src/PropGenerator.Desktop/bin/Release/net10.0/PropGenerator

Or put it in the start menu once with `tools/install-launcher.sh`: it adds Prop Generator (with its icon) to the
applications menu and `~/.local/bin/prop-generator`, which runs this checkout's Release build as the user service
`prop-generator`, capped at 6 GB, and only says so when it is already open.

The app starts the painter worker itself (in a systemd scope capped at 2 GB of memory when a systemd user session
exists) and stops it when the window closes. Two options exist for tests: `--prefs FILE` and `--library FILE` (the list
behind Mine) use other files than the ones in `~/.config/prop-generator/`.

Run the tests:

    cd painter && python3 -m pytest tests -q && cd ..
    dotnet test --project tests/Core.Tests -c Release
    dotnet test --project tests/Desktop.Tests -c Release

Core.Tests checks everything that is not the window (settings, palette, work files, the worker client against the
real painter, GLB export, the catalog merge); Desktop.Tests drives the window headless (no screen, no OpenGL) with a
fake painter.

easy-props' kinds run on its own code: `src/PropGenerator.Engine` hosts the bundle `tools/engine/build.sh` makes from a
tagged release (see `tools/engine/README.md`), and Engine.Tests checks the host, the bundle and every preset on it.
`src/PropGenerator.Nature` holds our own kinds (slab cliffs, stylized rocks) and the code they share with easy-props
(the export path, three's maths, JavaScript's numbers). Nature.Tests checks that code against easy-props' own numbers
and against pins of its output, all committed under `tests/Nature.Tests/Fixtures`:

    dotnet test --project tests/Nature.Tests -c Release
    dotnet test --project tests/Engine.Tests -c Release

Work files saved by the earlier C# port of easy-props 0.4.2 keep opening: `tests/Desktop.Tests/Fixtures/port-0.4.2`
holds files it made and the cut-rock summaries they are checked against. The port, its golden dumps
and meshoptimizer's native build are no longer in the tree.

The painting recipe lives in `painter/propgen_painter`. A golden test checks that it still reproduces the v5 floor atlas
pixel for pixel, give or take float rounding (a few pixels one level off on another numpy build or CPU).

## Claude's connection

File > Settings (Ctrl+,) opens Settings. Tick "Allow Claude to connect", then copy the Claude Code command shown there. It holds
the address (by default `http://127.0.0.1:47120/mcp`, this computer only) and a secret token. The connection is off
until you turn it on.

Claude's tools: `list_controls`, `set_control`, `start_from_preset`, `undo`, `redo`, `save`, `export`, `screenshot`,
`get_state`, `run_command`, and for the parts of a prop `list_parts`, `pick_part`, `move_part`, `turn_part` and
`resize_part` (Copy, Remove and Restore part go through `run_command`). `run_command(id)` runs any command of the list behind the menus, as choosing it would
(for example `surprise`, `frame`, `maximize`, `next-shading`, `scene-grey`); a greyed command is refused with the
reason the menu shows, and an unknown id is answered with the valid ids. Commands that open a dialog only you can
answer are refused (among them My props, Recently removed, the
backups to and from a file, the backup folder, Cloud backup, Save a picture, Report a problem, saving a palette or a
preset and renaming a preset). `get_state` also reports the shading, detail level, scene, budget, faces against the budget and
whether the view is maximized; its status line is the latest message, or the mouse hint once the message's 6 seconds
are over.

## Blender add-on

In Blender you shape things, in Prop Generator you paint them. The add-on in `blender/prop_generator_bridge` moves
meshes between the two. It only copies files; it starts no programs. Version 1 of the app shows a mesh from Blender
(unpainted, in plain grey); painting arbitrary shapes comes in version 2, but the round trip and the face numbers work
now.

**Install.** In Blender 5.2: Edit > Preferences > Add-ons > Install from disk, pick
`blender/prop_generator_bridge.zip` from the release package (from a checkout, make it with
`cd blender && zip -r prop_generator_bridge.zip prop_generator_bridge`; the folder itself cannot be installed), and tick
the add-on. The panel is in the 3D view sidebar
(press N), tab "Prop Generator".

**Use.**

1. Select a mesh object and press **Send to Prop Generator**. The app (if it is running) shows the mesh.
2. In the app, File > **Send to Blender** writes the floor tiles, painted as they are now, to the outbox as
   `pg_<name>_floor_tile.glb` and `pg_<name>_floor_tri.glb`; a rock is written as one GLB, `pg_<name>_rock.glb`, with
   its colours (`pg_untitled_...` for a piece not saved yet).
3. In Blender, **Get painted** imports the newest send from the outbox: both floor tiles, each as an object named after
   its file, side by side from the 3D cursor (the triangle 0.5 m to the +X side of the square); nothing is hidden. A
   rock (one file) comes in as one object named after its file, at the 3D cursor. When the newest file is a single GLB
   named like a mesh you sent from Blender, it comes in as `<object> painted` at that object's place and the object is
   hidden (this is for painting arbitrary shapes in version 2).
4. In the app, **Back to the piece** goes back to the floor tiles (New and Open do too).

**Folders.** `~/.cache/prop-generator/inbox/` (Blender to app) and `~/.cache/prop-generator/outbox/` (app to
Blender), made on first use. The add-on keeps the folder in its preferences (Folder). To use another one, set the same
folder there and start the app with the environment variable `PROPGEN_BRIDGE_ROOT` set to it. The app watches the inbox
while it runs and loads a file half a second after it stops changing.

**Face numbers.** Every face of a sent mesh has a stable number, kept in the integer face attribute `pg_face_id` on the
Blender mesh (numbers up to 16,777,215 are exact in the GLB's float; sending a mesh with a larger one is refused). Faces without one get the next free numbers (one more than the largest in use); sending again keeps every
number, and a face made by splitting keeps its original's number while the copy gets a new one. Blender's glTF exporter
cannot carry a per-face attribute: it averages face and corner attributes onto the vertices, which mixes neighbouring
faces' numbers, and it drops degenerate triangles, so a list of numbers by triangle would drift. The one thing it keeps
exactly is every corner of a UV map. So the add-on exports a triangulated copy (modifiers applied, no materials) with
your first UV map and a second one, `pg_face_id`, whose u is the face number and v is 0. In the GLB that is
`TEXCOORD_1` (u = number, v = 1, because glTF counts v downwards). The app reads the numbers from there into the
piece's faces (kind "unmarked" until version 2 can mark them). A per-vertex `_PG_FACE_ID` attribute whose three corners
agree is read as well. A mesh without usable numbers is still shown; each triangle then counts as a face, and the
status line says so.

**Axes.** Blender is Z up and glTF is Y up; the exporters turn (x, y, z) into (x, z, -y), and the app turns it back.

Test the add-on (uses `~/.cache/prop-generator/scratch/`, never /tmp):

    blender -b --factory-startup -P blender/tests/test_bridge.py

## Performance

Measured on 2026-11-03 on an AMD Ryzen 5 5600X 6-Core Processor with 31 GB RAM (Linux, software OpenGL under a private
Xvfb for the soak). Painting is on the CPU, 4 worker processes.

Paint speed (`python3 painter/bench.py --size N --repeat 5`, run under a 4 GB cap):

| Tile size | Seconds per paint | Peak memory (all 4 processes summed) |
|---|---|---|
| 256 | 0.118 | 271 MB |
| 1024 | 0.537 | 775 MB |

Dragging a paint slider (Carving strength), measured in the real app under a private Xvfb with `PROPGEN_TRACE=1`
(the app then writes, with times, each paint asked for, shown or dropped, and each frame drawn), driven with real mouse
drags (xdotool). "Slider to frame" is from the slider's value changing to the frame that shows that value being drawn.

| Drag | Frames while dragging | Slider to frame (preview, 256 px) | Hand stops to full detail (1024 px) |
|---|---|---|---|
| Steady drag, 106 value changes in 2.3 s | 7.3 a second | median 151 ms, at most 244 ms | 1.2 s (held still: 0.6 s wait, then the paint) |
| Slow fine drag, 1 px every 40 ms with 0.5 s pauses | 4.4 a second | median 147 ms, at most 232 ms | 1.1 s (let go 0.5 s after the last change) |
| Short drag, then let go | 3.8 a second | median 134 ms, at most 158 ms | 0.72 s after the last change |

A full-detail paint takes about 0.6 s on the CPU and the painter does one paint at a time, so while it runs the drag's
previews wait. Before this was fixed, the slow fine drag got 2.1 frames a second, previews up to 482 ms late, and 3 of
its 4 full-detail frames showed a value the slider had already left (the view showed the old value, then jumped).
Now a held drag paints full detail only after 0.6 s held still, a full-detail paint of a value the slider has left is
never shown, and on letting go the preview of the final value comes first, then full detail.

Soak (`tools/soak.sh`, 30 minutes): the app ran under a private Xvfb, driven through Claude's connection with 3600
paint-slider changes and 3600 tone changes (about 4 a second, no call failed), then save, export and a SIGTERM close.
Two things to know: it ran 30 minutes, not an hour, and Claude's
set_control is a finished change, so every paint-slider change is a full-detail (1024 px) paint: it drives the
commits, not the 256 px previews of a mouse drag (those are measured above, with real drags).

| Measure | Result |
|---|---|
| App memory, median of the minute around 5 min | 777.7 MB |
| App memory, median of the last minute (30 min) | 676.6 MB |
| Growth | -13.0 %: it went down, not up (the pass rule is growth of 10 % or less, on these medians). The app's memory moves by about 100 MB up and down between garbage collections, so single readings are noisy |
| Worker peak (worker plus its pool processes, 2 GB cap) | 1237 MB |
| Processes left after close (app, worker, pool, Xvfb) | none |

Run it again with `tools/soak.sh` (the first argument is the length in seconds, 1800 by default); it needs Xvfb (on PATH,
or named by `XVFB`), works under `~/.cache/prop-generator/scratch/soak/` and keeps its samples (every 10 s) and summary
in `tools/soak-out/`.
