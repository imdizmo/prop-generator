# Cliff matching harness

Renders one of the hand-made cliffs (a GLB named by `CLIFF_ORIGINAL`, never copied into the repo)
and our cliff from the same cameras in headless Blender, and scores the match.

    tools/cliff_match/run_match.sh <settings.json | ours.glb> [object-name-prefix]   # default SM_GF_Cliff_01

- A `.glb` in place of the settings is scored as it is (no export), for example the formula check
  `run_match.sh ~/.cache/prop-generator/scratch/slab-rock-formula/formula_zip.glb SM_GF_Cliff_01` (about 0.97 per view).
- `settings.json`: the slab-cliff parameter keys (`width`, `top-tilt`, `palette` ...) and optional `seed`; keys left out take defaults.
- `CliffExport/`: console exporter (`CliffExport <settings.json> <out.glb>`), not in the solution or any test project.
- `match_views.py`: standalone Blender script. The front, side and top views are ortho at 400 px, framed on the
  original: their width is 1.15 x its largest extent and the front and side cameras sit at half its height. A
  three-quarter view is added for the eye.
- Output: `~/.cache/prop-generator/cliffs/match/` (or `$CLIFF_MATCH_OUT`): `sheet.png` original over ours,
  `overlay.png` red = only original, blue = only ours, and one JSON line with overlap per view, shifts, shading
  difference and ten-band width ratios.
- `blender_views.py` is the older interactive version this was made from.

## Close-ups beside the hand-made pieces

    tools/cliff_match/closeups.sh <preset-id> [seed=1] [look-x,look-z]

Renders the starting point's seed beside Cliff_01, Cliff_Top_01 and LimestoneLarge_01 from the same close camera (the
same metres per pixel for every piece, `closeups.py`), shaded and as wireframes, to
`~/.cache/prop-generator/cliffs/sheets/match_<preset-id>.png`. Point the camera at a join with `look-x,look-z` (metres
along the width and above the base). `closeups.py` alone renders any GLB items at that framing;
`CLOSEUP_DISTANCE` moves the camera closer for a detailed look and `CLOSEUP_COLOUR=1` shows the vertex colours.

## Reach: the five hand-made pieces

    tools/cliff_match/reach.sh

`reach/<piece>.json` holds the slider values (and seed) that come closest to each hand-made piece:
`cliff_01` (`SM_GF_Cliff_01`), `cliff_02` (`SM_GF_Cliff_02`), `top_01` (`SM_GF_Cliff_Top_01`), `top_02`
(`SM_GF_Cliff_Top_02`) and `limestone` (`SM_LimestoneLarge_01`). `reach.sh` scores each with `run_match.sh` and prints
one line per piece: the front, side and top overlap, and `pass` when all three reach 0.93. It exits non-zero when any
piece fails. Each piece's match output goes to `~/.cache/prop-generator/cliffs/reach/<piece>/`, its sheet to
`~/.cache/prop-generator/cliffs/reach/<piece>.png`.

**Cliff 01 and Cliff 02 reach 0.93 in every view; the tops and the limestone are below it.** The files hold the best
values found by a search over the sliders, End turn, Foot, Underside tilt, Taper and the 60° Top tilt included, with
bites, grooves and chips kept low so each piece stays as calm as its original (the 2026-11-08 Task 9b run, rescored after fix round 1):

| piece | front | side | top | status |
|---|---|---|---|---|
| cliff_01 | 0.932 | 0.931 | 0.932 | pass |
| cliff_02 | 0.931 | 0.932 | 0.933 | pass |
| top_01 | 0.922 | 0.885 | 0.886 | below threshold |
| top_02 | 0.920 | 0.920 | 0.918 | below threshold |
| limestone | 0.870 | 0.865 | 0.890 | below threshold |

What still keeps the three below, from the overlays:

- **Top 01**: its plates (Layers 4) read as the stacked masses, but the original's top view has cut-back corners on
  all four sides (a cross-like outline) and its underside slopes the other way front to back; the plates' footprints
  stay near rectangles.
- **Top 02**: one mass whose sides slope straight in from a wide top to a narrow base over its whole height. Foot
  narrows only the blocks on the ground (here the bottom plate), and Taper narrows toward the top, so the body above
  the foot keeps upright sides.
- **Limestone**: plates under a tapering top (Tiers 3, Layers 5, Taper 0.43), but the original's spire is one tall
  slanted point. With Layers above 0 the top block is a plate, and plates tilt at most 4°, so the tip stays flat; a
  tiered build without plates (Tiers 2, Top tilt 27.5, Taper 0.56) gets the slanted point and the same overlap
  (0.870, 0.870, 0.891) but loses the plates.
