#!/usr/bin/env bash
# Exports the slab cliff of a settings JSON to a GLB, renders it beside the user's original piece in headless
# Blender and prints the overlap per view and the width bands. Images go to ~/.cache/prop-generator/cliffs/match/.
#   run_match.sh <settings.json> [object-name-prefix]     (default SM_GF_Cliff_01)
# A .glb in place of the settings is scored as it is, without the export. CLIFF_MATCH_OUT moves the output folder.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
settings="${1:?usage: run_match.sh <settings.json> [object-name-prefix]}"
object="${2:-SM_GF_Cliff_01}"
original="${CLIFF_ORIGINAL:?set CLIFF_ORIGINAL to the GLB of the hand-made cliffs (never copied into the repo)}"
out="${CLIFF_MATCH_OUT:-$HOME/.cache/prop-generator/cliffs/match}"
mkdir -p "$out"

if [[ "$settings" == *.glb ]]; then
  cp "$settings" "$out/ours.glb"
else
  systemd-run --user --scope -q -p MemoryMax=8G -p MemorySwapMax=0 \
    dotnet run --project "$here/CliffExport" -p:UseSharedCompilation=false -- "$settings" "$out/ours.glb"
  dotnet build-server shutdown >/dev/null 2>&1 || true
fi

log="$out/blender.log"
if ! systemd-run --user --scope -q -p MemoryMax=4G -p MemorySwapMax=0 \
  blender -b --factory-startup -P "$here/match_views.py" -- "$original" "$object" "$out/ours.glb" "$out" >"$log" 2>&1; then
  echo "error: Blender failed; its log is kept at $log" >&2
  exit 1
fi
if ! grep -E '^MATCH ' "$log" | sed 's/^MATCH //' | grep .; then
  echo "error: Blender printed no MATCH line; its log is kept at $log" >&2
  exit 1
fi
echo "sheet:   $out/sheet.png"
echo "overlay: $out/overlay.png"
echo "log:     $log"
