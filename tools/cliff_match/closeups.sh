#!/usr/bin/env bash
# Renders one starting point's seed beside three of the hand-made pieces (Cliff_01, Cliff_Top_01, LimestoneLarge_01)
# at the same close framing (the same metres per pixel), a join and an edge in view, so their joins and edges can be
# compared side by side.
#   closeups.sh <preset-id> [seed=1] [look-x,look-z]
# The look point is where on the front the camera looks: x along the width and z above the base (default: the middle,
# at most 4 m up). Writes ~/.cache/prop-generator/cliffs/sheets/match_<preset-id>.png: the four shaded, and below each
# its wireframe.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
preset="${1:?usage: closeups.sh <preset-id> [seed=1] [look-x,look-z]}"
seed="${2:-1}"
look="${3:-}"
tile="${CLOSEUP_TILE:-560}"
handmade="${CLIFF_ORIGINAL:?set CLIFF_ORIGINAL to the GLB of the hand-made cliffs (never copied into the repo)}"
sheets="$HOME/.cache/prop-generator/cliffs/sheets"
work="$sheets/work-match-$preset"
rm -rf "$work"
mkdir -p "$work"
trap 'rm -rf "$work"' EXIT
cap() { systemd-run --user --scope -q -p "MemoryMax=$1" -p MemorySwapMax=0 "${@:2}"; }

cap 8G dotnet build "$here/CliffExport" -c Release -p:UseSharedCompilation=false -v q -nologo >/dev/null
dotnet build-server shutdown >/dev/null 2>&1 || true
cap 8G dotnet "$here/CliffExport/bin/Release/net10.0/CliffExport.dll" --preset "$preset" --seed "$seed" "$work/ours.glb" >/dev/null

ours="ours=$work/ours.glb"
if [[ -n "$look" ]]; then
  ours="$ours@$look"
fi

log="$work/blender.log"
if ! cap 4G blender -b --factory-startup -P "$here/closeups.py" -- "$work" "$tile" "$ours" \
  "cliff01=$handmade#SM_GF_Cliff_01" "top01=$handmade#SM_GF_Cliff_Top_01" "limestone=$handmade#SM_LimestoneLarge_01" >"$log" 2>&1; then
  cp "$log" "$sheets/match_$preset.blender.log"
  echo "error: Blender failed; its log is kept at $sheets/match_$preset.blender.log" >&2
  exit 1
fi

style=(-background '#202022' -fill '#e8e8e8' -pointsize 18 -geometry +3+3)
montage -label "$preset seed $seed" "$work/ours.png" -label "Cliff_01 (hand-made)" "$work/cliff01.png" \
  -label "Cliff_Top_01 (hand-made)" "$work/top01.png" -label "LimestoneLarge_01 (hand-made)" "$work/limestone.png" \
  -label "$preset seed $seed, wire" "$work/ours_wire.png" -label "Cliff_01, wire" "$work/cliff01_wire.png" \
  -label "Cliff_Top_01, wire" "$work/top01_wire.png" -label "LimestoneLarge_01, wire" "$work/limestone_wire.png" \
  -tile 4x "${style[@]}" "$sheets/match_$preset.png"
echo "match: $sheets/match_$preset.png"
