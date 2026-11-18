# Changelog

## Unreleased

New:
- **Suggestion** (for people on a team): a button at the top right to tell the team what you would change, with a
  picture of the window or your own screenshots attached. It is shown only when Prop Generator is connected to your
  team's server and the team knows you. Nothing is sent until you press Send, and what you wrote is kept if you close
  the box. Needs the team server 0.7.0 or newer.

## v0.3.0

New:
- **Crash reports.** If Prop Generator closes unexpectedly (any error or crash, or a kill by the memory cap), the next
  start asks whether to send a report to the team: the version, the system, the memory used and that run's log. You
  can read exactly what is sent first, and nothing is sent unless you press Send. Asked only when a team server is set.

Not tested yet:
- **Windows is still untried on a real Windows PC**, as in v0.2.0 (under Wine the 3D view stays black).

## v0.2.0

New:
- **Share props with your team.** Your props has Publish to team: it sends the picked prop (as saved, with its picture)
  to the team's server, the first time as a new team prop, after that as a new version of it. If a teammate published a
  newer version since you last had it, you're asked before yours replaces theirs. Team props lists everything the team
  published, with who and when; Take saves a copy into Your props. The server comes from the Jatify Launcher, or set it
  in Settings > Team; without one, the app works offline as before. If the server can't be reached, you can retry or
  carry on offline. No password: the server knows you by your Tailscale account.
- **Windows x64.** A .zip with everything inside (the .NET runtime, and Python with numpy for the floor painter), the
  one the Jatify Launcher installs. Unzip it anywhere and run PropGenerator.exe.

Not tested yet:
- **Windows has not been tried on a real Windows PC.** It was checked under Wine on Linux: the app opens, loads every
  kind and paints a floor, but the 3D view stayed black there. That is most likely Wine's graphics, and a first test on
  real Windows settles it.

## v0.1.0

The first release.

- **Floor tiles** painted in any wood colour by a hand-painted recipe: a square and a triangle tile, with colour,
  contrast and drift recoloured at once and every other setting repainting the wood, first fast and rough, then at
  full detail.
- **easy-props' props, on easy-props' own code** (its engine bundled, v0.4.7): cut rocks, grass, flowers, bushes,
  cacti, painted trees, old-style trees and palms, each with its presets as starting points, Normal and Low-poly styles,
  your own colours, and wind sway in the export.
- **Our own kinds:** slab cliffs and stylized rocks.
- **Export** to a Groundwork map folder (GLB models with collision, wind and catalog entries), or to Blender through
  the bundled add-on (`blender/prop_generator_bridge.zip`).
- **Your props** on one page, undo and redo, work files, backups (to a file, a daily folder backup, an S3-compatible
  cloud), colour palettes and My presets, a picture of the view, Report a problem.
- **Claude's connection:** Claude can drive the app over a local connection, off until you turn it on in Settings.
- **The shared Jatify look**, as the other Jatify tools wear it.
- **History strip** along the top of the view, one box per change: click or drag across it to step back and forth.
- **Objects drawer** that can be made bigger by dragging its edge and docked on the left or at the bottom.
- **Linux x64 only:** a .tar.gz and an AppImage. Each carries its own Python with numpy for the floor painter, so
  nothing else needs installing. The painter is capped at 2 GB of memory in a systemd scope where a systemd user session
  exists (a plain process without the cap elsewhere).
- **Windows is planned, not in this version.**
