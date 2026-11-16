#!/usr/bin/env bash
# Exports seeds of a slab-cliff starting point and renders them as a contact sheet in headless Blender, beside the
# round-8 cliff block (Cliff 01) from the same cameras, so the detail can be compared side by side.
#   seed_sheet.sh <preset-id> [count=12] [first-seed=1]
# Writes ~/.cache/prop-generator/cliffs/sheets/<preset-id>.png: the seeds in a 4-wide grid from the three-quarter
# camera, auto-framed, and a column with the round-8 block from the same camera, then the round-8 block and the first
# seed from a close camera at the same scale. <preset-id>_close.png has every seed from the close camera.
# The reference is ~/.cache/prop-generator/cliffs/iter/cliff01_round8.glb, or the formula GLB when that is missing.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
preset="${1:?usage: seed_sheet.sh <preset-id> [count=12] [first-seed=1]}"
count="${2:-12}"
first="${3:-1}"
tile="${SHEET_TILE:-480}"
sheets="$HOME/.cache/prop-generator/cliffs/sheets"
work="$sheets/work-$preset"
iter="$HOME/.cache/prop-generator/cliffs/iter"
reference="$iter/cliff01_round8.glb"
ref_label="round 8 (Cliff 01)"
if [[ ! -f "$reference" ]]; then
  reference="$iter/formula01.glb"
  ref_label="formula (round 8 missing)"
fi
rm -rf "$work"
mkdir -p "$work"
trap 'rm -rf "$work"' EXIT
cap() { systemd-run --user --scope -q -p "MemoryMax=$1" -p MemorySwapMax=0 "${@:2}"; }

cap 8G dotnet build "$here/CliffExport" -c Release -p:UseSharedCompilation=false -v q -nologo >/dev/null
dotnet build-server shutdown >/dev/null 2>&1 || true
dll="$here/CliffExport/bin/Release/net10.0/CliffExport.dll"
glbs=()
for ((k = 0; k < count; k++)); do
  seed=$((first + k))
  cap 8G dotnet "$dll" --preset "$preset" --seed "$seed" "$work/seed_$seed.glb" >/dev/null
  glbs+=("$work/seed_$seed.glb")
done

log="$work/blender.log"
if ! cap 4G blender -b --factory-startup -P "$here/seed_sheet.py" -- "$work" "$tile" "$reference" "${glbs[@]}" >"$log" 2>&1; then
  cp "$log" "$sheets/$preset.blender.log"
  echo "error: Blender failed; its log is kept at $sheets/$preset.blender.log" >&2
  exit 1
fi

style=(-background '#202022' -fill '#e8e8e8' -pointsize 20 -geometry +3+3)
quarters=()
closes=()
for ((k = 0; k < count; k++)); do
  quarters+=(-label "$preset seed $((first + k))" "$work/quarter_$k.png")
  closes+=(-label "$preset seed $((first + k)), close" "$work/close_$k.png")
done
montage "${quarters[@]}" -tile 4x "${style[@]}" "$work/grid.png"
montage -label "$ref_label" "$work/quarter_ref.png" -label "$ref_label, close" "$work/close_ref.png" \
  -label "$preset seed $first, close" "$work/close_0.png" -tile 1x "${style[@]}" "$work/side.png"
magick "$work/grid.png" "$work/side.png" -background '#202022' -gravity north +append "$sheets/$preset.png"
montage "${closes[@]}" -tile 4x "${style[@]}" "$work/close_grid.png"
montage -label "$ref_label, close" "$work/close_ref.png" -tile 1x "${style[@]}" "$work/close_side.png"
magick "$work/close_grid.png" "$work/close_side.png" -background '#202022' -gravity north +append "$sheets/${preset}_close.png"
echo "sheet: $sheets/$preset.png"
echo "close: $sheets/${preset}_close.png"
