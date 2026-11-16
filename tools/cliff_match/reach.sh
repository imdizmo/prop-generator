#!/usr/bin/env bash
# Scores the five reach settings (reach/*.json) against the user's hand-made pieces with run_match.sh and prints one
# line per piece: front, side and top overlap, and pass when every view reaches 0.93. Exits non-zero when any piece
# fails. Each piece's match images go to ~/.cache/prop-generator/cliffs/reach/<piece>/ and its sheet to
# ~/.cache/prop-generator/cliffs/reach/<piece>.png. The overlaps are match_views.py's, rounded to 3 places, so the
# printed value is the one compared with the threshold.
#   reach.sh
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="$HOME/.cache/prop-generator/cliffs/reach"
threshold=0.93
pieces=(
  "cliff_01 SM_GF_Cliff_01"
  "cliff_02 SM_GF_Cliff_02"
  "top_01 SM_GF_Cliff_Top_01"
  "top_02 SM_GF_Cliff_Top_02"
  "limestone SM_LimestoneLarge_01"
)
failed=0
for entry in "${pieces[@]}"; do
  read -r name prefix <<<"$entry"
  dir="$out/$name"
  mkdir -p "$dir"
  if ! CLIFF_MATCH_OUT="$dir" "$here/run_match.sh" "$here/reach/$name.json" "$prefix" >"$dir/run.log" 2>&1; then
    printf '%-10s error (see %s)\n' "$name" "$dir/run.log"
    failed=1
    continue
  fi
  cp "$dir/sheet.png" "$out/$name.png"
  if ! grep -m 1 '^{' "$dir/run.log" | python3 -c '
import json, sys
name, threshold = sys.argv[1], float(sys.argv[2])
text = sys.stdin.read().strip()
try:
    r = json.loads(text)
    ious = [float(r[v]["iou"]) for v in ("front", "side", "top")]
except (ValueError, KeyError, TypeError):
    print(f"{name:<10} error (no score line in its run.log)")
    sys.exit(1)
ok = min(ious) >= threshold
verdict = "pass" if ok else "FAIL"
print(f"{name:<10} front {ious[0]:.3f}  side {ious[1]:.3f}  top {ious[2]:.3f}  {verdict}")
sys.exit(0 if ok else 1)
' "$name" "$threshold"; then
    failed=1
  fi
done
exit "$failed"
