# Changelog

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
